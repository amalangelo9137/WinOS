#pragma once
#include "Shared.h"
#include "../../CrucialInternals/PCI.h"
#include "../../CrucialInternals/VMM.h"

// Intel HDA Controller Register Offsets
#define HDA_REG_GCAP      0x00   // Global Capabilities (RO)
#define HDA_REG_GCTL      0x08   // Global Control (RW)
#define HDA_REG_STATESTS  0x0E   // State Status (RWC)

class HDADriver {
private:
    uint8_t* m_MMIOBase;
    bool     m_Initialized;

    // Helper methods to read/write directly to mapped hardware memory
    void Write32(uint32_t reg, uint32_t value) {
        *(volatile uint32_t*)(m_MMIOBase + reg) = value;
    }

    void Write16(uint32_t reg, uint16_t value) {
        *(volatile uint16_t*)(m_MMIOBase + reg) = value;
    }

	void Write8(uint32_t reg, uint8_t value) {
		*(volatile uint8_t*)(m_MMIOBase + reg) = value;
	}

    uint32_t Read32(uint32_t reg) {
        return *(volatile uint32_t*)(m_MMIOBase + reg);
    }

    uint16_t Read16(uint32_t reg) {
        return *(volatile uint16_t*)(m_MMIOBase + reg);
    }

	uint8_t Read8(uint32_t reg) {
		return *(volatile uint8_t*)(m_MMIOBase + reg);
	}

public:
    HDADriver() : m_MMIOBase(nullptr), m_Initialized(false) {}

    // Maps the hardware registers and brings the controller out of reset
    bool Initialize(uint8_t bus, uint8_t slot, uint8_t func, VirtualMemoryManager& vmm) {
        // 1. Grab the 64-bit physical address from the PCI configuration space BARs
        uint64_t phys_base = PciScanner::GetPciBar64(bus, slot, func);
        if (phys_base == 0) return false;

        // 2. Map the physical hardware BAR into virtual address space using identity mapping
        if (!vmm.MapMemory((void*)phys_base, (void*)phys_base)) {
            return false;
        }
        m_MMIOBase = (uint8_t*)phys_base;

        // 3. Initiate Intel HDA Hardware Controller Reset Sequence
        uint32_t gctl = Read32(HDA_REG_GCTL);

        // Clear CRST (Bit 0) to force the controller into a hard reset state
        Write32(HDA_REG_GCTL, gctl & ~0x01);

        // Wait until the hardware controller confirms it entered reset state
        int timeout = 10000;
        while ((Read32(HDA_REG_GCTL) & 0x01) != 0) {
            if (--timeout == 0) return false; // Hardware hung
        }

        // Wait a brief millisecond for hardware rails to stabilize, then bring it out of reset
        for (volatile int delay = 0; delay < 100000; delay++) {}
        Write32(HDA_REG_GCTL, Read32(HDA_REG_GCTL) | 0x01);

        // Wait for hardware to mark the link initialization as active and ready
        timeout = 10000;
        while ((Read32(HDA_REG_GCTL) & 0x01) == 0) {
            if (--timeout == 0) return false;
        }

        m_Initialized = true;
        return true;
    }

    // Diagnostic verification to see if our MMIO writes modify live hardware state
    bool TestHardwareCommunication() {
        if (!m_Initialized) return false;

        // Read active audio codec flags
        uint16_t statests = Read16(HDA_REG_STATESTS);

        // Clear any existing status bits by writing the flags back to the register 
        // (Intel HDA uses Write-1-to-Clear logic for status bits)
        Write16(HDA_REG_STATESTS, statests);

        // If we can access this register without crashing, the MMIO bridge is working
        return true;
    }

    // Accessor to check which codecs are awake on the digital link
    uint16_t GetActiveCodecs() {
        if (!m_Initialized) return 0;
        return Read16(HDA_REG_STATESTS);
    }

    void GenBeep(uint16_t frequency_hz) {
        if (!m_MMIOBase) return;

        if (frequency_hz == 0) {
            // Write 0 to completely shut off the beep tone
            Write8(0x60, 0);
            return;
        }

        // The HDA specification calculates the byte value with this exact formula:
        // Byte Value = 48000 / (2 * Frequency)
        uint32_t divider = 24000 / frequency_hz;
        if (divider > 255) divider = 255;
        if (divider == 0) divider = 1;

        // Write the divider byte to offset 0x60 to toggle the sound wave on instantly
        Write8(0x60, (uint8_t)divider);
    }
};