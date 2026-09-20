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
#include <stdbool.h>
#include <stdint.h>

extern CONST UINT32 _gUefiDriverRevision = 0;
CHAR8* gEfiCallerBaseName = "WinOS";

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

// Helper to load raw bytes from USB
static void* LoadFileIntoRAM(EFI_FILE* Directory, CHAR16* Path, UINTN* LoadedSize) {
	EFI_FILE* LoadedFile;
	EFI_STATUS Status = Directory->Open(Directory, &LoadedFile, Path, EFI_FILE_MODE_READ, 0);
	if (EFI_ERROR(Status)) return NULL;

	UINTN FileInfoSize = sizeof(EFI_FILE_INFO) + 1024;
	EFI_FILE_INFO* FileInfo;
	gBS->AllocatePool(EfiLoaderData, FileInfoSize, (void**)&FileInfo);
	Status = LoadedFile->GetInfo(LoadedFile, &gEfiFileInfoGuid, &FileInfoSize, (void**)FileInfo);

	UINTN FileSize = FileInfo->FileSize;
	gBS->FreePool(FileInfo);
	if (FileSize == 0) { LoadedFile->Close(LoadedFile); return NULL; }

	UINTN NumPages = (FileSize + 4095) / 4096;
	EFI_PHYSICAL_ADDRESS FileBufferAddr;
	Status = gBS->AllocatePages(AllocateAnyPages, EfiLoaderData, NumPages, &FileBufferAddr);

	void* FileBuffer = (void*)FileBufferAddr;
	Status = LoadedFile->Read(LoadedFile, &FileSize, FileBuffer);
	LoadedFile->Close(LoadedFile);
	if (LoadedSize != NULL) {
		*LoadedSize = FileSize;
	}
	return FileBuffer;
}

