#include <cstdint>

namespace Math
{
	static uint64_t Floor(uint64_t value) {
		return value; // because value is an unsigned int, which is already floored
	}

	static double Floor(double value) {
		// TODO
	}

	static uint64_t Ceil(uint64_t numerator, uint64_t denominator) {
		if (numerator % denominator != 0)
			return (numerator / denominator) + 1;
		else
			return numerator / denominator;
	}

	static double Ceil(double value) {
		// TODO
	}
}