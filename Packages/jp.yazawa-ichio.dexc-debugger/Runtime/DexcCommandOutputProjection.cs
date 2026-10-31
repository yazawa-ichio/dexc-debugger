using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Scripting;

namespace Dexc
{
	internal static class DexcCommandOutputProjection
	{
		internal static DebugValue Project(object output)
		{
			if (output == null)
			{
				return DebugValue.Null;
			}
			if (output is bool boolean)
			{
				return DebugValue.From(boolean);
			}
			if (output is sbyte int8)
			{
				return DebugValue.From(int8);
			}
			if (output is byte uint8)
			{
				return DebugValue.From(uint8);
			}
			if (output is short int16)
			{
				return DebugValue.From(int16);
			}
			if (output is ushort uint16)
			{
				return DebugValue.From(uint16);
			}
			if (output is int int32)
			{
				return DebugValue.From(int32);
			}
			if (output is uint uint32)
			{
				return DebugValue.From(uint32);
			}
			if (output is long int64)
			{
				return DebugValue.From(int64);
			}
			if (output is ulong uint64)
			{
				return DebugValue.From(uint64);
			}
			if (output is float float32)
			{
				return DebugValue.From(float32);
			}
			if (output is double float64)
			{
				return DebugValue.From(float64);
			}
			if (output is string text)
			{
				return DebugValue.From(text);
			}
			if (output is Enum enumeration)
			{
				return DebugValue.From(Enum.Format(output.GetType(), enumeration, "G"));
			}
			if (output is Color color)
			{
				return DebugValue.FromObject(
					new[] { Member("r", color.r), Member("g", color.g), Member("b", color.b), Member("a", color.a) }
				);
			}
			if (output is Vector2 two)
			{
				return DebugValue.FromObject(new[] { Member("x", two.x), Member("y", two.y) });
			}
			if (output is Vector3 three)
			{
				return DebugValue.FromObject(new[] { Member("x", three.x), Member("y", three.y), Member("z", three.z) });
			}
			if (output is Vector4 four)
			{
				return DebugValue.FromObject(new[] { Member("x", four.x), Member("y", four.y), Member("z", four.z), Member("w", four.w) });
			}
			if (output is Vector2Int twoInt)
			{
				return DebugValue.FromObject(new[] { IntegerMember("x", twoInt.x), IntegerMember("y", twoInt.y) });
			}
			if (output is Vector3Int threeInt)
			{
				return DebugValue.FromObject(
					new[] { IntegerMember("x", threeInt.x), IntegerMember("y", threeInt.y), IntegerMember("z", threeInt.z) }
				);
			}

			// A concrete collection field avoids JsonUtility's top-level array/list limitation.
			// Use the entire collection type as T so this wrapper only needs reference-type sharing.
			var serializable = output;
			if (output is Array || output.GetType().IsGenericType && output.GetType().GetGenericTypeDefinition() == typeof(List<>))
			{
				var wrapperType = typeof(JsonCollectionWrapper<>).MakeGenericType(output.GetType());
				serializable = Activator.CreateInstance(wrapperType);
				wrapperType
					.GetField(nameof(JsonCollectionWrapper<object>.Items), BindingFlags.Instance | BindingFlags.Public)
					.SetValue(serializable, output);
			}
			return DebugValue.From(JsonUtility.ToJson(serializable));
		}

		internal static string Summary(CommandExecutionResult result)
		{
			if (string.IsNullOrEmpty(result.OutputTypeName))
			{
				return string.Empty;
			}
			return result.OutputTypeName + (result.Output == null ? " (unavailable)" : " (" + result.Output.Kind + ")");
		}

		private static DebugValueMember Member(string name, float value)
		{
			return new DebugValueMember(name, DebugValue.From(value));
		}

		private static DebugValueMember IntegerMember(string name, int value)
		{
			return new DebugValueMember(name, DebugValue.From(value));
		}

		[Serializable, Preserve]
		private sealed class JsonCollectionWrapper<T>
			where T : class
		{
			[Preserve]
			public T Items;

			[Preserve]
			public JsonCollectionWrapper() { }
		}
	}
}
