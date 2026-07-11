#pragma once

#include "Shared.h"

enum KeyboardInputType : uint8_t {
    KeyboardInputNone = 0,
    KeyboardInputCharacter = 1,
    KeyboardInputBackspace = 2,
    KeyboardInputEnter = 3,
};

struct KeyboardInputEvent {
    uint8_t Type;
    char Character;
};

extern volatile int MouseX;
extern volatile int MouseY;
extern volatile bool MouseLeftDown;
extern volatile bool MouseStateDirty;

extern "C" void InitMouse();
bool PollKeyboardInput(KeyboardInputEvent* inputEvent);
