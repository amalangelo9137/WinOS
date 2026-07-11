#pragma once
#include "Shared.h"
#include "PMM.h"
#include <intrin.h>

#define PT_PRESENT  (1ULL << 0)
#define PT_WRITABLE (1ULL << 1)

extern PhysicalMemoryManager PMM;

class VirtualMemoryManager {
private:
    uint64_t* m_PML4;

    // Helper to extract index offsets from a virtual address
    size_t GetIndex(uint64_t virtual_addr, int level) {
        return (virtual_addr >> (12 + (level * 9))) & 0x1FF;
    }

    // Navigates downward or allocates missing directory tables dynamically
    uint64_t* GetNextTable(uint64_t* current_table, size_t index) {
        if (current_table[index] & PT_PRESENT) {
            // Mask out the flags to extract the pure physical address pointer
            return (uint64_t*)(current_table[index] & 0x000FFFFFFFFFF000ULL);
        }

        // Table doesn't exist yet! Allocate a new physical page frame for it
        void* new_table = PMM.AllocatePage();
        if (!new_table) return nullptr;

        // Zero out the new table using standard intrinsics
        __stosq((unsigned long long*)new_table, 0, 512);

        // Link the new table into the parent entry
        current_table[index] = (uint64_t)new_table | PT_PRESENT | PT_WRITABLE;
        return (uint64_t*)new_table;
    }

    void FlushTLB(void* virtual_addr) {
        __invlpg(virtual_addr);
    }

public:
    void Initialize() {
        // Read the current active PML4 address straight out of the CR3 register
        m_PML4 = (uint64_t*)__readcr3();
    }

    // Maps a hardware address space directly into active memory space
    bool MapMemory(void* virtual_addr, void* physical_addr) {
        uint64_t v_addr = (uint64_t)virtual_addr;
        uint64_t p_addr = (uint64_t)physical_addr;

        size_t pml4_idx = GetIndex(v_addr, 3);
        size_t pdpt_idx = GetIndex(v_addr, 2);
        size_t pd_idx = GetIndex(v_addr, 1);
        size_t pt_idx = GetIndex(v_addr, 0);

        uint64_t* pdpt = GetNextTable(m_PML4, pml4_idx);
        if (!pdpt) return false;

        uint64_t* pd = GetNextTable(pdpt, pdpt_idx);
        if (!pd) return false;

        uint64_t* pt = GetNextTable(pd, pd_idx);
        if (!pt) return false;

        // Write the physical address into the base Page Table entry
        pt[pt_idx] = (p_addr & 0x000FFFFFFFFFF000ULL) | PT_PRESENT | PT_WRITABLE;

        FlushTLB(virtual_addr);
        return true;
    }
};