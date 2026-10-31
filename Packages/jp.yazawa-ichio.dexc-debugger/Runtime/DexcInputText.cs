using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Dexc
{
	// Editing grammar shared by the UI layers; Core accepts DebugValue, not text.
	internal static class DexcInputText
	{
		internal static DebugValue ParseLike(DebugValue prototype, string text, Type type)
		{
			if (prototype == null || text == null)
				return null;
			DebugValue parsed;
			if (prototype.TryGetObject(out var members))
			{
				var components = text.Split(',');
				if (components.Length != members.Count)
					return null;
				var values = new List<DebugValueMember>();
				for (var index = 0; index < members.Count; index++)
				{
					var component = components[index].Trim();
					var separator = component.IndexOf('=');
					if (separator >= 0)
					{
						if (!string.Equals(component.Substring(0, separator).Trim(), members[index].Name, StringComparison.Ordinal))
							return null;
						component = component.Substring(separator + 1).Trim();
					}
					var value = ParseScalar(members[index].Value.Kind, component);
					if (value == null)
						return null;
					values.Add(new DebugValueMember(members[index].Name, value));
				}
				parsed = DebugValue.FromObject(values);
			}
			else
				parsed = ParseScalar(prototype.Kind, text);
			return FitsType(parsed, type) ? parsed : null;
		}

		internal static bool FitsType(DebugValue value, Type type)
		{
			if (value == null)
				return false;
			if (type == null)
				return true;
			if (value.TryGetSignedInteger(out var signed))
			{
				if (type == typeof(sbyte))
					return signed >= sbyte.MinValue && signed <= sbyte.MaxValue;
				if (type == typeof(short))
					return signed >= short.MinValue && signed <= short.MaxValue;
				if (type == typeof(int))
					return signed >= int.MinValue && signed <= int.MaxValue;
			}
			if (value.TryGetUnsignedInteger(out var unsigned))
			{
				if (type == typeof(byte))
					return unsigned <= byte.MaxValue;
				if (type == typeof(ushort))
					return unsigned <= ushort.MaxValue;
				if (type == typeof(uint))
					return unsigned <= uint.MaxValue;
			}
			if (value.TryGetFloatingPoint(out var number))
				return type != typeof(float) || number >= float.MinValue && number <= float.MaxValue;
			if (type.IsEnum)
				return value.TryGetString(out var name) && Enum.IsDefined(type, name);
			if (value.TryGetObject(out var members))
			{
				var integer = type == typeof(UnityEngine.Vector2Int) || type == typeof(UnityEngine.Vector3Int);
				return members.All(member => FitsType(member.Value, integer ? typeof(int) : typeof(float)));
			}
			return true;
		}

		internal static string Format(DebugValue value)
		{
			if (value == null)
				return string.Empty;
			if (value.TryGetString(out var text))
				return text;
			if (value.TryGetBoolean(out var boolean))
				return boolean ? "true" : "false";
			if (value.TryGetSignedInteger(out var signed))
				return signed.ToString(CultureInfo.InvariantCulture);
			if (value.TryGetUnsignedInteger(out var unsigned))
				return unsigned.ToString(CultureInfo.InvariantCulture);
			if (value.TryGetFloatingPoint(out var floating))
				return floating.ToString("R", CultureInfo.InvariantCulture);
			if (value.TryGetObject(out var members))
				return string.Join(", ", members.Select(member => member.Name + "=" + Format(member.Value)));
			if (value.TryGetArray(out var values))
				return string.Join(", ", values.Select(Format));
			return value.Kind.ToString();
		}

		internal static DebugValue ParseScalar(DebugValueKind kind, string text)
		{
			if (text == null)
				return null;
			switch (kind)
			{
				case DebugValueKind.String:
					return DebugValue.From(text);
				case DebugValueKind.Boolean:
					return bool.TryParse(text, out var boolean) ? DebugValue.From(boolean) : null;
				case DebugValueKind.SignedInteger:
					return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signed)
						? DebugValue.From(signed)
						: null;
				case DebugValueKind.UnsignedInteger:
					return
						!text.TrimStart().StartsWith("-", StringComparison.Ordinal)
						&& ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unsigned)
						? DebugValue.From(unsigned)
						: null;
				case DebugValueKind.FloatingPoint:
					return
						double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floating)
						&& !double.IsNaN(floating)
						&& !double.IsInfinity(floating)
						? DebugValue.From(floating)
						: null;
				default:
					return null;
			}
		}

		internal static DebugValue Parse(DebugInputSchema schema, string text)
		{
			if (schema == null || text == null)
				return null;
			if (schema.ValueKind != DebugValueKind.Object)
				return ParseScalar(schema.ValueKind, text);
			var components = text.Split(',');
			if (components.Length != schema.Members.Count)
				return null;
			var members = new List<DebugValueMember>();
			for (var index = 0; index < schema.Members.Count; index++)
			{
				var member = schema.Members[index];
				var component = components[index].Trim();
				var separator = component.IndexOf('=');
				if (separator >= 0)
				{
					if (!string.Equals(component.Substring(0, separator).Trim(), member.Name, StringComparison.Ordinal))
						return null;
					component = component.Substring(separator + 1).Trim();
				}
				var parsed = Parse(member.Schema, component);
				if (parsed == null)
					return null;
				members.Add(new DebugValueMember(member.Name, parsed));
			}
			return DebugValue.FromObject(members);
		}
	}
}
