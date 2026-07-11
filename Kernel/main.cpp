#include <intrin.h>

#include "WinOSBaseAssets/WinOS-Logo.h"
#include "WinOSBaseAssets/WinOS-Throbber.h"
#include "CrucialInternals/PMM.h"
#include "CrucialInternals/VMM.h"
#include "CrucialInternals/PCI.h"
#include "Shared.h"

extern "C" void InitGDT();
extern "C" void InitIDT();

extern "C" BOOT_CONFIG* GlobalConfig = nullptr;

PhysicalMemoryManager PMM;
VirtualMemoryManager VMM;

// Change these to adjust the size of the logo on screen!
#define LOGO_TARGET_WIDTH  256  
#define LOGO_TARGET_HEIGHT 256  
#define LOGO_SRC_WIDTH     512  // The actual size inside WinOS-Logo.h
#define LOGO_SRC_HEIGHT    512

extern "C" __declspec(dllexport) void KernelMain(BOOT_CONFIG* config) {
    GlobalConfig = config;

    InitGDT();
    InitIDT();

    PMM.Initialize(config->MemMap, config->MemMapSize, config->MemMapDescriptorSize); // init our memory manager!
    VMM.Initialize(); // init the virtual memory manager

    PciScanner::ScanBus(); // scans all pci devices connected

    int current_frame = 0;
    uint64_t total_pixels = (uint64_t)config->Height * config->PixelsPerScanLine;

    while (true) {
        // 1. OPTIMIZED CLEAR BACKBUFFER: Flood the memory with 0xFF000000 via hardware
        __stosd(
            (unsigned long*)config->BackBuffer,  // Destination
            0xFF000000,                          // 32-bit Color value to fill
            (unsigned long)total_pixels          // Count of pixels
        );

        // 2. DRAW SHRUNK LOGO TO BACKBUFFER
        int logo_start_x = (config->Width / 2) - (LOGO_TARGET_WIDTH / 2);
        int logo_start_y = (config->Height / 2) - (LOGO_TARGET_HEIGHT / 2);

        for (int y = 0; y < LOGO_TARGET_HEIGHT; y++) {
            for (int x = 0; x < LOGO_TARGET_WIDTH; x++) {

                // Scale factor math: Map the target small coordinate back to the 512x512 array space
                int src_x = (x * LOGO_SRC_WIDTH) / LOGO_TARGET_WIDTH;
                int src_y = (y * LOGO_SRC_HEIGHT) / LOGO_TARGET_HEIGHT;

                uint32_t color = WinOS_Logo[src_y * LOGO_SRC_WIDTH + src_x];

                // Skip transparent pixels
                if (color == 0x00000000) continue;

                int target_x = logo_start_x + x;
                int target_y = logo_start_y + y;

                if (target_x >= 0 && target_x < config->Width && target_y >= 0 && target_y < config->Height) {
                    uint64_t pixel_index = (uint64_t)target_y * config->PixelsPerScanLine + target_x;
                    config->BackBuffer[pixel_index] = color; // Draw to RAM, not screen!
                }
            }
        }

        // 3. DRAW THROBBER TO BACKBUFFER (Optimized Byte-Skipping)
        int throb_start_x = (config->Width / 2) - (THROB_WIDTH / 2);
        int throb_start_y = (config->Height / 2) - (THROB_HEIGHT / 2);

        uint32_t frame_offset = current_frame * BYTES_PER_FRAME;

        for (int y = 0; y < THROB_HEIGHT; y++) {
            for (int byte_x = 0; byte_x < (THROB_WIDTH / 8); byte_x++) {

                uint32_t current_byte_index = frame_offset + (y * (THROB_WIDTH / 8) + byte_x);
                uint8_t pixel_packet = WinOS_Throbber_Stream[current_byte_index];

                // CRITICAL SPEEDUP: If all 8 pixels are empty, don't waste CPU cycles checking bits!
                if (pixel_packet == 0x00) continue;

                // If the byte isn't empty, only then do we unpack the bits
                for (int bit = 0; bit < 8; bit++) {
                    bool is_white = (pixel_packet >> (7 - bit)) & 0x01;

                    if (is_white) {
                        int target_x = throb_start_x + (byte_x * 8) + bit;
                        int target_y = throb_start_y + y;

                        if (target_x >= 0 && target_x < config->Width && target_y >= 0 && target_y < config->Height) {
                            uint64_t pixel_index = (uint64_t)target_y * config->PixelsPerScanLine + target_x;
                            config->BackBuffer[pixel_index] = 0xFFFFFFFF;
                        }
                    }
                }
            }
        }

        // 4. THE OPTIMIZED BLIT: Copy from BackBuffer to physical screen using hardware strings
        __movsd(
            (unsigned long*)config->BaseAddress, // Destination
            (unsigned long*)config->BackBuffer,  // Source
            (unsigned long)total_pixels          // Count of 32-bit blocks (pixels)
        );

        // Advance animation frame
        current_frame = (current_frame + 1) % TOTAL_FRAMES;
    }
}