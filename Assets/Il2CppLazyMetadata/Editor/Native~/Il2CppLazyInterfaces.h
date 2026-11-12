// Lazy interfaces for libil2cpp: what a generic instance or an array keeps for the interfaces it implements is made
// when an interface is used, not when the class is initialized. This file is copied into libil2cpp/vm beside
// Il2CppLazyMetadata.h and included by the sources it changes. il2cpp-codegen.h does not include it, so the generated
// code is compiled without it; the changes themselves are single lines put into those sources by Il2CppLazyMetadata.cs.
//
// - slots: the vtable slots of an interface stay empty until the interface is called through. Filling a slot is what
//   makes its method (List<Foo>'s IList<Foo>.get_Item and the like), and most interfaces of a class are never called.
// - classes: the class of an interface (IList<Foo>, for a List<Foo>) is not made for the tables of the class that
//   implements it. Whoever uses the interface makes its class on the way there, and the tables take that one.
//
// An entry of Il2CppClass::implementedInterfaces, and the interfaceType of a pair of Il2CppClass::interfaceOffsets, is
// therefore not always a class. Its two low bits say what it is:
//   00  the class of the interface; its slots are filled
//   01  the class of the interface; its slots are empty                                       (interfaceOffsets only)
//   10  the Il2CppGenericClass of an interface whose class was not made; its slots are filled
//   11  the Il2CppGenericClass of an interface whose class was not made; its slots are empty  (interfaceOffsets only)
// Nothing but this file knows the marks: every reader of the two tables asks here. The lookup that the generated code
// inlines (ClassInlines.h) compares a pair with the class of the interface it calls, so it never matches a marked pair
// and falls to its slow path, which asks here as well.
//
// What is made late is made once, under the metadata lock, and shown complete. The readers take no lock:
// - A pair whose slots were filled late is shown through a new copy of the pairs. A reader that finds the interface in
//   the copy computes the address of the slot from what it read there, so it reads the slot after the copy, and the
//   slots were complete before the copy was shown. A mark taken off in place would not order those two reads on ARM.
// - A pair whose slots were there from the start, and an entry of implementedInterfaces, only waits for its class. That
//   one is written in place: whatever a reader finds, the class or the mark, is right.
//
// The bodies are compiled where the sources say so before the include:
//   IL2CPP_LAZY_INTERFACES_IN_CLASS_CPP            the end of vm/Class.cpp, which keeps SetupVTable to itself
//   IL2CPP_LAZY_INTERFACES_IN_ARRAY_METADATA_CPP   the end of metadata/ArrayMetadata.cpp, which keeps the array methods

#ifndef IL2CPP_LAZY_INTERFACES_H
#define IL2CPP_LAZY_INTERFACES_H

#include "vm/Il2CppLazyMetadata.h"
#include "il2cpp-class-internals.h"
#include "vm/Class.h"
#include "vm/GenericClass.h"

// As in Il2CppLazyMetadata.h: the bench takes each change only when the Player was started with its environment
// variable set, and counts how often each branch below is taken. A game is built with both changes always taken.
#if defined(IL2CPP_LAZY_METADATA_SWITCHED) && IL2CPP_LAZY_METADATA_SWITCHED
#define IL2CPP_LAZY_INTERFACE_SLOTS() (generic_memory::LazyInterfaces())
#define IL2CPP_LAZY_INTERFACE_CLASSES() (generic_memory::LazyInterfaceClasses())
#define IL2CPP_LAZY_BRANCH(name) generic_memory::Branch(name)
#else
#define IL2CPP_LAZY_INTERFACE_SLOTS() (true)
#define IL2CPP_LAZY_INTERFACE_CLASSES() (true)
#define IL2CPP_LAZY_BRANCH(name) ((void)0)
#endif

namespace il2cpp
{
namespace metadata
{
    // Both are defined at the end of metadata/ArrayMetadata.cpp, and called with the metadata lock held.

