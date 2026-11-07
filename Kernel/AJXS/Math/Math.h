namespace Math
{
#include "Memory/Memory.h"

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
}