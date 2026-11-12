// Lazy metadata for libil2cpp: the metadata of a generic instance is made when it is used, not when its class is
// initialized. This file is copied into libil2cpp/vm and included by the sources it changes; the changes themselves are
// single lines put into those sources by Il2CppLazyMetadata.cs, each of which calls into here or stands for itself.
//
// - members: Class::InitLocked no longer inflates every method, property and event of a generic instance. The runtime
//   makes them where it reads them, as it already did for a class that was not initialized yet.
// - rgctx: an entry of a generic context is resolved when the generated code first reads it.
// - interfaces: the vtable slots and the classes of the interfaces a class implements are made when an interface is
//   used. That part is in Il2CppLazyInterfaces.h, which the generated code does not read.
//
// What is made late is made once and shown complete. The readers of these tables take no lock. They used to be
// covered by the memory barrier that ends the initialization of a class, and no longer are.
//
// The part every reader of this file gets is small, because il2cpp-codegen.h includes it and so does all the
// generated code. A source that needs more says so before the include:
//   IL2CPP_LAZY_METADATA_IN_CLASS_CPP              vm/Class.cpp
//   IL2CPP_LAZY_METADATA_IN_GENERIC_METADATA_CPP   metadata/GenericMetadata.cpp

#ifndef IL2CPP_LAZY_METADATA_H
#define IL2CPP_LAZY_METADATA_H

#include "il2cpp-config.h"
#include "il2cpp-class-internals.h"

// IL2CPP_LAZY_METADATA_SWITCHED is defined by the bench, which measures one Player with each change on and off: the
// change is then taken only when the Player was started with its environment variable set. A game is built without
// it, and both changes are always taken.
#if defined(IL2CPP_LAZY_METADATA_SWITCHED) && IL2CPP_LAZY_METADATA_SWITCHED
#include "utils/GenericMemoryCounters.h"
#define IL2CPP_LAZY_MEMBERS() (generic_memory::LazyMethods())
#define IL2CPP_LAZY_RGCTX() (generic_memory::LazyRgctx())
#else
#define IL2CPP_LAZY_MEMBERS() (true)
#define IL2CPP_LAZY_RGCTX() (true)
#endif

// Resolves the entry of an rgctx that the generated code found empty. Defined in metadata/GenericMetadata.cpp.
const Il2CppRGCTXData* il2cpp_lazy_rgctx_resolve(const Il2CppRGCTXData* rgctxVar, int32_t index);

// What the generated code reads an rgctx through. An entry that was resolved is never empty again, so the test costs
// one comparison once it was.
inline const Il2CppRGCTXData& il2cpp_lazy_rgctx_entry(const Il2CppRGCTXData* rgctxVar, int32_t index)
{
    if (rgctxVar[index].rgctxDataDummy == NULL)
        il2cpp_lazy_rgctx_resolve(rgctxVar, index);
    return rgctxVar[index];
}

// The method of a vtable slot that the generated code read by its number and found empty: the slots of an interface
// are filled when the interface is used (Il2CppLazyInterfaces.h). Defined at the end of vm/Class.cpp.
const MethodInfo* il2cpp_lazy_interface_slot(Il2CppClass* klass, Il2CppMethodSlot slot);

#endif // IL2CPP_LAZY_METADATA_H

#if defined(IL2CPP_LAZY_METADATA_IN_CLASS_CPP) && !defined(IL2CPP_LAZY_METADATA_CLASS_CPP_DONE)
#define IL2CPP_LAZY_METADATA_CLASS_CPP_DONE

#include "metadata/GenericMethod.h"
#include "vm/Class.h"
#include "vm/GenericClass.h"
#include <string.h>

namespace il2cpp
{
namespace vm
{
    // What LazyLookUpInDefinition returns for "this class was looked through and does not have it".
    static const MethodInfo s_LazyMethodNotThere = {};

    // A lookup by name (the static constructor, the Invoke of a delegate, a message the engine sends) in a generic
    // instance whose methods were not made: the method is found in the definition and that one alone is inflated.
    // Returns NULL when the class is not such an instance or the lookup is by signature, and the caller goes on as it
    // always did. A lookup by signature compares inflated parameter types, so it needs the methods themselves.
    static const MethodInfo* LazyLookUpInDefinition(Il2CppClass* klass, const char* name, int argsCount, int32_t flags, const Il2CppType** argTypes)
    {
        if (klass->generic_class == NULL || klass->methods != NULL || argTypes != NULL || !IL2CPP_LAZY_MEMBERS())
            return NULL;

        void* iter = NULL;
        Il2CppClass* definition = GenericClass::GetTypeDefinition(klass->generic_class);
        while (const MethodInfo* method = Class::GetMethods(definition, &iter))
        {
            if (method->name[0] == name[0] &&
                (argsCount == Class::IgnoreNumberOfArguments || method->parameters_count == argsCount) &&
                ((method->flags & flags) == flags) &&
                !strcmp(name, method->name))
                return il2cpp::metadata::GenericMethod::GetMethod(method, klass->generic_class->context.class_inst, NULL);
        }

        return &s_LazyMethodNotThere;
    }
} // namespace vm
} // namespace il2cpp

