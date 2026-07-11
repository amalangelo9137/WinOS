#include "Shared.h"
#include <intrin.h>

#include "HIDs.h"

volatile int MouseX = 0;
volatile int MouseY = 0;
volatile bool MouseLeftDown = false;
volatile bool MouseStateDirty = false;

static uint8_t MouseCycle = 0;
static int8_t MousePacket[3];
static bool ShiftDown = false;
static bool CapsLockOn = false;
static KeyboardInputEvent KeyboardQueue[32] = {};
static volatile uint32_t KeyboardReadIndex = 0;
static volatile uint32_t KeyboardWriteIndex = 0;

extern "C" BOOT_CONFIG* GlobalConfig;

static void SyncBootMouseState() {
    if (GlobalConfig == nullptr || GlobalConfig->Mouse == nullptr) return;

    GlobalConfig->Mouse->X = MouseX;
    GlobalConfig->Mouse->Y = MouseY;
    GlobalConfig->Mouse->Left = MouseLeftDown;
    GlobalConfig->Mouse->Right = false;
}

static void MouseWait(uint8_t type) {
    uint32_t timeout = 100000;
    if (type == 0) {
        while (timeout-- && ((__inbyte(0x64) & 1) == 0)) {
        }
    }
    else {
        while (timeout-- && (__inbyte(0x64) & 2)) {
        }
    }
}

static void MouseWrite(uint8_t data) {
    MouseWait(1);
    __outbyte(0x64, 0xD4);
    MouseWait(1);
    __outbyte(0x60, data);
}

static uint8_t MouseRead() {
    MouseWait(0);
    return __inbyte(0x60);
}

static void PushKeyboardEvent(uint8_t type, char character) {
    uint32_t nextWriteIndex = (KeyboardWriteIndex + 1) % 32;
    if (nextWriteIndex == KeyboardReadIndex) {
        return;
    }

    KeyboardQueue[KeyboardWriteIndex].Type = type;
    KeyboardQueue[KeyboardWriteIndex].Character = character;
    KeyboardWriteIndex = nextWriteIndex;
}

static char TranslateLetter(char lowerCase, bool shifted) {
    bool upperCase = CapsLockOn ? !shifted : shifted;
    return upperCase ? (char)(lowerCase - ('a' - 'A')) : lowerCase;
}

static char TranslateScancode(uint8_t scancode, bool shifted) {
    switch (scancode) {
    case 0x02: return shifted ? '!' : '1';
    case 0x03: return shifted ? '@' : '2';
    case 0x04: return shifted ? '#' : '3';
    case 0x05: return shifted ? '$' : '4';
    case 0x06: return shifted ? '%' : '5';
    case 0x07: return shifted ? '^' : '6';
    case 0x08: return shifted ? '&' : '7';
    case 0x09: return shifted ? '*' : '8';
    case 0x0A: return shifted ? '(' : '9';
    case 0x0B: return shifted ? ')' : '0';
    case 0x0C: return shifted ? '_' : '-';
    case 0x0D: return shifted ? '+' : '=';
    case 0x10: return TranslateLetter('q', shifted);
    case 0x11: return TranslateLetter('w', shifted);
    case 0x12: return TranslateLetter('e', shifted);
    case 0x13: return TranslateLetter('r', shifted);
    case 0x14: return TranslateLetter('t', shifted);
    case 0x15: return TranslateLetter('y', shifted);
    case 0x16: return TranslateLetter('u', shifted);
    case 0x17: return TranslateLetter('i', shifted);
    case 0x18: return TranslateLetter('o', shifted);
    case 0x19: return TranslateLetter('p', shifted);
    case 0x1A: return shifted ? '{' : '[';
    case 0x1B: return shifted ? '}' : ']';
    case 0x1E: return TranslateLetter('a', shifted);
    case 0x1F: return TranslateLetter('s', shifted);
    case 0x20: return TranslateLetter('d', shifted);
    case 0x21: return TranslateLetter('f', shifted);
    case 0x22: return TranslateLetter('g', shifted);
    case 0x23: return TranslateLetter('h', shifted);
    case 0x24: return TranslateLetter('j', shifted);
    case 0x25: return TranslateLetter('k', shifted);
    case 0x26: return TranslateLetter('l', shifted);
    case 0x27: return shifted ? ':' : ';';
    case 0x28: return shifted ? '"' : '\'';
    case 0x29: return shifted ? '~' : '`';
    case 0x2B: return shifted ? '|' : '\\';
    case 0x2C: return TranslateLetter('z', shifted);
    case 0x2D: return TranslateLetter('x', shifted);
    case 0x2E: return TranslateLetter('c', shifted);
    case 0x2F: return TranslateLetter('v', shifted);
    case 0x30: return TranslateLetter('b', shifted);
    case 0x31: return TranslateLetter('n', shifted);
    case 0x32: return TranslateLetter('m', shifted);
    case 0x33: return shifted ? '<' : ',';
    case 0x34: return shifted ? '>' : '.';
    case 0x35: return shifted ? '?' : '/';
    case 0x39: return ' ';
    default: return 0;
    }
}