    // Makes the methods an array has for one of its generic interfaces and puts them where their slots are counted
    // from. A slot that has its method keeps it.
    void LazyArrayInterfaceSlots(Il2CppClass* klass, const Il2CppGenericClass* interfaceType, VirtualInvokeData* slots);

    // Makes the methods of an array, which nothing but a question about the methods of the type needs: its
    // constructors and accessors, and one for every method of every generic interface it implements.
    void LazyArrayMethods(Il2CppClass* klass);
} // namespace metadata

namespace vm
{
    class LazyInterfaces
    {
    public:
        static const uintptr_t kSlotsEmpty = 1;
        static const uintptr_t kClassUnmade = 2;
        static const uintptr_t kMarks = 3;

        // ---- What reads an entry

        // Whether an entry stands for an interface. Makes nothing.
        static inline bool Is(const Il2CppClass* entry, const Il2CppClass* itf)
        {
            const uintptr_t bits = (uintptr_t)entry;
            if ((bits & kMarks) == 0)
                return entry == itf;
            if ((bits & kClassUnmade) == 0)
                return (const Il2CppClass*)(bits & ~kMarks) == itf;

            // Two Il2CppGenericClass are not taken to be one because they stand for one type; a generic inst is unique.
            const Il2CppGenericClass* unmade = (const Il2CppGenericClass*)(bits & ~kMarks);
            const Il2CppGenericClass* asked = itf->generic_class;
            return asked != NULL && (asked == unmade || (asked->context.class_inst == unmade->context.class_inst && GenericClass::HasSameGenericTypeDefinition(asked, unmade)));
        }

        // Class::IsGenericClassAssignableFrom for an entry: whether an object of implementingClass, which has the entry
        // among its interfaces, can be assigned to the generic interface itf. A cast runs this for every interface of
        // the class, so what settles most entries is here: the entry is itf, or it is no instance of the generic
        // interface itf is an instance of.
        static inline bool IsAssignable(const Il2CppClass* itf, const Il2CppClass* entry, const Il2CppClass* implementingClass)
        {
            const uintptr_t bits = (uintptr_t)entry;
            if ((bits & kMarks) == 0)
                return Class::IsGenericClassAssignableFrom(itf, entry, implementingClass);
            if (Is(entry, itf))
                return true;

            const Il2CppGenericClass* other = (bits & kClassUnmade) != 0 ? (const Il2CppGenericClass*)(bits & ~kMarks) : ((const Il2CppClass*)(bits & ~kMarks))->generic_class;
            if (other == NULL || !GenericClass::HasSameGenericTypeDefinition(other, itf->generic_class))
                return false;
            return IsAssignableByVariance(itf, entry, implementingClass);
        }

        // The rest of IsAssignable: an entry that is another instance of the generic interface itf is an instance of.
        static bool IsAssignableByVariance(const Il2CppClass* itf, const Il2CppClass* entry, const Il2CppClass* implementingClass);

        // How many methods the interface of an entry has, which is how many slots it takes.
        static uint16_t MethodCount(const Il2CppClass* entry);

        // The generic class of the interface of an entry, or NULL when the interface is not a generic instance.
        static const Il2CppGenericClass* GenericClassOf(const Il2CppClass* entry);

        // The method a slot of an array has already, for the table of the methods of the array: a slot that was filled
        // when its interface was called through keeps its method, and the table lists that one. NULL for an empty slot.
        static inline MethodInfo* MethodInSlot(const VirtualInvokeData& slot)
        {
            if (slot.method != NULL)
                IL2CPP_LAZY_BRANCH("array method taken from its slot");
            return const_cast<MethodInfo*>(slot.method);
        }

        // Makes every entry of klass->implementedInterfaces a class, for whoever lists the interfaces of a type.
        static void MakeImplemented(Il2CppClass* klass);

        // Makes every entry of both tables of a class what it was before this file: a class, with its slots filled.
        static void MakeAll(Il2CppClass* klass);

