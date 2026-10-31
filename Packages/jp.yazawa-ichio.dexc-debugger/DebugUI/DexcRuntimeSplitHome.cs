using System.Linq;
using UnityEngine;

namespace Dexc
{
	/// <summary>
	/// Side by side the column of sections stands in for Home: the menu opens on <see cref="DebugPanels.Home"/> or on the
	/// section opened last, and the gamepad goes into the column where it would go back to Home. The last section is kept
	/// in <see cref="DebugStorage"/>, like favorites, so the menu opens where it was left in the previous run too.
	/// </summary>
	internal static class DexcRuntimeSplitHome
	{
		/// <summary>State of this class. Owned by <see cref="DexcUIRoot"/>; nothing here survives a root reset.</summary>
		internal sealed class State
		{
			internal bool Loaded;
			internal string LastSection;
			internal int SectionFocus = -1;
			internal bool Shown;
		}

		const string LastSectionKey = "UI/LastSection";
		const string DefaultSection = "home:CommandList";

		static State Data => DexcUIRoot.Current.SplitHome;

		/// <summary>Set by the renderer drawing the menu: whether it shows the pages side by side.</summary>
		internal static bool Shown
		{
			get => Data.Shown;
			set => Data.Shown = value;
		}

		/// <summary>Whether the sections stand in for Home: side by side, outside the boot screen, which has no sections.</summary>
		internal static bool Active => Data.Shown && !DebugBootMenu.UsesRuntimeMenu;

		/// <summary>The section the gamepad is on in the column of sections, or -1 while it is on the page.</summary>
		internal static int SectionFocus => Active ? Data.SectionFocus : -1;

		static string LastSection
		{
			get
			{
				var data = Data;
				if (!data.Loaded)
				{
					data.Loaded = true;
					data.LastSection = DebugStorage.TryLoad(LastSectionKey, out var text) ? text : null;
				}
				return data.LastSection;
			}
		}

		/// <summary>Keeps the section a page belongs to, stored at once when it changes.</summary>
		internal static void Remember(DexcRuntimeUIPage page)
		{
			var key = DexcRuntimeUIRenderer.SectionKey(page);
			if (key == null || key == LastSection)
				return;
			Data.LastSection = key;
			DebugStorage.Save(LastSectionKey, key);
			DebugStorage.Flush();
		}

		/// <summary>Side by side there is no Home page: Home opens the home panel when one is registered, else the last section.</summary>
		internal static void Apply()
		{
			if (!Active || DexcRuntimeUIState.Page != DexcRuntimeUIPage.Home)
				return;
			var panel = DebugPanels.Home;
			if (panel != null && DebugPanels.All.Contains(panel))
			{
				DexcRuntimePanelForm.Open(panel);
				return;
			}
			var rows = DexcRuntimeUIRenderer.SectionRows();
			var row =
				rows.FirstOrDefault(item => item.Key == LastSection)
				?? rows.FirstOrDefault(item => item.Key == DefaultSection)
				?? rows.FirstOrDefault();
			row?.Activated?.Invoke();
		}

		/// <summary>Moves the gamepad into the column of sections, on the current page's section. False when not side by side.</summary>
		internal static bool Enter()
		{
			if (!Active)
				return false;
			var current = DexcRuntimeUIRenderer.SectionKey(DexcRuntimeUIState.Page);
			Data.SectionFocus = Mathf.Max(0, DexcRuntimeUIRenderer.SectionRows().FindIndex(row => row.Key == current));
			DexcRuntimeUIState.NavigationFocusVisible = true;
			return true;
		}

		internal static void Leave()
		{
			Data.SectionFocus = -1;
		}

		/// <summary>
		/// The gamepad or keyboard in the column of sections: up and down pick a section, right or Submit opens it and
		/// Cancel closes the menu, as it does on Home. False while the gamepad is on the page.
		/// </summary>
		internal static bool HandleInput(int vertical, int horizontal, bool submit, bool cancel)
		{
			if (SectionFocus < 0)
				return false;
			var rows = DexcRuntimeUIRenderer.SectionRows();
			if (rows.Count == 0)
			{
				Leave();
				return false;
			}
			DexcRuntimeUIState.NavigationFocusVisible = true;
			if (cancel)
			{
				Leave();
				DexcRuntimeUIState.Close();
			}
			else if (vertical != 0)
				Data.SectionFocus = Mathf.Clamp(Data.SectionFocus + vertical, 0, rows.Count - 1);
			else if (submit || horizontal > 0)
			{
				var row = rows[Mathf.Clamp(Data.SectionFocus, 0, rows.Count - 1)];
				Leave();
				row.Activated?.Invoke();
			}
			return true;
		}
	}
}
