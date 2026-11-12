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
#include <vector>

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

    // A class or a method the runtime made, kept by its address so that it can be named once the run is over.
    enum { kMadeClass = 0, kMadeMethod = 1 };

    struct Made
    {
        int kind;
        const void* what;
    };

    struct State
    {
        std::mutex lock;
        Site sites[kMaxSites];
        int siteCount;
        // The blocks il2cpp's own allocator and its hash tables hold now, which include the regions of the metadata pools.
        std::unordered_map<void*, size_t> live;
        uint64_t liveBytes;
        uint64_t liveBase;
        std::vector<Made> made;
        size_t madeBase;
        // Read once from the environment: 1 leaves the methods of a generic instance uninflated until something asks.
        int lazyMethods;
        // Read once from the environment: 1 leaves the entries of an rgctx empty until the generated code reads them.
        int lazyRgctx;
        // Read once from the environment: 1 leaves the interface slots of a vtable empty until the interface is called through.
        int lazyInterfaces;
        // Read once from the environment: 1 leaves the class of an interface unmade until something asks for that class.
        int lazyInterfaceClasses;
        // Read once from the environment: one digit for each further switch, which the source that uses it names.
        char lazyMore[16];
        // How often each branch of the lazy code was taken, by the name the branch gives itself.
        enum { kMaxBranches = 48 };
        const char* branchNames[kMaxBranches];
        uint64_t branchCounts[kMaxBranches];
        int branchCount;
        // Set from the environment: a file that gets one line for each branch taken, for runs that end without a dump.
        FILE* branchLog;
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
            const char* interfaceClasses = getenv("GENERIC_MEMORY_LAZY_INTERFACE_CLASSES");
            created->lazyInterfaceClasses = interfaceClasses != NULL && interfaceClasses[0] == '1';
            const char* more = getenv("GENERIC_MEMORY_LAZY_MORE");
            if (more != NULL)
                strncpy(created->lazyMore, more, sizeof(created->lazyMore) - 1);
            const char* branchLog = getenv("GENERIC_MEMORY_BRANCH_LOG");
            if (branchLog != NULL && branchLog[0] != 0)
                created->branchLog = fopen(branchLog, "a");
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

    inline bool LazyInterfaceClasses()
    {
        return state().lazyInterfaceClasses != 0;
    }

    // The switch at a place of GENERIC_MEMORY_LAZY_MORE, counted from 0. Off when the variable is shorter.
    inline bool LazyMore(int index)
    {
        const char* more = state().lazyMore;
        for (int i = 0; i < index; i++)
        {
            if (more[i] == 0)
                return false;
        }
        return more[index] == '1';
    }

    // Counts one pass through a branch of the lazy code. The name is a string literal. Only a run that was started with
    // GENERIC_MEMORY_BRANCH_LOG counts: some branches are on paths that the benches time, and the count takes a lock.
    inline void Branch(const char* name)
    {
        State& s = state();
        if (s.branchLog == NULL)
            return;
        std::lock_guard<std::mutex> guard(s.lock);
        int at = 0;
        while (at < s.branchCount && s.branchNames[at] != name && strcmp(s.branchNames[at], name) != 0)
            at++;
        if (at == s.branchCount)
        {
            if (s.branchCount == State::kMaxBranches)
                return;
            s.branchNames[s.branchCount++] = name;
        }
        s.branchCounts[at]++;
        fprintf(s.branchLog, "%s\n", name);
        fflush(s.branchLog);
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

    inline void Record(int kind, const void* what)
    {
        State& s = state();
        std::lock_guard<std::mutex> guard(s.lock);
        Made made = { kind, what };
        s.made.push_back(made);
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
        s.madeBase = s.made.size();
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
        fprintf(out, "lazyInterfaceClasses\t%d\n", s.lazyInterfaceClasses);
        fprintf(out, "lazyMore\t%s\n", s.lazyMore[0] != 0 ? s.lazyMore : "-");
        for (int i = 0; i < s.branchCount; i++)
            fprintf(out, "branch\t%s\t%llu\n", s.branchNames[i], (unsigned long long)s.branchCounts[i]);
        fclose(out);
        return true;
    }

    // Hands every class and method made since the last Snapshot to the caller, which knows how to name them. The lock is
    // not held meanwhile: naming a type may make the runtime allocate, and what it makes then is not part of the list.
    inline bool Names(const char* path, void (*describe)(FILE* out, int kind, const void* what))
    {
        std::vector<Made> made;
        {
            State& s = state();
            std::lock_guard<std::mutex> guard(s.lock);
            made.assign(s.made.begin() + s.madeBase, s.made.end());
        }
        FILE* out = fopen(path, "w");
        if (out == NULL)
            return false;
        for (size_t i = 0; i < made.size(); i++)
            describe(out, made[i].kind, made[i].what);
        fclose(out);
        return true;
    }
}