        // The slow path of the lookup of the generated code, for the pair that is itf itself: the slot, with the slots
        // of itf filled and the pair shown without marks. NULL when klass has no such pair.
        static const VirtualInvokeData* ExactInvokeData(const Il2CppClass* klass, const Il2CppClass* itf, Il2CppMethodSlot slot);

        // The same slow path, for a pair that is not itf but may stand in for it by variance: whether it does. Its
        // slots are filled when it does. A call through a variant interface runs this for every pair, each time.
        static inline bool IsVariantAndFilled(const Il2CppClass* klass, uint16_t index, const Il2CppClass* itf)
        {
            const Il2CppClass* entry = klass->interfaceOffsets[index].interfaceType;
            const uintptr_t bits = (uintptr_t)entry;
            if ((bits & kMarks) == 0)
                return Class::IsGenericClassAssignableFromVariance(itf, entry, klass);

            const Il2CppGenericClass* other = (bits & kClassUnmade) != 0 ? (const Il2CppGenericClass*)(bits & ~kMarks) : ((const Il2CppClass*)(bits & ~kMarks))->generic_class;
            if (other == NULL || !GenericClass::HasSameGenericTypeDefinition(other, itf->generic_class))
                return false;
            return FillIfVariant(klass, index, itf);
        }

        // The rest of IsVariantAndFilled: a marked pair that is another instance of the generic interface itf is an instance of.
        static bool FillIfVariant(const Il2CppClass* klass, uint16_t index, const Il2CppClass* itf);

        // The method of a vtable slot that was read by its number and found empty.
        static const MethodInfo* SlotMethod(Il2CppClass* klass, Il2CppMethodSlot slot);

        // ---- What writes an entry

        // The entry of implementedInterfaces for an interface of the definition of a generic instance.
        static Il2CppClass* ImplementedEntry(const Il2CppType* definitionInterface, const Il2CppGenericContext* context);

        // The same for a pair of interfaceOffsets, marked when its slots can wait.
        static Il2CppClass* OffsetEntry(const Il2CppClass* klass, const Il2CppClass* definition, const Il2CppType* definitionInterface, int32_t offset, const Il2CppGenericContext* context);

        // The entry for one of the generic interfaces an array implements on its own (IList<T> and the others).
        static Il2CppClass* ArrayEntry(Il2CppClass* definition, const Il2CppType** types, uint32_t typeCount);

        // Marks the pairs an array has on top of System.Array's, and leaves their slots empty.
        static void MarkArraySlots(Il2CppClass* klass, Il2CppRuntimeInterfaceOffsetPair* pairs, size_t firstPair, size_t endPair, int32_t firstSlot, int32_t endSlot);

        // Whether the loop of SetupVTable leaves a slot alone. While a class is initialized (count is 0) that is a
        // slot of a marked pair, which is emptied here. While the slots of one interface are filled it is every other slot.
        static bool LeavesSlot(Il2CppClass* klass, uint16_t slot, int32_t first, int32_t count);

    private:
        static void Fill(Il2CppClass* klass, uint16_t index, Il2CppClass* made);
        // What an empty slot holds. Nothing calls it: every reader of a slot fills it first.
        static void Trap();
    };
} // namespace vm
} // namespace il2cpp

#endif // IL2CPP_LAZY_INTERFACES_H

#if defined(IL2CPP_LAZY_INTERFACES_IN_CLASS_CPP) && !defined(IL2CPP_LAZY_INTERFACES_CLASS_CPP_DONE)
#define IL2CPP_LAZY_INTERFACES_CLASS_CPP_DONE

#include "metadata/GenericMetadata.h"
#include "os/Atomic.h"
#include "os/Mutex.h"
#include "vm/Class.h"
#include "vm/Exception.h"
#include "vm/GenericClass.h"
#include "vm/MetadataAlloc.h"
#include "vm/MetadataCache.h"
#include "vm/MetadataLock.h"
#include "vm/Type.h"
#include "il2cpp-tabledefs.h"
#include <string.h>
#include <vector>

