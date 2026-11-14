// my own implementation attempt of WinOS.
// Welcome to WinOS (not WinOS-Dev)!
#include <Guid/FileInfo.h>
#include <IndustryStandard/PeImage.h>
#include <Library/UefiBootServicesTableLib.h>
#include <Library/UefiLib.h>
#include <Protocol/GraphicsOutput.h>
#include <Protocol/LoadedImage.h>
#include <Protocol/SimpleFileSystem.h>
#include <intrin.h>

#include "Shared.h"
#include <Base.h>
#include <Library/BaseLib.h>
#include <ProcessorBind.h>
#include <Uefi/UefiBaseType.h>
#include <Uefi/UefiMultiPhase.h>
#include <Uefi/UefiSpec.h>
#include <stdint.h>

extern CONST UINT32 _gUefiDriverRevision = 0;
CHAR8* gEfiCallerBaseName = "WinOS";

BOOT_CONFIG BootConfig;

static BOOLEAN EndsWithIgnoreCase(const char* value, const char* suffix) {
	if (value == NULL || suffix == NULL) {
		return FALSE;
	}

	UINTN valueLength = AsciiStrLen(value);
	UINTN suffixLength = AsciiStrLen(suffix);
	if (suffixLength > valueLength) {
		return FALSE;
	}

	return AsciiStriCmp(value + (valueLength - suffixLength), suffix) == 0;
}

static UINT32 InferAssetTypeFromPath(const char* path) {
	if (path == NULL) {
		return AssetTypeUnknown;
	}

	if (EndsWithIgnoreCase(path, ".bmp")) return AssetTypeBitmap;
	if (EndsWithIgnoreCase(path, ".gif")) return AssetTypeGif;
	if (EndsWithIgnoreCase(path, ".wdexe")) return AssetTypeAppPackage;
	if (EndsWithIgnoreCase(path, ".psf")) return AssetTypePsfFont;
	return AssetTypeUnknown;
}

