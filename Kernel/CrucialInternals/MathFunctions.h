#include <stdint.h>

/// <summary>
/// This function returns the actual size of 'pages' number of pages with each having a size of 'pageSize'.
/// It literally does pages * pageSize lmao
/// </summary>
/// <param name="pages">Number of pages</param>
/// <param name="pageSize">Size of each page in bytes</param>
/// <returns>Total size of given number of pages</returns>
static uint64_t PagesToBytes(uint64_t pages, uint32_t pageSize);

/// <summary>
/// Does the reverse of PagesToBytes().
/// </summary>
/// <param name="bytes">Number of bytes to include</param>
/// <param name="pageSize">Size of each page in bytes</param>
/// <returns>Number of pages to contain 'bytes'</returns>
static uint64_t BytesToPages(uint64_t bytes, uint32_t pageSize);

/// <summary>
/// Floors a number (for integer values). Useless asf.
/// </summary>
/// <param name="value">Value</param>
/// <returns>'value'</returns>
static uint64_t Floor(uint64_t value);

/// <summary>
/// Floors a given value (for decimal place numbers [double precision])
/// </summary>
/// <param name="value">Number to floor</param>
/// <returns>Floored value</returns>
static double Floor(double value);

/// <summary>
/// Ceilings two numbers (for integer values)
/// </summary>
/// <param name="numerator">Numerator</param>
/// <param name="denominator">Denominator</param>
/// <returns>Ceiling value of division</returns>
static uint64_t Ceil(uint64_t numerator, uint64_t denominator);

/// <summary>
/// Ceilings a given value (for decimal place numbers [double precision])
/// </summary>
/// <param name="value">Number to ceil</param>
/// <returns>Ceiled value</returns>
static double Ceil(double value);