#pragma once

// Counters of the generic-memory bench. They are not part of Unity's libil2cpp: the copy that has them is built only by
// the bench project, through UNITY_IL2CPP_PATH.

#include <stdint.h>
#include <stddef.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <mutex>
#include <unordered_map>

namespace generic_memory
{
    struct Site
    {
        const char* file;
        int line;
        uint64_t count;
        uint64_t bytes;
        uint64_t baseCount;
        uint64_t baseBytes;
    };

    enum { kMaxSites = 512 };

    struct State
    {
        std::mutex lock;
        Site sites[kMaxSites];
        int siteCount;
        // The blocks il2cpp's own allocator and its hash tables hold now, which include the regions of the metadata pools.
        std::unordered_map<void*, size_t> live;
        uint64_t liveBytes;
        uint64_t liveBase;
        // Read once from the environment: 1 leaves the methods of a generic instance uninflated until something asks.
        int lazyMethods;
        // Read once from the environment: 1 leaves the entries of an rgctx empty until the generated code reads them.
        int lazyRgctx;
        // Read once from the environment: 1 leaves the interface slots of a vtable empty until the interface is called through.
        int lazyInterfaces;
    };

    inline State& state()
    {
        static State* s = NULL;
        if (s == NULL)
        {
            State* created = new State();
            const char* lazy = getenv("GENERIC_MEMORY_LAZY_METHODS");
            created->lazyMethods = lazy != NULL && lazy[0] == '1';
            const char* rgctx = getenv("GENERIC_MEMORY_LAZY_RGCTX");
            created->lazyRgctx = rgctx != NULL && rgctx[0] == '1';
            const char* interfaces = getenv("GENERIC_MEMORY_LAZY_INTERFACES");
            created->lazyInterfaces = interfaces != NULL && interfaces[0] == '1';
            s = created;
        }
        return *s;
    }

    inline bool LazyMethods()
    {
        return state().lazyMethods != 0;
    }

    inline bool LazyRgctx()
    {
        return state().lazyRgctx != 0;
    }

    inline bool LazyInterfaces()
    {
        return state().lazyInterfaces != 0;
    }

    // One allocation out of a metadata pool, named by the line that asked for it.
    inline void Pool(const char* file, int line, size_t bytes)
    {
        State& s = state();
        std::lock_guard<std::mutex> guard(s.lock);
        for (int i = 0; i < s.siteCount; i++)
        {
            if (s.sites[i].file == file && s.sites[i].line == line)
            {
                s.sites[i].count++;
                s.sites[i].bytes += bytes;
                return;
            }
        }
        if (s.siteCount < kMaxSites)
        {
            Site& site = s.sites[s.siteCount++];
            site.file = file;
            site.line = line;
            site.count = 1;
            site.bytes = bytes;
        }
    }

    inline void HeapAlloc(void* block, size_t bytes)
    {
        if (block == NULL)
            return;
        State& s = state();
        std::lock_guard<std::mutex> guard(s.lock);
        s.live[block] = bytes;
        s.liveBytes += bytes;
    }

    inline void HeapFree(void* block)
    {
        if (block == NULL)
            return;
        State& s = state();
        std::lock_guard<std::mutex> guard(s.lock);
        std::unordered_map<void*, size_t>::iterator found = s.live.find(block);
        if (found != s.live.end())
        {
            s.liveBytes -= found->second;
            s.live.erase(found);
        }
    }

    inline void Snapshot()
    {
        State& s = state();
        std::lock_guard<std::mutex> guard(s.lock);
        for (int i = 0; i < s.siteCount; i++)
        {
            s.sites[i].baseCount = s.sites[i].count;
            s.sites[i].baseBytes = s.sites[i].bytes;
        }
        s.liveBase = s.liveBytes;
    }

    inline const char* BaseName(const char* path)
    {
        const char* name = path;
        for (const char* at = path; *at; at++)
        {
            if (*at == '/' || *at == '\\')
                name = at + 1;
        }
        return name;
    }

    // Writes what was allocated since the last Snapshot, one line for each site that allocated.
    inline bool Dump(const char* path)
    {
        State& s = state();
        std::lock_guard<std::mutex> guard(s.lock);
        FILE* out = fopen(path, "w");
        if (out == NULL)
            return false;
        uint64_t poolBytes = 0;
        for (int i = 0; i < s.siteCount; i++)
        {
            uint64_t count = s.sites[i].count - s.sites[i].baseCount;
            uint64_t bytes = s.sites[i].bytes - s.sites[i].baseBytes;
            poolBytes += bytes;
            if (count != 0)
                fprintf(out, "site\t%s:%d\t%llu\t%llu\n", BaseName(s.sites[i].file), s.sites[i].line, (unsigned long long)count, (unsigned long long)bytes);
        }
        fprintf(out, "pool\t%llu\n", (unsigned long long)poolBytes);
        fprintf(out, "heap\t%lld\n", (long long)(s.liveBytes - s.liveBase));
        fprintf(out, "heapTotal\t%llu\n", (unsigned long long)s.liveBytes);
        fprintf(out, "lazyMethods\t%d\n", s.lazyMethods);
        fprintf(out, "lazyRgctx\t%d\n", s.lazyRgctx);
        fprintf(out, "lazyInterfaces\t%d\n", s.lazyInterfaces);
        fclose(out);
        return true;
    }
}
