#include <stdint.h>

#pragma pack(push, 1)
struct IDTEntry {
    uint16_t Offset0;
    uint16_t Selector;
    uint8_t IST;
    uint8_t Type_Attr;
    uint16_t Offset1;
    uint32_t Offset2;
    uint32_t Ignore;
};

struct IDTR {
    uint16_t Limit;
    uint64_t Offset;
};
#pragma pack(pop)

IDTEntry idt[256];
IDTR idtr;

extern "C" void LoadIDT(IDTR* idtPtr);
extern "C" void isr33(); // Keyboard MASM stub
extern "C" void isr44(); // Mouse MASM stub

void SetIDTGate(void* handler, uint8_t entryOffset, uint8_t type_attr, uint8_t selector) {
    uint64_t offset = (uint64_t)handler;
    idt[entryOffset].Offset0 = (uint16_t)(offset & 0x0000FFFF);
    idt[entryOffset].Selector = selector;
    idt[entryOffset].IST = 0;
    idt[entryOffset].Type_Attr = type_attr;
    idt[entryOffset].Offset1 = (uint16_t)((offset & 0xFFFF0000) >> 16);
    idt[entryOffset].Offset2 = (uint32_t)((offset & 0xFFFFFFFF00000000) >> 32);
    idt[entryOffset].Ignore = 0;
}

extern "C" void InitIDT() {
    idtr.Limit = sizeof(idt) - 1;
    idtr.Offset = (uint64_t)&idt[0];

    for (int i = 0; i < 256; i++) {
        SetIDTGate((void*)0, i, 0x8E, 0x08);
    }

    // Map the hardware interrupts
    SetIDTGate((void*)isr33, 33, 0x8E, 0x08);
    SetIDTGate((void*)isr44, 44, 0x8E, 0x08);

    LoadIDT(&idtr);
}