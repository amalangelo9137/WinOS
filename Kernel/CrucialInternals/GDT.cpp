#include <stdint.h>

// Tell Visual Studio to STOP padding these structs
#pragma pack(push, 1)

struct GDTDescriptor {
    uint16_t Size;
    uint64_t Offset;
};

struct GDTEntry {
    uint16_t Limit0;
    uint16_t Base0;
    uint8_t Base1;
    uint8_t AccessByte;
    uint8_t Limit1_Flags;
    uint8_t Base2;
};

struct GDT {
    GDTEntry Null; // 0x00
    GDTEntry Code; // 0x08
    GDTEntry Data; // 0x10
} DefaultGDT = {
    {0, 0, 0, 0x00, 0x00, 0},
    {0, 0, 0, 0x9A, 0xA0, 0}, // Code
    {0, 0, 0, 0x92, 0xA0, 0}  // Data
};

#pragma pack(pop)

extern "C" void LoadGDT(GDTDescriptor* gdtDescriptor);

extern "C" void InitGDT() {
    GDTDescriptor gdtDescriptor;
    gdtDescriptor.Size = sizeof(GDT) - 1;
    gdtDescriptor.Offset = (uint64_t)&DefaultGDT;
    LoadGDT(&gdtDescriptor);
}