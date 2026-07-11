#include <intrin.h>

#include "WinOSBaseAssets/WinOS-Logo.h"
#include "WinOSBaseAssets/WinOS-Throbber.h"

#include "CrucialInternals/PMM.h"
#include "CrucialInternals/VMM.h"
#include "CrucialInternals/PCI.h"

#include "Drivers/Audio/AudioDriver.h"

#include "Shared.h"

extern "C" void InitGDT();
extern "C" void InitIDT();

extern "C" BOOT_CONFIG* GlobalConfig = nullptr;

#define LOGO_TARGET_WIDTH  256  
#define LOGO_TARGET_HEIGHT 256  
#define LOGO_SRC_WIDTH     512
#define LOGO_SRC_HEIGHT    512

PhysicalMemoryManager PMM;
VirtualMemoryManager VMM;
HDADriver AudioSystem;

// Reads a 64-bit value from a CPU Model-Specific Register
uint64_t ReadMSR(uint32_t msr) {
    return (uint64_t)__readmsr(msr);
}

// Writes a 64-bit value to a CPU Model-Specific Register
void WriteMSR(uint32_t msr, uint64_t value) {
    __writemsr(msr, (unsigned __int64)value);
}

#define MTRR_CAP_MSR          0xFE
#define MTRR_DEF_TYPE_MSR     0x2FF
#define MTRR_PHYS_BASE_BASE   0x200
#define MTRR_PHYS_MASK_BASE   0x201

void EnableWriteCombining(uint64_t phys_addr, uint64_t size) {
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

    // CRITICAL HIGH-PERFORMANCE STEP: Clear the physical screen ONCE at boot.
    // This wipes UEFI artifacts out completely so we don't have to rewrite blank space every frame.
    __stosd(
        (unsigned long*)config->BaseAddress,
        0xFF000000, // Solid Black Background
        (unsigned long)((uint64_t)config->PixelsPerScanLine * config->Height)
    );

    // Scan the motherboard for Intel High Definition Audio Hardware
    for (int bus = 0; bus < 256; bus++) {
        for (int slot = 0; slot < 32; slot++) {
            for (int func = 0; func < 8; func++) {
                uint16_t vendorId = PciScanner::ReadVendorId(bus, slot, func);
                if (vendorId == 0xFFFF) continue;

                uint32_t reg8 = PciScanner::ReadConfigDword(bus, slot, func, 0x08);
                uint8_t classCode = (uint8_t)(reg8 >> 24);

                if (classCode == 0x04) { // Multimedia Audio Device
                    if (AudioSystem.Initialize(bus, slot, func, VMM)) {
                        // Audio channel link verified
                    }
                }
            }
        }
    }

    // Pre-calculate rendering layout boundaries
    int logo_start_x = (config->Width / 2) - (LOGO_TARGET_WIDTH / 2);
    int logo_start_y = (config->Height / 2) - (LOGO_TARGET_HEIGHT / 2);

    int throb_start_x = (config->Width / 2) - (THROB_WIDTH / 2);
    int throb_start_y = (config->Height / 2) - (THROB_HEIGHT / 2);

    // Dynamic Delta Range Tracking: Find the exact vertical slice containing elements
    int dirty_start_y = (logo_start_y < throb_start_y) ? logo_start_y : throb_start_y;
    int dirty_end_y = ((logo_start_y + LOGO_TARGET_HEIGHT) > (throb_start_y + THROB_HEIGHT)) ? (logo_start_y + LOGO_TARGET_HEIGHT) : (throb_start_y + THROB_HEIGHT);

    // Dynamic Delta Range Tracking: Find horizontal bounds and align to 64-bit bounds (2 pixels = 8 bytes)
    int dirty_start_x = (logo_start_x < throb_start_x) ? logo_start_x : throb_start_x;
    int dirty_end_x = ((logo_start_x + LOGO_TARGET_WIDTH) > (throb_start_x + THROB_WIDTH)) ? (logo_start_x + LOGO_TARGET_WIDTH) : (throb_start_x + THROB_WIDTH);

    int aligned_start_x = dirty_start_x & ~1;
    int aligned_end_x = (dirty_end_x + 1) & ~1;
    int width_pixels = aligned_end_x - aligned_start_x;

    // Direct pointers for high-speed blitting arrays
    uint32_t* real_screen = (uint32_t*)config->BaseAddress;
    uint32_t* back_buffer = (uint32_t*)config->BackBuffer;
    uint64_t pitch = config->PixelsPerScanLine;

    int current_frame = 0;

    while (true) {
        // 1. OPTIMIZED CLEAN DELTA: Clear ONLY the horizontal scanlines containing assets in RAM
        __stosd(
            (unsigned long*)(back_buffer + (dirty_start_y * pitch)),
            0xFF000000,
            (unsigned long)((dirty_end_y - dirty_start_y) * pitch)
        );

        // 2. DRAW SHRUNK LOGO TO BACKBUFFER
        for (int y = 0; y < LOGO_TARGET_HEIGHT; y++) {
            for (int x = 0; x < LOGO_TARGET_WIDTH; x++) {
                int src_x = (x * LOGO_SRC_WIDTH) / LOGO_TARGET_WIDTH;
                int src_y = (y * LOGO_SRC_HEIGHT) / LOGO_TARGET_HEIGHT;

                uint32_t color = WinOS_Logo[src_y * LOGO_SRC_WIDTH + src_x];
                if (color == 0x00000000) continue;

                int target_x = logo_start_x + x;
                int target_y = logo_start_y + y;

                if (target_x >= 0 && target_x < config->Width && target_y >= 0 && target_y < config->Height) {
                    back_buffer[target_y * pitch + target_x] = color;
                }
            }
        }

        // 3. DRAW THROBBER TO BACKBUFFER
        int throb_byte_width = THROB_WIDTH / 8;
        uint32_t frame_offset = current_frame * BYTES_PER_FRAME;

        for (int y = 0; y < THROB_HEIGHT; y++) {
            for (int byte_x = 0; byte_x < throb_byte_width; byte_x++) {
                uint32_t current_byte_index = frame_offset + (y * throb_byte_width + byte_x);
                uint8_t pixel_packet = WinOS_Throbber_Stream[current_byte_index];

                if (pixel_packet == 0x00) continue;

                for (int bit = 0; bit < 8; bit++) {
                    if ((pixel_packet >> (7 - bit)) & 0x01) {
                        int target_x = throb_start_x + (byte_x * 8) + bit;
                        int target_y = throb_start_y + y;

                        if (target_x >= 0 && target_x < config->Width && target_y >= 0 && target_y < config->Height) {
                            back_buffer[target_y * pitch + target_x] = 0xFFFFFFFF;
                        }
                    }
                }
            }
        }

        // 4. HIGH-PERFORMANCE DELTA BURST BLIT
        // Instead of copying millions of pixels, we iterate through the active row span
        // and copy only the horizontal pixel blocks that changed using 64-bit bursts.
        for (int y = dirty_start_y; y < dirty_end_y; y++) {
            uint64_t row_offset = (uint64_t)y * pitch + aligned_start_x;

            __movsq(
                (unsigned long long*)(real_screen + row_offset),
                (unsigned long long const*)(back_buffer + row_offset),
                (width_pixels * 4) / 8 // 4 bytes per pixel, 8 bytes per chunk
            );
        }

        current_frame = (current_frame + 1) % TOTAL_FRAMES;
    }
}