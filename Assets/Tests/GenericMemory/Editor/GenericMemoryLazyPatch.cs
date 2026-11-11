using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GenericMemory.Editor
{
	/// <summary>
	/// The changes of Assets/Il2CppLazyMetadata in the form the bench builds: with switched, each is taken only when the
	/// Player runs with GENERIC_MEMORY_LAZY_METHODS=1 or GENERIC_MEMORY_LAZY_RGCTX=1, so one build measures with and without.
	/// </summary>
	public static class GenericMemoryLazyPatch
	{
		/// <summary>
		/// Applies both changes to the il2cpp folder at <paramref name="root"/> (the one that has libil2cpp in it).
		/// With <paramref name="switched"/>, each change is taken only when the Player runs with GENERIC_MEMORY_LAZY_METHODS=1 or
		/// GENERIC_MEMORY_LAZY_RGCTX=1; that form needs the counters of the bench (utils/GenericMemoryCounters.h) and is what the bench builds.
		/// </summary>
		public static void Apply(string root, bool switched)
		{
			var lib = Path.Combine(root, "libil2cpp");
			var methods = switched ? " && generic_memory::LazyMethods()" : "";
			var include = switched ? "#include \"utils/GenericMemoryCounters.h\"\n" : "";

			Edit(Path.Combine(lib, "vm", "Class.cpp"), new[]
			{
				("#include \"utils/Memory.h\"\n", "#include \"utils/Memory.h\"\n" + include),
				// 1. Initialization no longer inflates the members of a generic instance.
				(
					"        SetupMethodsLocked(klass, lock);\n        SetupTypeHierarchyLocked(klass, lock);",
					"        // Generic memory bench: the methods of a generic instance are inflated when something asks for them.\n"
					+ "        const bool lazyMembers = klass->generic_class != NULL" + methods + ";\n"
					+ "        if (!lazyMembers)\n"
					+ "            SetupMethodsLocked(klass, lock);\n"
					+ "        SetupTypeHierarchyLocked(klass, lock);"
				),
				(
					"        SetupEventsLocked(klass, lock);\n        SetupPropertiesLocked(klass, lock);\n        SetupNestedTypesLocked(klass, lock);",
					"        if (!lazyMembers)\n        {\n            SetupEventsLocked(klass, lock);\n            SetupPropertiesLocked(klass, lock);\n        }\n"
					+ "        SetupNestedTypesLocked(klass, lock);"
				),
				// 2. A lookup by name (the static constructor, a delegate's Invoke) finds the method in the definition
				//    and inflates that one, instead of all of them.
				(
					"        while (klass != NULL)\n        {\n            void* iter = NULL;\n"
					+ "            while (const MethodInfo* method = Class::GetMethods(klass, &iter))\n            {\n"
					+ "                if (method->name[0] == name[0] &&",
					"        while (klass != NULL)\n        {\n"
					+ "            if (klass->generic_class != NULL && klass->methods == NULL && argTypes == NULL" + methods + ")\n"
					+ "            {\n"
					+ "                void* definitionIter = NULL;\n"
					+ "                const MethodInfo* found = NULL;\n"
					+ "                Il2CppClass* definition = GenericClass::GetTypeDefinition(klass->generic_class);\n"
					+ "                while (const MethodInfo* method = Class::GetMethods(definition, &definitionIter))\n"
					+ "                {\n"
					+ "                    if (method->name[0] == name[0] &&\n"
					+ "                        (argsCount == IgnoreNumberOfArguments || method->parameters_count == argsCount) &&\n"
					+ "                        ((method->flags & flags) == flags) &&\n"
					+ "                        !strcmp(name, method->name))\n"
					+ "                    {\n"
					+ "                        found = method;\n"
					+ "                        break;\n"
					+ "                    }\n"
					+ "                }\n"
					+ "                if (found != NULL)\n"
					+ "                    return il2cpp::metadata::GenericMethod::GetMethod(found, klass->generic_class->context.class_inst, NULL);\n"
					+ "                klass = klass->parent;\n"
					+ "                continue;\n"
					+ "            }\n\n"
					+ "            void* iter = NULL;\n"
					+ "            while (const MethodInfo* method = Class::GetMethods(klass, &iter))\n            {\n"
					+ "                if (method->name[0] == name[0] &&"
				),
				// 3. The instance's method for a definition's method, without the whole table.
				(
					"        const MethodInfo* genericClassMethod = genericInstanceClass->methods[index];",
					"        if (genericInstanceClass->methods == NULL)\n"
					+ "            return il2cpp::metadata::GenericMethod::GetMethod(methodDefinition, genericInstanceClass->generic_class->context.class_inst, NULL);\n\n"
					+ "        const MethodInfo* genericClassMethod = genericInstanceClass->methods[index];"
				),
				// 8. The properties and the events are made once: a thread that found none and waited for the lock looks again.
				//    Not switched, like 6.
				(
					"            il2cpp::os::FastAutoLock lock(&g_MetadataLock);\n            SetupEventsLocked(klass, lock);\n",
					"            il2cpp::os::FastAutoLock lock(&g_MetadataLock);\n            if (!klass->events)\n                SetupEventsLocked(klass, lock);\n"
				),
				(
					"            il2cpp::os::FastAutoLock lock(&g_MetadataLock);\n            SetupPropertiesLocked(klass, lock);\n",
					"            il2cpp::os::FastAutoLock lock(&g_MetadataLock);\n            if (!klass->properties)\n                SetupPropertiesLocked(klass, lock);\n"
				),
			});
			// 4. The vtable of an array reads the methods of the generic interfaces it implements right after initializing them.
			Edit(Path.Combine(lib, "metadata", "ArrayMetadata.cpp"), new[]
			{
				(
					"            Class::Init(implementingInterface);\n\n            const MethodInfo* matchingInterfacesMethod = NULL;",
					"            Class::Init(implementingInterface);\n            Class::SetupMethods(implementingInterface);\n\n"
					+ "            const MethodInfo* matchingInterfacesMethod = NULL;"
				),
			});
			// 5. The message of a failed interface call names the method.
			Edit(Path.Combine(lib, "vm", "ClassInlines.cpp"), new[]
			{
				("Method::GetName(itf->methods[slot])", "(itf->methods != NULL ? Method::GetName(itf->methods[slot]) : \"?\")"),
			});
			// 6. A table made after its class was initialized is complete in memory before the class points at it.
			//    Not switched: the barrier costs nothing that the measurements see.
			Edit(Path.Combine(lib, "vm", "GenericClass.cpp"), new[]
			{
				("        genericInstanceType->methods = methods;\n", "        il2cpp::os::Atomic::FullMemoryBarrier();\n        genericInstanceType->methods = methods;\n"),
				("        genericInstanceType->properties = properties;\n", "        il2cpp::os::Atomic::FullMemoryBarrier();\n        genericInstanceType->properties = properties;\n"),
				("        genericInstanceType->events = events;\n", "        il2cpp::os::Atomic::FullMemoryBarrier();\n        genericInstanceType->events = events;\n"),
			});
			// 7. A named argument of an attribute reads the property out of the table of the class that declares it.
			Edit(Path.Combine(lib, "metadata", "CustomAttributeDataReader.cpp"), new[]
			{
				(
					"            propArg.prop = &klass->properties[propertyIndex];",
					"            vm::Class::SetupProperties(const_cast<Il2CppClass*>(klass));\n            propArg.prop = &klass->properties[propertyIndex];"
				),
			});

			Edit(Path.Combine(lib, "metadata", "GenericMetadata.h"), new[]
			{
				(
					"        static Il2CppRGCTXData* InflateRGCTXLocked(const Il2CppImage* image, uint32_t token, const Il2CppGenericContext* context, const il2cpp::os::FastAutoLock& lock);",
					"        static Il2CppRGCTXData* InflateRGCTXLocked(const Il2CppImage* image, uint32_t token, const Il2CppGenericContext* context, const il2cpp::os::FastAutoLock& lock);\n"
					+ "        // Generic memory bench: one entry of an rgctx, resolved when it is first read.\n"
					+ "        static const Il2CppRGCTXData* ResolveRGCTXEntry(const Il2CppRGCTXData* data, int32_t index);\n"
					+ "        static void ResolveRGCTXEntryLocked(Il2CppRGCTXData* slot, const Il2CppImage* image, uint32_t token, const Il2CppGenericContext* context, int32_t rgctxIndex, const il2cpp::os::FastAutoLock& lock);"
				),
			});
			Edit(Path.Combine(lib, "metadata", "GenericMetadata.cpp"), new[]
			{
				("#include \"vm/MetadataCache.h\"\n", "#include \"vm/MetadataCache.h\"\n#include \"vm/MetadataLock.h\"\n#include \"os/Atomic.h\"\n" + include),
				(
					"        Il2CppRGCTXData* dataValues = (Il2CppRGCTXData*)MetadataCalloc(collection.count, sizeof(Il2CppRGCTXData));\n"
					+ "        for (RGCTXIndex rgctxIndex = 0; rgctxIndex < collection.count; rgctxIndex++)\n"
					+ "        {\n"
					+ "            const Il2CppRGCTXDefinition* definitionData = collection.items + rgctxIndex;\n"
					+ "            switch (definitionData->type)\n",
					RgctxBody(switched)
				),
				// The cases of the original wrote into the array by index; they now write the one slot they are given.
				("dataValues[rgctxIndex].type = GenericMetadata::InflateIfNeeded(", "slot->type = GenericMetadata::InflateIfNeeded("),
				("dataValues[rgctxIndex].klass = Class::FromIl2CppType(", "slot->klass = Class::FromIl2CppType("),
				("Class::InitSizeAndFieldLayoutLocked(dataValues[rgctxIndex].klass, lock);", "Class::InitSizeAndFieldLayoutLocked(slot->klass, lock);"),
				("dataValues[rgctxIndex].method = GenericMethod::GetMethod(Inflate(", "slot->method = GenericMethod::GetMethod(Inflate("),
				("dataValues[rgctxIndex].method = method;", "slot->method = method;"),
				// The loop of the original returned the array after its closing brace; the entry resolver returns nothing.
				(
					"                default:\n                    IL2CPP_ASSERT(0);\n            }\n        }\n\n        return dataValues;\n    }",
					"                default:\n                    IL2CPP_ASSERT(0);\n            }\n        }\n    }"
				),
				(
					"namespace il2cpp\n{\nnamespace metadata\n{\n    const Il2CppType** GenericMetadata::InflateParameters(",
					"// Generic memory bench: what the generated code calls when it reads an entry that is still empty.\n"
					+ "const Il2CppRGCTXData* il2cpp_lazy_rgctx_resolve(const Il2CppRGCTXData* rgctxVar, int32_t index)\n{\n"
					+ "    return il2cpp::metadata::GenericMetadata::ResolveRGCTXEntry(rgctxVar, index);\n}\n\n"
					+ "namespace il2cpp\n{\nnamespace metadata\n{\n    const Il2CppType** GenericMetadata::InflateParameters("
				),
			});
			Edit(Path.Combine(lib, "codegen", "il2cpp-codegen.h"), new[]
			{
				(
					"// generic sharing\ninline RuntimeClass* il2cpp_rgctx_data(const Il2CppRGCTXData* rgctxVar, int32_t index)\n{\n"
					+ "    return InitializedTypeInfo(rgctxVar[index].klass);\n}\n\n"
					+ "inline RuntimeClass* il2cpp_rgctx_data_no_init(const Il2CppRGCTXData* rgctxVar, int32_t index)\n{\n"
					+ "    return rgctxVar[index].klass;\n}\n\n"
					+ "inline const Il2CppType* il2cpp_rgctx_type(const Il2CppRGCTXData* rgctxVar, int32_t index)\n{\n"
					+ "    return rgctxVar[index].type;\n}\n\n"
					+ "inline const MethodInfo* il2cpp_rgctx_method(const Il2CppRGCTXData* rgctxVar, int32_t index)\n{\n"
					+ "    return rgctxVar[index].method;\n}",
					"// generic sharing\n"
					+ "// Generic memory bench: an entry that is still empty is resolved by the runtime when it is read.\n"
					+ "const Il2CppRGCTXData* il2cpp_lazy_rgctx_resolve(const Il2CppRGCTXData* rgctxVar, int32_t index);\n\n"
					+ "inline const Il2CppRGCTXData* il2cpp_lazy_rgctx_entry(const Il2CppRGCTXData* rgctxVar, int32_t index)\n{\n"
					+ "    if (rgctxVar[index].rgctxDataDummy == NULL)\n"
					+ "        il2cpp_lazy_rgctx_resolve(rgctxVar, index);\n"
					+ "    return rgctxVar + index;\n}\n\n"
					+ "inline RuntimeClass* il2cpp_rgctx_data(const Il2CppRGCTXData* rgctxVar, int32_t index)\n{\n"
					+ "    return InitializedTypeInfo(il2cpp_lazy_rgctx_entry(rgctxVar, index)->klass);\n}\n\n"
					+ "inline RuntimeClass* il2cpp_rgctx_data_no_init(const Il2CppRGCTXData* rgctxVar, int32_t index)\n{\n"
					+ "    return il2cpp_lazy_rgctx_entry(rgctxVar, index)->klass;\n}\n\n"
					+ "inline const Il2CppType* il2cpp_rgctx_type(const Il2CppRGCTXData* rgctxVar, int32_t index)\n{\n"
					+ "    return il2cpp_lazy_rgctx_entry(rgctxVar, index)->type;\n}\n\n"
					+ "inline const MethodInfo* il2cpp_rgctx_method(const Il2CppRGCTXData* rgctxVar, int32_t index)\n{\n"
					+ "    return il2cpp_lazy_rgctx_entry(rgctxVar, index)->method;\n}"
				),
			});
		}

		// What replaces the head of the loop of InflateRGCTXLocked: the array starts empty with three slots ahead of it that
		// say where its entries come from, and the body of the loop becomes the resolver of one entry.
		static string RgctxBody(bool switched)
		{
			var empty =
				"            // Generic memory bench: three slots ahead of the entries say where they come from, and the entries stay empty until read.\n"
				+ "            Il2CppRGCTXData* block = (Il2CppRGCTXData*)MetadataCalloc(collection.count + 3, sizeof(Il2CppRGCTXData));\n"
				+ "            block[0].rgctxDataDummy = (void*)image;\n"
				+ "            block[1].rgctxDataDummy = (void*)(uintptr_t)token;\n"
				+ "            block[2].rgctxDataDummy = (void*)context;\n"
				+ "            return block + 3;\n";
			var head = switched
				? "        if (generic_memory::LazyRgctx())\n        {\n" + empty + "        }\n\n"
					+ "        Il2CppRGCTXData* dataValues = (Il2CppRGCTXData*)MetadataCalloc(collection.count, sizeof(Il2CppRGCTXData));\n"
					+ "        for (RGCTXIndex rgctxIndex = 0; rgctxIndex < collection.count; rgctxIndex++)\n"
					+ "            ResolveRGCTXEntryLocked(dataValues + rgctxIndex, image, token, context, rgctxIndex, lock);\n\n"
					+ "        return dataValues;\n"
				: "        {\n" + empty + "        }\n";
			return head
				+ "    }\n\n"
				+ "    const Il2CppRGCTXData* GenericMetadata::ResolveRGCTXEntry(const Il2CppRGCTXData* data, int32_t index)\n"
				+ "    {\n"
				+ (switched ? "        if (!generic_memory::LazyRgctx())\n            return data;\n\n" : "")
				+ "        FastAutoLock lock(&il2cpp::vm::g_MetadataLock);\n"
				+ "        Il2CppRGCTXData* values = const_cast<Il2CppRGCTXData*>(data);\n"
				+ "        if (values[index].rgctxDataDummy == NULL)\n"
				+ "        {\n"
				+ "            // Generic memory bench: the readers take no lock, so the entry is resolved aside and shown to them only once it is complete,\n"
				+ "            // the way GenericClass::CreateClass shows a class.\n"
				+ "            Il2CppRGCTXData resolved;\n"
				+ "            resolved.rgctxDataDummy = NULL;\n"
				+ "            ResolveRGCTXEntryLocked(&resolved, (const Il2CppImage*)values[-3].rgctxDataDummy, (uint32_t)(uintptr_t)values[-2].rgctxDataDummy,\n"
				+ "                (const Il2CppGenericContext*)values[-1].rgctxDataDummy, index, lock);\n"
				+ "            il2cpp::os::Atomic::ExchangePointer(&values[index].rgctxDataDummy, resolved.rgctxDataDummy);\n"
				+ "        }\n"
				+ "        return data;\n"
				+ "    }\n\n"
				+ "    void GenericMetadata::ResolveRGCTXEntryLocked(Il2CppRGCTXData* slot, const Il2CppImage* image, uint32_t token, const Il2CppGenericContext* context, int32_t rgctxIndex, const FastAutoLock& lock)\n"
				+ "    {\n"
				+ "        RGCTXCollection collection = MetadataCache::GetRGCTXs(image, token);\n"
				+ "        {\n"
				+ "            const Il2CppRGCTXDefinition* definitionData = collection.items + rgctxIndex;\n"
				+ "            switch (definitionData->type)\n";
		}

		/// <summary>
		/// Replaces each text with its counterpart, once. A file that already has the new text is left as it is, so applying
		/// twice does no harm; a file that has neither is not the libil2cpp these changes were written for.
		/// </summary>
		static void Edit(string path, IEnumerable<(string Old, string New)> pairs)
		{
			var raw = File.ReadAllText(path);
			var crlf = raw.Contains("\r\n");
			var text = raw.Replace("\r\n", "\n");
			foreach (var (old, replacement) in pairs)
			{
				if (text.Contains(replacement))
				{
					continue;
				}
				var at = text.IndexOf(old, StringComparison.Ordinal);
				if (at < 0)
				{
					throw new InvalidOperationException("expected text not found in " + Path.GetFileName(path) + ": " + old.Substring(0, Math.Min(60, old.Length)).Replace("\n", " "));
				}
				text = text.Substring(0, at) + replacement + text.Substring(at + old.Length);
			}
			File.WriteAllText(path, crlf ? text.Replace("\n", "\r\n") : text);
		}
	}
}
