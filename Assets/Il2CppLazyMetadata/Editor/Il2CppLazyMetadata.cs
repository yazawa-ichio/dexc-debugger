using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEngine;

namespace Il2CppLazy
{
	/// <summary>
	/// Makes the IL2CPP runtime of an iOS build create the metadata of generic types when it is used, not when a type is
	/// initialized. An iOS build writes the source of libil2cpp into the Xcode project (Il2CppOutputProject/IL2CPP) and
	/// Xcode compiles it, so the source is changed there, after Unity has written the project. Nothing of the Editor's
	/// installation is touched, and the C# and the IL of the game stay as they are.
	///
	/// What the runtime does differently is written in the headers of Native~, which are copied into libil2cpp:
	/// Il2CppLazyMetadata.h (the members of a generic instance and the entries of an rgctx) and Il2CppLazyInterfaces.h
	/// (the vtable slots and the classes of the interfaces a generic instance or an array implements). This class puts
	/// the lines into the sources of libil2cpp that call into them. Each of them names the text it expects, and the
	/// build stops when that text is not there.
	///
	/// Il2CppCastCache.h is for speed, not for memory: the runtime remembers the answers of the two searches behind a
	/// cast to an interface and a call through a variant interface, which the changes above make somewhat slower.
	///
	/// Set the environment variable IL2CPP_LAZY_METADATA=0 to build without any of it, and IL2CPP_CAST_CACHE=0 to build
	/// with everything but the cast cache.
	/// </summary>
	public sealed class Il2CppLazyMetadata : IPostprocessBuildWithReport
	{
		const string Header = "Il2CppLazyMetadata.h";
		const string InterfacesHeader = "Il2CppLazyInterfaces.h";
		const string CastCacheHeader = "Il2CppCastCache.h";

		public int callbackOrder => 1000;

