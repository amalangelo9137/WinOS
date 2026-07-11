#pragma once
#include <stdint.h>
#include <stdbool.h>

typedef enum {
    AssetTypeUnknown = 0,
    AssetTypeBitmap = 1,
    AssetTypeGif = 2,
    AssetTypeAppPackage = 3,
    AssetTypePsfFont = 4,
} ASSET_TYPE;

// Represents a single file loaded into RAM by the EFI Index
typedef struct {
    char     Name[32];
    void* Address;
    uint32_t Size;
    uint32_t Type;
} ASSET_ENTRY;

typedef struct {
    uint32_t     Count;
    ASSET_ENTRY* Entries;
} ASSET_TABLE;

typedef struct {
    int X, Y;
    bool Left, Right;
} MOUSE_STATE;

typedef struct {
    unsigned char Magic[2];     // 0x36, 0x04
    unsigned char Mode;         // 0 = 256 chars, 1 = 512 chars
    unsigned char CharSize;     // Height of character in pixels
} PSF1_HEADER;

// The ultimate payload handed to the Kernel
typedef struct {
    uint32_t* BaseAddress;       // The actual screen pixels
    uint32_t* BackBuffer;        // The hidden drawing canvas
    uint32_t  Width;
    uint32_t  Height;
    uint32_t  PixelsPerScanLine;

    MOUSE_STATE* Mouse;
    PSF1_HEADER* Font;
    ASSET_TABLE* Assets;         // The dynamic Index Table

    // Pure pointers to avoid breaking compiler cross-compatibility
    void* MemMap;
    uint64_t  MemMapSize;
    uint64_t  MemMapDescriptorSize;
} BOOT_CONFIG;