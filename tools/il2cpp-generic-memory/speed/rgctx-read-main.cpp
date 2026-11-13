// Times the kernels of rgctx-read-kernel.cpp, the best of 9 runs over 4096 items, 200 passes each:
//   loop     nanoseconds for one item of the loop (two reads of the rgctx and two calls)
//   forward  nanoseconds for one call of the method that passes on (one read, one tail call)
// usage: rgctx-read [rounds]
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <ctime>
#include <algorithm>
#include <vector>

struct Method;
struct Klass;
union Data { void* dummy; const Method* method; Klass* klass; };
struct Klass { void* image; void* gc; const char* name; Data* rgctx_data; };
struct Method { void* pointer; void* invoker; const char* name; Klass* klass; };
struct Holder { void* klass; void* monitor; void* items; };

// Never called here: every entry is resolved before the timing starts.
const Data* resolve(const Data* rgctx, int32_t index)
{
	fputs("an empty entry", stderr);
	abort();
	return rgctx;
}

__attribute__((preserve_most)) const Data* resolve_keeping(const Data* rgctx, int32_t index)
{
	fputs("an empty entry", stderr);
	abort();
	return rgctx;
}

[[noreturn]] void raise_null()
{
	fputs("null", stderr);
	abort();
}

struct Item { int32_t id; };

// The callees are in this file, the callers in others, so nothing is inlined: as in generated code without LTO.
static int32_t ReadId(void* item, const Method*) { return ((Item*)item)->id; }
static int32_t ReadOne(void*, const Method*) { return 1; }
static int32_t ReadAt(void* items, int32_t index, const Method*) { return ((Item**)items)[index]->id; }

#define KERNELS(X) X(stock_os) X(old_os) X(new_os) X(cold_os) X(keep_os) X(stock_o3) X(old_o3) X(new_o3) X(cold_o3) X(keep_o3)
#define DECLARE(name) int64_t walk_##name(void** items, int32_t count, const Method* method); int32_t forward_##name(Holder* self, int32_t index, const Method* method);
KERNELS(DECLARE)

static double now()
{
	timespec time;
	clock_gettime(CLOCK_MONOTONIC, &time);
	return time.tv_sec * 1e9 + time.tv_nsec;
}

int main(int count, char** arguments)
{
	int rounds = count > 1 ? atoi(arguments[1]) : 5;
	const int items = 4096;
	const int passes = 200;
	static Item storage[items];
	static void* list[items];
	for (int at = 0; at < items; at++)
	{
		storage[at].id = at & 7;
		list[at] = &storage[at];
	}
	static Data rgctx[16];
	static Klass klass = { nullptr, nullptr, "Owner", rgctx };
	static Method readId = { (void*)ReadId, nullptr, "get_Item", &klass };
	static Method readOne = { (void*)ReadOne, nullptr, "get_Count", &klass };
	static Method readAt = { (void*)ReadAt, nullptr, "get_Item", &klass };
	static Method caller = { nullptr, nullptr, "Query", &klass };
	static Holder holder = { nullptr, nullptr, list };
	for (int at = 0; at < 16; at++)
	{
		rgctx[at].method = &readOne;
	}
	rgctx[5].method = &readId;
	rgctx[7].method = &readAt;
	struct Entry
	{
		const char* name;
		int64_t (*kernel)(void**, int32_t, const Method*);
		int32_t (*forward)(Holder*, int32_t, const Method*);
		std::vector<double> loop;
		std::vector<double> passing;
	};
	std::vector<Entry> entries;
#define ADD(name) entries.push_back(Entry{#name, walk_##name, forward_##name, {}, {}});
	KERNELS(ADD)
	volatile int64_t sink = 0;
	for (int round = 0; round < rounds; round++)
	{
		for (auto& entry : entries)
		{
			double least = 1e300;
			for (int run = 0; run < 9; run++)
			{
				double start = now();
				for (int pass = 0; pass < passes; pass++)
				{
					sink += entry.kernel(list, items, &caller);
				}
				least = std::min(least, (now() - start) / ((double)items * passes));
			}
			entry.loop.push_back(least);
			least = 1e300;
			// Through a pointer that the compiler cannot see through, so the method is called and not inlined.
			int32_t (*volatile forward)(Holder*, int32_t, const Method*) = entry.forward;
			for (int run = 0; run < 9; run++)
			{
				int64_t sum = 0;
				double start = now();
				for (int pass = 0; pass < passes; pass++)
				{
					int32_t (*call)(Holder*, int32_t, const Method*) = forward;
					for (int at = 0; at < items; at++)
					{
						sum += call(&holder, at, &caller);
					}
				}
				least = std::min(least, (now() - start) / ((double)items * passes));
				sink += sum;
			}
			entry.passing.push_back(least);
		}
	}
	for (auto& entry : entries)
	{
		std::sort(entry.loop.begin(), entry.loop.end());
		std::sort(entry.passing.begin(), entry.passing.end());
		printf("%s\tloop %.3f (median %.3f)\tforward %.3f (median %.3f)\n", entry.name, entry.loop.front(), entry.loop[entry.loop.size() / 2], entry.passing.front(), entry.passing[entry.passing.size() / 2]);
	}
	return 0;
}
