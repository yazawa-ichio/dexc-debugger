using System.Linq;

namespace Dexc
{
	internal static class DexcRuntimeCommandFormComponents
	{
		internal static bool Supports(DebugCommandFieldDescriptor field)
		{
			var schema = field?.Schema;
			return schema != null
				&& schema.ValueKind == DebugValueKind.Object
				&& schema.Members.Count >= 2
				&& schema.Members.Count <= 4
				&& schema.Members.All(member =>
					member.Schema.ValueKind == DebugValueKind.SignedInteger
					|| member.Schema.ValueKind == DebugValueKind.UnsignedInteger
					|| member.Schema.ValueKind == DebugValueKind.FloatingPoint
				);
		}

		internal static int RowCount(DebugCommandFieldDescriptor field)
		{
			return Supports(field) ? field.Schema.Members.Count : 1;
		}

		internal static string Text(DebugCommandFieldDescriptor field, string memberName)
		{
			var key = Key(field, memberName);
			if (DexcRuntimeUIState.StructuredInputText.TryGetValue(key, out var text))
				return text;
			var value = DexcCommandRuntime.Drafts.GetInputValue(DexcRuntimeUIState.DraftId, field.Id);
			if (value != null && value.TryGetObject(out var members))
			{
				var member = members.FirstOrDefault(item => item.Name == memberName);
				if (member != null)
					return DexcRuntimeUIRenderer.ValueText(member.Value);
			}
			return string.Empty;
		}

		internal static void Change(DebugCommandFieldDescriptor field, string memberName, string text)
		{
			DexcRuntimeUIState.StructuredInputText[Key(field, memberName)] = text ?? string.Empty;
			// Use the existing schema parser and validation path, including Int32 vector bounds.
			var raw = string.Join(", ", field.Schema.Members.Select(member => member.Name + "=" + Text(field, member.Name)));
			DexcRuntimeUIRenderer.UpdateCommandInputText(field, raw);
		}

		internal static bool IsInvalid(DebugCommandFieldDescriptor field, DebugInputMemberSchema member)
		{
			return DexcRuntimeUIRenderer.ParseSchemaValue(member.Schema, Text(field, member.Name)) == null;
		}

		internal static void Invalidate(DebugCommandFieldDescriptor field)
		{
			if (field?.Schema == null)
				return;
			foreach (var member in field.Schema.Members)
				DexcRuntimeUIState.StructuredInputText.Remove(Key(field, member.Name));
		}

		static string Key(DebugCommandFieldDescriptor field, string memberName)
		{
			return "command-component:"
				+ DexcRuntimeUIState.SessionId
				+ ":"
				+ DexcRuntimeUIState.DraftId
				+ ":"
				+ field.Id
				+ ":"
				+ memberName;
		}
	}
}
