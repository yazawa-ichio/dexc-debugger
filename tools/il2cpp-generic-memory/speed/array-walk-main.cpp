// Times the kernels of array-walk-kernel.cpp as SpeedBench.Report does: one warm-up call, then the best of 7 calls,
// in nanoseconds for each element. The array has 1 << 20 references to 1024 objects, as SpeedBench has.
// usage: array-walk [rounds] [log2 of the count] [spacing | -spacing | @file]   (see array-walk.sh)
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <ctime>
#include <algorithm>
#include <vector>

struct Obj { void* klass; void* monitor; int32_t id; };
struct Arr { void* klass; void* monitor; void* bounds; uintptr_t max_length; Obj* items[1]; };

[[noreturn]] void raise_null() { fprintf(stderr, "null\n"); abort(); }
[[noreturn]] void raise_range() { fprintf(stderr, "range\n"); abort(); }
static volatile int64_t s_sink;
void sink(int32_t value) { s_sink += value; }

#define KERNELS(X) X(os) X(o2) X(o3) X(o3nu) X(asmos) X(asmo3) X(asmo3single)
#define DECLARE(name) int64_t walk_##name(Arr** holder);
KERNELS(DECLARE)

extern "C" int32_t player_os(Arr* array);
extern "C" int32_t player_o3(Arr* array);
extern "C" int32_t player_o3single(Arr* array);
int64_t walk_asmos(Arr** holder) { sink(player_os(*holder)); return (int64_t)16 * (int32_t)(*holder)->max_length; }
int64_t walk_asmo3(Arr** holder) { sink(player_o3(*holder)); return (int64_t)16 * (int32_t)(*holder)->max_length; }
int64_t walk_asmo3single(Arr** holder) { sink(player_o3single(*holder)); return (int64_t)16 * (int32_t)(*holder)->max_length; }

static double now()
{
	timespec time;
	clock_gettime(CLOCK_MONOTONIC, &time);
	return time.tv_sec * 1e9 + time.tv_nsec;
}

static double best(int64_t (*kernel)(Arr**), Arr** holder)
{
	kernel(holder);
	double least = 1e300;
	for (int run = 0; run < 7; run++)
	{
		double start = now();
		int64_t calls = kernel(holder);
		double taken = now() - start;
		least = std::min(least, taken / calls);
	}
	return least;
}

int main(int count, char** arguments)
{
	int rounds = count > 1 ? atoi(arguments[1]) : 5;
	int log = count > 2 ? atoi(arguments[2]) : 20;
	size_t length = (size_t)1 << log;
	int given = count > 3 ? atoi(arguments[3]) : 32;
	size_t spacing = (size_t)(given < 0 ? -given : given);
	// A negative spacing puts the objects in a shuffled order, so the loads of the objects have no stride.
	static size_t order[1024];
	for (size_t at = 0; at < 1024; at++) order[at] = at;
	if (given < 0)
	{
		uint32_t state = 12345;
		for (size_t at = 1023; at > 0; at--)
		{
			state = state * 1664525u + 1013904223u;
			size_t other = (state >> 8) % (at + 1);
			std::swap(order[at], order[other]);
		}
	}
	// "@file": the objects sit at the offsets the file lists (1024 numbers with commas, as SpeedBench writes them).
	static size_t offsets[1024];
	bool listed = count > 3 && arguments[3][0] == '@';
	size_t span = 1024 * spacing;
	if (listed)
	{
		FILE* file = fopen(arguments[3] + 1, "r");
		if (!file) { fputs("no layout file", stderr); return 1; }
		span = 0;
		for (size_t at = 0; at < 1024; at++)
		{
			unsigned long long offset = 0;
			if (fscanf(file, "%llu,", &offset) != 1) { fputs("short layout file", stderr); return 1; }
			offsets[at] = (size_t)offset;
			span = std::max(span, (size_t)offset + 64);
		}
		fclose(file);
	}
	Obj* objects = (Obj*)calloc(1, span + 4096);
	Arr* array = (Arr*)calloc(1, sizeof(Arr) + length * sizeof(Obj*));
	array->max_length = length;
	for (size_t at = 0; at < length; at++)
	{
		Obj* one = (Obj*)((char*)objects + (listed ? offsets[at & 1023] : order[at & 1023] * spacing));
		one->id = (int32_t)(at & 1023);
		array->items[at] = one;
	}
	Arr* holder = array;
	struct Entry { const char* name; int64_t (*kernel)(Arr**); std::vector<double> values; };
	std::vector<Entry> entries;
#define ADD(name) entries.push_back(Entry{#name, walk_##name, {}});
	KERNELS(ADD)
	for (int round = 0; round < rounds; round++)
	{
		for (auto& entry : entries)
		{
			entry.values.push_back(best(entry.kernel, &holder));
		}
	}
	for (auto& entry : entries)
	{
		std::sort(entry.values.begin(), entry.values.end());
		printf("%s\tbest %.3f\tmedian %.3f\tworst %.3f\n", entry.name, entry.values.front(), entry.values[entry.values.size() / 2], entry.values.back());
	}
	return 0;
}
