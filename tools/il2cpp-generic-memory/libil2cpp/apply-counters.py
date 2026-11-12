"""Puts the bench counters into a copy of Unity's il2cpp folder.

usage: apply-counters.py <copied il2cpp folder>

The copy is what UNITY_IL2CPP_PATH points at while the bench project builds. The Editor's own folder is never touched.
Each edit names the text it expects, so a Unity version that changed the source stops here instead of building wrong.
"""

import io
import os
import shutil
import sys


def edit(path, pairs):
    raw = io.open(path, encoding="utf-8-sig", newline="").read()
    crlf = "\r\n" in raw
    text = raw.replace("\r\n", "\n")
    before = text
    for old, new in pairs:
        if new in text:
            continue
        if old not in text:
            raise SystemExit("not found in " + path + ": " + old[:70])
        text = text.replace(old, new, 1)
    # A file that has everything already is left alone: external/ is one folder that every copy shares, and the
    # build of another copy may be reading it.
    if text != before:
        io.open(path, "w", encoding="utf-8", newline="").write(text.replace("\n", "\r\n") if crlf else text)


def main():
    root = sys.argv[1]
    lib = os.path.join(root, "libil2cpp")
    here = os.path.dirname(os.path.abspath(__file__))
    shutil.copyfile(os.path.join(here, "GenericMemoryCounters.h"), os.path.join(lib, "utils", "GenericMemoryCounters.h"))

    edit(
        os.path.join(lib, "vm", "MetadataAlloc.h"),
        [
            (
                "    void* MetadataMalloc(size_t size);\n    void* MetadataCalloc(size_t count, size_t size);",
                "    // Generic memory bench: the caller's file and line name the allocation in the bench counters.\n"
                "    void* MetadataMalloc(size_t size, const char* file = __builtin_FILE(), int line = __builtin_LINE());\n"
                "    void* MetadataCalloc(size_t count, size_t size, const char* file = __builtin_FILE(), int line = __builtin_LINE());",
            ),
        ],
    )
    # Every class and method the runtime makes is kept by its address, to be named when the run is over. Each statement goes
    # on a line that is there already, so that the lines of the allocations below keep the numbers the reports know them by.
    edit(
        os.path.join(lib, "vm", "MetadataAlloc.h"),
        [('#include "il2cpp-config.h"\nstruct Il2CppGenericClass;', '#include "il2cpp-config.h"\n#include "utils/GenericMemoryCounters.h"\nstruct Il2CppGenericClass;')],
    )
    edit(
        os.path.join(lib, "vm", "GenericClass.cpp"),
        [
            (
                "definition->vtable_count));\n            klass->klass = klass;\n",
                "definition->vtable_count));\n            klass->klass = klass; generic_memory::Record(generic_memory::kMadeClass, klass);\n",
            ),
        ],
    )
    edit(
        os.path.join(lib, "metadata", "ArrayMetadata.cpp"),
        [
            (
                "(slots * sizeof(VirtualInvokeData)));\n        klass->klass = klass;\n",
                "(slots * sizeof(VirtualInvokeData)));\n        klass->klass = klass; generic_memory::Record(generic_memory::kMadeClass, klass);\n",
            ),
            (
                "        method->klass = declaringType;\n",
                "        method->klass = declaringType; generic_memory::Record(generic_memory::kMadeMethod, method);\n",
            ),
            (
                "        inflatedMethod->name = genericArrayMethod.name;\n",
                "        inflatedMethod->name = genericArrayMethod.name; generic_memory::Record(generic_memory::kMadeMethod, inflatedMethod);\n",
            ),
        ],
    )
    edit(
        os.path.join(lib, "metadata", "GenericMethod.cpp"),
        [
            (
                "    return (MethodInfo*)MetadataCalloc(1, SizeOfGenericMethodInfo(hasFullGenericSignature));\n",
                "    MethodInfo* made = (MethodInfo*)MetadataCalloc(1, SizeOfGenericMethodInfo(hasFullGenericSignature)); generic_memory::Record(generic_memory::kMadeMethod, made); return made;\n",
            ),
        ],
    )
    edit(
        os.path.join(lib, "vm", "MetadataAlloc.cpp"),
        [
            ('#include "utils/MemoryPool.h"\n', '#include "utils/MemoryPool.h"\n#include "utils/GenericMemoryCounters.h"\n'),
            (
                "    void* MetadataMalloc(size_t size)\n    {\n",
                "    void* MetadataMalloc(size_t size, const char* file, int line)\n    {\n"
                "        generic_memory::Pool(file, line, (size + 7) & ~(size_t)7);\n",
            ),
            (
                "    void* MetadataCalloc(size_t count, size_t size)\n    {\n",
                "    void* MetadataCalloc(size_t count, size_t size, const char* file, int line)\n    {\n"
                "        generic_memory::Pool(file, line, (count * size + 7) & ~(size_t)7);\n",
            ),
            (
                "    Il2CppGenericClass* MetadataAllocGenericClass()\n    {\n",
                "    Il2CppGenericClass* MetadataAllocGenericClass()\n    {\n"
                '        generic_memory::Pool("Il2CppGenericClass", 0, sizeof(Il2CppGenericClass));\n',
            ),
            (
                "    Il2CppGenericMethod* MetadataAllocGenericMethod()\n    {\n",
                "    Il2CppGenericMethod* MetadataAllocGenericMethod()\n    {\n"
                '        generic_memory::Pool("Il2CppGenericMethod", 0, sizeof(Il2CppGenericMethod));\n',
            ),
        ],
    )
    edit(
        os.path.join(lib, "utils", "Memory.cpp"),
        [
            ('#include "utils/Memory.h"\n', '#include "utils/Memory.h"\n#include "utils/GenericMemoryCounters.h"\n'),
            (
                "        return s_Callbacks.malloc_func(size);",
                "        void* block = s_Callbacks.malloc_func(size);\n        generic_memory::HeapAlloc(block, size);\n        return block;",
            ),
            (
                "        return s_Callbacks.aligned_malloc_func(size, alignment);",
                "        void* block = s_Callbacks.aligned_malloc_func(size, alignment);\n        generic_memory::HeapAlloc(block, size);\n        return block;",
            ),
            (
                "        return s_Callbacks.free_func(memory);",
                "        generic_memory::HeapFree(memory);\n        return s_Callbacks.free_func(memory);",
            ),
            (
                "        return s_Callbacks.aligned_free_func(memory);",
                "        generic_memory::HeapFree(memory);\n        return s_Callbacks.aligned_free_func(memory);",
            ),
            (
                "        return s_Callbacks.calloc_func(count, size);",
                "        void* block = s_Callbacks.calloc_func(count, size);\n        generic_memory::HeapAlloc(block, count * size);\n        return block;",
            ),
            (
                "        return s_Callbacks.realloc_func(memory, newSize);",
                "        generic_memory::HeapFree(memory);\n        void* block = s_Callbacks.realloc_func(memory, newSize);\n"
                "        generic_memory::HeapAlloc(block, newSize);\n        return block;",
            ),
            (
                "        return s_Callbacks.aligned_realloc_func(memory, newSize, alignment);",
                "        generic_memory::HeapFree(memory);\n        void* block = s_Callbacks.aligned_realloc_func(memory, newSize, alignment);\n"
                "        generic_memory::HeapAlloc(block, newSize);\n        return block;",
            ),
        ],
    )
    # The hash tables of the metadata (generic methods, generic classes, generic insts) take their buckets from here.
    edit(
        os.path.join(root, "external", "google", "sparsehash", "internal", "libc_allocator_with_realloc.h"),
        [
            (
                "_START_GOOGLE_NAMESPACE_\n\ntemplate<class T>\nclass libc_allocator_with_realloc {",
                '#include "utils/GenericMemoryCounters.h"\n\n_START_GOOGLE_NAMESPACE_\n\ntemplate<class T>\nclass libc_allocator_with_realloc {',
            ),
            (
                "    return static_cast<pointer>(malloc(n * sizeof(value_type)));",
                "    pointer block = static_cast<pointer>(malloc(n * sizeof(value_type)));\n"
                "    generic_memory::HeapAlloc(block, n * sizeof(value_type));\n    return block;",
            ),
            (
                "  void deallocate(pointer p, size_type) {\n    free(p);",
                "  void deallocate(pointer p, size_type) {\n    generic_memory::HeapFree(p);\n    free(p);",
            ),
            (
                "    return static_cast<pointer>(realloc(static_cast<void*>(p), n * sizeof(value_type)));",
                "    generic_memory::HeapFree(p);\n"
                "    pointer block = static_cast<pointer>(realloc(static_cast<void*>(p), n * sizeof(value_type)));\n"
                "    generic_memory::HeapAlloc(block, n * sizeof(value_type));\n    return block;",
            ),
        ],
    )
    edit(
        os.path.join(lib, "il2cpp-api-functions.h"),
        [
            (
                "DO_API(uint64_t, il2cpp_stats_get_value, (Il2CppStat stat));",
                "DO_API(uint64_t, il2cpp_stats_get_value, (Il2CppStat stat));\n\n"
                "// Generic memory bench: the bench counters\nDO_API(void, il2cpp_generic_memory_snapshot, ());\nDO_API(bool, il2cpp_generic_memory_dump, (const char *path));",
            ),
            (
                "DO_API(bool, il2cpp_generic_memory_dump, (const char *path));",
                "DO_API(bool, il2cpp_generic_memory_dump, (const char *path));\nDO_API(bool, il2cpp_generic_memory_names, (const char *path));",
            ),
        ],
    )
    edit(
        os.path.join(lib, "il2cpp-api.cpp"),
        [
            ('#include "utils/Memory.h"\n', '#include "utils/Memory.h"\n#include "utils/GenericMemoryCounters.h"\n'),
            (
                "// stats\n\nextern Il2CppRuntimeStats il2cpp_runtime_stats;",
                "// Generic memory bench: the bench counters\n\nvoid il2cpp_generic_memory_snapshot()\n{\n    generic_memory::Snapshot();\n}\n\n"
                "bool il2cpp_generic_memory_dump(const char *path)\n{\n    return generic_memory::Dump(path);\n}\n\n"
                "// stats\n\nextern Il2CppRuntimeStats il2cpp_runtime_stats;",
            ),
            # The names of what was made since the snapshot: a class with its size, a method with the class it belongs to.
            # They go below the line the edit above ends with, so that what that edit put in stays as it wrote it and is
            # found again when this runs a second time.
            (
                "extern Il2CppRuntimeStats il2cpp_runtime_stats;\n",
                "extern Il2CppRuntimeStats il2cpp_runtime_stats;\n\n"
                "static void il2cpp_generic_memory_describe(FILE* out, int kind, const void* what)\n{\n"
                "    if (kind == generic_memory::kMadeClass)\n    {\n"
                "        const Il2CppClass* klass = (const Il2CppClass*)what;\n"
                "        std::string name = il2cpp::vm::Type::GetName(&klass->byval_arg, IL2CPP_TYPE_NAME_FORMAT_FULL_NAME);\n"
                "        fprintf(out, \"class\\t%p\\t%s\\t%u\\t%u\\t%u\\t%u\\t%d\\t%d\\n\", what, name.c_str(),\n"
                "            (unsigned)(sizeof(Il2CppClass) + klass->vtable_count * sizeof(VirtualInvokeData)), (unsigned)klass->vtable_count,\n"
                "            (unsigned)klass->method_count, (unsigned)klass->interfaces_count, klass->initialized ? 1 : 0, klass->methods != NULL ? 1 : 0);\n"
                "    }\n    else\n    {\n"
                "        const MethodInfo* method = (const MethodInfo*)what;\n"
                "        std::string name = method->klass != NULL ? il2cpp::vm::Type::GetName(&method->klass->byval_arg, IL2CPP_TYPE_NAME_FORMAT_FULL_NAME) : std::string(\"?\");\n"
                "        bool generic = method->is_inflated && method->genericMethod != NULL && method->genericMethod->context.method_inst != NULL;\n"
                "        fprintf(out, \"method\\t%p\\t%s\\t%s\\t%u\\t%d\\t%u\\t%u\\n\", what, name.c_str(), method->name != NULL ? method->name : \"?\",\n"
                "            (unsigned)method->parameters_count, generic ? 1 : 0, (unsigned)method->flags, (unsigned)method->slot);\n"
                "    }\n}\n\n"
                "bool il2cpp_generic_memory_names(const char *path)\n{\n    return generic_memory::Names(path, il2cpp_generic_memory_describe);\n}\n",
            ),
        ],
    )
    print("counters applied to " + root)


main()