// Declared in Il2CppLazyMetadata.h, for the two places of il2cpp-codegen.h that read a slot by its number.
const MethodInfo* il2cpp_lazy_interface_slot(Il2CppClass* klass, Il2CppMethodSlot slot)
{
    return il2cpp::vm::LazyInterfaces::SlotMethod(klass, slot);
}

namespace il2cpp
{
namespace vm
{
    // The generic class of an entry whose class was not made, or NULL.
    static inline Il2CppGenericClass* LazyEntryUnmade(const Il2CppClass* entry)
    {
        const uintptr_t bits = (uintptr_t)entry;
        return (bits & LazyInterfaces::kClassUnmade) != 0 ? (Il2CppGenericClass*)(bits & ~LazyInterfaces::kMarks) : NULL;
    }

    // The class of an entry that has one, or NULL.
    static inline Il2CppClass* LazyEntryMade(const Il2CppClass* entry)
    {
        const uintptr_t bits = (uintptr_t)entry;
        return (bits & LazyInterfaces::kClassUnmade) != 0 ? NULL : (Il2CppClass*)(bits & ~LazyInterfaces::kMarks);
    }

    static inline bool LazyEntrySlotsEmpty(const Il2CppClass* entry)
    {
        return ((uintptr_t)entry & LazyInterfaces::kSlotsEmpty) != 0;
    }

    // Whether no instance of a generic interface can stand in for another instance of it: every parameter of the
    // interface is invariant, and the rule that lets an array of one value type pass for an array of another (int[] for
    // IList<uint>) does not apply. What Class::IsGenericClassAssignableFromVariance would find out, without the class.
    static bool LazyVarianceIsRuledOut(const Il2CppGenericClass* unmade, const Il2CppGenericClass* asked, const Il2CppClass* implementingClass)
    {
        Il2CppClass* definition = GenericClass::GetTypeDefinition(asked);
        Class::Init(definition);
        const GenericParameterFlags* flags = definition->genericParameterFlags;
        const bool arrayRule = implementingClass->rank || implementingClass->declaringType == il2cpp_defaults.array_class;
        for (uint32_t i = 0; i < asked->context.class_inst->type_argc; i++)
        {
            if ((flags->flags[i] & IL2CPP_GENERIC_PARAMETER_ATTRIBUTE_VARIANCE_MASK) != IL2CPP_GENERIC_PARAMETER_ATTRIBUTE_NON_VARIANT)
                return false;
            if (arrayRule && (!Type::IsReference(asked->context.class_inst->type_argv[i]) || !Type::IsReference(unmade->context.class_inst->type_argv[i])))
                return false;
        }

        return true;
    }

    // The class of an entry for a question about itf, which the entry is not. One that was not made is made only when
    // it may stand in for itf by variance: it is an instance of the generic interface itf is an instance of, and that
    // interface is variant (or an array makes it so).
    static Il2CppClass* LazyEntryClassFor(const Il2CppClass* entry, const Il2CppClass* itf, const Il2CppClass* implementingClass)
    {
        Il2CppGenericClass* unmade = LazyEntryUnmade(entry);
        if (unmade == NULL)
            return LazyEntryMade(entry);
        if (itf->generic_class == NULL || !GenericClass::HasSameGenericTypeDefinition(unmade, itf->generic_class))
            return NULL;
        // Made since, by whoever used the interface: there is nothing to save any more.
        if (Il2CppClass* made = os::Atomic::LoadPointerRelaxed(&unmade->cached_class))
            return made;
        if (LazyVarianceIsRuledOut(unmade, itf->generic_class, implementingClass))
        {
            IL2CPP_LAZY_BRANCH("variance ruled out without the class");
            return NULL;
        }

        IL2CPP_LAZY_BRANCH("class made to compare by variance");
        return GenericClass::GetClass(unmade);
    }

