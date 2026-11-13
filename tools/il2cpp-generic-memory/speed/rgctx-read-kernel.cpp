// What shared generic code does when it calls a method of another generic type: it reads the method out of the rgctx
// of its class and calls it. Two shapes, compiled once for each way of reading the entry (FORM) and each set of
// compiler flags, under the names given as NAME and FORWARD:
//   NAME     a loop with two reads and two calls for each item (the inner loop of a query over a List<T>)
//   FORWARD  a method that only passes on to another one, as Repository<T>.this[int] does: one read, one tail call
// The ways of reading:
//   FORM 0  the stock read: rgctx[index]
//   FORM 1  the read of the lazy patch as it was: test the entry, resolve it when empty, read it again
//   FORM 2  the same test with the entry read once and handed on as a value
//   FORM 3  as 2, with the empty case marked as unlikely
//   FORM 4  as 3, and the function that resolves keeps the caller's registers (preserve_most)
#include <cstdint>

struct Method;
struct Klass;
union Data { void* dummy; const Method* method; Klass* klass; };
struct Klass { void* image; void* gc; const char* name; Data* rgctx_data; };
struct Method { void* pointer; void* invoker; const char* name; Klass* klass; };
struct Holder { void* klass; void* monitor; void* items; };

const Data* resolve(const Data* rgctx, int32_t index);
const Data* resolve_keeping(const Data* rgctx, int32_t index) __attribute__((preserve_most));
[[noreturn]] void raise_null();

#if FORM == 0
static inline Data entry(const Data* rgctx, int32_t index)
{
	return rgctx[index];
}
#elif FORM == 1
static inline const Data& entry(const Data* rgctx, int32_t index)
{
	if (rgctx[index].dummy == nullptr)
		resolve(rgctx, index);
	return rgctx[index];
}
#elif FORM == 2
static inline Data entry(const Data* rgctx, int32_t index)
{
	Data value = rgctx[index];
	if (value.dummy == nullptr)
		value = resolve(rgctx, index)[index];
	return value;
}
#elif FORM == 3
static inline Data entry(const Data* rgctx, int32_t index)
{
	Data value = rgctx[index];
	if (__builtin_expect(value.dummy == nullptr, 0))
		value = resolve(rgctx, index)[index];
	return value;
}
#else
static inline Data entry(const Data* rgctx, int32_t index)
{
	Data value = rgctx[index];
	if (__builtin_expect(value.dummy == nullptr, 0))
		value = resolve_keeping(rgctx, index)[index];
	return value;
}
#endif

typedef int32_t (*OfItem)(void* item, const Method* method);
typedef int32_t (*OfIndex)(void* items, int32_t index, const Method* method);

int64_t NAME(void** items, int32_t count, const Method* method)
{
	int64_t sum = 0;
	for (int32_t at = 0; at < count; at++)
	{
		const Method* callee = entry(method->klass->rgctx_data, 5).method;
		sum += ((OfItem)callee->pointer)(items[at], callee);
		const Method* other = entry(method->klass->rgctx_data, 9).method;
		sum += ((OfItem)other->pointer)(items[at], other);
	}
	return sum;
}

int32_t FORWARD(Holder* self, int32_t index, const Method* method)
{
	void* items = self->items;
	if (!items)
		raise_null();
	const Method* callee = entry(method->klass->rgctx_data, 7).method;
	return ((OfIndex)callee->pointer)(items, index, callee);
}
