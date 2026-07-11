#pragma once
#include "Shared.h"
#include <intrin.h>

struct PciDevice {
    uint8_t  Bus;
    uint8_t  Slot;
    uint8_t  Function;
    uint16_t VendorId;
    uint16_t DeviceId;
    uint8_t  ClassCode;
    uint8_t  SubclassCode;
};

class PciScanner {
public:
    static uint32_t ReadConfigDword(uint8_t bus, uint8_t slot, uint8_t func, uint8_t offset) {
        uint32_t address =
            ((uint32_t)1 << 31) |               // Enable bit
            ((uint32_t)bus << 16) |             // Bus selection
            ((uint32_t)slot << 11) |            // Device/Slot selection
            ((uint32_t)func << 8) |             // Function selection
            ((uint32_t)offset & 0xFC);          // Register offset alignment

        __outdword(0xCF8, address);                 // Tell motherboard where to look
        return __indword(0xCFC);                    // Read data returned
    }

    static uint16_t ReadVendorId(uint8_t bus, uint8_t slot, uint8_t func) {
        uint32_t reg0 = ReadConfigDword(bus, slot, func, 0x00);
        return (uint16_t)(reg0 & 0xFFFF);       // Vendor ID is the lower 16 bits
    }

    static void ScanBus() {
        // Loop through all possible combinations on the motherboard
        for (int bus = 0; bus < 256; bus++) {
            for (int slot = 0; slot < 32; slot++) {
                for (int func = 0; func < 8; func++) {

                    uint16_t vendorId = ReadVendorId(bus, slot, func);
                    if (vendorId == 0xFFFF) continue; // 0xFFFF means no device present

                    uint32_t reg0 = ReadConfigDword(bus, slot, func, 0x00);
                    uint16_t deviceId = (uint16_t)(reg0 >> 16);

                    uint32_t reg8 = ReadConfigDword(bus, slot, func, 0x08);
                    uint8_t classCode = (uint8_t)(reg8 >> 24);
                    uint8_t subclassCode = (uint8_t)(reg8 >> 16);

                    // HARDWARE TARGET MATCHING:
                    // USB xHCI Controllers always have Class 0x0C and Subclass 0x03
                    if (classCode == 0x0C && subclassCode == 0x03) {
                        // Found your USB 3.0 controller!
                    }

                    // Audio controllers are usually Class 0x04
                    if (classCode == 0x04) {
                        // Found your Audio subsystem!
                    }
                }
            }
        }
    }

    static uint64_t GetPciBar64(uint8_t bus, uint8_t slot, uint8_t func) {
        // BAR0 is at offset 0x10, BAR1 is at offset 0x14
        uint32_t bar0 = ReadConfigDword(bus, slot, func, 0x10);
        uint32_t bar1 = ReadConfigDword(bus, slot, func, 0x14);

        // Mask out the lower 4 bits because they contain architecture flags (memory vs I/O space)
        uint64_t physical_address = (bar0 & 0xFFFFFFF0);
        physical_address |= ((uint64_t)bar1 << 32);

        return physical_address;
    }
};