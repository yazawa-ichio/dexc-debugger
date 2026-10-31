using System.Collections.Generic;

namespace Dexc
{
	internal enum DexcFavoriteKind
	{
		Command,
		Setting,
	}

	/// <summary>
	/// Commands and setting groups the user starred. They are kept by path, because ids change every session, and in
	/// <see cref="DebugStorage"/>, so they last across runs like boot values. A path that no longer exists is skipped
	/// where favorites are listed, not forgotten: the command may come back in the next build.
	/// </summary>
	internal static class DexcRuntimeFavorites
	{
		/// <summary>State of this class. Owned by <see cref="DexcUIRoot"/>; nothing here survives a root reset.</summary>
		internal sealed class State
		{
			internal List<string> Commands;
			internal List<string> Settings;
		}

		const string CommandKey = "UI/Favorites/Command";
		const string SettingKey = "UI/Favorites/Setting";

		static State Data => DexcUIRoot.Current.Favorites;

		static string Key(DexcFavoriteKind kind) => kind == DexcFavoriteKind.Command ? CommandKey : SettingKey;

		static List<string> Paths(DexcFavoriteKind kind)
		{
			var data = Data;
			var paths = kind == DexcFavoriteKind.Command ? data.Commands : data.Settings;
			if (paths != null)
				return paths;
			// One path per line: paths never hold a line break, and the stored text stays readable in PlayerPrefs.
			paths = new List<string>();
			if (DebugStorage.TryLoad(Key(kind), out var text) && !string.IsNullOrEmpty(text))
				paths.AddRange(text.Split('\n'));
			if (kind == DexcFavoriteKind.Command)
				data.Commands = paths;
			else
				data.Settings = paths;
			return paths;
		}

		/// <summary>The starred paths, in the order they were starred.</summary>
		internal static IReadOnlyList<string> All(DexcFavoriteKind kind) => Paths(kind);

		internal static bool Contains(DexcFavoriteKind kind, string path) => !string.IsNullOrEmpty(path) && Paths(kind).Contains(path);

		/// <summary>Stars the path, or takes the star off, and stores the list at once.</summary>
		internal static void Toggle(DexcFavoriteKind kind, string path)
		{
			if (string.IsNullOrEmpty(path))
				return;
			var paths = Paths(kind);
			if (!paths.Remove(path))
				paths.Add(path);
			DebugStorage.Save(Key(kind), string.Join("\n", paths));
			DebugStorage.Flush();
		}

		/// <summary>What the open detail page stands for, when it can be starred.</summary>
		internal static bool TryGetCurrent(DexcRuntimeUIPage page, out DexcFavoriteKind kind, out string path)
		{
			kind = DexcFavoriteKind.Command;
			path = null;
			if (page == DexcRuntimeUIPage.CommandDetail && DexcRuntimeUIState.Command != null)
				path = DexcRuntimeUIState.Command.Path;
			else if (page == DexcRuntimeUIPage.SettingDetail && DexcRuntimeSettingForm.Current != null)
			{
				kind = DexcFavoriteKind.Setting;
				path = DexcRuntimeSettingForm.Current.Label;
			}
			return path != null;
		}

		/// <summary>The header's star on a page that can be starred: filled when starred, null on other pages.</summary>
		internal static string Star(DexcRuntimeUIPage page) =>
			TryGetCurrent(page, out var kind, out var path) ? Contains(kind, path) ? "★" : "☆" : null;

		internal static void ToggleCurrent(DexcRuntimeUIPage page)
		{
			if (TryGetCurrent(page, out var kind, out var path))
				Toggle(kind, path);
		}
	}
}
