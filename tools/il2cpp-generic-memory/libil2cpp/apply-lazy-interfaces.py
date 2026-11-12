"""Makes a copy of libil2cpp fill the interface slots of a vtable when an interface is first called through.

usage: apply-lazy-interfaces.py <copied il2cpp folder>

The copy must already have the bench counters (apply-counters.py) and the two lazy patches. GENERIC_MEMORY_LAZY_INTERFACES=1
turns this one on in the Player.

What it changes:
- A generic instance and an array get the slots of their interfaces left empty. The pair that names the interface has
  the low bit of its pointer set, so the lookup the generated code inlines does not find it and falls to its slow path.
- The slow path fills the slots of that one interface, then gives the class a copy of its pairs in which that pair
  names the interface plainly. The readers take no lock, and what tells one of them that the slots are there has to
  be what it computes their address from: it reads the pairs through the pointer the class holds, the offset out of
  the pair, and the slot at that offset. A pointer that was changed in place would not order the read of the slot
  behind it on ARM, where two reads joined only by a branch may run in either order.
- A slot that a call through the class can reach (a virtual method that is not both final and new) is filled as before,
  so the inlined vtable read of a virtual call is untouched.
Each edit names the text it expects, so a Unity version that changed the source stops here instead of building wrong.
"""

import io
import os
import shutil
import sys


def read(path):
    raw = io.open(path, encoding="utf-8-sig", newline="").read()
    return raw.replace("\r\n", "\n"), "\r\n" in raw


def write(path, text, crlf):
    io.open(path, "w", encoding="utf-8", newline="").write(text.replace("\n", "\r\n") if crlf else text)


def edit(path, pairs):
    text, crlf = read(path)
    for old, new in pairs:
        if new in text:
            continue
        if old not in text:
            raise SystemExit("not found in " + path + ": " + old[:90])
        text = text.replace(old, new, 1)
    write(path, text, crlf)


MACROS = """// Lazy interface slots: a pair whose interfaceType has its low bit set names an interface whose slots are not filled yet.
#define IL2CPP_LAZY_INTERFACE_PENDING(type) ((((uintptr_t)(type)) & 1) != 0)
#define IL2CPP_LAZY_INTERFACE_CLASS(type) ((Il2CppClass*)(((uintptr_t)(type)) & ~(uintptr_t)1))

"""