    // The entry for an interface of the definition of a generic instance, with the arguments of the instance put in.
    static Il2CppClass* LazyEntryFor(const Il2CppType* type, const Il2CppGenericContext* context)
    {
        if (IL2CPP_LAZY_INTERFACE_CLASSES() && type->type == IL2CPP_TYPE_GENERICINST && type->data.generic_class->context.class_inst != NULL)
        {
            // What GenericMetadata::InflateIfNeeded does for a generic instance, without the Il2CppType it wraps the result in.
            Il2CppGenericClass* generic = il2cpp::metadata::GenericMetadata::GetGenericClass(
                GenericClass::GetTypeDefinition(type->data.generic_class),
                il2cpp::metadata::GenericMetadata::GetInflatedGenericIntance(type->data.generic_class->context.class_inst, context, false));
            IL2CPP_ASSERT(((uintptr_t)generic & LazyInterfaces::kMarks) == 0);
            if (Il2CppClass* made = os::Atomic::LoadPointerRelaxed(&generic->cached_class))
                return made;

            IL2CPP_LAZY_BRANCH("entry made without its class");
            return (Il2CppClass*)((uintptr_t)generic | LazyInterfaces::kClassUnmade);
        }

        return Class::FromIl2CppType(il2cpp::metadata::GenericMetadata::InflateIfNeeded(type, context, false));
    }

    // Whether the slots of an interface of a generic instance can wait. A slot that a virtual call through the class
    // reads has to be there from the start, because that read is inlined in the generated code and checks nothing.
    // Such a slot holds a method whose own slot it is. A method that is final and new is the exception: nothing
    // overrides it, and the generated code calls it directly.
    static bool LazySlotsCanWait(const Il2CppClass* klass, const Il2CppClass* definition, const Il2CppClass* entry, int32_t offset)
    {
        // The slots of an interface's own class, and of a class that stands for a COM object, are read by their numbers (RCW.cpp).
        if (Class::IsInterface(klass) || klass->is_import_or_windows_runtime)
            return false;

        const int32_t count = LazyInterfaces::MethodCount(entry);
        if (count == 0 || offset < 0 || offset + count > definition->vtable_count)
            return false;

        for (int32_t slot = offset; slot < offset + count; slot++)
        {
            const MethodInfo* method = MetadataCache::GetMethodInfoFromVTableSlot(definition, (uint16_t)slot);
            if (method == NULL || method->slot != slot)
                continue;
            if (!(method->flags & METHOD_ATTRIBUTE_FINAL) || !(method->flags & METHOD_ATTRIBUTE_NEW_SLOT))
                return false;
        }

        return true;
    }

    bool LazyInterfaces::IsAssignableByVariance(const Il2CppClass* itf, const Il2CppClass* entry, const Il2CppClass* implementingClass)
    {
        const Il2CppClass* candidate = LazyEntryClassFor(entry, itf, implementingClass);
        return candidate != NULL && candidate != itf && Class::IsGenericClassAssignableFromVariance(itf, candidate, implementingClass);
    }

    uint16_t LazyInterfaces::MethodCount(const Il2CppClass* entry)
    {
        if (const Il2CppGenericClass* unmade = LazyEntryUnmade(entry))
            return GenericClass::GetTypeDefinition(unmade)->method_count;
        return LazyEntryMade(entry)->method_count;
    }

    const Il2CppGenericClass* LazyInterfaces::GenericClassOf(const Il2CppClass* entry)
    {
        if (const Il2CppGenericClass* unmade = LazyEntryUnmade(entry))
            return unmade;
        return LazyEntryMade(entry)->generic_class;
    }

    void LazyInterfaces::MakeImplemented(Il2CppClass* klass)
    {
        Il2CppClass** entries = klass->implementedInterfaces;
        for (uint16_t i = 0; i < klass->interfaces_count; i++)
        {
            Il2CppGenericClass* unmade = LazyEntryUnmade(entries[i]);
            if (unmade == NULL)
                continue;

            // GenericClass::GetClass shows a class when it is complete, and every thread that gets here stores the same one.
            IL2CPP_LAZY_BRANCH("class made to list the interfaces");
            os::Atomic::ExchangePointer(&entries[i], GenericClass::GetClass(unmade));
        }
    }

