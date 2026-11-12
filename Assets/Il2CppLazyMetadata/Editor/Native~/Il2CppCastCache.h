// A cast cache for libil2cpp: the answers of two searches that the runtime otherwise repeats on every call. This file
// is copied into libil2cpp/vm beside Il2CppLazyInterfaces.h and included by the two sources that search.
//
// - Class::IsAssignableFrom with an interface on the left ("x is IFoo", "(IFoo)x", "x as IFoo") walks every interface
//   of the class of the object, and for a generic interface tests variance on each of them.
// - ClassInlines::GetInterfaceInvokeDataFromVTableSlowPath, which a call through a generic interface reaches when the
//   class implements the interface only by variance (a List<Dog> called as IReadOnlyList<IAnimal>), searches the
//   interfaces of the class for one that can stand in, on every call.
//
// Each of the two gets one table of 2048 entries, keyed by the two classes: 32 bytes an entry, 64 KB a table, static,
// never growing, and nothing in it is ever freed (classes are not). The answers do not change: what a class implements
// is fixed once the class is initialized. A table has one place for a key, so two pairs of classes that share a place
// take it from each other and cost what the search costs.
//
// The readers take no lock. An entry is three words under a version: a writer makes the version odd, stores, and makes
// it even again; a reader that sees an odd version, or two versions around its reads, has a miss. Everything is read
// and written through std::atomic, which also orders a hit behind what the writer did before it stored: the slots of a
// pair are filled before their place is remembered (Il2CppLazyInterfaces.h), so a hit never names an empty slot.
//
// IL2CPP_CAST_CACHE() says whether the tables are used:
//   a game           always, unless the build was made with IL2CPP_CAST_CACHE=0, which writes IL2CPP_CAST_CACHE_OFF
//                    ahead of this file (the lines that ask stay in the sources and compile to nothing)
//   the bench        when the Player was started with the fifth switch on (GENERIC_MEMORY_LAZY_MORE=1)

#ifndef IL2CPP_CAST_CACHE_H
#define IL2CPP_CAST_CACHE_H

#include <atomic>
#include <stddef.h>
#include <stdint.h>

#if !defined(IL2CPP_CAST_CACHE_STANDALONE)
#include "vm/Il2CppLazyMetadata.h"
#endif

#if defined(IL2CPP_CAST_CACHE_OFF)
#define IL2CPP_CAST_CACHE() (false)
#elif defined(IL2CPP_LAZY_METADATA_SWITCHED) && IL2CPP_LAZY_METADATA_SWITCHED
#define IL2CPP_CAST_CACHE() (generic_memory::CastCache())
#else
#define IL2CPP_CAST_CACHE() (true)
#endif

namespace il2cpp
{
namespace vm
{
    // A fixed table from two pointers to a small value, with one place for each key.
    template<int Bits>
    class PairCache
    {
    public:
        inline bool TryGet(const void* first, const void* second, intptr_t* value)
        {
            Entry& entry = m_Entries[Index(first, second)];
            const uint32_t before = entry.version.load();
            if (before & 1)
                return false;
            const void* a = entry.first.load();
            const void* b = entry.second.load();
            const intptr_t stored = entry.value.load();
            if (entry.version.load() != before || a != first || b != second)
                return false;
            *value = stored;
            return true;
        }

        inline void Set(const void* first, const void* second, intptr_t value)
        {
            Entry& entry = m_Entries[Index(first, second)];
            uint32_t before = entry.version.load();
            if ((before & 1) || !entry.version.compare_exchange_strong(before, before + 1))
                return; // another thread is writing this entry, and what it remembers is as good
            entry.first.store(first);
            entry.second.store(second);
            entry.value.store(value);
            entry.version.store(before + 2);
        }

    private:
        struct Entry
        {
            std::atomic<uint32_t> version;
            std::atomic<const void*> first;
            std::atomic<const void*> second;
            std::atomic<intptr_t> value;
        };

        static inline size_t Index(const void* first, const void* second)
        {
            // Classes are 8 bytes apart at least: the low bits say nothing.
            size_t hash = ((size_t)first >> 3) * 31 + ((size_t)second >> 3);
            hash ^= hash >> Bits;
            hash ^= hash >> (2 * Bits);
            return hash & (((size_t)1 << Bits) - 1);
        }

        Entry m_Entries[1 << Bits];
    };

    typedef PairCache<11> CastCache;
} // namespace vm
} // namespace il2cpp

#endif // IL2CPP_CAST_CACHE_H
