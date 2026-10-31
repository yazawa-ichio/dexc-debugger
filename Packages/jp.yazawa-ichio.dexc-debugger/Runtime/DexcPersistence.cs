using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Dexc
{
	/// <summary>
	/// Key/value storage used by DebugBoot and persistent DebugSetting values.
	/// Implement this to redirect reads and writes on platforms without PlayerPrefs (consoles, custom save systems).
	/// </summary>
	public interface IDebugStorage
	{
		bool TryLoad(string key, out string value);
		void Save(string key, string value);
		void Delete(string key);
		void Flush();
	}

	/// <summary>Chooses where DebugBoot and persistent DebugSetting values are stored.</summary>
	public static class DebugStorage
	{
		/// <summary>
		/// Active storage. Made by <see cref="Factory"/> when one is set, otherwise <see cref="DebugPlayerPrefsStorage"/>.
		/// A storage assigned here lasts until Dexc renews its state, which it does on entering Play Mode and on returning
		/// to Edit Mode; to keep the same place across those, set <see cref="Factory"/> instead.
		/// </summary>
		public static IDebugStorage Storage
		{
			get
			{
				var root = DexcRoot.Current;
				return root.Storage ?? (root.Storage = root.StorageFactory?.Invoke() ?? new DebugPlayerPrefsStorage());
			}
			set { DexcRoot.Current.Storage = value ?? throw new ArgumentNullException(nameof(value)); }
		}

		/// <summary>
		/// How to make the storage. Dexc keeps it when it renews its state and makes a fresh storage from it each time, so
		/// a project pointed at a file stays pointed there in Play Mode and in Edit Mode, and re-reads the file each time.
		/// Setting it flushes the current storage and replaces it on next use. Null goes back to PlayerPrefs.
		/// </summary>
		public static Func<IDebugStorage> Factory
		{
			get => DexcRoot.Current.StorageFactory;
			set
			{
				var root = DexcRoot.Current;
				root.Storage?.Flush();
				root.StorageFactory = value;
				root.Storage = null;
			}
		}

		public static bool TryLoad(string key, out string value)
		{
			value = null;
			if (string.IsNullOrEmpty(key))
				return false;
			try
			{
				return Storage.TryLoad(key, out value);
			}
			catch (Exception exception)
			{
				Debug.LogWarning("Dexc storage load failed for '" + key + "': " + exception.Message);
				return false;
			}
		}

		public static void Save(string key, string value)
		{
			if (string.IsNullOrEmpty(key))
				return;
			try
			{
				Storage.Save(key, value ?? string.Empty);
			}
			catch (Exception exception)
			{
				Debug.LogWarning("Dexc storage save failed for '" + key + "': " + exception.Message);
			}
		}

		public static void Delete(string key)
		{
			if (string.IsNullOrEmpty(key))
				return;
			try
			{
				Storage.Delete(key);
			}
			catch (Exception exception)
			{
				Debug.LogWarning("Dexc storage delete failed for '" + key + "': " + exception.Message);
			}
		}

		public static void Flush()
		{
			try
			{
				Storage.Flush();
			}
			catch (Exception exception)
			{
				Debug.LogWarning("Dexc storage flush failed: " + exception.Message);
			}
		}

#if UNITY_INCLUDE_TESTS
		internal static void ResetForTests()
		{
			DexcRoot.Current.Storage = null;
			DexcRoot.Current.StorageFactory = null;
		}
#endif
	}

	/// <summary>Default storage backed by PlayerPrefs.</summary>
	public sealed class DebugPlayerPrefsStorage : IDebugStorage
	{
		readonly string m_Prefix;

		public DebugPlayerPrefsStorage(string prefix = "Dexc/")
		{
			m_Prefix = prefix ?? string.Empty;
		}

		public bool TryLoad(string key, out string value)
		{
			var full = m_Prefix + key;
			if (!PlayerPrefs.HasKey(full))
			{
				value = null;
				return false;
			}
			value = PlayerPrefs.GetString(full, string.Empty);
			return true;
		}

		public void Save(string key, string value)
		{
			PlayerPrefs.SetString(m_Prefix + key, value);
		}

		public void Delete(string key)
		{
			PlayerPrefs.DeleteKey(m_Prefix + key);
		}

		public void Flush()
		{
			PlayerPrefs.Save();
		}
	}

	/// <summary>Storage backed by a single text file (one "key\tvalue" line per entry). Suitable where PlayerPrefs is unavailable.</summary>
	public sealed class DebugFileStorage : IDebugStorage
	{
		readonly string m_Path;
		readonly Dictionary<string, string> m_Values = new Dictionary<string, string>(StringComparer.Ordinal);
		bool m_Loaded;
		bool m_Dirty;

		// Keys saved or deleted before the file was read: the file's older value must not come back over them.
		readonly HashSet<string> m_Changed = new HashSet<string>(StringComparer.Ordinal);

		public DebugFileStorage(string path = null)
		{
			m_Path = string.IsNullOrEmpty(path) ? Path.Combine(Application.persistentDataPath, "dexc-debug.txt") : path;
		}

		public string FilePath => m_Path;

		void EnsureLoaded()
		{
			if (m_Loaded)
				return;
			// The store counts as loaded only once the file has been read. A read that fails is tried again later, so a Flush
			// never writes the file over with only the keys saved since.
			if (File.Exists(m_Path))
			{
				foreach (var line in File.ReadAllLines(m_Path, Encoding.UTF8))
				{
					var separator = line.IndexOf('\t');
					if (separator <= 0)
						continue;
					var key = Unescape(line.Substring(0, separator));
					if (!m_Changed.Contains(key))
						m_Values[key] = Unescape(line.Substring(separator + 1));
				}
			}
			m_Changed.Clear();
			m_Loaded = true;
		}

		public bool TryLoad(string key, out string value)
		{
			if (!m_Loaded && m_Changed.Contains(key))
				return m_Values.TryGetValue(key, out value);
			EnsureLoaded();
			return m_Values.TryGetValue(key, out value);
		}

		/// <summary>Kept in memory until <see cref="Flush"/>, even while the file cannot be read: the Flush reads it first.</summary>
		public void Save(string key, string value)
		{
			if (!m_Loaded)
				m_Changed.Add(key);
			m_Values[key] = value;
			m_Dirty = true;
		}

		public void Delete(string key)
		{
			if (!m_Loaded)
			{
				m_Changed.Add(key);
				m_Values.Remove(key);
				m_Dirty = true;
			}
			else if (m_Values.Remove(key))
				m_Dirty = true;
		}

		public void Flush()
		{
			if (!m_Dirty)
				return;
			EnsureLoaded();
			var directory = Path.GetDirectoryName(m_Path);
			if (!string.IsNullOrEmpty(directory))
				Directory.CreateDirectory(directory);
			var builder = new StringBuilder();
			foreach (var pair in m_Values)
				builder.Append(Escape(pair.Key)).Append('\t').Append(Escape(pair.Value)).Append('\n');
			File.WriteAllText(m_Path, builder.ToString(), Encoding.UTF8);
			// Still dirty when the write fails, so the next Flush tries again.
			m_Dirty = false;
		}

		static string Escape(string text)
		{
			return text.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");
		}

		static string Unescape(string text)
		{
			var builder = new StringBuilder(text.Length);
			for (var i = 0; i < text.Length; i++)
			{
				var c = text[i];
				if (c != '\\' || i + 1 >= text.Length)
				{
					builder.Append(c);
					continue;
				}
				var next = text[++i];
				builder.Append(
					next == 't' ? '\t'
					: next == 'n' ? '\n'
					: next == 'r' ? '\r'
					: next
				);
			}
			return builder.ToString();
		}
	}

	/// <summary>In-memory storage. Useful for tests or to disable persistence.</summary>
	public sealed class DebugMemoryStorage : IDebugStorage
	{
		readonly Dictionary<string, string> m_Values = new Dictionary<string, string>(StringComparer.Ordinal);

		public bool TryLoad(string key, out string value)
		{
			return m_Values.TryGetValue(key, out value);
		}

		public void Save(string key, string value)
		{
			m_Values[key] = value;
		}

		public void Delete(string key)
		{
			m_Values.Remove(key);
		}

		public void Flush() { }

		public int Count => m_Values.Count;
	}

	/// <summary>Serializes DebugValue to and from a compact text form used by storage.</summary>
	internal static class DexcPersistedValue
	{
		internal static string Serialize(DebugValue value)
		{
			var builder = new StringBuilder();
			Write(builder, value);
			return builder.ToString();
		}

		static void Write(StringBuilder builder, DebugValue value)
		{
			switch (value.Kind)
			{
				case DebugValueKind.Boolean:
					value.TryGetBoolean(out var b);
					builder.Append(b ? "b1" : "b0");
					break;
				case DebugValueKind.SignedInteger:
					value.TryGetSignedInteger(out var s);
					builder.Append('i').Append(s.ToString(CultureInfo.InvariantCulture));
					break;
				case DebugValueKind.UnsignedInteger:
					value.TryGetUnsignedInteger(out var u);
					builder.Append('u').Append(u.ToString(CultureInfo.InvariantCulture));
					break;
				case DebugValueKind.FloatingPoint:
					value.TryGetFloatingPoint(out var f);
					builder.Append('f').Append(f.ToString("R", CultureInfo.InvariantCulture));
					break;
				case DebugValueKind.String:
					value.TryGetString(out var text);
					builder.Append('s').Append(text.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(text);
					break;
				case DebugValueKind.Array:
					value.TryGetArray(out var items);
					builder.Append('a').Append(items.Count.ToString(CultureInfo.InvariantCulture)).Append('[');
					foreach (var item in items)
					{
						Write(builder, item);
						builder.Append(';');
					}
					builder.Append(']');
					break;
				case DebugValueKind.Object:
					value.TryGetObject(out var members);
					builder.Append('o').Append(members.Count.ToString(CultureInfo.InvariantCulture)).Append('{');
					foreach (var member in members)
					{
						builder
							.Append(member.Name.Length.ToString(CultureInfo.InvariantCulture))
							.Append(':')
							.Append(member.Name)
							.Append('=');
						Write(builder, member.Value);
						builder.Append(';');
					}
					builder.Append('}');
					break;
				default:
					builder.Append('n');
					break;
			}
		}

		internal static bool TryDeserialize(string text, out DebugValue value)
		{
			value = null;
			if (string.IsNullOrEmpty(text))
				return false;
			try
			{
				var position = 0;
				value = Read(text, ref position);
				return value != null;
			}
			catch
			{
				value = null;
				return false;
			}
		}

		static DebugValue Read(string text, ref int position)
		{
			var kind = text[position++];
			switch (kind)
			{
				case 'n':
					return DebugValue.Null;
				case 'b':
					return DebugValue.From(text[position++] == '1');
				case 'i':
					return DebugValue.From(long.Parse(ReadToken(text, ref position), CultureInfo.InvariantCulture));
				case 'u':
					return DebugValue.From(ulong.Parse(ReadToken(text, ref position), CultureInfo.InvariantCulture));
				case 'f':
					return DebugValue.From(double.Parse(ReadToken(text, ref position), CultureInfo.InvariantCulture));
				case 's':
				{
					var length = ReadLength(text, ref position);
					var result = text.Substring(position, length);
					position += length;
					return DebugValue.From(result);
				}
				case 'a':
				{
					var count = ReadCount(text, ref position, '[');
					var items = new List<DebugValue>(count);
					for (var i = 0; i < count; i++)
					{
						items.Add(Read(text, ref position));
						Expect(text, ref position, ';');
					}
					Expect(text, ref position, ']');
					return DebugValue.FromArray(items);
				}
				case 'o':
				{
					var count = ReadCount(text, ref position, '{');
					var members = new List<DebugValueMember>(count);
					for (var i = 0; i < count; i++)
					{
						var length = ReadLength(text, ref position);
						var name = text.Substring(position, length);
						position += length;
						Expect(text, ref position, '=');
						members.Add(new DebugValueMember(name, Read(text, ref position)));
						Expect(text, ref position, ';');
					}
					Expect(text, ref position, '}');
					return DebugValue.FromObject(members);
				}
				default:
					throw new FormatException("Unknown value kind.");
			}
		}

		static string ReadToken(string text, ref int position)
		{
			var start = position;
			while (position < text.Length && text[position] != ';' && text[position] != ']' && text[position] != '}')
				position++;
			return text.Substring(start, position - start);
		}

		static int ReadLength(string text, ref int position)
		{
			var start = position;
			while (text[position] != ':')
				position++;
			var length = int.Parse(text.Substring(start, position - start), CultureInfo.InvariantCulture);
			position++;
			return length;
		}

		static int ReadCount(string text, ref int position, char open)
		{
			var start = position;
			while (text[position] != open)
				position++;
			var count = int.Parse(text.Substring(start, position - start), CultureInfo.InvariantCulture);
			position++;
			return count;
		}

		static void Expect(string text, ref int position, char c)
		{
			if (text[position++] != c)
				throw new FormatException("Expected '" + c + "'.");
		}
	}
}