		public void OnPostprocessBuild(BuildReport report)
		{
			if (report.summary.platform != BuildTarget.iOS || Environment.GetEnvironmentVariable("IL2CPP_LAZY_METADATA") == "0")
			{
				return;
			}
			if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.iOS) != ScriptingImplementation.IL2CPP)
			{
				return;
			}
			var root = FindIl2Cpp(report.summary.outputPath);
			if (root == null)
			{
				throw new BuildFailedException("IL2CPP lazy metadata: the source of libil2cpp was not found under " + report.summary.outputPath);
			}
			try
			{
				Apply(root, false, Environment.GetEnvironmentVariable("IL2CPP_CAST_CACHE") != "0");
			}
			catch (InvalidOperationException exception)
			{
				// A Unity version whose libil2cpp reads differently. Better no build than one that is half changed.
				throw new BuildFailedException("IL2CPP lazy metadata: " + exception.Message);
			}
			Debug.Log("IL2CPP lazy metadata: applied to " + root);
		}

		/// <summary>The folder that holds libil2cpp in the Xcode project: Il2CppOutputProject/IL2CPP, or wherever Class.cpp is.</summary>
		static string FindIl2Cpp(string output)
		{
			var expected = Path.Combine(output, "Il2CppOutputProject", "IL2CPP");
			if (File.Exists(Path.Combine(expected, "libil2cpp", "vm", "Class.cpp")))
			{
				return expected;
			}
			if (!Directory.Exists(output))
			{
				return null;
			}
			var found = Directory.GetFiles(output, "Class.cpp", SearchOption.AllDirectories)
				.FirstOrDefault(path => path.Replace('\\', '/').EndsWith("/libil2cpp/vm/Class.cpp", StringComparison.Ordinal));
			return found == null ? null : Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(found)));
		}

		/// <summary>
		/// Applies the changes to the il2cpp folder at <paramref name="root"/> (the one that has libil2cpp in it).
		/// With <paramref name="switched"/> each change is taken only when the Player runs with its environment variable set
		/// (GENERIC_MEMORY_LAZY_METHODS=1, GENERIC_MEMORY_LAZY_RGCTX=1, GENERIC_MEMORY_LAZY_INTERFACES=1,
		/// GENERIC_MEMORY_LAZY_INTERFACE_CLASSES=1). That form is for measuring: it needs the counters of the generic-memory
		/// bench in the same folder, and one Player built with it runs with and without each change.
		/// Without <paramref name="castCache"/> the lines of the cast cache are still put in, and compile to nothing: so
		/// a folder that was changed before follows the choice of the build that changes it again.
		/// </summary>
		public static void Apply(string root, bool switched = false, bool castCache = true)
		{
			var lib = Path.Combine(root, "libil2cpp");
			CopyHeader(lib, Header, switched ? "#define IL2CPP_LAZY_METADATA_SWITCHED 1\n" : "");
			CopyHeader(lib, InterfacesHeader, "");
			CopyHeader(lib, CastCacheHeader, castCache ? "" : "#define IL2CPP_CAST_CACHE_OFF 1\n");

			// A source that calls into Il2CppLazyInterfaces.h includes it itself, whatever else it is compiled together with.
			const string interfaces = "#include \"vm/" + InterfacesHeader + "\"\n";

			Edit(Path.Combine(lib, "vm", "Class.cpp"),
				After("#include <stdarg.h>\n", "\n#define IL2CPP_LAZY_METADATA_IN_CLASS_CPP\n#include \"vm/" + Header + "\"\n" + interfaces),
				// Initialization leaves the members of a generic instance alone, and the methods of an array.
				Swap(
					"        SetupMethodsLocked(klass, lock);\n        SetupTypeHierarchyLocked(klass, lock);",
					"        const bool lazyMembers = (klass->generic_class != NULL && IL2CPP_LAZY_MEMBERS()) || (klass->rank != 0 && IL2CPP_LAZY_INTERFACE_SLOTS());\n"
					+ "        if (!lazyMembers)\n"
					+ "            SetupMethodsLocked(klass, lock);\n"
					+ "        SetupTypeHierarchyLocked(klass, lock);"),
				Swap(
					"        SetupEventsLocked(klass, lock);\n        SetupPropertiesLocked(klass, lock);\n        SetupNestedTypesLocked(klass, lock);",
					"        if (!lazyMembers)\n        {\n            SetupEventsLocked(klass, lock);\n            SetupPropertiesLocked(klass, lock);\n        }\n"
					+ "        SetupNestedTypesLocked(klass, lock);"),
				// A lookup by name finds the method in the definition and inflates that one.
				Before(
					"            void* iter = NULL;\n"
					+ "            while (const MethodInfo* method = Class::GetMethods(klass, &iter))\n            {\n"
					+ "                if (method->name[0] == name[0] &&",
					"            if (const MethodInfo* lazy = LazyLookUpInDefinition(klass, name, argsCount, flags, argTypes))\n"
					+ "            {\n"
					+ "                if (lazy != &s_LazyMethodNotThere)\n"
					+ "                    return lazy;\n"
					+ "                klass = klass->parent;\n"
					+ "                continue;\n"
					+ "            }\n\n"),
				// The instance's method for a definition's method, without the whole table.
				Before(
					"        const MethodInfo* genericClassMethod = genericInstanceClass->methods[index];",
					"        if (genericInstanceClass->methods == NULL)\n"
					+ "            return il2cpp::metadata::GenericMethod::GetMethod(methodDefinition, genericInstanceClass->generic_class->context.class_inst, NULL);\n\n"),
				// The properties and the events are made once. A thread that found none waits for the lock, and by then
				// another may have made them: making them again would put a second table under a thread that is walking
				// the first, and the walk ends where the table the class has now ends.
				Swap(
					"            il2cpp::os::FastAutoLock lock(&g_MetadataLock);\n            SetupEventsLocked(klass, lock);\n",
					"            il2cpp::os::FastAutoLock lock(&g_MetadataLock);\n            if (!klass->events)\n                SetupEventsLocked(klass, lock);\n"),
				Swap(
					"            il2cpp::os::FastAutoLock lock(&g_MetadataLock);\n            SetupPropertiesLocked(klass, lock);\n",
					"            il2cpp::os::FastAutoLock lock(&g_MetadataLock);\n            if (!klass->properties)\n                SetupPropertiesLocked(klass, lock);\n"),
				// The list of the interfaces of a generic instance is filled before the class points at it. Whoever lists the
				// interfaces of a type looks at the pointer without the lock, and a list that is there but half filled ends the
				// listing early. The original wrote the pointer first, which went unseen while initialization held the lock for long.
				// An entry of the list is the class of an interface only if that class was made already.
				Swap(
					"                klass->implementedInterfaces = (Il2CppClass**)MetadataCalloc(genericTypeDefinition->interfaces_count, sizeof(Il2CppClass*));\n"
					+ "                for (uint16_t i = 0; i < genericTypeDefinition->interfaces_count; i++)\n"
					+ "                    klass->implementedInterfaces[i] = Class::FromIl2CppType(il2cpp::metadata::GenericMetadata::InflateIfNeeded(MetadataCache::GetInterfaceFromOffset(genericTypeDefinition, i), context, false));\n",
					"                Il2CppClass** implemented = (Il2CppClass**)MetadataCalloc(genericTypeDefinition->interfaces_count, sizeof(Il2CppClass*));\n"
					+ "                for (uint16_t i = 0; i < genericTypeDefinition->interfaces_count; i++)\n"
					+ "                    implemented[i] = LazyInterfaces::ImplementedEntry(MetadataCache::GetInterfaceFromOffset(genericTypeDefinition, i), context);\n"
					+ "                il2cpp::os::Atomic::FullMemoryBarrier();\n"
					+ "                klass->implementedInterfaces = implemented;\n"),

				// The interfaces of a class are read through Il2CppLazyInterfaces.h, which knows an entry that is not a class.
				After(
					"            Class::SetupInterfaces(klass);\n            if (klass->interfaces_count == 0)\n                return NULL;\n",
					"            LazyInterfaces::MakeImplemented(klass);\n"),
				Swap(
					"                    if (IsGenericClassAssignableFrom(klass, iter->implementedInterfaces[i], oklass))",
					"                    if (LazyInterfaces::IsAssignable(klass, iter->implementedInterfaces[i], oklass))"),
				Swap(
					"                    if (IsGenericClassAssignableFrom(klass, iter->interfaceOffsets[i].interfaceType, oklass))",
					"                    if (LazyInterfaces::IsAssignable(klass, iter->interfaceOffsets[i].interfaceType, oklass))"),
				Swap(
					"                    if (iter->implementedInterfaces[i] == klass)",
					"                    if (LazyInterfaces::Is(iter->implementedInterfaces[i], klass))"),
				Swap(
					"                    if (iter->interfaceOffsets[i].interfaceType == klass)",
					"                    if (LazyInterfaces::Is(iter->interfaceOffsets[i].interfaceType, klass))"),
				Swap(
					"                    if (oklass->implementedInterfaces[i] == klassc)",
					"                    if (LazyInterfaces::Is(oklass->implementedInterfaces[i], klassc))"),
				Swap(
					"                if (klass->implementedInterfaces[i] == klassc)",
					"                if (LazyInterfaces::Is(klass->implementedInterfaces[i], klassc))"),

				// SetupVTable keeps the loop that knows what a slot of a generic instance holds. Called again for a class
				// that is initialized, with the slots of one interface, it fills those and no other.
				Swap(
					"    static void SetupVTable(Il2CppClass *klass, const il2cpp::os::FastAutoLock& lock);\n",
					"    static void SetupVTable(Il2CppClass *klass, const il2cpp::os::FastAutoLock& lock, int32_t lazyFirst = 0, int32_t lazyCount = 0);\n"),
				Swap(
					"    static void SetupVTable(Il2CppClass *klass, const il2cpp::os::FastAutoLock& lock)\n    {\n        if (klass->is_vtable_initialized)\n            return;\n",
					"    static void SetupVTable(Il2CppClass *klass, const il2cpp::os::FastAutoLock& lock, int32_t lazyFirst, int32_t lazyCount)\n    {\n        if (klass->is_vtable_initialized && lazyCount == 0)\n            return;\n"),
				Swap(
					"                    klass->interfaceOffsets[i].interfaceType = Class::FromIl2CppType(il2cpp::metadata::GenericMetadata::InflateIfNeeded(interfaceOffset.interfaceType, context, false));\n",
					"                    klass->interfaceOffsets[i].interfaceType = LazyInterfaces::OffsetEntry(klass, genericTypeDefinition, interfaceOffset.interfaceType, interfaceOffset.offset, context);\n"),
				After(
					"                for (uint16_t i = 0; i < genericTypeDefinition->vtable_count; i++)\n                {\n",
					"                    if (LazyInterfaces::LeavesSlot(klass, i, lazyFirst, lazyCount))\n                        continue;\n\n"),
				// A slot that is filled after its class was initialized names a method that is complete in memory.
				Before(
					"                    klass->vtable[i].method = method;\n                    if (method != NULL)\n                    {\n                        // For default interface methods",
					"                    if (lazyCount != 0)\n                        il2cpp::os::Atomic::FullMemoryBarrier();\n"),
				// The methods of an array are made for whoever asks for the methods of the type. Its vtable is set up by then.
				After(
					"            Class::InitLocked(klass->element_class, lock);\n            SetupVTable(klass, lock);\n",
					"            il2cpp::metadata::LazyArrayMethods(klass);\n"),
				// A slot that is read by its number may be one that was left empty.
				After(
					"            vtableSlotMethod = klass->vtable[virtualMethod->slot].method;\n",
					"            if (vtableSlotMethod == NULL)\n                vtableSlotMethod = LazyInterfaces::SlotMethod(klass, virtualMethod->slot);\n"),
				// The bodies that call SetupVTable.
				After(
					"} /* namespace vm */\n} /* namespace il2cpp */\n",
					"\n#define IL2CPP_LAZY_INTERFACES_IN_CLASS_CPP\n" + interfaces));

			Edit(Path.Combine(lib, "vm", "Object.cpp"),
				After("#include \"vm/ClassInlines.h\"\n", interfaces),
				After(
					"            vtableSlotMethod = obj->klass->vtable[virtualMethod->slot].method;\n",
					"            if (vtableSlotMethod == NULL)\n                vtableSlotMethod = LazyInterfaces::SlotMethod(obj->klass, virtualMethod->slot);\n"));

			Edit(Path.Combine(lib, "vm", "ClassInlines.cpp"),
				After("#include \"vm/Class.h\"\n", interfaces),
				// The message of a failed interface call names the method.
				Swap("Method::GetName(itf->methods[slot])", "(itf->methods != NULL ? Method::GetName(itf->methods[slot]) : \"?\")"),
				// The lookup the generated code inlines does not see a pair that is marked. Its slow path looks for the pair
				// that is the interface itself first, and then, as it always did, for one that stands in for it by variance.
				Before(
					"        if (itf->generic_class != NULL)\n        {\n            for (uint16_t i = 0; i < klass->interface_offsets_count; ++i)\n",
					"        if (const VirtualInvokeData* exact = LazyInterfaces::ExactInvokeData(klass, itf, slot))\n            return exact;\n\n"),
				Swap(
					"                if (Class::IsGenericClassAssignableFromVariance(itf, pair->interfaceType, klass))",
					"                if (LazyInterfaces::IsVariantAndFilled(klass, i, itf))"));

			// What stands for a COM object finds its interfaces in the tables as they always were.
			Edit(Path.Combine(lib, "vm", "RCW.cpp"),
				After("#include \"vm/GenericClass.h\"\n", interfaces),
				After(
					"        Class::Init(queriedInterface);\n        uint16_t vtableCount = queriedInterface->vtable_count;\n",
					"        LazyInterfaces::MakeAll(queriedInterface);\n"));

			Edit(Path.Combine(lib, "metadata", "ArrayMetadata.cpp"),
				After("#include \"metadata/ArrayMetadata.h\"\n", interfaces),
				// The vtable of an array reads the methods of the generic interfaces it implements right after initializing them.
				After("            Class::Init(implementingInterface);\n", "            Class::SetupMethods(implementingInterface);\n"),
				// The methods of an array are not made when it is initialized: the ones for its generic interfaces are made
				// when an interface is called through, and the table of them all when something asks for the methods of the
				// type. The table then takes the methods that slots already hold, and the pairs may be without their classes.
				Swap(
					"        SetupArrayVTableAndInterfaceOffsets(klass);\n        SetupArrayMethods(klass);\n",
					"        SetupArrayVTableAndInterfaceOffsets(klass);\n        if (!IL2CPP_LAZY_INTERFACE_SLOTS())\n            SetupArrayMethods(klass);\n"),
				Every("interfaceType->generic_class", "genericClassOfInterface", 3),
				Swap(
					"            Il2CppClass* interfaceType = klass->interfaceOffsets[i].interfaceType;\n",
					"            const Il2CppGenericClass* genericClassOfInterface = il2cpp::vm::LazyInterfaces::GenericClassOf(klass->interfaceOffsets[i].interfaceType);\n"),
				Swap(
					"                MethodInfo* arrayMethod = ConstructGenericArrayMethod(*iter, klass, &context);\n"
					+ "                klass->methods[offset++] = arrayMethod;\n\n"
					+ "                size_t vtableIndex = klass->interfaceOffsets[i].offset + iter->interfaceMethodDefinition->slot;\n",
					"                size_t vtableIndex = klass->interfaceOffsets[i].offset + iter->interfaceMethodDefinition->slot;\n"
					+ "                MethodInfo* arrayMethod = il2cpp::vm::LazyInterfaces::MethodInSlot(klass->vtable[vtableIndex]);\n"
					+ "                if (arrayMethod == NULL)\n"
					+ "                    arrayMethod = ConstructGenericArrayMethod(*iter, klass, &context);\n"
					+ "                klass->methods[offset++] = arrayMethod;\n\n"),
				// The generic interfaces of an array: ten entries, five in each table.
				Every(
					" = Class::GetInflatedGenericInstanceClass(il2cpp_defaults.generic_",
					" = il2cpp::vm::LazyInterfaces::ArrayEntry(il2cpp_defaults.generic_", 10),
				ArraySlots("index"),
				ArraySlots("index + 1"),
				ArraySlots("index + 2"),
				ArraySlots("index + 3"),
				ArraySlots("index + 4"),
				Before(
					"        klass->interface_offsets_count = static_cast<uint16_t>(interfaceOffsetsCount);\n",
					"        il2cpp::vm::LazyInterfaces::MarkArraySlots(klass, newInterfaceOffsets, arrayInterfacesCount, interfaceOffsetsCount, arrayVTableSlot, vtableSlot);\n"),
				// The body that makes those methods.
				After(
					"} /* namespace vm */\n} /* namespace il2cpp */\n",
					"\n#define IL2CPP_LAZY_INTERFACES_IN_ARRAY_METADATA_CPP\n" + interfaces));

			// A table made after its class was initialized is read by threads that take no lock, and nothing orders
			// their reads behind the writes any more: initialization used to end with a memory barrier. So the table
			// is complete in memory before the class points at it.
			Edit(Path.Combine(lib, "vm", "GenericClass.cpp"),
				Before("        genericInstanceType->methods = methods;\n", "        il2cpp::os::Atomic::FullMemoryBarrier();\n"),
				Before("        genericInstanceType->properties = properties;\n", "        il2cpp::os::Atomic::FullMemoryBarrier();\n"),
				Before("        genericInstanceType->events = events;\n", "        il2cpp::os::Atomic::FullMemoryBarrier();\n"));

			// A named argument of an attribute reads the property out of the table of the class that declares it.
			Edit(Path.Combine(lib, "metadata", "CustomAttributeDataReader.cpp"),
				Before("            propArg.prop = &klass->properties[propertyIndex];", "            vm::Class::SetupProperties(const_cast<Il2CppClass*>(klass));\n"));

			// The entries of an rgctx. InflateRGCTXLocked keeps the loop that knows how each kind of entry is resolved. It is
			// told where to put what it resolves and which entry to resolve, and without that it resolves none.
			const string inflate = "InflateRGCTXLocked(const Il2CppImage* image, uint32_t token, const Il2CppGenericContext* context, const ";
			Edit(Path.Combine(lib, "metadata", "GenericMetadata.h"),
				Swap(
					"        static Il2CppRGCTXData* " + inflate + "il2cpp::os::FastAutoLock& lock);",
					"        static Il2CppRGCTXData* " + inflate + "il2cpp::os::FastAutoLock& lock, Il2CppRGCTXData* aside = NULL, int32_t only = -1);"));
			Edit(Path.Combine(lib, "metadata", "GenericMetadata.cpp"),
				After("#include \"Cpp/ReentrantLock.h\"\n", "\n#define IL2CPP_LAZY_METADATA_IN_GENERIC_METADATA_CPP\n#include \"vm/" + Header + "\"\n"),
				Swap(
					"    Il2CppRGCTXData* GenericMetadata::" + inflate + "FastAutoLock& lock)\n",
					"    Il2CppRGCTXData* GenericMetadata::" + inflate + "FastAutoLock& lock, Il2CppRGCTXData* aside, int32_t only)\n"),
				Swap(
					"        Il2CppRGCTXData* dataValues = (Il2CppRGCTXData*)MetadataCalloc(collection.count, sizeof(Il2CppRGCTXData));\n",
					"        Il2CppRGCTXData* dataValues = LazyRgctxValues(collection.count, image, token, context, aside);\n"),
				After(
					"        for (RGCTXIndex rgctxIndex = 0; rgctxIndex < collection.count; rgctxIndex++)\n        {\n",
					"            if (LazyRgctxLeaves(rgctxIndex, aside, only))\n                continue;\n\n"));

			// The generated code reads an rgctx only through four inline functions, each of which indexes it once. The two
			// places where it reads a vtable slot by its number and uses the method ask for a slot that was left empty.
			Edit(Path.Combine(lib, "codegen", "il2cpp-codegen.h"),
				After("#include \"vm/ClassInlines.h\"\n", "#include \"vm/" + Header + "\"\n"),
				Every("rgctxVar[index]", "il2cpp_lazy_rgctx_entry(rgctxVar, index)", 4),
				After(
					"    const RuntimeMethod* methodDefinition = obj->klass->vtable[slot].method;\n",
					"    if (methodDefinition == NULL)\n        methodDefinition = il2cpp_lazy_interface_slot(obj->klass, slot);\n"),
				Swap(
					"    return pThis->klass->vtable[slot].method;\n",
					"    const RuntimeMethod* slotMethod = pThis->klass->vtable[slot].method;\n"
					+ "    return slotMethod != NULL ? slotMethod : il2cpp_lazy_interface_slot(pThis->klass, slot);\n"));

			// The cast cache (Il2CppCastCache.h). These changes are made to the sources as the changes above leave them.
			const string castCacheInclude = "#include \"vm/" + CastCacheHeader + "\"\n";

			// A cast to an interface. The part of Class::IsAssignableFrom that searches the interfaces of the class becomes a
			// function of its own, whose answer is remembered. What is asked before it (the same class, a class on the left)
			// stays ahead of the table, and so does the initialization of both classes.
			Edit(Path.Combine(lib, "vm", "Class.cpp"),
				// Behind the two includes at the top, which the change that put them there has to find together.
				After("#include \"vm/" + Header + "\"\n" + interfaces, castCacheInclude),
				Before(
					"    bool Class::IsAssignableFrom(Il2CppClass *klass, Il2CppClass *oklass)\n    {\n        // Cast to original class - fast path\n",
					"    static CastCache s_AssignableToInterface;\n"
					+ "    static bool SearchInterfaces(Il2CppClass* klass, Il2CppClass* oklass);\n\n"),
				Before(
					"        if (klass->generic_class != NULL)\n        {\n            // checking for simple reference equality is not enough",
					"        intptr_t castAnswer;\n"
					+ "        const bool castCached = IL2CPP_CAST_CACHE();\n"
					+ "        if (castCached && s_AssignableToInterface.TryGet(klass, oklass, &castAnswer))\n"
					+ "            return castAnswer != 0;\n\n"
					+ "        const bool castFound = SearchInterfaces(klass, oklass);\n"
					+ "        if (castCached)\n"
					+ "            s_AssignableToInterface.Set(klass, oklass, castFound ? 1 : 0);\n"
					+ "        return castFound;\n"
					+ "    }\n\n"
					+ "    // The end of Class::IsAssignableFrom: whether a class, or one of its parents, implements the interface on the left.\n"
					+ "    static bool SearchInterfaces(Il2CppClass* klass, Il2CppClass* oklass)\n"
					+ "    {\n"),
				// That end is no longer inside a member of Class.
				Swap(
					"                if (IsGenericClassAssignableFrom(klass, iter, oklass))\n                    return true;\n\n"
					+ "                for (uint16_t i = 0; i < iter->interfaces_count; ++i)\n",
					"                if (Class::IsGenericClassAssignableFrom(klass, iter, oklass))\n                    return true;\n\n"
					+ "                for (uint16_t i = 0; i < iter->interfaces_count; ++i)\n"));

			// A call through a generic interface that the inlined lookup did not find. Where the class has the slots of an
			// interface it implements by variance is remembered once the search has found it, and by then they are filled.
			Edit(Path.Combine(lib, "vm", "ClassInlines.cpp"),
				After(interfaces, castCacheInclude),
				Swap(
					"    const VirtualInvokeData* ClassInlines::GetInterfaceInvokeDataFromVTableSlowPath(const Il2CppClass* klass, const Il2CppClass* itf, Il2CppMethodSlot slot)\n"
					+ "    {\n"
					+ "        if (const VirtualInvokeData* exact = LazyInterfaces::ExactInvokeData(klass, itf, slot))\n",
					"    static CastCache s_SlotsByVariance;\n\n"
					+ "    const VirtualInvokeData* ClassInlines::GetInterfaceInvokeDataFromVTableSlowPath(const Il2CppClass* klass, const Il2CppClass* itf, Il2CppMethodSlot slot)\n"
					+ "    {\n"
					+ "        intptr_t varianceOffset;\n"
					+ "        const bool varianceCached = itf->generic_class != NULL && IL2CPP_CAST_CACHE();\n"
					+ "        if (varianceCached && s_SlotsByVariance.TryGet(klass, itf, &varianceOffset))\n"
					+ "            return &klass->vtable[varianceOffset + slot];\n\n"
					+ "        if (const VirtualInvokeData* exact = LazyInterfaces::ExactInvokeData(klass, itf, slot))\n"),
				Before(
					"                    return &klass->vtable[pair->offset + slot];\n",
					"                    if (varianceCached)\n"
					+ "                        s_SlotsByVariance.Set(klass, itf, pair->offset);\n"));
		}

		/// <summary>Copies a header into libil2cpp/vm, with the line that chooses its form ahead of it, if it has one.</summary>
		static void CopyHeader(string lib, string header, string ahead)
		{
			var folder = Path.GetDirectoryName(CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(typeof(Il2CppLazyMetadata).Assembly.GetName().Name));
			var text = File.ReadAllText(Path.Combine(folder, "Native~", header)).Replace("\r\n", "\n");
			File.WriteAllText(Path.Combine(lib, "vm", header), ahead + text);
		}

		/// <summary>One change to a source of libil2cpp: the text that is there, what takes its place, and how often it occurs.</summary>
		readonly struct Change
		{
			public readonly string Old;
			public readonly string New;
			public readonly int Times;

			public Change(string old, string replacement, int times)
			{
				Old = old;
				New = replacement;
				Times = times;
			}
		}

		static Change Swap(string old, string replacement)
		{
			return new Change(old, replacement, 1);
		}

		static Change Before(string anchor, string added)
		{
			return new Change(anchor, added + anchor, 1);
		}

		static Change After(string anchor, string added)
		{
			return new Change(anchor, anchor + added, 1);
		}

		static Change Every(string old, string replacement, int times)
		{
			return new Change(old, replacement, times);
		}

		/// <summary>The line of ArrayMetadata.cpp that counts the slots of one of the generic interfaces of an array.</summary>
		static Change ArraySlots(string at)
		{
			return Swap(
				"            vtableSlot += newInterfaceOffsets[" + at + "].interfaceType->method_count;\n",
				"            vtableSlot += il2cpp::vm::LazyInterfaces::MethodCount(newInterfaceOffsets[" + at + "].interfaceType);\n");
		}

		/// <summary>
		/// Makes each change once. A file that already has the new text is left as it is, so applying twice does no harm;
		/// a file that has neither is not the libil2cpp these changes were written for.
		/// </summary>
		static void Edit(string path, params Change[] changes)
		{
			var raw = File.ReadAllText(path);
			var crlf = raw.Contains("\r\n");
			var text = raw.Replace("\r\n", "\n");
			foreach (var change in changes)
			{
				var count = 0;
				for (var at = text.IndexOf(change.Old, StringComparison.Ordinal); at >= 0; at = text.IndexOf(change.Old, at + change.Old.Length, StringComparison.Ordinal))
				{
					count++;
				}
				if (text.Contains(change.New))
				{
					// Made already. Unless the new text is there for another reason and the old text is still all there:
					// then this change would be left out without a word.
					if (count == change.Times && !change.New.Contains(change.Old))
					{
						throw new InvalidOperationException("both the old and the new text are in " + Path.GetFileName(path) + ": "
							+ change.Old.Substring(0, Math.Min(60, change.Old.Length)).Replace("\n", " "));
					}
					continue;
				}
				if (count != change.Times)
				{
					throw new InvalidOperationException("expected text found " + count + " times, not " + change.Times + ", in " + Path.GetFileName(path) + ": "
						+ change.Old.Substring(0, Math.Min(60, change.Old.Length)).Replace("\n", " "));
				}
				text = text.Replace(change.Old, change.New);
			}
			File.WriteAllText(path, crlf ? text.Replace("\n", "\r\n") : text);
		}
	}
}
