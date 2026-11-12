// A stress test of il2cpp::vm::PairCache (Il2CppCastCache.h), outside the runtime: many threads ask and remember under
// keys that fight for the same places, and every hit is compared with what that key must give.
//
// The same loop runs on a copy of the table whose reader does not look at the version. That one has to give wrong
// answers here; if it does not, this test could not have seen a torn read in the real one either, and says so.
//
// usage: cast-cache-test [seconds for each run, default 2]
// Build: speed/cast-cache-test.sh (MSVC for this machine, the NDK's clang for an arm64 Android device).

#define IL2CPP_CAST_CACHE_STANDALONE 1
#include "Il2CppCastCache.h"

#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <thread>
#include <vector>

// PairCache with a reader that trusts whatever it finds: what the version is there to prevent.
template<int Bits>
class TornCache
{
public:
    inline bool TryGet(const void* first, const void* second, intptr_t* value)
    {
        Entry& entry = m_Entries[Index(first, second)];
        const void* a = entry.first.load();
        const void* b = entry.second.load();
        const intptr_t stored = entry.value.load();
        if (a != first || b != second)
            return false;
        *value = stored;
        return true;
    }

    inline void Set(const void* first, const void* second, intptr_t value)
    {
        Entry& entry = m_Entries[Index(first, second)];
        entry.first.store(first);
        entry.second.store(second);
        entry.value.store(value);
    }

private:
    struct Entry
    {
        std::atomic<const void*> first;
        std::atomic<const void*> second;
        std::atomic<intptr_t> value;
    };

    static inline size_t Index(const void* first, const void* second)
    {
        size_t hash = ((size_t)first >> 3) * 31 + ((size_t)second >> 3);
        hash ^= hash >> Bits;
        hash ^= hash >> (2 * Bits);
        return hash & (((size_t)1 << Bits) - 1);
    }

    Entry m_Entries[1 << Bits];
};

static const int kKeys = 64;
static long long s_Keys[kKeys];

// What a pair of keys must give. Different for the two orders of a pair, and for pairs that share a place.
static inline intptr_t Answer(const void* first, const void* second)
{
    return (intptr_t)(((size_t)first >> 3) * 1000003u + ((size_t)second >> 3) * 7919u + 1);
}

struct Counts
{
    std::atomic<unsigned long long> hits;
    std::atomic<unsigned long long> misses;
    std::atomic<unsigned long long> wrong;
};

template<typename Cache>
static void Worker(Cache* cache, Counts* counts, unsigned seed, std::atomic<bool>* stop)
{
    unsigned long long hits = 0, misses = 0, wrong = 0;
    unsigned state = seed * 2654435761u + 1;
    while (!stop->load(std::memory_order_relaxed))
    {
        for (int i = 0; i < 4096; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            const void* first = &s_Keys[state % kKeys];
            const void* second = &s_Keys[(state >> 8) % kKeys];
            intptr_t value;
            if (cache->TryGet(first, second, &value))
            {
                hits++;
                if (value != Answer(first, second))
                    wrong++;
            }
            else
            {
                misses++;
                cache->Set(first, second, Answer(first, second));
            }
        }
    }
    counts->hits += hits;
    counts->misses += misses;
    counts->wrong += wrong;
}

template<typename Cache>
static unsigned long long Run(const char* name, int threads, double seconds)
{
    // Static storage, as in the runtime: every entry starts as zero.
    static Cache cache;
    Counts counts;
    counts.hits = 0;
    counts.misses = 0;
    counts.wrong = 0;
    std::atomic<bool> stop(false);
    std::vector<std::thread> workers;
    for (int i = 0; i < threads; i++)
        workers.push_back(std::thread(Worker<Cache>, &cache, &counts, (unsigned)(i + 1), &stop));
    std::this_thread::sleep_for(std::chrono::milliseconds((int)(seconds * 1000)));
    stop = true;
    for (size_t i = 0; i < workers.size(); i++)
        workers[i].join();
    std::printf("%-34s threads %d  hits %llu  misses %llu  wrong %llu\n", name, threads,
        (unsigned long long)counts.hits, (unsigned long long)counts.misses, (unsigned long long)counts.wrong);
    return counts.wrong;
}

int main(int argc, char** argv)
{
    const double seconds = argc > 1 ? std::atof(argv[1]) : 2.0;
    int threads = (int)std::thread::hardware_concurrency();
    if (threads < 4)
        threads = 4;

    // 4096 pairs of keys. Four places: every place is fought over. 2048 places: the size the runtime uses.
    const unsigned long long tornSeen = Run<TornCache<2> >("no version, 4 places", threads, seconds);
    unsigned long long wrong = 0;
    wrong += Run<il2cpp::vm::PairCache<2> >("PairCache, 4 places", threads, seconds);
    wrong += Run<il2cpp::vm::PairCache<6> >("PairCache, 64 places", threads, seconds);
    wrong += Run<il2cpp::vm::CastCache>("PairCache, 2048 places (runtime)", threads, seconds);
    wrong += Run<il2cpp::vm::PairCache<2> >("PairCache, 4 places, 2 threads", 2, seconds);

    if (tornSeen == 0)
        std::printf("NOT PROVEN: the reader without a version gave no wrong answer, so this run could not have seen one\n");
    if (wrong != 0)
    {
        std::printf("FAIL: %llu wrong answers\n", wrong);
        return 1;
    }
    std::printf(tornSeen != 0 ? "PASS\n" : "PASS (see above)\n");
    return tornSeen != 0 ? 0 : 2;
}
