namespace Memory
{
	/// <summary>
	/// This function returns the actual size of 'pages' number of pages with each having a size of 'pageSize'.
	/// It literally does pages * pageSize lmao
	/// </summary>
	/// <param name="pages">Number of pages</param>
	/// <param name="pageSize">Size of each page in bytes</param>
	/// <returns>Total size of given number of pages</returns>
	uint64_t PagesToBytes(uint64_t pages, uint32_t pageSize);

	/// <summary>
	/// Does the reverse of PagesToBytes().
	/// </summary>
	/// <param name="bytes">Number of bytes to include</param>
	/// <param name="pageSize">Size of each page in bytes</param>
	/// <param name="ceil">Whether to Ceil the answer (default = false)</param>
	/// <returns>Number of pages to contain 'bytes'</returns>
	uint64_t BytesToPages(uint64_t bytes, uint32_t pageSize, bool ceil = false);

	/// <summary>
	/// Finds the page index of an address in memory
	/// </summary>
	/// <param name="address">Address in RAM</param>
	/// <param name="pageSize">Size of each page in bytes</param>
	/// <returns></returns>
	uint64_t AddressToPageIndex(uint64_t address, uint32_t pageSize);
}