EFI_STATUS EFIAPI UefiMain(IN EFI_HANDLE ImageHandle, IN EFI_SYSTEM_TABLE* SystemTable) {
	gST->ConOut->ClearScreen(gST->ConOut);
	Print(L"==> WinOS UEFI Loader v1.0 <==\n");

	// get graphics output protocol from uefi
	EFI_GRAPHICS_OUTPUT_PROTOCOL* GOP;
	gBS->LocateProtocol(&gEfiGraphicsOutputProtocolGuid, NULL, (void**)&GOP);

	// setup the filesystem to load files from
	EFI_LOADED_IMAGE_PROTOCOL* LoadedImage;
	gBS->HandleProtocol(ImageHandle, &gEfiLoadedImageProtocolGuid, (void**)&LoadedImage);
	EFI_SIMPLE_FILE_SYSTEM_PROTOCOL* FileSystem;
	gBS->HandleProtocol(LoadedImage->DeviceHandle, &gEfiSimpleFileSystemProtocolGuid, (void**)&FileSystem);
	EFI_FILE* RootDir;
	FileSystem->OpenVolume(FileSystem, &RootDir);
	RootDir->SetPosition(RootDir, 0);

	// loads everything in index.wfs (need to simplify and hardcode)
	Print(L"[i] WinOS init / File Parsing > Parsing index file (at {ESP}/index.wfs)...\n");
	void* IndexRaw = LoadFileIntoRAM(RootDir, L"\\index.wfs", NULL);
	ASSET_TABLE* Table;
	gBS->AllocatePool(EfiLoaderData, sizeof(ASSET_TABLE), (void**)&Table);
	gBS->AllocatePool(EfiLoaderData, sizeof(ASSET_ENTRY) * 50, (void**)&Table->Entries);
	Table->Count = 0;

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
					Print(L"[!] WinOS init / File Parsing > Skipping mismatched asset type: %a\n", NameStart);
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

	// make BootConfig ready for the jump
	Print(L"[i] WinOS init / Kernel Values > Building BootConfig...\n");
	BOOT_CONFIG BootConfig;
	BootConfig.Assets = Table;
	BootConfig.BaseAddress = (uint32_t*)GOP->Mode->FrameBufferBase;
	BootConfig.Width = GOP->Mode->Info->HorizontalResolution;
	BootConfig.Height = GOP->Mode->Info->VerticalResolution;
	BootConfig.PixelsPerScanLine = GOP->Mode->Info->PixelsPerScanLine;

	Print(L"[i] WinOS init / Kernel Values > Successfully built BootConfig :D\n");

	// allocate a back buffer for smoother video (until we get a graphics driver?)
	Print(L"[i] WinOS init / BackBuffering > Allocating a back buffer...\n");
	UINTN FBSize = BootConfig.Width * BootConfig.Height * 4;
	UINTN FBPages = (FBSize + 4095) / 4096;
	EFI_PHYSICAL_ADDRESS BBAddr;
	gBS->AllocatePages(AllocateAnyPages, EfiLoaderData, FBPages, &BBAddr);
	BootConfig.BackBuffer = (uint32_t*)BBAddr;

	// load basic font (need to revamp this)
	Print(L"[i] WinOS init / Kernel Loading > Loading Font (at {ESP}/font.psf)...\n");
	VOID* FontFileBuffer = LoadFileIntoRAM(RootDir, L"font.psf", NULL);
	if (FontFileBuffer == NULL) {
		Print(L"[X] WinOS init / Kernel Loading > Bro place the font at the root directory :) (at {ESP}/font.psf)\n");
		return EFI_NOT_FOUND;
	}
	BootConfig.Font = (PSF1_HEADER*)FontFileBuffer;

	// CRUCIAL : find KERNEL
	Print(L"[i] WinOS init / Kernel Loading > Loading Kernel (at {ESP}/KERNEL)...\n");
	void* KernelRaw = LoadFileIntoRAM(RootDir, L"\\KERNEL", NULL);
	if (!KernelRaw) { Print(L"[X] WinOS init / Kernel Loading > Bro place the kernel file at the root directory :) (at {ESP}/KERNEL)\n"); while (true) __halt(); }

	// ssarching for the exact byte sequences to determine if its the KERNEL
	EFI_IMAGE_DOS_HEADER* DOSHeader = (EFI_IMAGE_DOS_HEADER*)KernelRaw;
	EFI_IMAGE_NT_HEADERS64* NTHeaders = (EFI_IMAGE_NT_HEADERS64*)((UINT8*)KernelRaw + DOSHeader->e_lfanew);

	EFI_PHYSICAL_ADDRESS KernelBase = NTHeaders->OptionalHeader.ImageBase;
	UINTN KernelPages = (NTHeaders->OptionalHeader.SizeOfImage + 4095) / 4096;
	if (gBS->AllocatePages(AllocateAddress, EfiLoaderCode, KernelPages, &KernelBase) != EFI_SUCCESS) {
		gBS->AllocatePages(AllocateAnyPages, EfiLoaderCode, KernelPages, &KernelBase);
	}

	EFI_IMAGE_SECTION_HEADER* Section = (EFI_IMAGE_SECTION_HEADER*)((UINT8*)NTHeaders + sizeof(EFI_IMAGE_NT_HEADERS64));
	for (UINTN i = 0; i < NTHeaders->FileHeader.NumberOfSections; i++) {
		gBS->CopyMem((UINT8*)KernelBase + Section[i].VirtualAddress, (UINT8*)KernelRaw + Section[i].PointerToRawData, Section[i].SizeOfRawData);
	}

	typedef void (*KERNEL_ENTRY)(BOOT_CONFIG*);
	KERNEL_ENTRY KernelStart = (KERNEL_ENTRY)(KernelBase + NTHeaders->OptionalHeader.AddressOfEntryPoint);

	// 6. Exit & Jump
	UINTN MMapSize = 0;
	UINTN MMapKey = 0;
	UINTN DSize = 0;
	UINT32 DVer = 0;
	EFI_MEMORY_DESCRIPTOR* MMap = NULL;

	// Ask how much space is needed
	gBS->GetMemoryMap(&MMapSize, MMap, &MMapKey, &DSize, &DVer);

	// Add safety overhead buffer
	MMapSize += 4096;
	gBS->AllocatePool(EfiLoaderData, MMapSize, (void**)&MMap);

	// Grab the actual map data
	if (EFI_ERROR(gBS->GetMemoryMap(&MMapSize, MMap, &MMapKey, &DSize, &DVer))) {
		while (TRUE) __halt();
	}

	// Fill the configuration variables safely
	BootConfig.MemMap = (void*)MMap;
	BootConfig.MemMapSize = (uint64_t)MMapSize;
	BootConfig.MemMapDescriptorSize = (uint64_t)DSize;

	// Hand execution over to the kernel pipeline
	Print(L"[i] WinOS init > LESGOOOOOO\n");
	EFI_STATUS ExitStatus = gBS->ExitBootServices(ImageHandle, MMapKey);
	if (EFI_ERROR(ExitStatus)) {
		// Retry once if the map key shifted due to last-millisecond allocations
		gBS->GetMemoryMap(&MMapSize, MMap, &MMapKey, &DSize, &DVer);
		gBS->ExitBootServices(ImageHandle, MMapKey);
	}

	KernelStart(&BootConfig);

	while (TRUE) __halt();
}

EFI_STATUS EFIAPI UefiUnload()
{
	return EFI_SUCCESS;
}
