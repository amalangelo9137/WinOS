#pragma once
#include "Shared.h"
#include <intrin.h>

class PciScanner {
public:
    static uint32_t ReadConfigDword(uint8_t bus, uint8_t slot, uint8_t func, uint8_t offset) {
        uint32_t address =
            ((uint32_t)1 << 31) |
            ((uint32_t)bus << 16) |
            ((uint32_t)slot << 11) |
            ((uint32_t)func << 8) |
            ((uint32_t)offset & 0xFC);

        __outdword(0xCF8, address);
        return __indword(0xCFC);
    }

    static uint16_t ReadVendorId(uint8_t bus, uint8_t slot, uint8_t func) {
        uint32_t reg0 = ReadConfigDword(bus, slot, func, 0x00);
        return (uint16_t)(reg0 & 0xFFFF);
    }

    static uint64_t GetPciBar64(uint8_t bus, uint8_t slot, uint8_t func) {
        uint32_t bar0 = ReadConfigDword(bus, slot, func, 0x10);
        uint32_t bar1 = ReadConfigDword(bus, slot, func, 0x14);

        uint64_t physical_address = (bar0 & 0xFFFFFFF0);
        physical_address |= ((uint64_t)bar1 << 32);

        return physical_address;
    }
};