CLASS_HELPERS = """    // Lazy interface slots: what SetupVTable did for each slot of a generic instance.
    static void SetupGenericVTableSlot(Il2CppClass* klass, Il2CppClass* genericTypeDefinition, Il2CppGenericContext* context, uint16_t i, VirtualInvokeData& target, const il2cpp::os::FastAutoLock& lock)
    {
%s    }

    // Lazy interface slots: marks the interfaces whose slots can wait. A slot that a virtual call through the class can
    // read has to be there from the start, because that read is inlined in the generated code and checks nothing. Such
    // a slot holds a method whose own slot it is; a method that is final and new is the exception, since nothing
    // overrides it and the generated code calls it directly.
    static void MarkLazyInterfaces(Il2CppClass* klass, Il2CppClass* genericTypeDefinition)
    {
        for (uint16_t i = 0; i < klass->interface_offsets_count; i++)
        {
            Il2CppRuntimeInterfaceOffsetPair* pair = klass->interfaceOffsets + i;
            Il2CppClass* itf = pair->interfaceType;
            if (itf->method_count == 0 || pair->offset < 0 || pair->offset + itf->method_count > klass->vtable_count)
                continue;

            bool canWait = true;
            for (int32_t slot = pair->offset; canWait && slot < pair->offset + itf->method_count; slot++)
            {
                const MethodInfo* method = MetadataCache::GetMethodInfoFromVTableSlot(genericTypeDefinition, (uint16_t)slot);
                if (method == NULL || method->slot != slot)
                    continue;
                canWait = (method->flags & METHOD_ATTRIBUTE_FINAL) && (method->flags & METHOD_ATTRIBUTE_NEW_SLOT);
            }

            if (canWait)
                pair->interfaceType = (Il2CppClass*)((uintptr_t)itf | 1);
        }
    }

    static bool IsLazySlot(const Il2CppClass* klass, int32_t slot)
    {
        const Il2CppRuntimeInterfaceOffsetPair* pairs = klass->interfaceOffsets;
        for (uint16_t i = 0; i < klass->interface_offsets_count; i++)
        {
            const Il2CppRuntimeInterfaceOffsetPair* pair = pairs + i;
            if (!IL2CPP_LAZY_INTERFACE_PENDING(pair->interfaceType))
                continue;
            if (slot >= pair->offset && slot < pair->offset + IL2CPP_LAZY_INTERFACE_CLASS(pair->interfaceType)->method_count)
                return true;
        }
        return false;
    }

    // Lazy interface slots: what an empty slot holds. Nothing should call it: every reader fills the slot first.
    void Class::LazyInterfaceTrap()
    {
        Exception::Raise(Exception::GetExecutionEngineException("An interface slot was called before it was filled."));
    }

    void Class::ResolveLazyInterface(Il2CppClass* klass, uint16_t index)
    {
        il2cpp::os::FastAutoLock lock(&g_MetadataLock);
        Il2CppRuntimeInterfaceOffsetPair* pairs = klass->interfaceOffsets;
        if (!IL2CPP_LAZY_INTERFACE_PENDING(pairs[index].interfaceType))
            return;

        Il2CppClass* itf = IL2CPP_LAZY_INTERFACE_CLASS(pairs[index].interfaceType);
        const int32_t first = pairs[index].offset;
        const uint16_t count = itf->method_count;

        // The slots are made aside. A slot that nothing fills keeps what it holds.
        std::vector<VirtualInvokeData> filled(klass->vtable + first, klass->vtable + first + count);
        if (klass->rank)
        {
            il2cpp::metadata::ArrayMetadata::FillLazyInterface(klass, itf, &filled[0]);
        }
        else
        {
            Il2CppClass* genericTypeDefinition = GenericClass::GetTypeDefinition(klass->generic_class);
            Il2CppGenericContext* context = &klass->generic_class->context;
            for (uint16_t at = 0; at < count; at++)
                SetupGenericVTableSlot(klass, genericTypeDefinition, context, (uint16_t)(first + at), filled[at], lock);
        }

        // A reader that finds a method in a slot goes on to read the method, so the methods are complete in memory
        // before any slot names one.
        il2cpp::os::Atomic::FullMemoryBarrier();
        for (uint16_t at = 0; at < count; at++)
        {
            klass->vtable[first + at].methodPtr = filled[at].methodPtr;
            klass->vtable[first + at].method = filled[at].method;
        }

        // The readers take no lock. They get a copy of the pairs in which this one names the interface plainly, and
        // they get it through the pointer of the class: whoever reads the new pairs computes the address of the slot
        // from what it read there, so it reads the slot after them, and the slots were complete before the copy was shown.
        Il2CppRuntimeInterfaceOffsetPair* resolved = (Il2CppRuntimeInterfaceOffsetPair*)MetadataMalloc(klass->interface_offsets_count * sizeof(Il2CppRuntimeInterfaceOffsetPair));
        memcpy(resolved, pairs, klass->interface_offsets_count * sizeof(Il2CppRuntimeInterfaceOffsetPair));
        resolved[index].interfaceType = itf;
        il2cpp::os::Atomic::FullMemoryBarrier();
        il2cpp::os::Atomic::ExchangePointer(&klass->interfaceOffsets, resolved);
    }

    const MethodInfo* Class::ResolveLazySlot(Il2CppClass* klass, Il2CppMethodSlot slot)
    {
        const Il2CppRuntimeInterfaceOffsetPair* pairs = klass->interfaceOffsets;
        for (uint16_t i = 0; i < klass->interface_offsets_count; i++)
        {
            const Il2CppRuntimeInterfaceOffsetPair* pair = pairs + i;
            if (!IL2CPP_LAZY_INTERFACE_PENDING(pair->interfaceType))
                continue;
            if (slot >= pair->offset && slot < pair->offset + IL2CPP_LAZY_INTERFACE_CLASS(pair->interfaceType)->method_count)
            {
                ResolveLazyInterface(klass, i);
                break;
            }
        }
        return klass->vtable[slot].method;
    }

"""

LOOP_HEADER = (
    "                for (uint16_t i = 0; i < genericTypeDefinition->vtable_count; i++)\n"
    "                {\n"
)
LOOP_END = "                }\n            }\n        }\n        else if (klass->rank)"
SETUP_VTABLE = "    static void SetupVTable(Il2CppClass *klass, const il2cpp::os::FastAutoLock& lock)\n    {\n"

