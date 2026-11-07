#include "MathFunctions.h"

static uint64_t PagesToBytes(uint64_t pages, uint32_t pageSize) {
	return pages * pageSize;
}

static uint64_t BytesToPages(uint64_t bytes, uint32_t pageSize) {

}

static uint64_t Floor(uint64_t value) {
	return value;
}

static double Floor(double value) {
	uint64_t bits;
}

static uint64_t Ceil(uint64_t numerator, uint64_t denominator) {
	if (numerator % denominator != 0)
		return (numerator / denominator) + 1;
	else
		return numerator / denominator;
}

static double Ceil(double value) {

}