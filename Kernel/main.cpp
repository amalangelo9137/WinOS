#include <intrin.h>
#include <cstdint>

// assets
#include "WinOSBaseAssets/WinOS-Logo.h"

// crucial internals like memory managers and descriptor tables
#include "CrucialInternals/PMM.h"
#include "CrucialInternals/VMM.h"

// shared between uefi app and kernel
#include "Shared.h"

extern "C" void InitGDT();
extern "C" void InitIDT();

extern "C" BOOT_CONFIG* GlobalConfig = nullptr;


// logo res
constexpr auto LOGO_TARGET_WIDTH = 256;
constexpr auto LOGO_TARGET_HEIGHT = 256;
constexpr auto LOGO_SRC_WIDTH = 512;
constexpr auto LOGO_SRC_HEIGHT = 512;

PhysicalMemoryManager PMM;
VirtualMemoryManager VMM;

// Reads a 64-bit value from a CPU Model-Specific Register
static uint64_t ReadMSR(uint32_t msr) {
	return (uint64_t)__readmsr(msr);
}

// Writes a 64-bit value to a CPU Model-Specific Register
static void WriteMSR(uint32_t msr, uint64_t value) {
	__writemsr(msr, (unsigned __int64)value);
}

constexpr auto MTRR_CAP_MSR = 0xFE;
constexpr auto MTRR_DEF_TYPE_MSR = 0x2FF;
constexpr auto MTRR_PHYS_BASE_BASE = 0x200;
constexpr auto MTRR_PHYS_MASK_BASE = 0x201;

static void EnableWriteCombining(uint64_t phys_addr, uint64_t size) {
	uint64_t mtrr_cap = ReadMSR(MTRR_CAP_MSR);
	uint8_t variable_count = (uint8_t)(mtrr_cap & 0xFF);

	// MTRR alignment mask configuration
	uint64_t mask = ~(size - 1) & 0x000FFFFFFFFFF000ULL;

	for (uint8_t i = 0; i < variable_count; i++) {
		uint32_t base_msr = MTRR_PHYS_BASE_BASE + (i * 2);
		uint32_t mask_msr = MTRR_PHYS_MASK_BASE + (i * 2);

		uint64_t current_mask = ReadMSR(mask_msr);

		if ((current_mask & (1ULL << 11)) == 0) {
			_disable();

			uint64_t def_type = ReadMSR(MTRR_DEF_TYPE_MSR);
			WriteMSR(MTRR_DEF_TYPE_MSR, def_type & ~(1ULL << 11));

			// Type 0x01 = Write-Combining hardware attribute
			uint64_t base_value = (phys_addr & 0x000FFFFFFFFFF000ULL) | 0x01;
			WriteMSR(base_msr, base_value);

			uint64_t mask_value = mask | (1ULL << 11);
			WriteMSR(mask_msr, mask_value);

			WriteMSR(MTRR_DEF_TYPE_MSR, def_type | (1ULL << 11));

			_enable();
			return;
		}
	}
}

extern "C" __declspec(dllexport) void KernelMain(BOOT_CONFIG* config) {
	GlobalConfig = config;

	// Initialize core memory managers
	PMM.Initialize(config->MemMap, config->MemMapSize, config->MemMapDescriptorSize);
	VMM.Initialize();

	// Framebuffer configuration tracking variables
	uint8_t* fb_base = (uint8_t*)config->BaseAddress;
	uint64_t fb_size = (uint64_t)config->PixelsPerScanLine * config->Height * 4;

	// Round size up to power-of-two for physical MTRR register alignment validation
	uint64_t mtrr_size = 1ULL;
	while (mtrr_size < fb_size) {
		mtrr_size <<= 1;
	}
	EnableWriteCombining((uint64_t)config->BaseAddress, mtrr_size);

	// Remap the virtual page descriptors to apply Write-Through cache flags
	for (uint64_t offset = 0; offset < fb_size; offset += 4096) {
		void* addr = fb_base + offset;
		VMM.MapMemoryEx(addr, addr, PT_WRITE_THROUGH);
	}

	// draw logo
	for (int y = 0; y < LOGO_TARGET_HEIGHT; y++) {
		for (int x = 0; x < LOGO_TARGET_WIDTH; x++) {
			int src_x = (x * LOGO_SRC_WIDTH) / LOGO_TARGET_WIDTH;
			int src_y = (y * LOGO_SRC_HEIGHT) / LOGO_TARGET_HEIGHT;

			uint32_t color = WinOS_Logo[src_y * LOGO_SRC_WIDTH + src_x];
			if (color == 0x00000000) continue;

			int target_x = ((config->Width / 2) - (LOGO_TARGET_WIDTH / 2)) + x;
			int target_y = ((config->Height / 2) - (LOGO_TARGET_HEIGHT / 2)) + y;

			if (target_x >= 0 && target_x < config->Width && target_y >= 0 && target_y < config->Height) {
				config->BaseAddress[target_y * config->PixelsPerScanLine + target_x] = color;
			}
		}
	}

	while (true) {

	}
}