NEW_LOOP = (
    "                // Lazy interface slots: the slots of an interface that can wait stay empty until it is called through.\n"
    "                const bool lazyInterfaces = generic_memory::LazyInterfaces() && klass->interfaceOffsets != NULL;\n"
    "                if (lazyInterfaces)\n"
    "                    MarkLazyInterfaces(klass, genericTypeDefinition);\n"
    + LOOP_HEADER
    + "                    if (lazyInterfaces && IsLazySlot(klass, i))\n"
    "                    {\n"
    "                        klass->vtable[i].method = NULL;\n"
    "                        klass->vtable[i].methodPtr = (Il2CppMethodPointer)&Class::LazyInterfaceTrap;\n"
    "                        continue;\n"
    "                    }\n"
    "                    SetupGenericVTableSlot(klass, genericTypeDefinition, context, i, klass->vtable[i], lock);\n"
)


def class_cpp(path):
    text, crlf = read(path)
    if "SetupGenericVTableSlot" not in text:
        start = text.index(LOOP_HEADER)
        body_start = start + len(LOOP_HEADER)
        end = text.index(LOOP_END, body_start)
        body = text[body_start:end]
        # The body of the loop becomes the body of a function: 20 spaces in, 8 spaces out.
        dedented = "".join((line[12:] if line.startswith(" " * 12) else line) + "\n" for line in body.split("\n")[:-1])
        # It fills the slot it is handed, which at initialization is the one in the vtable.
        if dedented.count("klass->vtable[i]") != 6:
            raise SystemExit("the loop of SetupVTable does not write the slot the way this expects: " + path)
        dedented = dedented.replace("klass->vtable[i]", "target")
        text = text[:start] + NEW_LOOP + text[end:]
        if SETUP_VTABLE not in text:
            raise SystemExit("not found in " + path + ": SetupVTable")
        text = text.replace(SETUP_VTABLE, CLASS_HELPERS % dedented + SETUP_VTABLE, 1)
    write(path, text, crlf)
    edit(
        path,
        [
            (
                "                    if (IsGenericClassAssignableFrom(klass, iter->interfaceOffsets[i].interfaceType, oklass))",
                "                    if (IsGenericClassAssignableFrom(klass, IL2CPP_LAZY_INTERFACE_CLASS(iter->interfaceOffsets[i].interfaceType), oklass))",
            ),
            (
                "                    if (iter->interfaceOffsets[i].interfaceType == klass)",
                "                    if (IL2CPP_LAZY_INTERFACE_CLASS(iter->interfaceOffsets[i].interfaceType) == klass)",
            ),
            (
                "            vtableSlotMethod = klass->vtable[virtualMethod->slot].method;\n",
                "            vtableSlotMethod = klass->vtable[virtualMethod->slot].method;\n"
                "            if (vtableSlotMethod == NULL)\n"
                "                vtableSlotMethod = ResolveLazySlot(klass, virtualMethod->slot);\n",
            ),
            (
                "namespace il2cpp\n{\nnamespace vm\n{\n",
                "// Lazy interface slots: what the generated code calls when it reads the method of a slot that is still empty.\n"
                "const MethodInfo* il2cpp_lazy_interface_slot(Il2CppClass* klass, Il2CppMethodSlot slot)\n"
                "{\n"
                "    return il2cpp::vm::Class::ResolveLazySlot(klass, slot);\n"
                "}\n\n"
                "namespace il2cpp\n{\nnamespace vm\n{\n",
            ),
            ("#include <limits>\n", "#include <limits>\n#include <vector>\n"),
        ],
    )


