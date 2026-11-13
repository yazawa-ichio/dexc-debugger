// The loop of SpeedBench.IndexArray as il2cpp writes it: 16 passes over an array of references, a range check and
// a null check for each element. Compiled once for each set of compiler flags, under the name given as NAME.
#include <cstdint>

struct Obj { void* klass; void* monitor; int32_t id; };
struct Arr { void* klass; void* monitor; void* bounds; uintptr_t max_length; Obj* items[1]; };

[[noreturn]] void raise_null();
[[noreturn]] void raise_range();
void sink(int32_t value);

#ifndef PASSES
#define PASSES 16
#endif

int64_t NAME(Arr** holder)
{
	Arr* array = *holder;
	int32_t sum = 0;
	for (int32_t pass = 0; pass < PASSES; pass++)
	{
		for (int32_t at = 0;; at++)
		{
			if (!array) raise_null();
			if (at >= (int32_t)array->max_length) break;
			if ((uintptr_t)at >= array->max_length) raise_range();
			Obj* item = array->items[at];
			if (!item) raise_null();
			sum += item->id;
		}
	}
	sink(sum);
	if (!array) raise_null();
	return (int64_t)PASSES * (int32_t)array->max_length;
}