static UINT32 DetectAssetType(const void* buffer, UINTN size) {
	if (buffer == NULL || size < 4) {
		return AssetTypeUnknown;
	}

	const UINT8* bytes = (const UINT8*)buffer;
	if (size >= 2 && bytes[0] == 'B' && bytes[1] == 'M') return AssetTypeBitmap;
	if (size >= 4 && bytes[0] == 'W' && bytes[1] == 'D' && bytes[2] == 'X' && bytes[3] == 'E') return AssetTypeAppPackage;
	if (size >= 3 && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F') return AssetTypeGif;
	if (size >= sizeof(PSF1_HEADER)) {
		const PSF1_HEADER* font = (const PSF1_HEADER*)buffer;
		if (font->Magic[0] == 0x36 && font->Magic[1] == 0x04) {
			return AssetTypePsfFont;
		}
	}

	return AssetTypeUnknown;
}

/// <summary>
/// Get a file from the loaded file system.
/// </summary>
/// <param name="Directory">Pointer to the root dir (normally)</param>
/// <param name="Path">File path</param>
/// <param name="LoadedSize">Size of obtained file (use & to write to variable, eg. &LoadedSize)</param>
/// <returns>The file location where it is loaded to RAM</returns>
static void* LoadFileIntoRAM(EFI_FILE* Directory, CHAR16* Path, UINTN* LoadedSize)
{
	EFI_STATUS Status;
	EFI_FILE* LoadedFile;

	//
	// Open file
	//
	Status = Directory->Open(
		Directory,
		&LoadedFile,
		Path,
		EFI_FILE_MODE_READ,
		0
	);

	if (EFI_ERROR(Status))
	{
		Print(L"[X] WinOS-EFI/main.c/LoadFileIntoRAM()> Failed to open file\n");
		Print(L"    Path: %s\n", Path);
		Print(L"    Status: %r\n", Status);
		return NULL;
	}

	Print(L"[i] WinOS-EFI/main.c/LoadFileIntoRAM()> Opened file\n");
	Print(L"    Path: %s\n", Path);

	//
	// Allocate buffer for EFI_FILE_INFO
	//
	UINTN FileInfoSize = sizeof(EFI_FILE_INFO) + 1024;
	EFI_FILE_INFO* FileInfo;

	Status = gBS->AllocatePool(
		EfiLoaderData,
		FileInfoSize,
		(void**)&FileInfo
	);

	if (EFI_ERROR(Status))
	{
		Print(L"[X] WinOS-EFI/main.c/LoadFileIntoRAM()> Failed to allocate file info buffer\n");
		Print(L"    Path: %s\n", Path);
		Print(L"    Requested size: %u bytes\n", FileInfoSize);
		Print(L"    Status: %r\n", Status);

		Status = LoadedFile->Close(LoadedFile);

		if (EFI_ERROR(Status))
		{
			Print(L"[!] WinOS-EFI/main.c/LoadFileIntoRAM()> Failed to close file after error\n");
			Print(L"    Status: %r\n", Status);
		}

		return NULL;
	}

	//
	// Get file information
	//
	Status = LoadedFile->GetInfo(
		LoadedFile,
		&gEfiFileInfoGuid,
		&FileInfoSize,
		(void**)FileInfo
	);

	if (EFI_ERROR(Status))
	{
		Print(L"[X] WinOS-EFI/main.c/LoadFileIntoRAM()> Failed to get file information\n");
		Print(L"    Path: %s\n", Path);
		Print(L"    File info buffer size: %u bytes\n", FileInfoSize);
		Print(L"    Status: %r\n", Status);

		Status = LoadedFile->Close(LoadedFile);

		if (EFI_ERROR(Status))
		{
			Print(L"[!] WinOS-EFI/main.c/LoadFileIntoRAM()> Failed to close file after error\n");
			Print(L"    Status: %r\n", Status);
		}

		Status = gBS->FreePool(FileInfo);

		if (EFI_ERROR(Status))
		{
			Print(L"[!] WinOS-EFI/main.c/LoadFileIntoRAM()> Failed to free file info buffer\n");
			Print(L"    Status: %r\n", Status);
		}

		return NULL;
	}

	UINTN FileSize = FileInfo->FileSize;

	//
	// FileInfo is no longer needed.
	//
	Status = gBS->FreePool(FileInfo);

	if (EFI_ERROR(Status))
	{
		Print(L"[!] WinOS-EFI/main.c/LoadFileIntoRAM()> Failed to free file info buffer\n");
		Print(L"    Path: %s\n", Path);
		Print(L"    Status: %r\n", Status);

		Status = LoadedFile->Close(LoadedFile);

		if (EFI_ERROR(Status))
		{
			Print(L"[!] WinOS-EFI/main.c/LoadFileIntoRAM()> Failed to close file\n");
			Print(L"    Status: %r\n", Status);
		}

		return NULL;
	}

	Print(L"[i] WinOS-EFI/main.c/LoadFileIntoRAM()> File information retrieved\n");
	Print(L"    Path: %s\n", Path);
	Print(L"    File size: %u bytes\n", FileSize);

	//
	// Empty files are not useful to the caller.
	//
	if (FileSize == 0)
	{
		Print(L"[!] WinOS-EFI/main.c/LoadFileIntoRAM()> File is empty\n");
		Print(L"    Path: %s\n", Path);

		Status = LoadedFile->Close(LoadedFile);

		if (EFI_ERROR(Status))
		{
			Print(L"[!] WinOS-EFI/main.c/LoadFileIntoRAM()> Failed to close empty file\n");
			Print(L"    Status: %r\n", Status);
		}

		return NULL;
	}

	//
	// Calculate required number of pages.
	//
	UINTN NumPages = (FileSize + 4095) / 4096;

	Print(L"[i] WinOS-EFI/main.c/LoadFileIntoRAM()> Calculated memory requirements\n");
	Print(L"    File size: %u bytes\n", FileSize);
	Print(L"    Page size: 4096 bytes\n");
	Print(L"    Pages required: %u\n", NumPages);

	//
	// Allocate pages for file contents.
	//
	EFI_PHYSICAL_ADDRESS FileBufferAddr;

	Status = gBS->AllocatePages(
		AllocateAnyPages,
		EfiLoaderData,
		NumPages,
		&FileBufferAddr
	);

	if (EFI_ERROR(Status))
	{
		Print(L"[X] WinOS-EFI/main.c/LoadFileIntoRAM()> Failed to allocate file pages\n");
		Print(L"    Path: %s\n", Path);
		Print(L"    File size: %u bytes\n", FileSize);
		Print(L"    Pages requested: %u\n", NumPages);
		Print(L"    Status: %r\n", Status);

		Status = LoadedFile->Close(LoadedFile);

		if (EFI_ERROR(Status))
		{
			Print(L"[!] LoadFileIntoRAM: Failed to close file after error\n");
			Print(L"    Status: %r\n", Status);
		}

		return NULL;
	}

	void* FileBuffer = (void*)FileBufferAddr;

	Print(L"[i] WinOS-EFI/main.c/LoadFileIntoRAM()> Allocated file buffer\n");
	Print(L"    Address: %p\n", FileBuffer);
	Print(L"    Pages: %u\n", NumPages);
	Print(L"    Capacity: %u bytes\n", NumPages * 4096);

	//
	// Preserve the requested size because Read() modifies FileSize.
	//
	UINTN ExpectedFileSize = FileSize;

	//
	// Read file into allocated memory.
	//
	Status = LoadedFile->Read(
		LoadedFile,
		&FileSize,
		FileBuffer
	);

	if (EFI_ERROR(Status))
	{
		Print(L"[X] WinOS-EFI/main.c/LoadFileIntoRAM()> Failed to read file\n");
		Print(L"    Path: %s\n", Path);
		Print(L"    Buffer: %p\n", FileBuffer);
		Print(L"    Expected size: %u bytes\n", ExpectedFileSize);
		Print(L"    Bytes read: %u bytes\n", FileSize);
		Print(L"    Pages allocated: %u\n", NumPages);
		Print(L"    Status: %r\n", Status);

		Status = LoadedFile->Close(LoadedFile);

		if (EFI_ERROR(Status))
		{
			Print(L"[!] WinOS-EFI/main.c/LoadFileIntoRAM()> Failed to close file after read error\n");
			Print(L"    Status: %r\n", Status);
		}

		//
		// IMPORTANT:
		// FileBuffer was allocated with AllocatePages().
		// This allocation must be released here on failure.
		//
		Status = gBS->FreePages(FileBufferAddr, NumPages);

		if (EFI_ERROR(Status))
		{
			Print(L"[!] WinOS-EFI/main.c/LoadFileIntoRAM()> Failed to free file buffer\n");
			Print(L"    Address: %p\n", FileBuffer);
			Print(L"    Pages: %u\n", NumPages);
			Print(L"    Status: %r\n", Status);
		}

		return NULL;
	}

	//
	// File is no longer needed.
	//
	Status = LoadedFile->Close(LoadedFile);

	if (EFI_ERROR(Status))
	{
		Print(L"[!] WinOS-EFI/main.c/LoadFileIntoRAM()> File loaded but Close() failed\n");
		Print(L"    Path: %s\n", Path);
		Print(L"    Status: %r\n", Status);

		//
		// The file contents were successfully loaded, so the buffer
		// still belongs to the caller.
		//
	}

	//
	// Report actual loaded size to caller.
	//
	if (LoadedSize != NULL)
	{
		*LoadedSize = FileSize;
	}

	Print(L"[i] WinOS-EFI/main.c/LoadFileIntoRAM()> File loaded successfully\n");
	Print(L"    Path: %s\n", Path);
	Print(L"    Buffer: %p\n", FileBuffer);
	Print(L"    Expected size: %u bytes\n", ExpectedFileSize);
	Print(L"    Loaded size: %u bytes\n", FileSize);
	Print(L"    Pages: %u\n", NumPages);

	if (LoadedSize != NULL)
	{
		Print(L"    LoadedSize output: %u\n", *LoadedSize);
	}
	else
	{
		Print(L"    LoadedSize output: NULL\n");
	}

	return FileBuffer;
}

/// <summary>
/// Browses index.wfs to check for files and stuff. Derived straight from WinOS-Dev, and may be deprecated soon.
/// </summary>
/// <param name="IndexRaw">Raw Index</param>
/// <param name="Table">Asset table</param>
/// <param name="RootDir">Root directory</param>
static void CheckIndexFile(void* IndexRaw, ASSET_TABLE* Table, EFI_FILE* RootDir)
{
	if (IndexRaw != NULL) {
		char* IndexPtr = (char*)IndexRaw;
		while (*IndexPtr != '\0' && Table->Count < 50) {
			while (*IndexPtr == '\r' || *IndexPtr == '\n' || *IndexPtr == ' ') IndexPtr++;
			if (*IndexPtr == '\0') break;

			char* NameStart = IndexPtr;
			while (*IndexPtr != ':' && *IndexPtr != '\0') IndexPtr++;
			if (*IndexPtr == '\0') break;
			*IndexPtr = '\0';
			IndexPtr++;

			char* PathStart = IndexPtr;
			while (*IndexPtr != '\r' && *IndexPtr != '\n' && *IndexPtr != '\0') IndexPtr++;
			if (*IndexPtr != '\0') { *IndexPtr = '\0'; IndexPtr++; }

			CHAR16 UPath[128];
			UPath[0] = L'\\';
			int uIdx = 1, pIdx = 0;
			while (PathStart[pIdx] == ' ' || PathStart[pIdx] == '\\') pIdx++;
			while (PathStart[pIdx] != '\0' && uIdx < 127) {
				UPath[uIdx++] = (CHAR16)PathStart[pIdx++];
			}
			UPath[uIdx] = L'\0';

			UINTN AssetSize = 0;
			void* AssetAddr = LoadFileIntoRAM(RootDir, UPath, &AssetSize);
			if (AssetAddr) {
				UINT32 expectedType = InferAssetTypeFromPath(PathStart);
				UINT32 actualType = DetectAssetType(AssetAddr, AssetSize);
				if (expectedType != AssetTypeUnknown &&
					actualType != AssetTypeUnknown &&
					expectedType != actualType) {
					Print(L"[!] WinOS-EFI/main.c/CheckIndexFile()> Skipping mismatched asset type: %a\n", NameStart);
					continue;
				}

				AsciiStrCpyS(Table->Entries[Table->Count].Name, 32, NameStart);
				Table->Entries[Table->Count].Address = AssetAddr;
				Table->Entries[Table->Count].Size = (UINT32)AssetSize;
				Table->Entries[Table->Count].Type = actualType;
				Table->Count++;
			}
		}
	}

}

/// <summary>
/// Allocates a back buffer for smooth graphics. Idk if we need it after we have a graphics driver loaded.
/// </summary>
/// <returns>{boot configuration variable}.BackBuffer or smth lol</returns>
static uint32_t* AllocateBackBuffer() {
	Print(L"[i] WinOS-EFI/main.c/AllocateBackBuffer()> Allocating a back buffer...\n");

	UINTN FBSize = BootConfig.Width * BootConfig.Height * 4;
	Print(L"[i] WinOS-EFI/main.c/AllocateBackBuffer()> Frame Buffer size = %u\n", FBSize);

	UINTN FBPages = (FBSize + 4095) / 4096;
	Print(L"[i] WinOS-EFI/main.c/AllocateBackBuffer()> Frame Buffer pages = %u\n", FBPages);

	EFI_PHYSICAL_ADDRESS BBAddr;
	if (EFI_ERROR(gBS->AllocatePages(AllocateAnyPages, EfiLoaderData, FBPages, &BBAddr)))
	{
		Print(L"[X] WinOS-EFI/main.c/AllocateBackBuffer()> Back Buffer allocation failed!\n");
		return NULL;
	}
	else
	{
		Print(L"[i] WinOS-EFI/main.c/AllocateBackBuffer()> Back Buffer allocation success!\n");
		return (uint32_t*)BBAddr;
	}
}

/// <summary>
/// Build boot configurations (init).
/// </summary>
void BuildBootConfig(ASSET_TABLE* Table, EFI_GRAPHICS_OUTPUT_PROTOCOL* GOP) {
	Print(L"[i] WinOS-EFI/main.c/BuildBootConfig()> Building BootConfig...\n");

	BootConfig.Assets = Table;
	BootConfig.BaseAddress = (uint32_t*)GOP->Mode->FrameBufferBase;
	BootConfig.Width = GOP->Mode->Info->HorizontalResolution;
	BootConfig.Height = GOP->Mode->Info->VerticalResolution;
	BootConfig.PixelsPerScanLine = GOP->Mode->Info->PixelsPerScanLine;
	BootConfig.BackBuffer = AllocateBackBuffer();

	Print(L"[i] WinOS-EFI/main.c/BuildBootConfig()> Successfully built BootConfig :D\n");
}

EFI_STATUS EFIAPI UefiMain(IN EFI_HANDLE ImageHandle, IN EFI_SYSTEM_TABLE* SystemTable)
{
	EFI_STATUS Status;

	gST->ConOut->ClearScreen(gST->ConOut);

	Print(L"==> WinOS UEFI Loader v1.4 Patch 1 (Debug Edition) <==\n");

	//
	// ------------------------------------------------------------
	// 1. Locate Graphics Output Protocol
	// ------------------------------------------------------------
	//

	EFI_GRAPHICS_OUTPUT_PROTOCOL* GOP = NULL;

	Status = gBS->LocateProtocol(
		&gEfiGraphicsOutputProtocolGuid,
		NULL,
		(void**)&GOP
	);

	if (EFI_ERROR(Status))
	{
		Print(L"[X] WinOS-EFI/main.c/UefiMain()> Failed to locate Graphics Output Protocol\n");
		Print(L"    Status: %r\n", Status);

		while (TRUE)
			__halt();
	}

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> GOP located\n");
	Print(L"    GOP: %p\n", GOP);
	Print(L"    FrameBuffer: %p\n",
		(void*)GOP->Mode->FrameBufferBase);
	Print(L"    Resolution: %u x %u\n",
		GOP->Mode->Info->HorizontalResolution,
		GOP->Mode->Info->VerticalResolution);
	Print(L"    PixelsPerScanLine: %u\n",
		GOP->Mode->Info->PixelsPerScanLine);


	//
	// ------------------------------------------------------------
	// 2. Get Loaded Image Protocol
	// ------------------------------------------------------------
	//

	EFI_LOADED_IMAGE_PROTOCOL* LoadedImage = NULL;

	Status = gBS->HandleProtocol(
		ImageHandle,
		&gEfiLoadedImageProtocolGuid,
		(void**)&LoadedImage
	);

	if (EFI_ERROR(Status))
	{
		Print(L"[X] WinOS-EFI/main.c/UefiMain()> Failed to get Loaded Image Protocol\n");
		Print(L"    ImageHandle: %p\n", ImageHandle);
		Print(L"    Status: %r\n", Status);

		while (TRUE)
			__halt();
	}

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> Loaded Image Protocol located\n");
	Print(L"    LoadedImage: %p\n", LoadedImage);
	Print(L"    DeviceHandle: %p\n", LoadedImage->DeviceHandle);


	//
	// ------------------------------------------------------------
	// 3. Get Simple File System Protocol
	// ------------------------------------------------------------
	//

	EFI_SIMPLE_FILE_SYSTEM_PROTOCOL* FileSystem = NULL;

	Status = gBS->HandleProtocol(
		LoadedImage->DeviceHandle,
		&gEfiSimpleFileSystemProtocolGuid,
		(void**)&FileSystem
	);

	if (EFI_ERROR(Status))
	{
		Print(L"[X] WinOS-EFI/main.c/UefiMain()> Failed to get Simple File System Protocol\n");
		Print(L"    DeviceHandle: %p\n", LoadedImage->DeviceHandle);
		Print(L"    Status: %r\n", Status);

		while (TRUE)
			__halt();
	}

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> Simple File System Protocol located\n");
	Print(L"    FileSystem: %p\n", FileSystem);


	//
	// ------------------------------------------------------------
	// 4. Open filesystem volume
	// ------------------------------------------------------------
	//

	EFI_FILE* RootDir = NULL;

	Status = FileSystem->OpenVolume(
		FileSystem,
		&RootDir
	);

	if (EFI_ERROR(Status))
	{
		Print(L"[X] WinOS-EFI/main.c/UefiMain()> Failed to open filesystem volume\n");
		Print(L"    FileSystem: %p\n", FileSystem);
		Print(L"    Status: %r\n", Status);

		while (TRUE)
			__halt();
	}

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> Filesystem volume opened\n");
	Print(L"    RootDir: %p\n", RootDir);


	//
	// ------------------------------------------------------------
	// 5. Reset root directory position
	// ------------------------------------------------------------
	//

	Status = RootDir->SetPosition(
		RootDir,
		0
	);

	if (EFI_ERROR(Status))
	{
		Print(L"[i] WinOS-EFI/main.c/UefiMain()> Failed to reset root directory position\n");
		Print(L"    RootDir: %p\n", RootDir);
		Print(L"    Position: 0\n");
		Print(L"    Status: %r\n", Status);

		RootDir->Close(RootDir);

		while (TRUE)
			__halt();
	}

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> Root directory position reset\n");


	//
	// ------------------------------------------------------------
	// 6. Load index.wfs
	// ------------------------------------------------------------
	//

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> Loading index file\n");
	Print(L"    Path: \\\\index.wfs\n");

	void* IndexRaw = LoadFileIntoRAM(
		RootDir,
		L"\\index.wfs",
		NULL
	);

	//
	// index.wfs is currently optional.
	// CheckIndexFile() already accepts NULL.
	//
	if (IndexRaw == NULL)
	{
		Print(L"[!] WinOS-EFI/main.c/UefiMain()> index.wfs was not loaded\n");
		Print(L"    Continuing without indexed assets\n");
	}
	else
	{
		Print(L"[i] WinOS-EFI/main.c/UefiMain()> index.wfs loaded\n");
		Print(L"    Address: %p\n", IndexRaw);
	}


	//
	// ------------------------------------------------------------
	// 7. Allocate ASSET_TABLE
	// ------------------------------------------------------------
	//

	ASSET_TABLE* Table = NULL;

	Status = gBS->AllocatePool(
		EfiLoaderData,
		sizeof(ASSET_TABLE),
		(void**)&Table
	);

	if (EFI_ERROR(Status))
	{
		Print(L"[X] WinOS-EFI/main.c/UefiMain()> Failed to allocate ASSET_TABLE\n");
		Print(L"    Requested size: %u bytes\n",
			sizeof(ASSET_TABLE));
		Print(L"    Status: %r\n", Status);

		if (IndexRaw != NULL)
		{
			//
			// IndexRaw ownership belongs to us here.
			//
			// The actual page count is not available because
			// this call requested LoadedSize == NULL.
			//
			// We therefore leave it alone for now.
		}

		RootDir->Close(RootDir);

		while (TRUE)
			__halt();
	}

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> ASSET_TABLE allocated\n");
	Print(L"    Address: %p\n", Table);
	Print(L"    Size: %u bytes\n", sizeof(ASSET_TABLE));


	//
	// ------------------------------------------------------------
	// 8. Allocate asset entries
	// ------------------------------------------------------------
	//

	UINTN MaxAssetEntries = 50;
	UINTN AssetEntriesSize =
		sizeof(ASSET_ENTRY) * MaxAssetEntries;

	Status = gBS->AllocatePool(
		EfiLoaderData,
		AssetEntriesSize,
		(void**)&Table->Entries
	);

	if (EFI_ERROR(Status))
	{
		Print(L"[X] WinOS-EFI/main.c/UefiMain()> Failed to allocate asset entries\n");
		Print(L"    Maximum entries: %u\n", MaxAssetEntries);
		Print(L"    Requested size: %u bytes\n", AssetEntriesSize);
		Print(L"    Status: %r\n", Status);

		gBS->FreePool(Table);
		RootDir->Close(RootDir);

		while (TRUE)
			__halt();
	}

	Table->Count = 0;

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> Asset entries allocated\n");
	Print(L"    Address: %p\n", Table->Entries);
	Print(L"    Maximum entries: %u\n", MaxAssetEntries);
	Print(L"    Buffer size: %u bytes\n", AssetEntriesSize);


	//
	// ------------------------------------------------------------
	// 9. Parse index
	// ------------------------------------------------------------
	//

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> Parsing asset index...\n");

	CheckIndexFile(
		IndexRaw,
		Table,
		RootDir
	);

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> Asset index processing complete\n");
	Print(L"    Asset count: %u\n", Table->Count);


	//
	// ------------------------------------------------------------
	// 10. Build BootConfig
	// ------------------------------------------------------------
	//

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> Building BootConfig...\n");

	BuildBootConfig(
		Table,
		GOP
	);

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> BootConfig ready\n");
	Print(L"    Assets: %p\n", BootConfig.Assets);
	Print(L"    FrameBuffer: %p\n", BootConfig.BaseAddress);
	Print(L"    Resolution: %u x %u\n",
		BootConfig.Width,
		BootConfig.Height);
	Print(L"    PixelsPerScanLine: %u\n",
		BootConfig.PixelsPerScanLine);
	Print(L"    BackBuffer: %p\n",
		BootConfig.BackBuffer);


	//
	// ------------------------------------------------------------
	// 11. Load font
	// ------------------------------------------------------------
	//

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> Loading font\n");
	Print(L"    Path: \\font.psf\n");

	void* FontFileBuffer = LoadFileIntoRAM(
		RootDir,
		L"font.psf",
		NULL
	);

	if (FontFileBuffer == NULL)
	{
		Print(L"[i] WinOS-EFI/main.c/UefiMain()> Failed to load font\n");
		Print(L"    Path: \\font.psf\n");

		RootDir->Close(RootDir);

		return EFI_NOT_FOUND;
	}

	BootConfig.Font = (PSF1_HEADER*)FontFileBuffer;

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> Font loaded\n");
	Print(L"    Address: %p\n", FontFileBuffer);
	Print(L"    BootConfig.Font: %p\n",
		BootConfig.Font);


	//
	// ------------------------------------------------------------
	// 12. Load kernel
	// ------------------------------------------------------------
	//

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> Loading kernel\n");
	Print(L"    Path: \\KERNEL\n");

	UINTN KernelFileSize = 0;

	void* KernelRaw = LoadFileIntoRAM(
		RootDir,
		L"\\KERNEL",
		&KernelFileSize
	);

	if (KernelRaw == NULL)
	{
		Print(L"[X] WinOS-EFI/main.c/UefiMain()> Failed to load kernel\n");
		Print(L"    Path: \\KERNEL\n");

		RootDir->Close(RootDir);

		while (TRUE)
			__halt();
	}

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> Kernel loaded\n");
	Print(L"    Address: %p\n", KernelRaw);
	Print(L"    Size: %u bytes\n", KernelFileSize);


	//
	// ------------------------------------------------------------
	// 13. Validate DOS header
	// ------------------------------------------------------------
	//

	if (KernelFileSize < sizeof(EFI_IMAGE_DOS_HEADER))
	{
		Print(L"[X] WinOS-EFI/main.c/UefiMain()> Kernel is too small for a DOS header\n");
		Print(L"    Kernel size: %u bytes\n", KernelFileSize);
		Print(L"    Required: %u bytes\n",
			sizeof(EFI_IMAGE_DOS_HEADER));

		while (TRUE)
			__halt();
	}

	EFI_IMAGE_DOS_HEADER* DOSHeader =
		(EFI_IMAGE_DOS_HEADER*)KernelRaw;

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> Checking DOS header\n");
	Print(L"    e_magic: 0x%x\n", DOSHeader->e_magic);
	Print(L"    e_lfanew: %u\n", DOSHeader->e_lfanew);

	if (DOSHeader->e_magic != EFI_IMAGE_DOS_SIGNATURE)
	{
		Print(L"[X] WinOS-EFI/main.c/UefiMain()> Invalid DOS header signature\n");
		Print(L"    Expected: 0x%x\n",
			EFI_IMAGE_DOS_SIGNATURE);
		Print(L"    Received: 0x%x\n",
			DOSHeader->e_magic);

		while (TRUE)
			__halt();
	}


	//
	// ------------------------------------------------------------
	// 14. Validate NT header location
	// ------------------------------------------------------------
	//

	if (DOSHeader->e_lfanew < 0)
	{
		Print(L"[X] WinOS-EFI/main.c/UefiMain()> Invalid negative e_lfanew\n");
		Print(L"    e_lfanew: %d\n", DOSHeader->e_lfanew);

		while (TRUE)
			__halt();
	}

	UINTN NTHeaderOffset =
		(UINTN)DOSHeader->e_lfanew;

	if (NTHeaderOffset >
		KernelFileSize - sizeof(EFI_IMAGE_NT_HEADERS64))
	{
		Print(L"[X] WinOS-EFI/main.c/UefiMain()> NT header exceeds kernel file bounds\n");
		Print(L"    Kernel size: %u bytes\n", KernelFileSize);
		Print(L"    NT header offset: %u\n", NTHeaderOffset);
		Print(L"    NT header size: %u bytes\n",
			sizeof(EFI_IMAGE_NT_HEADERS64));

		while (TRUE)
			__halt();
	}

	EFI_IMAGE_NT_HEADERS64* NTHeaders =
		(EFI_IMAGE_NT_HEADERS64*)
		((UINT8*)KernelRaw + NTHeaderOffset);

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> NT header located\n");
	Print(L"    Address: %p\n", NTHeaders);
	Print(L"    Offset: %u\n", NTHeaderOffset);
	Print(L"    Signature: 0x%x\n", NTHeaders->Signature);


	//
	// ------------------------------------------------------------
	// 15. Validate NT signature
	// ------------------------------------------------------------
	//

	if (NTHeaders->Signature != EFI_IMAGE_NT_SIGNATURE)
	{
		Print(L"[X] WinOS-EFI/main.c/UefiMain()> Invalid NT header signature\n");
		Print(L"    Expected: 0x%x\n",
			EFI_IMAGE_NT_SIGNATURE);
		Print(L"    Received: 0x%x\n",
			NTHeaders->Signature);

		while (TRUE)
			__halt();
	}

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> PE headers look valid\n");


	//
	// ------------------------------------------------------------
	// 16. Calculate kernel memory requirements
	// ------------------------------------------------------------
	//

	EFI_PHYSICAL_ADDRESS KernelBase =
		NTHeaders->OptionalHeader.ImageBase;

	UINTN KernelImageSize =
		NTHeaders->OptionalHeader.SizeOfImage;

	UINTN KernelPages =
		(KernelImageSize + 4095) / 4096;

	UINTN NumberOfSections =
		NTHeaders->FileHeader.NumberOfSections;

	UINTN EntryPointOffset =
		NTHeaders->OptionalHeader.AddressOfEntryPoint;

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> Kernel memory requirements\n");
	Print(L"    Preferred ImageBase: %p\n",
		(void*)KernelBase);
	Print(L"    SizeOfImage: %u bytes\n",
		KernelImageSize);
	Print(L"    Pages required: %u\n",
		KernelPages);
	Print(L"    Sections: %u\n",
		NumberOfSections);
	Print(L"    EntryPoint RVA: 0x%x\n",
		EntryPointOffset);


	//
	// ------------------------------------------------------------
	// 17. Allocate kernel at preferred address
	// ------------------------------------------------------------
	//

	Status = gBS->AllocatePages(
		AllocateAddress,
		EfiLoaderCode,
		KernelPages,
		&KernelBase
	);

	if (EFI_ERROR(Status))
	{
		Print(L"[!] WinOS-EFI/main.c/UefiMain()> Preferred kernel address unavailable\n");
		Print(L"    Preferred address: %p\n",
			(void*)NTHeaders->OptionalHeader.ImageBase);
		Print(L"    Pages requested: %u\n", KernelPages);
		Print(L"    Status: %r\n", Status);

		//
		// Retry anywhere.
		//
		KernelBase =
			NTHeaders->OptionalHeader.ImageBase;

		Status = gBS->AllocatePages(
			AllocateAnyPages,
			EfiLoaderCode,
			KernelPages,
			&KernelBase
		);

		if (EFI_ERROR(Status))
		{
			Print(L"[X] WinOS-EFI/main.c/UefiMain()> Failed to allocate kernel memory\n");
			Print(L"    Pages requested: %u\n", KernelPages);
			Print(L"    Status: %r\n", Status);

			while (TRUE)
				__halt();
		}

		Print(L"[i] WinOS-EFI/main.c/UefiMain()> Kernel allocated at fallback address\n");
	}
	else
	{
		Print(L"[i] WinOS-EFI/main.c/UefiMain()> Kernel allocated at preferred address\n");
	}

	Print(L"    KernelBase: %p\n", (void*)KernelBase);
	Print(L"    Pages: %u\n", KernelPages);


	//
	// ------------------------------------------------------------
	// 18. Copy PE sections into kernel memory
	// ------------------------------------------------------------
	//

	EFI_IMAGE_SECTION_HEADER* Section =
		(EFI_IMAGE_SECTION_HEADER*)
		((UINT8*)NTHeaders +
			sizeof(EFI_IMAGE_NT_HEADERS64));

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> Copying kernel sections\n");

	for (UINTN i = 0; i < NumberOfSections; i++)
	{
		Print(L"    Section %u\n", i);
		Print(L"        VirtualAddress: 0x%x\n",
			Section[i].VirtualAddress);
		Print(L"        RawOffset: 0x%x\n",
			Section[i].PointerToRawData);
		Print(L"        RawSize: %u bytes\n",
			Section[i].SizeOfRawData);

		gBS->CopyMem(
			(UINT8*)KernelBase +
			Section[i].VirtualAddress,

			(UINT8*)KernelRaw +
			Section[i].PointerToRawData,

			Section[i].SizeOfRawData
		);
	}

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> Kernel sections copied\n");


	//
	// ------------------------------------------------------------
	// 19. Calculate kernel entry point
	// ------------------------------------------------------------
	//

	typedef void (*KERNEL_ENTRY)(BOOT_CONFIG*);

	KERNEL_ENTRY KernelStart =
		(KERNEL_ENTRY)
		(KernelBase +
			NTHeaders->OptionalHeader.AddressOfEntryPoint);

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> Kernel entry point prepared\n");
	Print(L"    KernelBase: %p\n", (void*)KernelBase);
	Print(L"    EntryPoint RVA: 0x%x\n",
		NTHeaders->OptionalHeader.AddressOfEntryPoint);
	Print(L"    KernelStart: %p\n",
		(void*)KernelStart);


	//
	// ------------------------------------------------------------
	// 20. Get memory map size
	// ------------------------------------------------------------
	//

	UINTN MMapSize = 0;
	UINTN MMapKey = 0;
	UINTN DSize = 0;
	UINT32 DVer = 0;
	EFI_MEMORY_DESCRIPTOR* MMap = NULL;

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> Querying memory map size\n");

	Status = gBS->GetMemoryMap(
		&MMapSize,
		NULL,
		&MMapKey,
		&DSize,
		&DVer
	);

	//
	// EFI_BUFFER_TOO_SMALL is expected here.
	//
	if (Status != EFI_BUFFER_TOO_SMALL)
	{
		Print(L"[X] WinOS-EFI/main.c/UefiMain()> Initial GetMemoryMap() failed unexpectedly\n");
		Print(L"    Status: %r\n", Status);
		Print(L"    Reported size: %u\n", MMapSize);
		Print(L"    Descriptor size: %u\n", DSize);

		while (TRUE)
			__halt();
	}

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> Memory map size obtained\n");
	Print(L"    Required size: %u bytes\n", MMapSize);
	Print(L"    Descriptor size: %u bytes\n", DSize);
	Print(L"    Descriptor version: 0x%x\n", DVer);


	//
	// ------------------------------------------------------------
	// 21. Add safety space and allocate memory map
	// ------------------------------------------------------------
	//

	UINTN MMapSafetyBuffer = 4096;

	MMapSize += MMapSafetyBuffer;

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> Allocating memory map buffer\n");
	Print(L"    Buffer size: %u bytes\n", MMapSize);
	Print(L"    Safety buffer: %u bytes\n",
		MMapSafetyBuffer);

	Status = gBS->AllocatePool(
		EfiLoaderData,
		MMapSize,
		(void**)&MMap
	);

	if (EFI_ERROR(Status))
	{
		Print(L"[X] WinOS-EFI/main.c/UefiMain()> Failed to allocate memory map buffer\n");
		Print(L"    Requested size: %u bytes\n", MMapSize);
		Print(L"    Status: %r\n", Status);

		while (TRUE)
			__halt();
	}

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> Memory map buffer allocated\n");
	Print(L"    Address: %p\n", MMap);
	Print(L"    Capacity: %u bytes\n", MMapSize);


	//
	// ------------------------------------------------------------
	// 22. Get actual memory map
	// ------------------------------------------------------------
	//

	Status = gBS->GetMemoryMap(
		&MMapSize,
		MMap,
		&MMapKey,
		&DSize,
		&DVer
	);

	if (EFI_ERROR(Status))
	{
		Print(L"[X] WinOS-EFI/main.c/UefiMain()> Failed to retrieve memory map\n");
		Print(L"    Buffer: %p\n", MMap);
		Print(L"    Buffer capacity: %u bytes\n", MMapSize);
		Print(L"    Status: %r\n", Status);

		gBS->FreePool(MMap);

		while (TRUE)
			__halt();
	}

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> Memory map retrieved\n");
	Print(L"    Address: %p\n", MMap);
	Print(L"    Used size: %u bytes\n", MMapSize);
	Print(L"    Descriptor size: %u bytes\n", DSize);
	Print(L"    Descriptor version: 0x%x\n", DVer);
	Print(L"    Map key: %u\n", MMapKey);


	//
	// ------------------------------------------------------------
	// 23. Store memory map in BootConfig
	// ------------------------------------------------------------
	//

	BootConfig.MemMap = (void*)MMap;
	BootConfig.MemMapSize = (uint64_t)MMapSize;
	BootConfig.MemMapDescriptorSize = (uint64_t)DSize;

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> BootConfig memory map configured\n");
	Print(L"    MemMap: %p\n", BootConfig.MemMap);
	Print(L"    MemMapSize: %llu\n",
		BootConfig.MemMapSize);
	Print(L"    DescriptorSize: %llu\n",
		BootConfig.MemMapDescriptorSize);


	//
// ------------------------------------------------------------
// 24. Exit UEFI Boot Services
// ------------------------------------------------------------
//

	Print(L"[i] WinOS-EFI/main.c/UefiMain()> Preparing to exit Boot Services...\n");

	//
	// IMPORTANT:
	// From the final GetMemoryMap() until ExitBootServices(),
	// avoid Boot Services calls or anything that could change
	// the memory map.
	//
	Status = gBS->ExitBootServices(
		ImageHandle,
		MMapKey
	);

	if (EFI_ERROR(Status))
	{
		//
		// ExitBootServices() failed.
		//
		// The most important case is EFI_INVALID_PARAMETER,
		// which normally means the memory-map key became stale.
		//

		Print(L"[!] WinOS-EFI/main.c/UefiMain()> First ExitBootServices() failed\n");
		Print(L"    Status: %r\n", Status);

		//
		// Refresh the memory map.
		//
		UINTN NewMMapSize = 0;

		Status = gBS->GetMemoryMap(
			&NewMMapSize,
			NULL,
			&MMapKey,
			&DSize,
			&DVer
		);

		if (Status != EFI_BUFFER_TOO_SMALL)
		{
			Print(L"[X] WinOS-EFI/main.c/UefiMain()> Failed to query refreshed memory map size\n");
			Print(L"    Status: %r\n", Status);

			while (TRUE)
				__halt();
		}

		//
		// Give the new map some extra room.
		//
		MMapSize = NewMMapSize + 4096;

		Print(L"[i] WinOS-EFI/main.c/UefiMain()> Allocating refreshed memory map buffer\n");
		Print(L"    Required size: %u bytes\n", NewMMapSize);
		Print(L"    Buffer size: %u bytes\n", MMapSize);

		//
		// We no longer need the old map buffer.
		//
		Status = gBS->FreePool(MMap);

		if (EFI_ERROR(Status))
		{
			Print(L"[X] WinOS-EFI/main.c/UefiMain()> Failed to free old memory map buffer\n");
			Print(L"    Status: %r\n", Status);

			while (TRUE)
				__halt();
		}

		//
		// Allocate the new buffer.
		//
		Status = gBS->AllocatePool(
			EfiLoaderData,
			MMapSize,
			(void**)&MMap
		);

		if (EFI_ERROR(Status))
		{
			Print(L"[X] WinOS-EFI/main.c/UefiMain()> Failed to allocate refreshed memory map buffer\n");
			Print(L"    Requested size: %u bytes\n", MMapSize);
			Print(L"    Status: %r\n", Status);

			while (TRUE)
				__halt();
		}

		//
		// Get the current memory map.
		//
		Status = gBS->GetMemoryMap(
			&MMapSize,
			MMap,
			&MMapKey,
			&DSize,
			&DVer
		);

		if (EFI_ERROR(Status))
		{
			Print(L"[X] WinOS-EFI/main.c/UefiMain()> Failed to retrieve refreshed memory map\n");
			Print(L"    Buffer: %p\n", MMap);
			Print(L"    Buffer capacity: %u bytes\n", MMapSize);
			Print(L"    Status: %r\n", Status);

			while (TRUE)
				__halt();
		}

		//
		// Update BootConfig.
		//
		BootConfig.MemMap = (void*)MMap;
		BootConfig.MemMapSize = (uint64_t)MMapSize;
		BootConfig.MemMapDescriptorSize = (uint64_t)DSize;

		//
		// IMPORTANT:
		// Do not Print() here.
		//
		// GetMemoryMap() gave us the current MapKey.
		// ExitBootServices() must happen immediately.
		//
		Status = gBS->ExitBootServices(
			ImageHandle,
			MMapKey
		);

		if (EFI_ERROR(Status))
		{
			//
			// If this happens, the map changed again.
			// We will deal with repeated retries properly later.
			//
			Print(L"[X] WinOS-EFI/main.c/UefiMain()> ExitBootServices() retry failed\n");
			Print(L"    Status: %r\n", Status);

			while (TRUE)
				__halt();
		}
	}

	//
	// Boot Services are now gone.
	// DO NOT use gBS from this point onward.
	//

	KernelStart(&BootConfig);

	while (TRUE)
		__halt();

	//
	// ------------------------------------------------------------
	// 25. Jump to kernel
	// ------------------------------------------------------------
	//

	KernelStart(&BootConfig);

	//
	// KernelStart should never return.
	//
	while (TRUE)
		__halt();

	return EFI_SUCCESS;
}

EFI_STATUS EFIAPI UefiUnload()
{
	return EFI_SUCCESS;
}