def main():
    root = sys.argv[1]
    lib = os.path.join(root, "libil2cpp")
    here = os.path.dirname(os.path.abspath(__file__))
    shutil.copyfile(os.path.join(here, "GenericMemoryCounters.h"), os.path.join(lib, "utils", "GenericMemoryCounters.h"))

    edit(
        os.path.join(lib, "il2cpp-class-internals.h"),
        [("typedef struct VirtualInvokeData\n", MACROS + "typedef struct VirtualInvokeData\n")],
    )
    edit(
        os.path.join(lib, "vm", "Class.h"),
        [
            (
                "        static const MethodInfo* GetVirtualMethod(Il2CppClass* klass, const MethodInfo* virtualMethod);\n",
                "        static const MethodInfo* GetVirtualMethod(Il2CppClass* klass, const MethodInfo* virtualMethod);\n"
                "        // Lazy interface slots\n"
                "        static void ResolveLazyInterface(Il2CppClass* klass, uint16_t index);\n"
                "        static const MethodInfo* ResolveLazySlot(Il2CppClass* klass, Il2CppMethodSlot slot);\n"
                "        static void LazyInterfaceTrap();\n",
            ),
        ],
    )
    class_cpp(os.path.join(lib, "vm", "Class.cpp"))
    edit(
        os.path.join(lib, "vm", "Object.cpp"),
        [
            (
                "            vtableSlotMethod = obj->klass->vtable[virtualMethod->slot].method;\n",
                "            vtableSlotMethod = obj->klass->vtable[virtualMethod->slot].method;\n"
                "            if (vtableSlotMethod == NULL)\n"
                "                vtableSlotMethod = Class::ResolveLazySlot(obj->klass, virtualMethod->slot);\n",
            ),
        ],
    )
    edit(
        os.path.join(lib, "vm", "ClassInlines.cpp"),
        [
            (
                "        if (itf->generic_class != NULL)\n"
                "        {\n"
                "            for (uint16_t i = 0; i < klass->interface_offsets_count; ++i)\n"
                "            {\n"
                "                const Il2CppRuntimeInterfaceOffsetPair* pair = klass->interfaceOffsets + i;\n"
                "                if (Class::IsGenericClassAssignableFromVariance(itf, pair->interfaceType, klass))\n"
                "                {\n",
                "        // Lazy interface slots: the inlined lookup does not see an interface whose slots are still empty. The pair is\n"
                "        // matched whether it is still marked or not: another thread may have filled it since the inlined lookup missed.\n"
                "        const Il2CppRuntimeInterfaceOffsetPair* lazyPairs = klass->interfaceOffsets;\n"
                "        for (uint16_t i = 0; i < klass->interface_offsets_count; ++i)\n"
                "        {\n"
                "            const Il2CppRuntimeInterfaceOffsetPair* pair = lazyPairs + i;\n"
                "            Il2CppClass* exact = pair->interfaceType;\n"
                "            if (IL2CPP_LAZY_INTERFACE_CLASS(exact) == itf)\n"
                "            {\n"
                "                if (IL2CPP_LAZY_INTERFACE_PENDING(exact))\n"
                "                    Class::ResolveLazyInterface(const_cast<Il2CppClass*>(klass), i);\n"
                "                return &klass->vtable[pair->offset + slot];\n"
                "            }\n"
                "        }\n"
                "\n"
                "        if (itf->generic_class != NULL)\n"
                "        {\n"
                "            for (uint16_t i = 0; i < klass->interface_offsets_count; ++i)\n"
                "            {\n"
                "                const Il2CppRuntimeInterfaceOffsetPair* pair = klass->interfaceOffsets + i;\n"
                "                Il2CppClass* candidate = pair->interfaceType;\n"
                "                if (Class::IsGenericClassAssignableFromVariance(itf, IL2CPP_LAZY_INTERFACE_CLASS(candidate), klass))\n"
                "                {\n"
                "                    if (IL2CPP_LAZY_INTERFACE_PENDING(candidate))\n"
                "                        Class::ResolveLazyInterface(const_cast<Il2CppClass*>(klass), i);\n",
            ),
        ],
    )
    edit(
        os.path.join(lib, "metadata", "ArrayMetadata.h"),
        [
            (
                "        static void SetupArrayVTable(Il2CppClass* klass, const il2cpp::os::FastAutoLock& lock);\n",
                "        static void SetupArrayVTable(Il2CppClass* klass, const il2cpp::os::FastAutoLock& lock);\n"
                "        // Lazy interface slots: makes the methods of one of the interfaces an array implements\n"
                "        static void FillLazyInterface(Il2CppClass* klass, Il2CppClass* interfaceType, VirtualInvokeData* slots);\n",
            ),
        ],
    )
    edit(
        os.path.join(lib, "metadata", "ArrayMetadata.cpp"),
        [
            ('#include "utils/Memory.h"\n', '#include "utils/Memory.h"\n#include "utils/GenericMemoryCounters.h"\n'),
            (
                "        size_t methodCount = 3 + (rank > 1 ? 2 : 1) + interfaces.size() * GetArrayGenericMethodsCount();\n",
                "        // Lazy interface slots: the methods of the interfaces are made when an interface is called through, and live only in the vtable.\n"
                "        const bool lazyInterfaces = generic_memory::LazyInterfaces();\n"
                "        size_t methodCount = 3 + (rank > 1 ? 2 : 1) + (lazyInterfaces ? 0 : interfaces.size() * GetArrayGenericMethodsCount());\n",
            ),
            (
                "        PopulateArrayGenericMethods(arrayClass, static_cast<uint16_t>(methodIndex));\n",
                "        if (!lazyInterfaces)\n"
                "            PopulateArrayGenericMethods(arrayClass, static_cast<uint16_t>(methodIndex));\n",
            ),
            (
                "        klass->interface_offsets_count = static_cast<uint16_t>(interfaceOffsetsCount);\n"
                "        klass->interfaceOffsets = newInterfaceOffsets;\n",
                "        if (generic_memory::LazyInterfaces())\n"
                "        {\n"
                "            // Lazy interface slots: every interface the array has on top of System.Array's own waits.\n"
                "            for (size_t i = arrayInterfacesCount; i < interfaceOffsetsCount; i++)\n"
                "            {\n"
                "                if (newInterfaceOffsets[i].interfaceType->method_count != 0)\n"
                "                    newInterfaceOffsets[i].interfaceType = (Il2CppClass*)((uintptr_t)newInterfaceOffsets[i].interfaceType | 1);\n"
                "            }\n"
                "            for (int32_t slot = arrayVTableSlot; slot < vtableSlot; slot++)\n"
                "                klass->vtable[slot].methodPtr = (Il2CppMethodPointer)&Class::LazyInterfaceTrap;\n"
                "        }\n"
                "        klass->interface_offsets_count = static_cast<uint16_t>(interfaceOffsetsCount);\n"
                "        klass->interfaceOffsets = newInterfaceOffsets;\n",
            ),
            (
                "    void ArrayMetadata::SetupArrayVTable(Il2CppClass* klass, const FastAutoLock& lock)\n",
                "    void ArrayMetadata::FillLazyInterface(Il2CppClass* klass, Il2CppClass* interfaceType, VirtualInvokeData* slots)\n"
                "    {\n"
                "        // One interface of PopulateArrayGenericMethods.\n"
                "        if (!interfaceType->generic_class)\n"
                "            return;\n"
                "\n"
                "        GetArrayGenericMethodsCount();\n"
                "        Il2CppClass* interfaceDefinition = GenericClass::GetTypeDefinition(interfaceType->generic_class);\n"
                "\n"
                "        Il2CppGenericContext context = { 0 };\n"
                "        context.method_inst = MetadataCache::GetGenericInst(&interfaceType->generic_class->context.class_inst->type_argv[0], 1);\n"
                "\n"
                "        for (GenericArrayMethods::const_iterator iter = s_GenericArrayMethods.begin(); iter != s_GenericArrayMethods.end(); ++iter)\n"
                "        {\n"
                "            if (iter->interfaceMethodDefinition->klass != interfaceDefinition)\n"
                "                continue;\n"
                "\n"
                "            MethodInfo* arrayMethod = ConstructGenericArrayMethod(*iter, klass, &context);\n"
                "            slots[iter->interfaceMethodDefinition->slot].method = arrayMethod;\n"
                "            slots[iter->interfaceMethodDefinition->slot].methodPtr = arrayMethod->virtualMethodPointer;\n"
                "        }\n"
                "    }\n"
                "\n"
                "    void ArrayMetadata::SetupArrayVTable(Il2CppClass* klass, const FastAutoLock& lock)\n",
            ),
        ],
    )
    edit(
        os.path.join(lib, "codegen", "il2cpp-codegen.h"),
        [
            (
                "IL2CPP_FORCE_INLINE const VirtualInvokeData& il2cpp_codegen_get_virtual_invoke_data(Il2CppMethodSlot slot, const RuntimeObject* obj)\n",
                "// Lazy interface slots: fills the interface that owns an empty slot and returns the method of the slot.\n"
                "const RuntimeMethod* il2cpp_lazy_interface_slot(RuntimeClass* klass, Il2CppMethodSlot slot);\n"
                "\n"
                "IL2CPP_FORCE_INLINE const VirtualInvokeData& il2cpp_codegen_get_virtual_invoke_data(Il2CppMethodSlot slot, const RuntimeObject* obj)\n",
            ),
            (
                "    const RuntimeMethod* methodDefinition = obj->klass->vtable[slot].method;\n"
                "    return il2cpp_codegen_get_generic_virtual_method_internal(methodDefinition, method);\n",
                "    const RuntimeMethod* methodDefinition = obj->klass->vtable[slot].method;\n"
                "    if (methodDefinition == NULL)\n"
                "        methodDefinition = il2cpp_lazy_interface_slot(obj->klass, slot);\n"
                "    return il2cpp_codegen_get_generic_virtual_method_internal(methodDefinition, method);\n",
            ),
            (
                "    return pThis->klass->vtable[slot].method;\n",
                "    const RuntimeMethod* slotMethod = pThis->klass->vtable[slot].method;\n"
                "    return slotMethod != NULL ? slotMethod : il2cpp_lazy_interface_slot(pThis->klass, slot);\n",
            ),
        ],
    )
    print("lazy interface slots applied to " + root)


main()