extern "C" void MouseHandler() {
    const uint8_t status = __inbyte(0x64);
    if ((status & 0x01) == 0 || (status & 0x20) == 0) return;

    const uint8_t data = __inbyte(0x60);
    if (MouseCycle == 0 && (data & 0x08) == 0) return;

    MousePacket[MouseCycle++] = (int8_t)data;

    if (MouseCycle != 3) return;

    MouseCycle = 0;

    if ((MousePacket[0] & 0xC0) != 0) return;

    MouseLeftDown = (MousePacket[0] & 0x01) != 0;
    MouseX += (int)MousePacket[1];
    MouseY -= (int)MousePacket[2];

    if (GlobalConfig != nullptr) {
        if (MouseX < 0) MouseX = 0;
        if (MouseY < 0) MouseY = 0;
        if (MouseX > (int)GlobalConfig->Width - 1) MouseX = (int)GlobalConfig->Width - 1;
        if (MouseY > (int)GlobalConfig->Height - 1) MouseY = (int)GlobalConfig->Height - 1;
    }

    SyncBootMouseState();
    MouseStateDirty = true;
}

extern "C" void KeyboardHandler() {
    uint8_t scancode = __inbyte(0x60);

    if (scancode == 0x2A || scancode == 0x36) {
        ShiftDown = true;
        return;
    }

    if (scancode == 0xAA || scancode == 0xB6) {
        ShiftDown = false;
        return;
    }

    if ((scancode & 0x80) != 0) {
        return;
    }

    if (scancode == 0x3A) {
        CapsLockOn = !CapsLockOn;
        return;
    }

    if (scancode == 0x0E) {
        PushKeyboardEvent(KeyboardInputBackspace, 0);
        return;
    }

    if (scancode == 0x1C) {
        PushKeyboardEvent(KeyboardInputEnter, '\n');
        return;
    }

    char translated = TranslateScancode(scancode, ShiftDown);
    if (translated != 0) {
        PushKeyboardEvent(KeyboardInputCharacter, translated);
    }
}

extern "C" void RemapPIC() {
    __outbyte(0x20, 0x11);
    __outbyte(0xA0, 0x11);
    __outbyte(0x21, 0x20);
    __outbyte(0xA1, 0x28);
    __outbyte(0x21, 0x04);
    __outbyte(0xA1, 0x02);
    __outbyte(0x21, 0x01);
    __outbyte(0xA1, 0x01);

    __outbyte(0x21, 0xF9);
    __outbyte(0xA1, 0xEF);
}

extern "C" void InitMouse() {
    RemapPIC();
    MouseCycle = 0;

    MouseWait(1);
    __outbyte(0x64, 0xA8);

    MouseWait(1);
    __outbyte(0x64, 0x20);
    const uint8_t controllerStatus = MouseRead() | 0x02;
    MouseWait(1);
    __outbyte(0x64, 0x60);
    __outbyte(0x60, controllerStatus);

    MouseWrite(0xFF);
    MouseRead();

    MouseWrite(0xF6);
    MouseRead();

    MouseWrite(0xF3);
    MouseRead();
    MouseWrite(200);
    MouseRead();

    MouseWrite(0xF4);
    MouseRead();

    SyncBootMouseState();
    MouseStateDirty = true;
}

bool PollKeyboardInput(KeyboardInputEvent* inputEvent) {
    if (inputEvent == nullptr || KeyboardReadIndex == KeyboardWriteIndex) {
        return false;
    }

    *inputEvent = KeyboardQueue[KeyboardReadIndex];
    KeyboardReadIndex = (KeyboardReadIndex + 1) % 32;
    return true;
}