    void LazyInterfaces::MakeAll(Il2CppClass* klass)
    {
        MakeImplemented(klass);
        for (uint16_t i = 0; i < klass->interface_offsets_count; i++)
        {
            const Il2CppClass* entry = klass->interfaceOffsets[i].interfaceType;
            if (((uintptr_t)entry & kMarks) == 0)
                continue;

            IL2CPP_LAZY_BRANCH("pair made whole for COM");
            Il2CppGenericClass* unmade = LazyEntryUnmade(entry);
            Fill(klass, i, unmade != NULL ? GenericClass::GetClass(unmade) : NULL);
        }
    }

    const VirtualInvokeData* LazyInterfaces::ExactInvokeData(const Il2CppClass* klass, const Il2CppClass* itf, Il2CppMethodSlot slot)
    {
        if (!IL2CPP_LAZY_INTERFACE_SLOTS() && !IL2CPP_LAZY_INTERFACE_CLASSES())
            return NULL;

        // The pair is matched marked or not: another thread may have taken the marks off since the inlined lookup missed it.
        const Il2CppRuntimeInterfaceOffsetPair* pairs = klass->interfaceOffsets;
        for (uint16_t i = 0; i < klass->interface_offsets_count; i++)
        {
            const Il2CppClass* entry = pairs[i].interfaceType;
            if (!Is(entry, itf))
                continue;

            if (((uintptr_t)entry & kMarks) != 0)
                Fill(const_cast<Il2CppClass*>(klass), i, const_cast<Il2CppClass*>(itf));
            return &klass->vtable[pairs[i].offset + slot];
        }

        return NULL;
    }

    bool LazyInterfaces::FillIfVariant(const Il2CppClass* klass, uint16_t index, const Il2CppClass* itf)
    {
        const Il2CppClass* entry = klass->interfaceOffsets[index].interfaceType;
        if (((uintptr_t)entry & kMarks) == 0)
            return Class::IsGenericClassAssignableFromVariance(itf, entry, klass);

        Il2CppClass* candidate = LazyEntryClassFor(entry, itf, klass);
        if (candidate == NULL || candidate == itf || !Class::IsGenericClassAssignableFromVariance(itf, candidate, klass))
            return false;

        IL2CPP_LAZY_BRANCH("call matched a marked pair by variance");
        Fill(const_cast<Il2CppClass*>(klass), index, candidate);
        return true;
    }

    const MethodInfo* LazyInterfaces::SlotMethod(Il2CppClass* klass, Il2CppMethodSlot slot)
    {
        const Il2CppRuntimeInterfaceOffsetPair* pairs = klass->interfaceOffsets;
        for (uint16_t i = 0; i < klass->interface_offsets_count; i++)
        {
            if (!LazyEntrySlotsEmpty(pairs[i].interfaceType))
                continue;
            if (slot >= pairs[i].offset && slot < pairs[i].offset + MethodCount(pairs[i].interfaceType))
            {
                IL2CPP_LAZY_BRANCH("slots filled for a read by number");
                Fill(klass, i, NULL);
                break;
            }
        }

        return klass->vtable[slot].method;
    }