#endif // IL2CPP_LAZY_METADATA_IN_CLASS_CPP

#if defined(IL2CPP_LAZY_METADATA_IN_GENERIC_METADATA_CPP) && !defined(IL2CPP_LAZY_METADATA_GENERIC_METADATA_CPP_DONE)
#define IL2CPP_LAZY_METADATA_GENERIC_METADATA_CPP_DONE

#include "metadata/GenericMetadata.h"
#include "os/Atomic.h"
#include "os/Mutex.h"
#include "vm/MetadataAlloc.h"
#include "vm/MetadataCache.h"
#include "vm/MetadataLock.h"
#include <vector>

// The entries of an rgctx stay empty until they are read. GenericMetadata::InflateRGCTXLocked keeps its loop, which
// knows how each kind of entry is resolved, and is told what to do with it:
// - called as before, it returns the values with no entry resolved. Three slots ahead of them say where the entries
//   come from: the image, the token and the generic context.
// - called with a place aside and an index, it resolves that one entry into the place.
namespace il2cpp
{
namespace metadata
{
    // The values InflateRGCTXLocked fills.
    static Il2CppRGCTXData* LazyRgctxValues(uint32_t count, const Il2CppImage* image, uint32_t token, const Il2CppGenericContext* context, Il2CppRGCTXData* aside)
    {
        if (aside != NULL)
            return aside;
        if (!IL2CPP_LAZY_RGCTX())
            return (Il2CppRGCTXData*)il2cpp::vm::MetadataCalloc(count, sizeof(Il2CppRGCTXData));

        Il2CppRGCTXData* block = (Il2CppRGCTXData*)il2cpp::vm::MetadataCalloc(count + 3, sizeof(Il2CppRGCTXData));
        block[0].rgctxDataDummy = (void*)image;
        block[1].rgctxDataDummy = (void*)(uintptr_t)token;
        block[2].rgctxDataDummy = (void*)context;
        return block + 3;
    }

    // Whether the loop of InflateRGCTXLocked leaves the entry at an index alone.
    static inline bool LazyRgctxLeaves(int32_t index, const Il2CppRGCTXData* aside, int32_t only)
    {
        if (aside != NULL)
            return index != only;
        return IL2CPP_LAZY_RGCTX();
    }
} // namespace metadata
} // namespace il2cpp

const Il2CppRGCTXData* il2cpp_lazy_rgctx_resolve(const Il2CppRGCTXData* rgctxVar, int32_t index)
{
    il2cpp::os::FastAutoLock lock(&il2cpp::vm::g_MetadataLock);
    Il2CppRGCTXData* values = const_cast<Il2CppRGCTXData*>(rgctxVar);
    // An entry may be empty because that is what it resolves to; without the lazy form every empty entry is one of those.
    if (!IL2CPP_LAZY_RGCTX() || values[index].rgctxDataDummy != NULL)
        return rgctxVar;

    const Il2CppImage* image = (const Il2CppImage*)values[-3].rgctxDataDummy;
    const uint32_t token = (uint32_t)(uintptr_t)values[-2].rgctxDataDummy;
    const Il2CppGenericContext* context = (const Il2CppGenericContext*)values[-1].rgctxDataDummy;

    // The readers take no lock, and resolving a class stores it before its layout is known. So the entry is resolved
    // aside and shown only once it is complete, the way GenericClass::CreateClass shows a class.
    std::vector<Il2CppRGCTXData> aside(il2cpp::vm::MetadataCache::GetRGCTXs(image, token).count);
    il2cpp::metadata::GenericMetadata::InflateRGCTXLocked(image, token, context, lock, &aside[0], index);
    il2cpp::os::Atomic::ExchangePointer(&values[index].rgctxDataDummy, aside[index].rgctxDataDummy);
    return rgctxVar;
}

#endif // IL2CPP_LAZY_METADATA_IN_GENERIC_METADATA_CPP
