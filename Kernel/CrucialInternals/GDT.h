#pragma once
#include <stdint.h>

// MSVC way to force 1-byte alignment (no padding)
#pragma pack(push, 1)

struct GDTEntry {
    uint16_t LimitLow;
    uint16_t BaseLow;
    uint8_t  BaseMiddle;
    uint8_t  Access;
    uint8_t  Flags;
    uint8_t  BaseHigh;
};

struct GDTDescriptor {
    uint16_t Size;
    uint64_t Offset;
};

struct GDTTable {
    GDTEntry Null;         // Offset 0x00
    GDTEntry KernelCode;   // Offset 0x08
    GDTEntry KernelData;   // Offset 0x10
    GDTEntry UserData;     // Offset 0x18
    GDTEntry UserCode;     // Offset 0x20
};

// Restore defa ult compiler alignment
#pragma pack(pop)

extern "C" void LoadGDT(GDTDescriptor* gdtDescriptor);
void InitGDT();