    // Takes the marks off a pair: fills its slots if they are empty, and shows it with the class of its interface. The
    // caller hands that class over when it has it; without one, a pair whose class was not made keeps that mark.
    void LazyInterfaces::Fill(Il2CppClass* klass, uint16_t index, Il2CppClass* made)
    {
        os::FastAutoLock lock(&g_MetadataLock);

        // Read again under the lock: another thread may have been first.
        Il2CppRuntimeInterfaceOffsetPair* pairs = klass->interfaceOffsets;
        const Il2CppClass* entry = pairs[index].interfaceType;
        if (((uintptr_t)entry & kMarks) == 0)
            return;
        if (made == NULL)
            made = LazyEntryMade(entry);

        if (!LazyEntrySlotsEmpty(entry))
        {
            // The slots are there since the class was initialized, so no reader depends on the order in which it sees this.
            if (made != NULL)
            {
                IL2CPP_LAZY_BRANCH("pair shown in place");
                os::Atomic::ExchangePointer(&pairs[index].interfaceType, made);
            }
            return;
        }

        const int32_t first = pairs[index].offset;
        const uint16_t count = MethodCount(entry);
        if (klass->rank)
        {
            // Made aside: a slot names a method only once the method is complete in memory.
            std::vector<VirtualInvokeData> filled(klass->vtable + first, klass->vtable + first + count);
            il2cpp::metadata::LazyArrayInterfaceSlots(klass, GenericClassOf(entry), &filled[0]);
            os::Atomic::FullMemoryBarrier();
            for (uint16_t at = 0; at < count; at++)
            {
                klass->vtable[first + at].methodPtr = filled[at].methodPtr;
                klass->vtable[first + at].method = filled[at].method;
            }
        }
        else
        {
            SetupVTable(klass, lock, first, count);
        }

        // The slots are complete in memory before the copy that says so is shown.
        Il2CppRuntimeInterfaceOffsetPair* shown = (Il2CppRuntimeInterfaceOffsetPair*)MetadataMalloc(klass->interface_offsets_count * sizeof(Il2CppRuntimeInterfaceOffsetPair));
        memcpy(shown, pairs, klass->interface_offsets_count * sizeof(Il2CppRuntimeInterfaceOffsetPair));
        shown[index].interfaceType = made != NULL ? made : (Il2CppClass*)((uintptr_t)entry & ~kSlotsEmpty);
        os::Atomic::FullMemoryBarrier();
        IL2CPP_LAZY_BRANCH("pair shown in a copy");
        os::Atomic::ExchangePointer(&klass->interfaceOffsets, shown);
    }

    void LazyInterfaces::Trap()
    {
        Exception::Raise(Exception::GetExecutionEngineException("An interface slot was called before it was filled."));
    }

    Il2CppClass* LazyInterfaces::ImplementedEntry(const Il2CppType* definitionInterface, const Il2CppGenericContext* context)
    {
        return LazyEntryFor(definitionInterface, context);
    }

    Il2CppClass* LazyInterfaces::OffsetEntry(const Il2CppClass* klass, const Il2CppClass* definition, const Il2CppType* definitionInterface, int32_t offset, const Il2CppGenericContext* context)
    {
        Il2CppClass* entry = LazyEntryFor(definitionInterface, context);
        if (IL2CPP_LAZY_INTERFACE_SLOTS() && LazySlotsCanWait(klass, definition, entry, offset))
        {
            IL2CPP_LAZY_BRANCH("pair made with empty slots");
            entry = (Il2CppClass*)((uintptr_t)entry | kSlotsEmpty);
        }

        return entry;
    }

    Il2CppClass* LazyInterfaces::ArrayEntry(Il2CppClass* definition, const Il2CppType** types, uint32_t typeCount)
    {
        // Only together with the slots: the bench can take the classes without the slots, and that combination was
        // checked with the classes of the interfaces of an array made as they always were.
        if (!IL2CPP_LAZY_INTERFACE_CLASSES() || !IL2CPP_LAZY_INTERFACE_SLOTS())
            return Class::GetInflatedGenericInstanceClass(definition, types, typeCount);

        // What Class::GetInflatedGenericInstanceClass does, up to the class.
        Il2CppGenericClass* generic = il2cpp::metadata::GenericMetadata::GetGenericClass(definition, MetadataCache::GetGenericInst(types, typeCount));
        IL2CPP_ASSERT(((uintptr_t)generic & kMarks) == 0);
        if (Il2CppClass* made = os::Atomic::LoadPointerRelaxed(&generic->cached_class))
            return made;

        IL2CPP_LAZY_BRANCH("array entry made without its class");
        return (Il2CppClass*)((uintptr_t)generic | kClassUnmade);
    }

