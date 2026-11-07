#include <cstdint>
#include "../Math.h"

namespace Memory
{
	uint64_t PagesToBytes(uint64_t pages, uint32_t pageSize) {
		return pages * pageSize;
	}

	uint64_t BytesToPages(uint64_t bytes, uint32_t pageSize, bool ceil) {
		if (ceil)
			return Math::Ceil(bytes, pageSize);
		else
			return bytes / pageSize;
	}

	uint64_t AddressToPageIndex(uint64_t address, uint32_t pageSize)
	{
		return address / pageSize;
	}
}