#pragma once
#include <stdint.h>

#pragma pack(push, 1)
struct IDTEntry {
    uint16_t OffsetLow;
    uint16_t Selector;
    uint8_t  IST;
    uint8_t  Attributes;
    uint16_t OffsetMid;
    uint32_t OffsetHigh;
    uint32_t Reserved;
};

struct IDTDescriptor {
    uint16_t Limit;
    uint64_t Address;
};
#pragma pack(pop)

void InitIDT();
extern "C" void LoadIDT(IDTDescriptor* idtDescriptor);