    void LazyInterfaces::MarkArraySlots(Il2CppClass* klass, Il2CppRuntimeInterfaceOffsetPair* pairs, size_t firstPair, size_t endPair, int32_t firstSlot, int32_t endSlot)
    {
        if (!IL2CPP_LAZY_INTERFACE_SLOTS())
            return;

        for (size_t i = firstPair; i < endPair; i++)
        {
            if (MethodCount(pairs[i].interfaceType) != 0)
                pairs[i].interfaceType = (Il2CppClass*)((uintptr_t)pairs[i].interfaceType | kSlotsEmpty);
        }
        for (int32_t slot = firstSlot; slot < endSlot; slot++)
            klass->vtable[slot].methodPtr = (Il2CppMethodPointer) & LazyInterfaces::Trap;
    }

    bool LazyInterfaces::LeavesSlot(Il2CppClass* klass, uint16_t slot, int32_t first, int32_t count)
    {
        if (count != 0)
            return slot < first || slot >= first + count;
        if (!IL2CPP_LAZY_INTERFACE_SLOTS())
            return false;

        const Il2CppRuntimeInterfaceOffsetPair* pairs = klass->interfaceOffsets;
        for (uint16_t i = 0; i < klass->interface_offsets_count; i++)
        {
            if (!LazyEntrySlotsEmpty(pairs[i].interfaceType))
                continue;
            if (slot >= pairs[i].offset && slot < pairs[i].offset + MethodCount(pairs[i].interfaceType))
            {
                klass->vtable[slot].method = NULL;
                klass->vtable[slot].methodPtr = (Il2CppMethodPointer) & LazyInterfaces::Trap;
                return true;
            }
        }

        return false;
    }
} // namespace vm
} // namespace il2cpp

#endif // IL2CPP_LAZY_INTERFACES_IN_CLASS_CPP

#if defined(IL2CPP_LAZY_INTERFACES_IN_ARRAY_METADATA_CPP) && !defined(IL2CPP_LAZY_INTERFACES_ARRAY_METADATA_CPP_DONE)
#define IL2CPP_LAZY_INTERFACES_ARRAY_METADATA_CPP_DONE

namespace il2cpp
{
namespace metadata
{
    // One interface of PopulateArrayGenericMethods. A slot may have its method already: PopulateArrayGenericMethods puts
    // every method where its slot is when it makes the table of the methods of the array (LazyArrayMethods).
    void LazyArrayInterfaceSlots(Il2CppClass* klass, const Il2CppGenericClass* interfaceType, VirtualInvokeData* slots)
    {
        GetArrayGenericMethodsCount();
        Il2CppClass* interfaceDefinition = GenericClass::GetTypeDefinition(interfaceType);

        Il2CppGenericContext context = { 0 };
        context.method_inst = MetadataCache::GetGenericInst(&interfaceType->context.class_inst->type_argv[0], 1);

        for (GenericArrayMethods::const_iterator iter = s_GenericArrayMethods.begin(); iter != s_GenericArrayMethods.end(); ++iter)
        {
            if (iter->interfaceMethodDefinition->klass != interfaceDefinition || slots[iter->interfaceMethodDefinition->slot].method != NULL)
                continue;

            MethodInfo* arrayMethod = ConstructGenericArrayMethod(*iter, klass, &context);
            slots[iter->interfaceMethodDefinition->slot].method = arrayMethod;
            slots[iter->interfaceMethodDefinition->slot].methodPtr = arrayMethod->virtualMethodPointer;
        }
    }

    void LazyArrayMethods(Il2CppClass* klass)
    {
        if (klass->methods != NULL)
            return;

        IL2CPP_LAZY_BRANCH("methods of an array made");
        SetupArrayMethods(klass);
    }
} // namespace metadata
} // namespace il2cpp

#endif // IL2CPP_LAZY_INTERFACES_IN_ARRAY_METADATA_CPP
