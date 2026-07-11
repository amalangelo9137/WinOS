#pragma once
#include "Shared.h"

#define PAGE_SIZE 4096

// Explicitly define the standard x86_64 EFI memory descriptor layout layout internally
struct KERNEL_EFI_MEMORY_DESCRIPTOR {
    uint32_t Type;
    uint32_t Pad;
    uint64_t PhysicalStart;
    uint64_t VirtualStart;
    uint64_t NumberOfPages;
    uint64_t Attribute;
};

class PhysicalMemoryManager {
private:
    uint8_t* m_Bitmap;
    size_t   m_BitmapSize;
    size_t   m_TotalPages;

    void SetBit(size_t page_index) {
        m_Bitmap[page_index / 8] |= (1 << (page_index % 8));
    }

    void ClearBit(size_t page_index) {
        m_Bitmap[page_index / 8] &= ~(1 << (page_index % 8));
    }

    bool TestBit(size_t page_index) {
        return (m_Bitmap[page_index / 8] & (1 << (page_index % 8))) != 0;
    }

public:
    void Initialize(void* mem_map, uint64_t map_size, uint64_t desc_size) {
        size_t descriptor_count = (size_t)(map_size / desc_size);
        uint64_t highest_address = 0;

        // Pass 1: Find the absolute highest physical memory address available
        for (size_t i = 0; i < descriptor_count; i++) {
            KERNEL_EFI_MEMORY_DESCRIPTOR* desc = (KERNEL_EFI_MEMORY_DESCRIPTOR*)((uint8_t*)mem_map + (i * desc_size));
            uint64_t end_address = desc->PhysicalStart + (desc->NumberOfPages * PAGE_SIZE);
            if (end_address > highest_address) {
                highest_address = end_address;
            }
        }

        m_TotalPages = (size_t)(highest_address / PAGE_SIZE);
        m_BitmapSize = m_TotalPages / 8;

        // Pass 2: Find a free block of RAM large enough to hold our bitmap array
        uint64_t bitmap_physical_start = 0;
        for (size_t i = 0; i < descriptor_count; i++) {
            KERNEL_EFI_MEMORY_DESCRIPTOR* desc = (KERNEL_EFI_MEMORY_DESCRIPTOR*)((uint8_t*)mem_map + (i * desc_size));

            // Type 7 = Conventional Memory (Safe free RAM)
            if (desc->Type == 7 && (desc->NumberOfPages * PAGE_SIZE) >= m_BitmapSize) {
                bitmap_physical_start = desc->PhysicalStart;
                break;
            }
        }

        m_Bitmap = (uint8_t*)bitmap_physical_start;

        // Initialize entire bitmap to 1s (Lock everything by default)
        for (size_t i = 0; i < m_BitmapSize; i++) {
            m_Bitmap[i] = 0xFF;
        }

        // Pass 3: Clear bits only for blocks confirmed as safe conventional memory
        for (size_t i = 0; i < descriptor_count; i++) {
            KERNEL_EFI_MEMORY_DESCRIPTOR* desc = (KERNEL_EFI_MEMORY_DESCRIPTOR*)((uint8_t*)mem_map + (i * desc_size));

            if (desc->Type == 7) {
                size_t start_page = (size_t)(desc->PhysicalStart / PAGE_SIZE);
                for (size_t page = 0; page < desc->NumberOfPages; page++) {
                    ClearBit(start_page + page);
                }
            }
        }

        // Pass 4: Protect the pages our own bitmap array is sitting on!
        size_t bitmap_start_page = (size_t)(bitmap_physical_start / PAGE_SIZE);
        size_t bitmap_pages_needed = (m_BitmapSize + PAGE_SIZE - 1) / PAGE_SIZE;
        for (size_t i = 0; i < bitmap_pages_needed; i++) {
            SetBit(bitmap_start_page + i);
        }
    }

    void* AllocatePage() {
        for (size_t i = 0; i < m_TotalPages; i++) {
            if (!TestBit(i)) {
                SetBit(i);
                return (void*)(i * PAGE_SIZE);
            }
        }
        return nullptr;
    }

    void FreePage(void* physical_address) {
        size_t page_index = (size_t)physical_address / PAGE_SIZE;
        if (page_index < m_TotalPages) {
            ClearBit(page_index);
        }
    }
};