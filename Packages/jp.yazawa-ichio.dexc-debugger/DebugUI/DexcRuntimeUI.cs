using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace Dexc
{
	/// <summary>Controls the Runtime Debug Menu shell. Display rendering is independent from this menu.</summary>
	public static class DebugMenu
	{
		[Conditional("DEXC_DEBUGGER")]
		public static void Open()
		{
			DexcRuntimeUIState.Open();
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void Close()
		{
			DexcRuntimeUIState.Close();
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void Toggle()
		{
			DexcRuntimeUIState.Toggle();
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void SetInputProvider(DebugMenuInputProvider provider)
		{
			DexcRuntimeUIState.SetInputProvider(provider);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void ClearInputProvider(DebugMenuInputProvider provider)
		{
			DexcRuntimeUIState.ClearInputProvider(provider);
		}

		/// <summary>Whether the Runtime menu is open. Always false when Dexc is disabled, since nothing can open it.</summary>
		public static bool IsOpen => DexcRuntimeUIState.IsOpen;

		/// <summary>The active input provider, or null. Set one before adding another; a second provider is refused.</summary>
		public static DebugMenuInputProvider InputProvider => DexcRuntimeUIState.InputProvider;

		/// <summary>The renderer currently drawing the Runtime menu. IMGUI while UI Toolkit falls back for want of PanelSettings.</summary>
		public static DebugMenuRenderer Renderer => DexcRuntimeUIRendererSelection.Drawing;

		/// <summary>
		/// Switches the Runtime menu renderer at runtime. Closes the menu if it is open and keeps the input provider.
		/// The switch is applied on the next Update. Returns false only when uGUI is requested without the uGUI package.
		/// A UI Toolkit renderer that cannot start (Skin without PanelSettings) falls back to IMGUI when the switch is applied;
		/// <see cref="Renderer"/> and a diagnostic then say so.
		/// </summary>
		public static bool SetRenderer(DebugMenuRenderer renderer)
		{
#if DEXC_DEBUGGER
			return DexcRuntimeUIRendererSelection.Request(renderer);
#else
			return renderer == DebugMenuRenderer.IMGUI;
#endif
		}

		/// <summary>Display name of a renderer ("IMGUI", "uGUI", "UI Toolkit").</summary>
		public static string RendererName(DebugMenuRenderer renderer)
		{
			switch (renderer)
			{
				case DebugMenuRenderer.UGUI:
					return "uGUI";
				case DebugMenuRenderer.UIToolkit:
					return "UI Toolkit";
				default:
					return "IMGUI";
			}
		}
	}

	/// <summary>Runtime menu renderer. uGUI needs com.unity.ugui; UI Toolkit needs a Skin with a PanelSettings template.</summary>
	public enum DebugMenuRenderer
	{
		IMGUI,
		UGUI,
		UIToolkit,
	}

	public abstract class DebugMenuInputProvider
	{
		public abstract DebugMenuInputFrame ReadInput();

		public virtual void OnInputStateChanged(DebugMenuInputState state) { }
	}

	public readonly struct DebugMenuInputFrame
	{
		public DebugMenuInputFrame(Vector2 navigation, bool submitPressed, bool cancelPressed)
		{
			Navigation = navigation;
			SubmitPressed = submitPressed;
			CancelPressed = cancelPressed;
		}

		public Vector2 Navigation { get; }
		public bool SubmitPressed { get; }
		public bool CancelPressed { get; }
	}

	public enum DebugMenuInputState
	{
		Closed,
		Navigation,
		TextInput,
	}

	internal enum DexcRuntimeUIPage
	{
		Home,
		CommandList,
		CommandDetail,
		SettingList,
		SettingDetail,
		InspectorHome,
		InspectorRegistered,
		InspectorScene,
		InspectorComponents,
		InspectorFields,
		InspectorWatches,
		History,
		Diagnostic,
		FormDetail,
		PanelList,
		PanelDetail,
	}

	internal readonly struct DexcRuntimeUILayout
	{
		internal DexcRuntimeUILayout(
			Rect screen,
			Rect safeArea,
			Rect panel,
			Rect header,
			Rect contentViewport,
			Rect footer,
			float rowHeight,
			DexcRuntimeUIProfileKind profile,
			Vector2 referenceResolution,
			float scale
		)
		{
			Screen = screen;
			SafeArea = safeArea;
			Panel = panel;
			Header = header;
			ContentViewport = contentViewport;
			Footer = footer;
			RowHeight = rowHeight;
			Profile = profile;
			ReferenceResolution = referenceResolution;
			Scale = scale;
		}

		internal Rect Screen { get; }
		internal Rect SafeArea { get; }
		internal Rect Panel { get; }
		internal Rect Header { get; }
		internal Rect ContentViewport { get; }
		internal Rect Footer { get; }
		internal float RowHeight { get; }
		internal DexcRuntimeUIProfileKind Profile { get; }
		internal Vector2 ReferenceResolution { get; }
		internal float Scale { get; }

		internal DexcRuntimeUILayout LogicalCoordinates()
		{
			var divisor = Scale > 0f ? Scale : 1f;
			return new DexcRuntimeUILayout(
				ScaleRect(Screen, divisor),
				ScaleRect(SafeArea, divisor),
				ScaleRect(Panel, divisor),
				ScaleRect(Header, divisor),
				ScaleRect(ContentViewport, divisor),
				ScaleRect(Footer, divisor),
				RowHeight,
				Profile,
				ReferenceResolution,
				Scale
			);
		}

		private static Rect ScaleRect(Rect value, float divisor)
		{
			return new Rect(value.x / divisor, value.y / divisor, value.width / divisor, value.height / divisor);
		}
	}

	internal enum DexcRuntimeUIProfileKind
	{
		AndroidPortrait,
		AndroidLandscape,
		Windows,
	}

	internal readonly struct DexcRuntimeUIReferenceLayout
	{
		internal DexcRuntimeUIReferenceLayout(
			Rect screen,
			Rect safeArea,
			Rect panel,
			DexcRuntimeUIProfileKind profile,
			Vector2 referenceResolution,
			float scale,
			float rowHeight,
			float hitHeight,
			bool canSplit = false,
			bool split = false
		)
		{
			Screen = screen;
			SafeArea = safeArea;
			Panel = panel;
			Profile = profile;
			ReferenceResolution = referenceResolution;
			Scale = scale;
			RowHeight = rowHeight;
			HitHeight = hitHeight;
			CanSplit = canSplit;
			Split = split;
		}

		/// <summary>The safe area is wide enough for the side-by-side panel, so the header offers the switch.</summary>
		internal bool CanSplit { get; }

		/// <summary>The panel shows the sections, a list and a page side by side.</summary>
		internal bool Split { get; }

		internal Rect Screen { get; }
		internal Rect SafeArea { get; }
		internal Rect Panel { get; }
		internal DexcRuntimeUIProfileKind Profile { get; }
		internal Vector2 ReferenceResolution { get; }
		internal float Scale { get; }
		internal float RowHeight { get; }
		internal float HitHeight { get; }
	}

	internal static class DexcRuntimeUIReferenceResolver
	{
		private static readonly Vector2 AndroidPortraitReference = new Vector2(390f, 844f);
		private static readonly Vector2 AndroidLandscapeReference = new Vector2(844f, 390f);
		private static readonly Vector2 WindowsReference = new Vector2(1366f, 768f);

		internal static DexcRuntimeUIProfileKind ResolveCurrentProfile(int width, int height)
		{
			return ResolveProfile(Application.isEditor, EditorTargetsAndroid, Application.platform, width, height);
		}

		internal static DexcRuntimeUIProfileKind ResolveProfile(
			bool editor,
			bool editorTargetsAndroid,
			RuntimePlatform platform,
			int width,
			int height
		)
		{
			var android = editor ? editorTargetsAndroid : platform == RuntimePlatform.Android;
			if (!android)
				return DexcRuntimeUIProfileKind.Windows;
			return height >= width ? DexcRuntimeUIProfileKind.AndroidPortrait : DexcRuntimeUIProfileKind.AndroidLandscape;
		}

		internal static DexcRuntimeUIReferenceLayout Calculate(int width, int height, Rect safeArea, DexcRuntimeUIProfileKind profile)
		{
			var screen = new Rect(0f, 0f, Mathf.Max(0, width), Mathf.Max(0, height));
			var safe = Intersect(screen, safeArea);
			var reference = ReferenceResolution(profile);
			var scale = safe.width > 0f && safe.height > 0f ? Mathf.Min(safe.width / reference.x, safe.height / reference.y) : 1f;
			if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f)
				scale = 1f;
			ResolveProfileValues(
				profile,
				out var panelSize,
				out var margin,
				out var alignment,
				out var rowHeight,
				out var hitHeight,
				out var layout,
				out var splitSize
			);
			hitHeight = DexcRuntimeFormLayout.RowHeight(rowHeight, hitHeight);
			margin *= scale;
			panelSize *= scale;
			var availableWidth = Mathf.Max(0f, safe.width - margin * 2f);
			var availableHeight = Mathf.Max(0f, safe.height - margin * 2f);
			// Side by side needs a landscape screen and the whole wide panel; the header's switch overrides the skin's choice
			// while the menu runs.
			var canSplit = safe.width > safe.height && availableWidth >= splitSize.x * scale;
			var mode = DexcRuntimeUIState.LayoutOverride ?? layout;
			var split = canSplit && mode != DexcRuntimeUILayoutMode.Single;
			if (split)
				panelSize = splitSize * scale;
			var panelWidth = Mathf.Min(panelSize.x, availableWidth);
			var panelHeight = Mathf.Min(panelSize.y, availableHeight);
			var left =
				alignment == DexcRuntimeUIAlignment.Bottom
				|| alignment == DexcRuntimeUIAlignment.LeftTop
				|| alignment == DexcRuntimeUIAlignment.LeftCenter;
			var panelX = left ? safe.x + margin : safe.xMax - margin - panelWidth;
			var panelY =
				alignment == DexcRuntimeUIAlignment.RightCenter || alignment == DexcRuntimeUIAlignment.LeftCenter
					? safe.y + (safe.height - panelHeight) * 0.5f
				: alignment == DexcRuntimeUIAlignment.RightTop || alignment == DexcRuntimeUIAlignment.LeftTop
					? safe.yMax - margin - panelHeight
				: safe.y + margin;
			return new DexcRuntimeUIReferenceLayout(
				screen,
				safe,
				new Rect(panelX, panelY, panelWidth, panelHeight),
				profile,
				reference,
				scale,
				rowHeight,
				hitHeight,
				canSplit,
				split
			);
		}

		/// <summary>The panel of the menu or boot menu on screen, in GUI coordinates (origin top left), or null while neither is open.</summary>
		internal static Rect? OpenPanel()
		{
			if (!DexcRuntimeUIState.IsOpen && !DebugBootMenu.IsOpen)
				return null;
			var reference = DexcRuntimeUIRenderer.CurrentReference();
			var panel = reference.Panel;
			return new Rect(panel.x, reference.Screen.height - panel.yMax, panel.width, panel.height);
		}

		internal static Vector2 ReferenceResolution(DexcRuntimeUIProfileKind profile)
		{
			return profile == DexcRuntimeUIProfileKind.AndroidPortrait ? AndroidPortraitReference
				: profile == DexcRuntimeUIProfileKind.AndroidLandscape ? AndroidLandscapeReference
				: WindowsReference;
		}

#if UNITY_INCLUDE_TESTS
		internal static DexcRuntimeUIProfileKind LegacyProfileForTests(int width, int height)
		{
			return width >= 1024 ? DexcRuntimeUIProfileKind.Windows
				: height >= width ? DexcRuntimeUIProfileKind.AndroidPortrait
				: DexcRuntimeUIProfileKind.AndroidLandscape;
		}
#endif

		/// <summary>
		/// The boot screen can sit somewhere else than the debug menu. They are shown at different times and mean different
		/// things, so a project that wants the start options out of the way of its own launch art can move just those.
		/// </summary>
		private static DexcRuntimeUIAlignment BootAlignment(DexcRuntimeUIAlignment profileAlignment)
		{
			return DebugBootMenu.UsesRuntimeMenu && DebugBootMenu.Alignment.HasValue ? DebugBootMenu.Alignment.Value : profileAlignment;
		}

		private static void ResolveProfileValues(
			DexcRuntimeUIProfileKind profile,
			out Vector2 panelSize,
			out float margin,
			out DexcRuntimeUIAlignment alignment,
			out float rowHeight,
			out float hitHeight,
			out DexcRuntimeUILayoutMode layout,
			out Vector2 splitSize
		)
		{
			var skin = DexcDebugger.CurrentSession == null ? null : DexcDebugger.CurrentSession.Options.RuntimeUISkin;
			layout = DexcRuntimeUILayoutMode.Single;
			splitSize = DexcRuntimeUISkinSnapshot.Profile.DefaultSplitPanelSize;
			var source =
				skin == null ? null
				: profile == DexcRuntimeUIProfileKind.AndroidPortrait ? skin.AndroidPortrait
				: profile == DexcRuntimeUIProfileKind.AndroidLandscape ? skin.AndroidLandscape
				: skin.Windows;
			if (source != null)
			{
				panelSize = source.PanelSize;
				margin = source.SafeAreaMargin;
				alignment = BootAlignment(source.Alignment);
				rowHeight = source.RowHeight;
				hitHeight = source.MinimumHitHeight;
				layout = source.Layout;
				splitSize = source.SplitPanelSize;
				return;
			}

			if (profile == DexcRuntimeUIProfileKind.AndroidPortrait)
			{
				panelSize = new Vector2(320f, 520f);
				margin = 12f;
				alignment = BootAlignment(DexcRuntimeUIAlignment.Bottom);
				rowHeight = 28f;
				hitHeight = 28f;
				return;
			}
			if (profile == DexcRuntimeUIProfileKind.AndroidLandscape)
			{
				panelSize = new Vector2(480f, 366f);
				margin = 12f;
				alignment = BootAlignment(DexcRuntimeUIAlignment.LeftTop);
				rowHeight = 28f;
				hitHeight = 28f;
				return;
			}
			panelSize = new Vector2(360f, 520f);
			margin = 12f;
			alignment = BootAlignment(DexcRuntimeUIAlignment.LeftCenter);
			rowHeight = 28f;
			hitHeight = 28f;
		}

		private static Rect Intersect(Rect first, Rect second)
		{
			var xMin = Mathf.Clamp(second.xMin, first.xMin, first.xMax);
			var xMax = Mathf.Clamp(second.xMax, first.xMin, first.xMax);
			var yMin = Mathf.Clamp(second.yMin, first.yMin, first.yMax);
			var yMax = Mathf.Clamp(second.yMax, first.yMin, first.yMax);
			return new Rect(xMin, yMin, Mathf.Max(0f, xMax - xMin), Mathf.Max(0f, yMax - yMin));
		}

		private static bool EditorTargetsAndroid
		{
			get
			{
#if UNITY_EDITOR && UNITY_ANDROID
				return true;
#else
				return false;
#endif
			}
		}
	}

	internal static class DexcRuntimeUIRendererSelection
	{
		/// <summary>State of this class. Owned by <see cref="DexcUIRoot"/>; nothing here survives a root reset.</summary>
		internal sealed class State
		{
#if DEXC_RUNTIME_UITOOLKIT
			internal bool UseUIToolkit = true;
#else
			internal bool UseUIToolkit;
#endif
#if DEXC_RUNTIME_UGUI && DEXC_HAS_UGUI && !DEXC_RUNTIME_UITOOLKIT
			internal bool UseUGUI = true;
#else
			internal bool UseUGUI;
#endif
			internal DebugMenuRenderer? Pending;
		}

		internal static State Data => DexcUIRoot.Current.RendererSelection;

		internal static bool UseUIToolkit
		{
			get => Data.UseUIToolkit;
			private set => Data.UseUIToolkit = value;
		}

		internal static bool UseUGUI
		{
			get => Data.UseUGUI;
			private set => Data.UseUGUI = value;
		}

		private static DebugMenuRenderer? Pending
		{
			get => Data.Pending;
			set => Data.Pending = value;
		}

		/// <summary>The renderer selected. UI Toolkit stays selected while it falls back, so a later Skin that works can still build it.</summary>
		internal static DebugMenuRenderer Current =>
			UseUIToolkit ? DebugMenuRenderer.UIToolkit
			: UseUGUI ? DebugMenuRenderer.UGUI
			: DebugMenuRenderer.IMGUI;

		/// <summary>The renderer actually drawing.</summary>
		internal static DebugMenuRenderer Drawing =>
			UseUIToolkit && DexcRuntimeUIToolkitRenderer.HasFailed ? DebugMenuRenderer.IMGUI : Current;

#if DEXC_DEBUGGER

		/// <summary>Queues a renderer switch; it is applied by the UI driver on the next Update so renderer callbacks never destroy their own renderer.</summary>
		internal static bool Request(DebugMenuRenderer renderer)
		{
			if (renderer == DebugMenuRenderer.UGUI)
			{
#if !DEXC_HAS_UGUI
				return false;
#endif
			}
			Pending = renderer;
			return true;
		}

		internal static bool Apply(DebugMenuRenderer renderer)
		{
			if (renderer == DebugMenuRenderer.UGUI)
			{
#if !DEXC_HAS_UGUI
				return false;
#endif
			}
			var provider = DexcRuntimeUIState.InputProvider;
			if (renderer == Current && !DexcRuntimeUIState.IsOpen)
			{
				// Same renderer: rebuild for the current session (skin) but keep the attached provider.
				DexcRuntimeBootstrap.Reinitialize();
				ReattachProvider(provider);
				return true;
			}
			var wasOpen = DexcRuntimeUIState.IsOpen;
			var bootMode = DebugBootMenu.UsesRuntimeMenu;
			var page = DexcRuntimeUIState.Page;
			var panel = DexcRuntimePanelForm.Current;
			if (wasOpen)
				DexcRuntimeUIState.CloseForRendererSwitch();
			UseUIToolkit = renderer == DebugMenuRenderer.UIToolkit;
			UseUGUI = renderer == DebugMenuRenderer.UGUI;
			DexcRuntimeBootstrap.Reinitialize();
			var active = renderer == DebugMenuRenderer.IMGUI;
			if (renderer == DebugMenuRenderer.UIToolkit)
				active = DexcRuntimeUIToolkitRenderer.IsActive;
#if DEXC_HAS_UGUI
			if (renderer == DebugMenuRenderer.UGUI)
				active = DexcRuntimeUGUIRenderer.IsActive;
#endif
			if (!active)
			{
				UseUIToolkit = false;
				UseUGUI = false;
			}
			ReattachProvider(provider);
			if (wasOpen)
			{
				DexcRuntimeUIState.Open();
				if (bootMode || page == DexcRuntimeUIPage.PanelList || page == DexcRuntimeUIPage.PanelDetail)
				{
					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.PanelList);
					if (page == DexcRuntimeUIPage.PanelDetail && panel != null)
						DexcRuntimePanelForm.Open(panel);
				}
			}
			return active;
		}
#endif

		/// <summary>Applies a renderer asked for before the menu existed. Called every frame, in every configuration.</summary>
		internal static void ApplyPending()
		{
#if DEXC_DEBUGGER
			if (!Pending.HasValue)
				return;
			var renderer = Pending.Value;
			Pending = null;
			Apply(renderer);
#endif
		}

		private static void ReattachProvider(DebugMenuInputProvider provider)
		{
			if (provider == null)
				return;
			DexcRuntimeUIState.SetInputProvider(provider);
		}

#if UNITY_INCLUDE_TESTS
		internal static void SetUseUGUIForTests(bool value)
		{
			UseUGUI = value;
			if (value)
				UseUIToolkit = false;
		}

		internal static void SetUseUIToolkitForTests(bool value)
		{
			UseUIToolkit = value;
			if (value)
				UseUGUI = false;
		}
#endif
	}

	internal static class DexcRuntimeUIState
	{
		internal const int PageSize = 24;

		internal static DebugPageRequest CreatePageRequest(int offset, int requested, bool hierarchy = false)
		{
			var session = DexcDebugger.CurrentSession;
			var limit = session == null ? requested : session.Options.MaxPageSize;
			if (hierarchy && session != null)
			{
				limit = Math.Min(limit, session.Options.MaxHierarchyChildren);
			}
			return new DebugPageRequest(offset, Math.Min(requested, limit));
		}

		/// <summary>State of this class. Owned by <see cref="DexcUIRoot"/>; nothing here survives a root reset.</summary>
		internal sealed class State
		{
			internal bool IsOpen;
			internal DexcRuntimeUIPage Page = DexcRuntimeUIPage.Home;
			internal int SelectedIndex;

			/// <summary>The header's choice between one page and side by side, over the skin's, while the menu runs.</summary>
			internal DexcRuntimeUILayoutMode? LayoutOverride;

			/// <summary>Side by side, where the list beside an open page is scrolled to.</summary>
			internal Vector2 SplitListScroll;
			internal bool NavigationFocusVisible = true;
			internal bool SuppressBootStart;
			internal string Search = string.Empty;
			internal string Notice = string.Empty;
			internal Vector2 ScrollPosition;
			internal Vector2 ListScrollPosition;
			internal bool ListSelectionVisibilityRequested;
			internal int SettingBindingVersion;
			internal DebugSessionId SessionId;
			internal bool HasSession;
			internal DebugCommandId CommandId;
			internal DebugCommandDescriptor Command;
			internal DebugCommandDraftId DraftId;
			internal bool HasDraft;
			internal CommandExecutionResult LastCommandResult;
			internal DebugCommandExecution AsyncExecution;
			internal Task<CommandExecutionResult> AsyncTask;
			internal DebugSettingId SettingId;
			internal DebugSettingListItem Setting;
			internal DebugInspectorTargetHandle InspectorTarget;
			internal DebugInspectorComponentHandle InspectorComponent;
			internal DebugSceneHandle InspectorScene;
			internal DebugObjectHandle InspectorParent;
			internal DexcRuntimeUIPage InspectorReturnPage = DexcRuntimeUIPage.InspectorRegistered;
			internal DebugInspectorSearchId InspectorSearch;
			internal readonly Dictionary<DebugCommandFieldId, string> CommandInputText = new Dictionary<DebugCommandFieldId, string>();
			internal readonly HashSet<DebugCommandFieldId> CommandInvalidInput = new HashSet<DebugCommandFieldId>();
			internal readonly Dictionary<string, string> StructuredInputText = new Dictionary<string, string>(StringComparer.Ordinal);
			internal DebugMenuInputProvider InputProvider;
			internal DebugMenuInputState InputState = DebugMenuInputState.Closed;
			internal DebugMenuInputFrame InputFrame;
			internal bool HasInputFrame;
			internal int InputFrameNumber = -1;
			internal int InputActionFrame = -1;
			internal float RepeatAt;
			internal Vector2 RepeatDirection;
			internal DebugSessionId ProviderSessionId;
			internal bool HasProviderSession;
			internal readonly HashSet<string> InputDiagnosticKeys = new HashSet<string>(StringComparer.Ordinal);
			internal readonly Dictionary<DebugCommandFieldId, RuntimeInputEditorSelection> InputEditorSelections =
				new Dictionary<DebugCommandFieldId, RuntimeInputEditorSelection>();
			internal IReadOnlyList<Type> InputEditorProviderTypes;
		}

		internal static State Data => DexcUIRoot.Current.UIState;
		internal static bool IsOpen
		{
			get => Data.IsOpen;
			set => Data.IsOpen = value;
		}
		internal static DexcRuntimeUIPage Page
		{
			get => Data.Page;
			set => Data.Page = value;
		}
		internal static int SelectedIndex
		{
			get => Data.SelectedIndex;
			set => Data.SelectedIndex = value;
		}

		/// <summary>False after a mouse click so the keyboard/gamepad focus marker stays hidden until navigation input is used again.</summary>
		internal static bool NavigationFocusVisible
		{
			get => Data.NavigationFocusVisible;
			set => Data.NavigationFocusVisible = value;
		}
		private static bool SuppressBootStart
		{
			get => Data.SuppressBootStart;
			set => Data.SuppressBootStart = value;
		}
		internal static string Search
		{
			get => Data.Search;
			set => Data.Search = value;
		}
		internal static string Notice
		{
			get => Data.Notice;
			set => Data.Notice = value;
		}
		internal static Vector2 ScrollPosition
		{
			get => Data.ScrollPosition;
			set => Data.ScrollPosition = value;
		}
		internal static Vector2 ListScrollPosition
		{
			get => Data.ListScrollPosition;
			set => Data.ListScrollPosition = value;
		}
		internal static bool ListSelectionVisibilityRequested
		{
			get => Data.ListSelectionVisibilityRequested;
			set => Data.ListSelectionVisibilityRequested = value;
		}
		internal static int SettingBindingVersion
		{
			get => Data.SettingBindingVersion;
			set => Data.SettingBindingVersion = value;
		}
		internal static DebugSessionId SessionId
		{
			get => Data.SessionId;
			set => Data.SessionId = value;
		}
		internal static bool HasSession
		{
			get => Data.HasSession;
			set => Data.HasSession = value;
		}
		internal static DebugCommandId CommandId
		{
			get => Data.CommandId;
			set => Data.CommandId = value;
		}
		internal static DebugCommandDescriptor Command
		{
			get => Data.Command;
			set => Data.Command = value;
		}
		internal static DebugCommandDraftId DraftId
		{
			get => Data.DraftId;
			set => Data.DraftId = value;
		}
		internal static DexcRuntimeUILayoutMode? LayoutOverride
		{
			get => Data.LayoutOverride;
			set => Data.LayoutOverride = value;
		}

		internal static bool HasDraft
		{
			get => Data.HasDraft;
			set => Data.HasDraft = value;
		}
		internal static CommandExecutionResult LastCommandResult
		{
			get => Data.LastCommandResult;
			set => Data.LastCommandResult = value;
		}
		internal static DebugCommandExecution AsyncExecution
		{
			get => Data.AsyncExecution;
			set => Data.AsyncExecution = value;
		}
		internal static Task<CommandExecutionResult> AsyncTask
		{
			get => Data.AsyncTask;
			set => Data.AsyncTask = value;
		}
		internal static DebugSettingId SettingId
		{
			get => Data.SettingId;
			set => Data.SettingId = value;
		}
		internal static DebugSettingListItem Setting
		{
			get => Data.Setting;
			set => Data.Setting = value;
		}
		internal static DebugInspectorTargetHandle InspectorTarget
		{
			get => Data.InspectorTarget;
			set => Data.InspectorTarget = value;
		}
		internal static DebugInspectorComponentHandle InspectorComponent
		{
			get => Data.InspectorComponent;
			set => Data.InspectorComponent = value;
		}
		internal static DebugSceneHandle InspectorScene
		{
			get => Data.InspectorScene;
			set => Data.InspectorScene = value;
		}
		internal static DebugObjectHandle InspectorParent
		{
			get => Data.InspectorParent;
			set => Data.InspectorParent = value;
		}
		internal static DexcRuntimeUIPage InspectorReturnPage
		{
			get => Data.InspectorReturnPage;
			set => Data.InspectorReturnPage = value;
		}
		internal static DebugInspectorSearchId InspectorSearch
		{
			get => Data.InspectorSearch;
			set => Data.InspectorSearch = value;
		}
		internal static Dictionary<DebugCommandFieldId, string> CommandInputText => Data.CommandInputText;
		internal static HashSet<DebugCommandFieldId> CommandInvalidInput => Data.CommandInvalidInput;
		internal static Dictionary<string, string> StructuredInputText => Data.StructuredInputText;
		internal static DebugMenuInputProvider InputProvider
		{
			get => Data.InputProvider;
			set => Data.InputProvider = value;
		}
		internal static DebugMenuInputState InputState
		{
			get => Data.InputState;
			set => Data.InputState = value;
		}
		internal static DebugMenuInputFrame InputFrame
		{
			get => Data.InputFrame;
			set => Data.InputFrame = value;
		}
		internal static bool HasInputFrame
		{
			get => Data.HasInputFrame;
			set => Data.HasInputFrame = value;
		}
		internal static int InputFrameNumber
		{
			get => Data.InputFrameNumber;
			set => Data.InputFrameNumber = value;
		}
		internal static int InputActionFrame
		{
			get => Data.InputActionFrame;
			set => Data.InputActionFrame = value;
		}
		internal static float RepeatAt
		{
			get => Data.RepeatAt;
			set => Data.RepeatAt = value;
		}
		internal static Vector2 RepeatDirection
		{
			get => Data.RepeatDirection;
			set => Data.RepeatDirection = value;
		}
		internal static DebugSessionId ProviderSessionId
		{
			get => Data.ProviderSessionId;
			set => Data.ProviderSessionId = value;
		}
		internal static bool HasProviderSession
		{
			get => Data.HasProviderSession;
			set => Data.HasProviderSession = value;
		}
		internal static HashSet<string> InputDiagnosticKeys => Data.InputDiagnosticKeys;
		internal static Dictionary<DebugCommandFieldId, RuntimeInputEditorSelection> InputEditorSelections => Data.InputEditorSelections;
		internal static IReadOnlyList<Type> InputEditorProviderTypes
		{
			get => Data.InputEditorProviderTypes;
			set => Data.InputEditorProviderTypes = value;
		}

		internal static bool Confirming => DexcRuntimeCommandExecution.IsConfirming;

		internal static void Open()
		{
#if DEXC_DEBUGGER
			DexcRuntimeBootstrap.EnsureMenu();
#endif
			DexcRuntimeSettingForm.Reset();
			DexcRuntimeInspectorForm.Reset();
			DexcRuntimeInspectorNavigation.Reset();
			SettingBindingVersion++;
			IsOpen = true;
			NavigationFocusVisible = true;
			Page = DexcRuntimeUIPage.Home;
			SelectedIndex = 0;
			DexcRuntimeCommandExecution.CancelConfirmation();
			Notice = string.Empty;
			NotifyInputState(DebugMenuInputState.Navigation);
		}

		/// <summary>Closes the menu for a renderer switch without treating it as boot-menu "start".</summary>
		internal static void CloseForRendererSwitch()
		{
			SuppressBootStart = true;
			try
			{
				Close();
			}
			finally
			{
				SuppressBootStart = false;
			}
		}

		internal static void Close()
		{
			if (DebugBootMenu.UsesRuntimeMenu && !SuppressBootStart)
			{
				// Closing the boot menu means "start the game" (the × button, Toggle). Back at its root does nothing.
				DebugBootMenu.Start();
				return;
			}
			DexcRuntimeSettingForm.Reset();
			DexcRuntimeInspectorForm.Reset();
			DexcRuntimeInspectorNavigation.Reset();
			SettingBindingVersion++;
			DexcRuntimeFormDetail.Reset();
			IsOpen = false;
			DexcRuntimeSplitHome.Leave();
			DexcRuntimeCommandExecution.CancelConfirmation();
			Search = string.Empty;
			StructuredInputText.Clear();
			DiscardDraft();
			NotifyInputState(DebugMenuInputState.Closed);
		}

		internal static void Toggle()
		{
			if (IsOpen)
				Close();
			else
				Open();
		}

		internal static void Back()
		{
			DebugBootMenu.NoteInteraction();
			if (DexcRuntimeSplitHome.HandleInput(0, 0, false, true))
				return;
			if (Page == DexcRuntimeUIPage.FormDetail)
			{
				DexcRuntimeFormDetail.Close();
				return;
			}
			if (Confirming)
			{
				DexcRuntimeCommandExecution.CancelConfirmation();
				return;
			}
			if (DexcRuntimeInspectorNavigation.BackScene())
				return;
			if (DebugBootMenu.UsesRuntimeMenu && Page == DexcRuntimeUIPage.PanelList)
				return;
			if (Page == DexcRuntimeUIPage.CommandList && DexcRuntimeCommandForm.LeaveFolder())
				return;
			if (Page == DexcRuntimeUIPage.SettingList && DexcRuntimeSettingForm.LeaveFolder())
				return;
			if (Page == DexcRuntimeUIPage.Home)
			{
				Close();
				return;
			}
			var target =
				Page == DexcRuntimeUIPage.CommandDetail ? DexcRuntimeUIPage.CommandList
				: Page == DexcRuntimeUIPage.SettingDetail ? DexcRuntimeUIPage.SettingList
				: Page == DexcRuntimeUIPage.InspectorComponents ? InspectorReturnPage
				: Page == DexcRuntimeUIPage.InspectorFields ? DexcRuntimeUIPage.InspectorComponents
				: Page == DexcRuntimeUIPage.InspectorRegistered
				|| Page == DexcRuntimeUIPage.InspectorScene
				|| Page == DexcRuntimeUIPage.InspectorWatches
					? DexcRuntimeUIPage.InspectorHome
				: Page == DexcRuntimeUIPage.PanelDetail ? DexcRuntimeUIPage.PanelList
				: DexcRuntimeUIPage.Home;
			// Side by side the sections stand in for Home: going back to it moves into their column instead.
			if (target == DexcRuntimeUIPage.Home && DexcRuntimeSplitHome.Enter())
				return;
			Navigate(target);
			DexcRuntimeCommandExecution.CancelConfirmation();
		}

		internal static void Navigate(DexcRuntimeUIPage page)
		{
			// The two lists share the search text. Entering one from anywhere but its own pages - from Home, or from the other
			// list beside it - starts it unfiltered; coming back from a page it opened keeps its search.
			if (
				page == DexcRuntimeUIPage.CommandList && Page != DexcRuntimeUIPage.CommandList && Page != DexcRuntimeUIPage.CommandDetail
				|| page == DexcRuntimeUIPage.SettingList && Page != DexcRuntimeUIPage.SettingList && Page != DexcRuntimeUIPage.SettingDetail
			)
				Search = string.Empty;
			// Coming from Home a list starts at its top; coming back from a page it opened, it keeps its folder.
			if (Page == DexcRuntimeUIPage.Home && page == DexcRuntimeUIPage.CommandList)
				DexcRuntimeCommandForm.OpenTop();
			if (Page == DexcRuntimeUIPage.Home && page == DexcRuntimeUIPage.SettingList)
				DexcRuntimeSettingForm.OpenTop();
			if (page != DexcRuntimeUIPage.SettingDetail && page != DexcRuntimeUIPage.FormDetail)
				DexcRuntimeSettingForm.Reset();
			if (page != DexcRuntimeUIPage.PanelDetail)
				DexcRuntimePanelForm.Reset();
			if (page != DexcRuntimeUIPage.InspectorFields && page != DexcRuntimeUIPage.FormDetail)
				DexcRuntimeInspectorForm.Reset();
			DexcRuntimeInspectorNavigation.ChangedPage(page);
			DexcRuntimeCommandExecution.CancelConfirmation();
			DexcRuntimeSplitHome.Leave();
			DexcRuntimeSplitHome.Remember(page);
			SettingBindingVersion++;
			Page = page;
			SelectedIndex = 0;
			ScrollPosition = Vector2.zero;
			ListScrollPosition = Vector2.zero;
			ListSelectionVisibilityRequested = false;
		}

		internal static void SetSelectedIndex(int index)
		{
			SelectedIndex = Mathf.Max(0, index);
		}

		internal static int ClampSelectedIndex(int count)
		{
			SelectedIndex = count <= 0 ? 0 : Mathf.Clamp(SelectedIndex, 0, count - 1);
			return SelectedIndex;
		}

		internal static void SetSession(DebugSessionId id)
		{
			DexcDebugger.SessionInvalidated -= OnSessionInvalidated;
			if (id.IsValid)
			{
				DexcDebugger.SessionInvalidated += OnSessionInvalidated;
			}
			if (HasSession && SessionId == id)
				return;
			var replacingSession = HasSession && SessionId != id;
			HasSession = id.IsValid;
			SessionId = id;
			CommandId = default(DebugCommandId);
			Command = null;
			SettingId = default(DebugSettingId);
			Setting = null;
			InspectorTarget = default(DebugInspectorTargetHandle);
			InspectorComponent = default(DebugInspectorComponentHandle);
			InspectorScene = default(DebugSceneHandle);
			InspectorParent = default(DebugObjectHandle);
			InspectorReturnPage = DexcRuntimeUIPage.InspectorRegistered;
			InspectorSearch = default(DebugInspectorSearchId);
			Search = string.Empty;
			CommandInputText.Clear();
			CommandInvalidInput.Clear();
			StructuredInputText.Clear();
			InputEditorSelections.Clear();
			InputDiagnosticKeys.Clear();
			ScrollPosition = Vector2.zero;
			ListScrollPosition = Vector2.zero;
			ListSelectionVisibilityRequested = false;
			DiscardDraft();
			if (replacingSession)
				Navigate(DexcRuntimeUIPage.Home);
		}

		internal static void SetInputProvider(DebugMenuInputProvider provider)
		{
			if (provider == null)
				throw new ArgumentNullException(nameof(provider));
			SetInputProviderCore(provider, IsOpen ? StateForCurrentFocus() : DebugMenuInputState.Closed);
		}

#if UNITY_INCLUDE_TESTS
		internal static void SetInputProviderForTests(DebugMenuInputProvider provider, string focusedControlName)
		{
			if (provider == null)
				throw new ArgumentNullException(nameof(provider));
			SetInputProviderCore(provider, StateForFocusName(focusedControlName));
		}
#endif

		private static void SetInputProviderCore(DebugMenuInputProvider provider, DebugMenuInputState state)
		{
			// The provider belongs to a session; without one there is nothing to hand input to, so the call does nothing.
			if (!DexcRuntimeUIBridge.TryGetSession(out var sessionId))
				return;
			if (InputProvider != null && !ReferenceEquals(InputProvider, provider))
			{
				DexcRuntimeUIBridge.AddDiagnostic(
					"input-provider-conflict",
					InputProvider.GetType().FullName + "/" + provider.GetType().FullName,
					"A different runtime input provider is already active."
				);
				return;
			}

			InputProvider = provider;
			DexcDebugger.SessionInvalidated -= OnSessionInvalidated;
			DexcDebugger.SessionInvalidated += OnSessionInvalidated;
			ProviderSessionId = sessionId;
			HasProviderSession = sessionId.IsValid;
			NotifyInputState(state);
		}

		internal static void ClearInputProvider(DebugMenuInputProvider provider)
		{
			if (provider == null)
				throw new ArgumentNullException(nameof(provider));
			if (DexcDebugger.CurrentSession == null || !ReferenceEquals(InputProvider, provider))
				return;
			NotifyInputState(DebugMenuInputState.Closed, true);
			InputProvider = null;
			// The UI session still owns drafts after the input provider is removed.
			HasInputFrame = false;
			HasProviderSession = false;
		}

		internal static void PollInput()
		{
			if (!DexcDebugger.IsEnabled)
				return;
			if (!DexcRuntimeUIBridge.TryGetSession(out var sessionId))
			{
				if (InputProvider != null)
				{
					NotifyInputState(DebugMenuInputState.Closed, true);
					InputProvider = null;
					HasProviderSession = false;
				}
				return;
			}

			if (HasProviderSession && ProviderSessionId != sessionId)
			{
				NotifyInputState(DebugMenuInputState.Closed, true);
				InputProvider = null;
				HasProviderSession = false;
				InputEditorSelections.Clear();
			}
			if (InputFrameNumber == Time.frameCount)
				return;
			InputFrameNumber = Time.frameCount;
			HasInputFrame = false;
			if (InputProvider == null)
				return;
			ProviderSessionId = sessionId;
			HasProviderSession = true;
			try
			{
				InputFrame = InputProvider.ReadInput();
				HasInputFrame = true;
			}
			catch (Exception exception)
			{
				var key = InputProvider.GetType().FullName + ":" + exception.GetType().FullName + ":" + exception.Message;
				if (InputDiagnosticKeys.Add(key))
					DexcRuntimeUIBridge.AddDiagnostic(
						"input-provider-read-failed",
						InputProvider.GetType().FullName,
						"The runtime input provider failed to read input.",
						exception
					);
			}
		}

		internal static void NotifyInputState(DebugMenuInputState state, bool retryClosed = false)
		{
			if (InputProvider == null || InputState == state && !retryClosed)
				return;
			InputState = state;
			try
			{
				InputProvider.OnInputStateChanged(state);
			}
			catch (Exception exception)
			{
				DexcRuntimeUIBridge.AddDiagnostic(
					"input-provider-state-failed",
					InputProvider.GetType().FullName + ":" + state,
					"The runtime input provider state callback failed.",
					exception
				);
				if (retryClosed && state == DebugMenuInputState.Closed)
				{
					try
					{
						InputProvider.OnInputStateChanged(DebugMenuInputState.Closed);
					}
					catch (Exception retryException)
					{
						DexcRuntimeUIBridge.AddDiagnostic(
							"input-provider-state-failed",
							InputProvider.GetType().FullName + ":Closed",
							"The runtime input provider Closed callback failed again.",
							retryException
						);
					}
				}
			}
		}

		internal static void UpdateInputStateFromFocus()
		{
			NotifyInputState(StateForCurrentFocus());
		}

		internal static DebugMenuInputState StateForCurrentFocus()
		{
			return StateForFocusName(GUI.GetNameOfFocusedControl());
		}

		internal static DebugMenuInputState StateForFocusName(string focusedControlName)
		{
			if (!IsOpen)
				return DebugMenuInputState.Closed;
			return string.IsNullOrEmpty(focusedControlName) ? DebugMenuInputState.Navigation : DebugMenuInputState.TextInput;
		}

		/// <summary>
		/// Returns the menu state to its initial values. Production no longer needs this: a new root hands out a fresh
		/// State with the same defaults. Tests keep it to reset the menu without replacing the whole root.
		/// </summary>
		internal static void ResetForSession()
		{
			DexcRuntimeSettingForm.ClearSession();
			DexcRuntimeInspectorForm.Reset();
			DexcRuntimeInspectorNavigation.Reset();
			DexcRuntimeHistory.Reset();
			DexcRuntimeFormDetail.Reset();
			DexcRuntimeCommandExecution.Reset();
			if (InputProvider != null)
				NotifyInputState(DebugMenuInputState.Closed, true);
			InputProvider = null;
			DexcDebugger.SessionInvalidated -= OnSessionInvalidated;
			HasProviderSession = false;
			HasInputFrame = false;
			InputEditorSelections.Clear();
			StructuredInputText.Clear();
			InputDiagnosticKeys.Clear();
			RepeatDirection = Vector2.zero;
			RepeatAt = 0f;
			InputActionFrame = -1;
			InputFrameNumber = -1;
			InputState = DebugMenuInputState.Closed;
			IsOpen = false;
			Notice = string.Empty;
			Search = string.Empty;
			SettingId = default(DebugSettingId);
			Setting = null;
			InspectorTarget = default(DebugInspectorTargetHandle);
			InspectorComponent = default(DebugInspectorComponentHandle);
			InspectorScene = default(DebugSceneHandle);
			InspectorParent = default(DebugObjectHandle);
			InspectorSearch = default(DebugInspectorSearchId);
			CommandInputText.Clear();
			CommandInvalidInput.Clear();
			ScrollPosition = Vector2.zero;
			ListScrollPosition = Vector2.zero;
			Navigate(DexcRuntimeUIPage.Home);
			InspectorReturnPage = DexcRuntimeUIPage.InspectorRegistered;
			ResetCommandState();
		}

		private static void OnSessionInvalidated()
		{
			DexcRuntimeSettingForm.ClearSession();
			DexcRuntimeInspectorForm.Reset();
			DexcRuntimeInspectorNavigation.Reset();
			DexcRuntimeHistory.Reset();
			if (InputProvider != null)
				NotifyInputState(DebugMenuInputState.Closed, true);
			InputProvider = null;
			DexcDebugger.SessionInvalidated -= OnSessionInvalidated;
			HasProviderSession = false;
			HasInputFrame = false;
			InputEditorSelections.Clear();
			StructuredInputText.Clear();
			RepeatDirection = Vector2.zero;
			RepeatAt = 0f;
			InputActionFrame = -1;
			InputFrameNumber = -1;
			InputState = DebugMenuInputState.Closed;
			IsOpen = false;
			Navigate(DexcRuntimeUIPage.Home);
			InspectorReturnPage = DexcRuntimeUIPage.InspectorRegistered;
			ResetCommandState();
		}

		internal static bool ConsumeInputAction()
		{
			if (InputActionFrame == Time.frameCount)
				return false;
			InputActionFrame = Time.frameCount;
			return true;
		}

		internal sealed class RuntimeInputEditorSelection
		{
			internal RuntimeInputEditorSelection(DebugInputEditorDescriptor descriptor, DebugInputEditor provider, string errorCode)
			{
				Descriptor = descriptor;
				Provider = provider;
				ErrorCode = errorCode ?? string.Empty;
			}

			internal DebugInputEditorDescriptor Descriptor { get; }
			internal DebugInputEditor Provider { get; }
			internal string ErrorCode { get; }
		}

		internal static void DiscardDraft()
		{
			foreach (
				var key in StructuredInputText.Keys.Where(key => key.StartsWith("command-component:", StringComparison.Ordinal)).ToArray()
			)
				StructuredInputText.Remove(key);
			CommandInvalidInput.Clear();
			DexcRuntimeCommandExecution.CancelConfirmation();
			if (HasDraft)
				DebugCommand.Draft.Discard(DraftId);
			HasDraft = false;
			DraftId = default(DebugCommandDraftId);
		}

		private static void ResetCommandState()
		{
			DexcRuntimeCommandExecution.ReleaseExecution();
			CommandId = default(DebugCommandId);
			Command = null;
			LastCommandResult = null;
			CommandInputText.Clear();
			CommandInvalidInput.Clear();
			DexcRuntimeCommandExecution.CancelConfirmation();
			DiscardDraft();
		}

		internal static void ReturnFromStaleInspector(string reason)
		{
			InspectorTarget = default(DebugInspectorTargetHandle);
			InspectorComponent = default(DebugInspectorComponentHandle);
			InspectorParent = default(DebugObjectHandle);
			Notice = string.IsNullOrEmpty(reason) ? "The selected inspector target is no longer available." : reason;
			Navigate(InspectorReturnPage);
		}
	}

	internal sealed class DexcRuntimeUIDriver : MonoBehaviour
	{
#if DEXC_DEBUGGER
		internal static void CreateForBootstrap()
		{
			var root = DexcUIRoot.Current;
			if (root.MenuDriver != null || !Application.isPlaying)
				return;
			var gameObject = new GameObject("DexcRuntimeUIDriver");
			// No hide flags: Play Mode owns the lifetime, so leaving it destroys this with everything else.
			DontDestroyOnLoad(gameObject);
			root.MenuDriver = gameObject.AddComponent<DexcRuntimeUIDriver>();
		}

		internal static void Destroy()
		{
			var root = DexcUIRoot.Current;
			var driver = root.MenuDriver;
			root.MenuDriver = null;
			DexcRoot.DestroyObject(driver == null ? null : driver.gameObject);
		}
#endif

		private void Update()
		{
			DexcRuntimeUIRendererSelection.ApplyPending();
			DexcRuntimeSettingForm.Tick();
			DexcRuntimeInspectorForm.Tick();
#if UNITY_ANDROID && !UNITY_EDITOR
			if (DexcRuntimeUIState.IsOpen && DexcRuntimeUIPlatformInput.BackPressedThisFrame() && DexcRuntimeUIState.ConsumeInputAction())
				DexcRuntimeUIState.Back();
#endif
			DexcRuntimeCommandExecution.Poll();
			DexcRuntimeUIState.PollInput();
			DexcRuntimeUIToolkitRenderer.TickActive();
#if DEXC_HAS_UGUI
			DexcRuntimeUGUIRenderer.TickActive();
#endif
		}

		private void OnGUI()
		{
			DexcRuntimeUIRenderer.DrawMenuAndDisplay();
		}

		private void OnDestroy()
		{
			DexcPlayerRenderer.Shutdown();
		}
	}

	internal static class DexcRuntimeUIPlatformInput
	{
		private static readonly Type KeyboardType = Type.GetType("UnityEngine.InputSystem.Keyboard, Unity.InputSystem", false);
		private static readonly PropertyInfo CurrentKeyboard =
			KeyboardType == null ? null : KeyboardType.GetProperty("current", BindingFlags.Public | BindingFlags.Static);
		private static readonly PropertyInfo EscapeKey =
			KeyboardType == null ? null : KeyboardType.GetProperty("escapeKey", BindingFlags.Public | BindingFlags.Instance);
		private static readonly PropertyInfo WasPressedThisFrame =
			EscapeKey == null
				? null
				: EscapeKey.PropertyType.GetProperty("wasPressedThisFrame", BindingFlags.Public | BindingFlags.Instance);

		internal static bool BackPressedThisFrame()
		{
			if (CurrentKeyboard == null || EscapeKey == null || WasPressedThisFrame == null)
				return false;
			try
			{
				var keyboard = CurrentKeyboard.GetValue(null, null);
				var escape = keyboard == null ? null : EscapeKey.GetValue(keyboard, null);
				return escape != null && (bool)WasPressedThisFrame.GetValue(escape, null);
			}
			catch (Exception)
			{
				return false;
			}
		}
	}

#if DEXC_DEBUGGER
	internal static class DexcRuntimeBootstrap
	{
		/// <summary>Rebuilds the UI drivers and renderers for the current session and renderer selection.</summary>
		internal static void Reinitialize()
		{
			InitializeCore();
		}

#if UNITY_INCLUDE_TESTS
		internal static void InitializeForTests()
		{
			InitializeCore();
		}
#endif

		/// <summary>
		/// Builds the menu objects. Called when the menu is first opened and when the renderer changes, never at startup:
		/// a project that does not open the menu gets nothing. Display and Inspector create their own drivers.
		/// </summary>
		internal static void EnsureMenu()
		{
			InitializeCore();
		}

		private static void InitializeCore()
		{
			if (!Application.isPlaying)
				return;
			DexcRuntimeUIDriver.CreateForBootstrap();
			// Only the selected renderer is built. Building both would put a Canvas hierarchy in every project that
			// never selects uGUI, which is what the old startup bootstrap did.
			if (DexcRuntimeUIRendererSelection.UseUIToolkit)
				DexcRuntimeUIToolkitRenderer.CreateForBootstrap();
			else
				DexcRuntimeUIToolkitRenderer.DestroyInstance();
#if DEXC_HAS_UGUI
			if (DexcRuntimeUIRendererSelection.UseUGUI)
				DexcRuntimeUGUIRenderer.CreateForBootstrap();
			else
				DexcRuntimeUGUIRenderer.DestroyInstance();
#endif
		}
	}
#endif

	internal static class DexcRuntimeUIRenderer
	{
		/// <summary>State of this class. Owned by <see cref="DexcUIRoot"/>; nothing here survives a root reset.</summary>
		internal sealed class State
		{
			internal DexcRuntimeUISkinSnapshot StyleSkin;
			internal DexcRuntimeIMGUIStyles Styles;
			internal float DrawScale = 1f;
			internal Rect ContentAreaPhysicalRect;
			internal float FormContentWidth;
			internal bool UseProfileOverride;
			internal DexcRuntimeUIProfileKind ProfileOverride;
			internal string PendingPanelTextFocus;
#if UNITY_INCLUDE_TESTS
			internal int WorldVisualDrawCountForTests;
			internal int MenuDrawCountForTests;
			internal Rect FooterFocusMarkerRectForTests;
			internal bool FooterFocusMarkerVisibleForTests;
			internal Font FontForTests;
			internal Rect DescriptionScreenRectForTests;
			internal Rect CloseScreenRectForTests;
#endif
			internal Rect ListViewportRect;
#if UNITY_INCLUDE_TESTS
			internal Rect ListViewportScreenRectForTests;
			internal Rect[] ListRowRectsForTests = Array.Empty<Rect>();
			internal Rect[] ListMainRectsForTests = Array.Empty<Rect>();
			internal Rect[] ListDetailRectsForTests = Array.Empty<Rect>();
			internal Rect[] ListRowScreenRectsForTests = Array.Empty<Rect>();
			internal Rect[] ListMainScreenRectsForTests = Array.Empty<Rect>();
			internal Rect[] ListDetailScreenRectsForTests = Array.Empty<Rect>();
			internal bool[] ListRowsFullyVisibleForTests = Array.Empty<bool>();
#endif
			internal float GuiPixelsPerPoint;
#if UNITY_INCLUDE_TESTS
			internal string CommandInputTextForTests;
#endif
			internal DexcUIRow FocusedRow;
			internal string SettingFocusName;
#if UNITY_INCLUDE_TESTS
			internal Action<string, Rect> ValueControlDrawnForTests;
			internal Action<string, Rect> ValueControlScreenRectForTests;
#endif
		}

		internal static State Data => DexcUIRoot.Current.Renderer;
		private static DexcRuntimeUISkinSnapshot StyleSkin
		{
			get => Data.StyleSkin;
			set => Data.StyleSkin = value;
		}
		private static DexcRuntimeIMGUIStyles Styles
		{
			get => Data.Styles;
			set => Data.Styles = value;
		}
		private static float DrawScale
		{
			get => Data.DrawScale;
			set => Data.DrawScale = value;
		}
		private static Rect ContentAreaPhysicalRect
		{
			get => Data.ContentAreaPhysicalRect;
			set => Data.ContentAreaPhysicalRect = value;
		}
		private static float FormContentWidth
		{
			get => Data.FormContentWidth;
			set => Data.FormContentWidth = value;
		}
		private static bool UseProfileOverride
		{
			get => Data.UseProfileOverride;
			set => Data.UseProfileOverride = value;
		}
		private static DexcRuntimeUIProfileKind ProfileOverride
		{
			get => Data.ProfileOverride;
			set => Data.ProfileOverride = value;
		}

		/// <summary>Panel text field to focus on the next repaint (Submit on a text or number widget).</summary>
		internal static string PendingPanelTextFocus
		{
			get => Data.PendingPanelTextFocus;
			set => Data.PendingPanelTextFocus = value;
		}
#if UNITY_INCLUDE_TESTS
		internal static int WorldVisualDrawCountForTests
		{
			get => Data.WorldVisualDrawCountForTests;
			set => Data.WorldVisualDrawCountForTests = value;
		}
		internal static int MenuDrawCountForTests
		{
			get => Data.MenuDrawCountForTests;
			set => Data.MenuDrawCountForTests = value;
		}
		internal static Rect FooterFocusMarkerRectForTests
		{
			get => Data.FooterFocusMarkerRectForTests;
			set => Data.FooterFocusMarkerRectForTests = value;
		}
		internal static bool FooterFocusMarkerVisibleForTests
		{
			get => Data.FooterFocusMarkerVisibleForTests;
			set => Data.FooterFocusMarkerVisibleForTests = value;
		}
		internal static Font FontForTests
		{
			get => Data.FontForTests;
			set => Data.FontForTests = value;
		}
		internal static Rect DescriptionScreenRectForTests
		{
			get => Data.DescriptionScreenRectForTests;
			set => Data.DescriptionScreenRectForTests = value;
		}
		internal static Rect CloseScreenRectForTests
		{
			get => Data.CloseScreenRectForTests;
			set => Data.CloseScreenRectForTests = value;
		}
#endif
		internal static Rect ListViewportRect
		{
			get => Data.ListViewportRect;
			set => Data.ListViewportRect = value;
		}
#if UNITY_INCLUDE_TESTS
		internal static Rect ListViewportScreenRectForTests
		{
			get => Data.ListViewportScreenRectForTests;
			set => Data.ListViewportScreenRectForTests = value;
		}
		internal static Rect[] ListRowRectsForTests
		{
			get => Data.ListRowRectsForTests;
			set => Data.ListRowRectsForTests = value;
		}
		internal static Rect[] ListMainRectsForTests
		{
			get => Data.ListMainRectsForTests;
			set => Data.ListMainRectsForTests = value;
		}
		internal static Rect[] ListDetailRectsForTests
		{
			get => Data.ListDetailRectsForTests;
			set => Data.ListDetailRectsForTests = value;
		}
		internal static Rect[] ListRowScreenRectsForTests
		{
			get => Data.ListRowScreenRectsForTests;
			set => Data.ListRowScreenRectsForTests = value;
		}
		internal static Rect[] ListMainScreenRectsForTests
		{
			get => Data.ListMainScreenRectsForTests;
			set => Data.ListMainScreenRectsForTests = value;
		}
		internal static Rect[] ListDetailScreenRectsForTests
		{
			get => Data.ListDetailScreenRectsForTests;
			set => Data.ListDetailScreenRectsForTests = value;
		}
		internal static bool[] ListRowsFullyVisibleForTests
		{
			get => Data.ListRowsFullyVisibleForTests;
			set => Data.ListRowsFullyVisibleForTests = value;
		}
#endif
		internal static float GuiPixelsPerPoint
		{
			get => Data.GuiPixelsPerPoint;
			set => Data.GuiPixelsPerPoint = value;
		}
#if UNITY_INCLUDE_TESTS
		internal static string CommandInputTextForTests
		{
			get => Data.CommandInputTextForTests;
			set => Data.CommandInputTextForTests = value;
		}
#endif
		static DexcUIRow FocusedRow
		{
			get => Data.FocusedRow;
			set => Data.FocusedRow = value;
		}
		static string SettingFocusName
		{
			get => Data.SettingFocusName;
			set => Data.SettingFocusName = value;
		}
#if UNITY_INCLUDE_TESTS
		internal static Action<string, Rect> ValueControlDrawnForTests
		{
			get => Data.ValueControlDrawnForTests;
			set => Data.ValueControlDrawnForTests = value;
		}
		internal static Action<string, Rect> ValueControlScreenRectForTests
		{
			get => Data.ValueControlScreenRectForTests;
			set => Data.ValueControlScreenRectForTests = value;
		}
#endif

		private static float HeaderHeight => CurrentRowHeight() + 12f;
		private static float FooterHeight => CurrentRowHeight() + 12f;
		private static float ListRowHeight => CurrentRowHeight();

		private static float CurrentRowHeight()
		{
			var profile = UseProfileOverride
				? ProfileOverride
				: DexcRuntimeUIReferenceResolver.ResolveCurrentProfile(Screen.width, Screen.height);
			var reference = DexcRuntimeUIReferenceResolver.Calculate(Screen.width, Screen.height, Screen.safeArea, profile);
			return DexcRuntimeFormLayout.RowHeight(reference.RowHeight, reference.HitHeight);
		}

		private const float ScrollContentGap = 4f;
		private const string CommandInputControlPrefix = "DexcCommandInput:";
#if UNITY_INCLUDE_TESTS
		internal static DexcRuntimeUISemanticPalette SemanticPaletteForTests => EnsureStyleCache().Palette;
#endif
		internal static bool UsesIMGUI => !DexcRuntimeUIToolkitRenderer.IsActive
#if DEXC_HAS_UGUI
			&& !DexcRuntimeUGUIRenderer.IsActive
#endif
		;

		internal static void DrawMenuAndDisplay()
		{
			if (!DexcDebugger.IsEnabled)
				return;
			// Display draws itself from the Runtime assembly; this path only draws the menu.
			GUI.depth = 0;
			if (DexcRuntimeUIToolkitRenderer.IsActive)
				return;
#if DEXC_HAS_UGUI
			if (DexcRuntimeUGUIRenderer.IsActive)
				return;
#endif
			ClearStaleCommandInputFocus();
			if (!DexcRuntimeUIState.IsOpen)
				return;
#if UNITY_INCLUDE_TESTS
			MenuDrawCountForTests++;
#endif
			var styles = EnsureStyleCache();
#if UNITY_INCLUDE_TESTS
			FontForTests = styles.SecondaryLabel.font ?? GUI.skin.font ?? GUI.skin.label.font;
#endif
			HandleNavigationInput();
			ClearStaleCommandInputFocus();
			if (!DexcRuntimeUIState.IsOpen)
				return;
			// Side by side Home gives way to the home panel or the last section; the page changes only between frames' events.
			if (Event.current.type == EventType.Layout)
			{
				DexcRuntimeSplitHome.Shown = CurrentReference().Split;
				DexcRuntimeSplitHome.Apply();
			}
			var physicalLayout = CalculateLayout(Screen.width, Screen.height, Screen.safeArea, DexcRuntimeUIState.Page);
			var layout = physicalLayout.LogicalCoordinates();
			ContentAreaPhysicalRect = physicalLayout.ContentViewport;
			var previousMatrix = GUI.matrix;
			var previousScale = DrawScale;
			DrawScale = physicalLayout.Scale;
			var screenOrigin = GUIUtility.GUIToScreenPoint(Vector2.zero);
			var screenUnit = GUIUtility.GUIToScreenPoint(Vector2.one);
			GuiPixelsPerPoint = Mathf.Max(1f, (Mathf.Abs(screenUnit.x - screenOrigin.x) + Mathf.Abs(screenUnit.y - screenOrigin.y)) * 0.5f);
			var matrixScale = DrawScale / GuiPixelsPerPoint;
			GUI.matrix = Matrix4x4.Scale(new Vector3(matrixScale, matrixScale, 1f));
			try
			{
#if UNITY_INCLUDE_TESTS
				FooterFocusMarkerRectForTests = default(Rect);
				FooterFocusMarkerVisibleForTests = false;
				DescriptionScreenRectForTests = default(Rect);
				CloseScreenRectForTests = default(Rect);
#endif
				ListViewportRect = default(Rect);
#if UNITY_INCLUDE_TESTS
				ListViewportScreenRectForTests = default(Rect);
				ListRowRectsForTests = Array.Empty<Rect>();
				ListMainRectsForTests = Array.Empty<Rect>();
				ListDetailRectsForTests = Array.Empty<Rect>();
				ListRowScreenRectsForTests = Array.Empty<Rect>();
				ListMainScreenRectsForTests = Array.Empty<Rect>();
				ListDetailScreenRectsForTests = Array.Empty<Rect>();
				ListRowsFullyVisibleForTests = Array.Empty<bool>();
				CommandInputTextForTests = string.Empty;
#endif
				GUI.Box(layout.Panel, GUIContent.none, styles.Panel);
				GUI.Box(layout.Header, GUIContent.none, styles.Header);
				if (layout.Footer.height > 0f)
					GUI.Box(layout.Footer, GUIContent.none, styles.Footer);
				GUILayout.BeginArea(layout.Header);
				DrawHeader(physicalLayout.Header);
				GUILayout.EndArea();
				// Side by side the sections take the panel's left, beside the header; a list takes the left of the content and the
				// page is drawn in what is left.
				var sections = new Rect(layout.Panel.x, layout.Panel.y, layout.Header.x - layout.Panel.x, layout.Panel.height);
				if (sections.width > 0f)
					DrawSections(sections, styles);
				var main = CurrentReference().Split ? DrawSplitColumns(layout.ContentViewport, styles) : layout.ContentViewport;
				GUILayout.BeginArea(main);
				GUILayout.BeginVertical(styles.Panel, GUILayout.Width(main.width), GUILayout.Height(main.height));
				var scrollViewWidth = Mathf.Max(1f, main.width - styles.Panel.padding.horizontal);
				var scrollViewHeight = Mathf.Max(1f, main.height - styles.Panel.padding.vertical);
				var scrollContentWidth = Mathf.Max(1f, scrollViewWidth - styles.ScrollbarWidth - ScrollContentGap);
				FormContentWidth = scrollContentWidth;
				if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.CommandList && Event.current.type == EventType.Repaint)
				{
					ListViewportRect = new Rect(0, 0, scrollContentWidth, scrollViewHeight);
				}
				var scrollPosition = styles.BeginScrollView(DexcRuntimeUIState.ScrollPosition, scrollViewWidth, scrollViewHeight);
				scrollPosition.x = 0f;
				DexcRuntimeUIState.ScrollPosition = scrollPosition;
				GUILayout.BeginVertical(GUILayout.Width(scrollContentWidth), GUILayout.ExpandWidth(false));
				if (!DexcDebugger.IsInitialized)
				{
					GUILayout.Label("Dexc Debugger is enabled but not initialized.", styles.WarningBox);
					GUILayout.Label("Call DexcDebugger.Initialize() before using feature screens.", styles.SecondaryLabel);
					GUILayout.EndVertical();
					DexcRuntimeUIState.ScrollPosition = styles.EndScrollView(DexcRuntimeUIState.ScrollPosition);
					GUILayout.EndVertical();
					GUILayout.EndArea();
					return;
				}

				switch (DexcRuntimeUIState.Page)
				{
					case DexcRuntimeUIPage.FormDetail:
						DrawFormDetail();
						break;
					case DexcRuntimeUIPage.Home:
						DrawHome();
						break;
					case DexcRuntimeUIPage.CommandList:
						DrawCommandList();
						break;
					case DexcRuntimeUIPage.CommandDetail:
						DrawCommandDetail();
						break;
					case DexcRuntimeUIPage.SettingList:
						DrawSettingList();
						break;
					case DexcRuntimeUIPage.SettingDetail:
						DrawSettingDetail();
						break;
					case DexcRuntimeUIPage.InspectorHome:
						DrawInspectorHome();
						break;
					case DexcRuntimeUIPage.InspectorRegistered:
						DrawInspectorRegistered();
						break;
					case DexcRuntimeUIPage.InspectorScene:
						DrawInspectorScene();
						break;
					case DexcRuntimeUIPage.InspectorComponents:
						DrawInspectorComponents();
						break;
					case DexcRuntimeUIPage.InspectorFields:
						DrawInspectorFields();
						break;
					case DexcRuntimeUIPage.InspectorWatches:
						DrawInspectorWatches();
						break;
					case DexcRuntimeUIPage.History:
						DrawHistory();
						break;
					case DexcRuntimeUIPage.Diagnostic:
						DrawDiagnostic();
						break;
					case DexcRuntimeUIPage.PanelList:
						DrawPanelList();
						break;
					case DexcRuntimeUIPage.PanelDetail:
						DrawPanelDetail();
						break;
				}
				if (!string.IsNullOrEmpty(DexcRuntimeUIState.Notice))
					GUILayout.Label(DexcRuntimeUIState.Notice, styles.WarningBox);
				GUILayout.EndVertical();
				DexcRuntimeUIState.ScrollPosition = styles.EndScrollView(DexcRuntimeUIState.ScrollPosition);
				GUILayout.EndVertical();
				GUILayout.EndArea();
				if (layout.Footer.height > 0f)
				{
					GUILayout.BeginArea(layout.Footer);
					DrawFooter();
					GUILayout.EndArea();
				}

				if (Event.current.type == EventType.Repaint && !string.IsNullOrEmpty(DexcRuntimeFormDetail.PendingFocus))
				{
					GUI.FocusControl(DexcRuntimeFormDetail.PendingFocus);
					DexcRuntimeFormDetail.PendingFocus = null;
				}
				DexcRuntimeUIState.UpdateInputStateFromFocus();
			}
			finally
			{
				GUI.matrix = previousMatrix;
				DrawScale = previousScale;
			}
		}

		/// <summary>
		/// Draws the column of sections and, for a list or a page opened from one, the list, and returns the rectangle left
		/// for the page itself. A list's own page goes in the list's column, with a hint where its pages open.
		/// </summary>
		private static Rect DrawSplitColumns(Rect content, DexcRuntimeIMGUIStyles styles)
		{
			SplitColumns(DexcRuntimeUIState.Page, content.width, out _, out var listWidth);
			if (listWidth > 0f)
			{
				var listPage = SplitListPage(DexcRuntimeUIState.Page).Value;
				var open = OpenEntryKey(DexcRuntimeUIState.Page);
				DrawColumn(new Rect(content.x, content.y, listWidth, content.height), styles, () =>
				{
					foreach (var row in ListRowsOf(listPage))
					{
						// The list beside a page is for the pointer; the gamepad goes back to it with Back or left.
						if (row.ListItem)
						{
							row.NavigationIndex = -1;
							DrawListRow(row, row.Key == open, null, null);
							GUILayout.Space(DexcRuntimeFormLayout.RowGap);
						}
						else if (row.Kind == DexcUIRowKind.Button && GUILayout.Button(row.Label, styles.SecondaryButton, GUILayout.MinHeight(ListRowHeight)))
							row.Activated();
					}
				}, true);
			}
			return new Rect(content.x + listWidth, content.y, content.width - listWidth, content.height);
		}

		/// <summary>The column of sections side by side, the panel's full height at its left.</summary>
		private static void DrawSections(Rect rect, DexcRuntimeIMGUIStyles styles)
		{
			DrawColumn(rect, styles, () =>
			{
				var current = SectionKey(DexcRuntimeUIState.Page);
				var rows = SectionRows();
				for (var index = 0; index < rows.Count; index++)
				{
					DrawListRow(rows[index], rows[index].Key == current, null, null);
					// The gamepad's place when it has gone into the column, where it would have gone back to Home.
					if (
						index == DexcRuntimeSplitHome.SectionFocus
						&& DexcRuntimeUIState.NavigationFocusVisible
						&& Event.current.type == EventType.Repaint
					)
						DrawOutline(GUILayoutUtility.GetLastRect(), styles.Palette.Focus, 2f);
					GUILayout.Space(DexcRuntimeFormLayout.RowGap);
				}
			});
		}

		/// <summary>One column side by side: its own area and, for a list, its own scroll position.</summary>
		private static void DrawColumn(Rect rect, DexcRuntimeIMGUIStyles styles, Action draw, bool scrolls = false)
		{
			var width = FormContentWidth;
			GUILayout.BeginArea(rect);
			GUILayout.BeginVertical(styles.Panel, GUILayout.Width(rect.width), GUILayout.Height(rect.height));
			var inner = Mathf.Max(1f, rect.width - styles.Panel.padding.horizontal - (scrolls ? styles.ScrollbarWidth + ScrollContentGap : 0f));
			FormContentWidth = inner;
			if (scrolls)
				DexcRuntimeUIState.Data.SplitListScroll = styles.BeginScrollView(
					DexcRuntimeUIState.Data.SplitListScroll,
					Mathf.Max(1f, rect.width - styles.Panel.padding.horizontal),
					Mathf.Max(1f, rect.height - styles.Panel.padding.vertical)
				);
			GUILayout.BeginVertical(GUILayout.Width(inner));
			draw();
			GUILayout.EndVertical();
			if (scrolls)
				DexcRuntimeUIState.Data.SplitListScroll = styles.EndScrollView(DexcRuntimeUIState.Data.SplitListScroll);
			GUILayout.EndVertical();
			GUILayout.EndArea();
			FormContentWidth = width;
		}

		internal static DexcRuntimeIMGUIStyles EnsureStyleCache()
		{
			var skin = DexcDebugger.CurrentSession == null ? null : DexcDebugger.CurrentSession.Options.RuntimeUISkin;
			if (Styles != null && ReferenceEquals(StyleSkin, skin))
				return Styles;
			Styles?.Dispose();
			StyleSkin = skin;
			Styles = DexcRuntimeIMGUIStyles.Create(skin);
			return Styles;
		}

		/// <summary>Destroys the generated IMGUI textures and forgets the cached styles.</summary>
		internal static void DisposeStyleCache()
		{
			Styles?.Dispose();
			Styles = null;
			StyleSkin = null;
		}

		private static DexcRuntimeUILayout CalculateLayout(int screenWidth, int screenHeight, Rect safeArea, DexcRuntimeUIPage page)
		{
			var profile = UseProfileOverride
				? ProfileOverride
				: DexcRuntimeUIReferenceResolver.ResolveCurrentProfile(screenWidth, screenHeight);
			return CalculateLayout(screenWidth, screenHeight, safeArea, page, profile);
		}

		private static DexcRuntimeUILayout CalculateLayout(
			int screenWidth,
			int screenHeight,
			Rect safeArea,
			DexcRuntimeUIPage page,
			DexcRuntimeUIProfileKind profile
		)
		{
			var reference = DexcRuntimeUIReferenceResolver.Calculate(screenWidth, screenHeight, safeArea, profile);
			var panelBottomLeft = reference.Panel;
			var panel = new Rect(
				panelBottomLeft.x,
				reference.Screen.height - panelBottomLeft.yMax,
				panelBottomLeft.width,
				panelBottomLeft.height
			);
			var headerHeight = Mathf.Min(DexcRuntimeFormLayout.HeaderHeight(reference.HitHeight) * reference.Scale, panel.height);
			var footerHeight = HasFooter(page)
				? Mathf.Min((reference.HitHeight + 12f) * reference.Scale, Mathf.Max(0f, panel.height - headerHeight))
				: 0f;
			// Side by side the sections run the panel's full height at its left; the header, the page and the footer are beside them.
			var side = SectionColumnWidth(reference) * reference.Scale;
			var header = new Rect(panel.x + side, panel.y, panel.width - side, headerHeight);
			var content = new Rect(panel.x + side, panel.y + headerHeight, panel.width - side, Mathf.Max(0f, panel.height - headerHeight - footerHeight));
			var footer = new Rect(panel.x + side, panel.yMax - footerHeight, panel.width - side, footerHeight);
			return new DexcRuntimeUILayout(
				reference.Screen,
				reference.SafeArea,
				panel,
				header,
				content,
				footer,
				DexcRuntimeFormLayout.RowHeight(reference.RowHeight, reference.HitHeight),
				reference.Profile,
				reference.ReferenceResolution,
				reference.Scale
			);
		}

#if UNITY_INCLUDE_TESTS
		internal static DexcRuntimeUILayout CalculateLayoutForTests(
			int screenWidth,
			int screenHeight,
			Rect safeArea,
			DexcRuntimeUIPage page
		)
		{
			return CalculateLayout(
				screenWidth,
				screenHeight,
				safeArea,
				page,
				DexcRuntimeUIReferenceResolver.LegacyProfileForTests(screenWidth, screenHeight)
			);
		}
#endif

		internal static DexcRuntimeUILayout CalculateLayoutForProfileTests(
			int screenWidth,
			int screenHeight,
			Rect safeArea,
			DexcRuntimeUIPage page,
			DexcRuntimeUIProfileKind profile
		)
		{
			return CalculateLayout(screenWidth, screenHeight, safeArea, page, profile);
		}

#if UNITY_INCLUDE_TESTS
		internal static void SetProfileOverrideForTests(DexcRuntimeUIProfileKind profile)
		{
			UseProfileOverride = true;
			ProfileOverride = profile;
		}

		internal static void ClearProfileOverrideForTests()
		{
			UseProfileOverride = false;
		}
#endif

		private static Rect SafeArea()
		{
			return CalculateLayout(Screen.width, Screen.height, Screen.safeArea, DexcRuntimeUIState.Page).Panel;
		}

		/// <summary>
		/// The back arrow appears where there is somewhere to go back to. On the boot screen the list is the root, so back
		/// there does nothing and is not drawn.
		/// </summary>
		/// <summary>The command page's History, in the header while the command is neither confirming nor running.</summary>
		internal static bool HasHeaderHistory() =>
			DexcRuntimeUIState.Page == DexcRuntimeUIPage.CommandDetail
			&& !DexcRuntimeUIState.Confirming
			&& !(DexcRuntimeUIState.AsyncExecution != null && DexcRuntimeUIState.AsyncTask != null && !DexcRuntimeUIState.AsyncTask.IsCompleted);

		internal static bool HasHeaderBack(DexcRuntimeUIPage page)
		{
			if (page == DexcRuntimeUIPage.Home)
				return false;
			// Side by side a section's top page has the sections beside it in place of Home to go back to.
			if (DexcRuntimeSplitHome.Active && BackLeavesSection(page))
				return false;
			return !DebugBootMenu.UsesRuntimeMenu || page != DexcRuntimeUIPage.PanelList;
		}

		/// <summary>Whether Back from <paramref name="page"/> goes to Home: the top page of a section.</summary>
		private static bool BackLeavesSection(DexcRuntimeUIPage page)
		{
			switch (page)
			{
				case DexcRuntimeUIPage.CommandList:
					return DexcRuntimeCommandForm.AtTop;
				case DexcRuntimeUIPage.SettingList:
					return DexcRuntimeSettingForm.AtTop;
				case DexcRuntimeUIPage.InspectorHome:
				case DexcRuntimeUIPage.History:
				case DexcRuntimeUIPage.Diagnostic:
				case DexcRuntimeUIPage.PanelList:
					return true;
				default:
					return false;
			}
		}

		/// <summary>
		/// The close button means "start the game" on the boot screen, which reads as the opposite of what it looks like.
		/// Start is in the footer and says so, so the boot screen does not draw this one.
		/// </summary>
		internal static bool HasHeaderClose()
		{
			return !DebugBootMenu.UsesRuntimeMenu;
		}

		internal static bool HasFooter(DexcRuntimeUIPage page)
		{
			// The boot screen keeps Start in the footer so it is reachable from every page, not only the list it sits in.
			return DebugBootMenu.UsesRuntimeMenu
				|| page == DexcRuntimeUIPage.CommandDetail
				|| page == DexcRuntimeUIPage.SettingDetail
				|| page == DexcRuntimeUIPage.InspectorFields
				|| page == DexcRuntimeUIPage.FormDetail && DexcRuntimeFormDetail.HasApply;
		}

		private static void HandleNavigationInput()
		{
			var current = Event.current;
			if (current != null && current.type == EventType.MouseDown)
				DexcRuntimeUIState.NavigationFocusVisible = false;
			if (current != null && current.type == EventType.KeyDown)
			{
				var textFocused = !string.IsNullOrEmpty(GUI.GetNameOfFocusedControl());
				// Escape while typing leaves the field, as the other renderers do. Going back from there would leave the page
				// and drop the edits waiting for Apply.
				if (textFocused && current.keyCode == KeyCode.Escape)
				{
					GUI.FocusControl(null);
					current.Use();
				}
				var back = !textFocused && (current.keyCode == KeyCode.Escape || current.keyCode == KeyCode.Backspace);
				var direction = 0;
				var horizontal = 0;
				var submit = false;
				if (!textFocused && !current.alt && !current.control && !current.command)
				{
					switch (current.keyCode)
					{
						case KeyCode.Tab:
							direction = current.shift ? -1 : 1;
							break;
						case KeyCode.UpArrow:
							direction = -1;
							break;
						case KeyCode.DownArrow:
							direction = 1;
							break;
						case KeyCode.LeftArrow:
							horizontal = -1;
							break;
						case KeyCode.RightArrow:
							horizontal = 1;
							break;
						case KeyCode.Return:
						case KeyCode.KeypadEnter:
						case KeyCode.Space:
							submit = true;
							break;
					}
				}
				if (back || direction != 0 || horizontal != 0 || submit)
				{
					DexcRuntimeUIState.NavigationFocusVisible = true;
					DebugBootMenu.NoteInteraction();
					// Share one action budget with the gamepad provider, but never turn a
					// native event into a provider frame. Text editing remains with Unity.
					if (DexcRuntimeUIState.ConsumeInputAction())
					{
						if (back)
							DexcRuntimeUIState.Back();
						else if (direction != 0)
							MoveProviderSelection(new Vector2(0f, -direction));
						else if (horizontal != 0)
						{
							// Left and right step the value the focus is on; elsewhere they move like up and down.
							var cycled =
								DexcRuntimeSplitHome.HandleInput(0, horizontal, false, false)
								|| TryCycleFocusedRow(horizontal)
								|| TrySplitStep(horizontal);
							if (!cycled && DexcRuntimeUIState.Page == DexcRuntimeUIPage.PanelDetail)
								DexcRuntimePanelForm.Adjust(horizontal);
							else if (!cycled)
								MoveProviderSelection(new Vector2(0f, -horizontal));
						}
						else
							ActivateProviderSelection();
					}
					current.Use();
				}
			}
			HandleProviderInput();
		}

		internal static void HandleProviderInput()
		{
			if (!DexcRuntimeUIState.IsOpen || !DexcRuntimeUIState.HasInputFrame)
				return;
			var input = DexcRuntimeUIState.InputFrame;
			if (IsTextFocused())
			{
				// Editing a text field: Back or Submit leaves the field; navigation stays with the field until then.
				if ((input.CancelPressed || input.SubmitPressed) && DexcRuntimeUIState.ConsumeInputAction())
				{
					DebugBootMenu.NoteInteraction();
					ReleaseTextFocus();
				}
				return;
			}
			if (input.CancelPressed)
			{
				if (DexcRuntimeUIState.ConsumeInputAction())
				{
					DebugBootMenu.NoteInteraction();
					DexcRuntimeUIState.Back();
				}
				return;
			}
			var direction = input.Navigation;
			var now = Time.unscaledTime;
			var move = false;
			var normalized = Vector2.zero;
			var changed = false;
			if (direction.sqrMagnitude >= 0.35f * 0.35f)
			{
				normalized = direction.normalized;
				changed = Vector2.Dot(normalized, DexcRuntimeUIState.RepeatDirection) < 0.8f;
				move = changed || now >= DexcRuntimeUIState.RepeatAt;
			}
			else
				DexcRuntimeUIState.RepeatDirection = Vector2.zero;
			if ((!move && !input.SubmitPressed) || !DexcRuntimeUIState.ConsumeInputAction())
				return;
			DexcRuntimeUIState.NavigationFocusVisible = true;
			DebugBootMenu.NoteInteraction();
			if (move)
			{
				DexcRuntimeUIState.RepeatDirection = normalized;
				DexcRuntimeUIState.RepeatAt = now + (changed ? 0.35f : 0.08f);
				MoveProviderSelection(normalized);
			}
			if (input.SubmitPressed)
				ActivateProviderSelection();
		}

		/// <summary>True while the active renderer is editing a text field.</summary>
		internal static bool IsTextFocused()
		{
			if (DexcRuntimeUIToolkitRenderer.IsActive)
				return DexcRuntimeUIToolkitRenderer.IsTextFocused;
#if DEXC_HAS_UGUI
			if (DexcRuntimeUGUIRenderer.IsActive)
				return DexcRuntimeUGUIRenderer.IsTextFocused;
#endif
			return Event.current != null
				? !string.IsNullOrEmpty(GUI.GetNameOfFocusedControl())
				: DexcRuntimeUIState.InputState == DebugMenuInputState.TextInput;
		}

		/// <summary>Leaves the text field being edited and returns to navigation. Pending text is committed by the renderer.</summary>
		internal static void ReleaseTextFocus()
		{
			if (DexcRuntimeUIToolkitRenderer.IsActive)
			{
				DexcRuntimeUIToolkitRenderer.ReleaseTextFocus();
				return;
			}
#if DEXC_HAS_UGUI
			if (DexcRuntimeUGUIRenderer.IsActive)
			{
				DexcRuntimeUGUIRenderer.ReleaseTextFocus();
				return;
			}
#endif
			if (Event.current != null)
				GUI.FocusControl(null);
		}

#if UNITY_INCLUDE_TESTS
		internal static void QueueProviderInputForTests(DebugMenuInputFrame input)
		{
			DexcRuntimeUIState.InputFrame = input;
			DexcRuntimeUIState.HasInputFrame = true;
			DexcRuntimeUIState.InputActionFrame = -1;
		}

		internal static void ProcessProviderInputForTests()
		{
			HandleProviderInput();
		}
#endif

		private static void MoveProviderSelection(Vector2 direction)
		{
			try
			{
				var sideways = Mathf.Abs(direction.x) > Mathf.Abs(direction.y);
				if (
					DexcRuntimeSplitHome.HandleInput(
						sideways ? 0 : direction.y > 0f ? -1 : 1,
						sideways ? (direction.x > 0f ? 1 : -1) : 0,
						false,
						false
					)
				)
					return;
				if (
					Mathf.Abs(direction.x) > Mathf.Abs(direction.y)
					&& (TryCycleFocusedRow(direction.x > 0f ? 1 : -1) || TrySplitStep(direction.x > 0f ? 1 : -1))
				)
					return;
				if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.PanelDetail && Mathf.Abs(direction.x) > Mathf.Abs(direction.y))
				{
					DexcRuntimePanelForm.Adjust(direction.x > 0f ? 1 : -1);
					return;
				}
				var delta =
					Mathf.Abs(direction.y) >= Mathf.Abs(direction.x)
						? (
							direction.y > 0f ? -1
							: direction.y < 0f ? 1
							: 0
						)
					: direction.x < 0f ? -1
					: direction.x > 0f ? 1
					: 0;
				if (delta == 0)
					return;
				var count = ProviderSelectionCount();
				if (count <= 0)
					return;
				var selected = DexcRuntimeUIState.ClampSelectedIndex(count);
				DexcRuntimeUIState.SetSelectedIndex(Mathf.Clamp(selected + delta, 0, count - 1));
				if (DexcRuntimeUIRenderer.UsesIMGUI)
					DexcRuntimeUIState.ListSelectionVisibilityRequested = true;
				else
					DexcRuntimeUIState.Data.ListScrollPosition.y = DexcRuntimeUIState.SelectedIndex * ListRowHeight;
			}
			catch (Exception exception)
			{
				DexcRuntimeUIState.Notice = exception.Message;
			}
		}

		internal static int ProviderSelectionCount()
		{
			if (DexcRuntimeUIToolkitRenderer.IsActive)
			{
				return DexcRuntimeUIToolkitRenderer.SelectionCount;
			}

#if DEXC_HAS_UGUI
			if (DexcRuntimeUGUIRenderer.IsActive)
			{
				return DexcRuntimeUGUIRenderer.SelectableCount;
			}

#endif
			var listRows = ListPageRows();
			if (listRows != null)
			{
				return NavigationCount(listRows) + ListFooterItemCount();
			}

			switch (DexcRuntimeUIState.Page)
			{
				case DexcRuntimeUIPage.PanelDetail:
					return DexcRuntimePanelForm.Focusable.Count;
				case DexcRuntimeUIPage.CommandDetail:
					return DexcRuntimeUIState.Confirming ? 2
						: DexcRuntimeUIState.AsyncExecution != null ? 1
						: DetailRowCount() + (CanExecuteCommand() ? 2 : 1) + StarCount();
				case DexcRuntimeUIPage.SettingDetail:
					return DetailRowCount() + (CanApplySetting() ? 2 : 1) + StarCount();
				case DexcRuntimeUIPage.InspectorFields:
					return DetailRowCount() + 2;
				default:
					return 0;
			}
		}

		/// <summary>The rows of the current page when the gamepad walks its rows, or null when it walks footer buttons.</summary>
		private static List<DexcUIRow> ListPageRows()
		{
			switch (DexcRuntimeUIState.Page)
			{
				case DexcRuntimeUIPage.Home:
					return DexcUIRows.HomeRows();
				case DexcRuntimeUIPage.CommandList:
					return DexcRuntimeCommandForm.ListRows();
				case DexcRuntimeUIPage.SettingList:
					return DexcRuntimeSettingForm.ListRows();
				case DexcRuntimeUIPage.PanelList:
					return DexcRuntimePanelForm.ListRows();
				case DexcRuntimeUIPage.FormDetail:
					return DexcRuntimeFormDetail.Rows();
				case DexcRuntimeUIPage.InspectorHome:
					return DexcUIRows.InspectorHomeRows();
				case DexcRuntimeUIPage.InspectorRegistered:
				case DexcRuntimeUIPage.InspectorScene:
				case DexcRuntimeUIPage.InspectorComponents:
				case DexcRuntimeUIPage.InspectorWatches:
				case DexcRuntimeUIPage.History:
				case DexcRuntimeUIPage.Diagnostic:
					return DexcRuntimeInspectorNavigation.ListRows();
				default:
					return null;
			}
		}

		private static int NavigationCount(List<DexcUIRow> rows)
		{
			return rows.Count == 0 ? 0 : rows.Max(row => row.NavigationIndex) + 1;
		}

		/// <summary>Footer buttons that come after a list page's rows: the boot menu's Reset and Start, or a choice's Apply.</summary>
		private static int ListFooterItemCount()
		{
			switch (DexcRuntimeUIState.Page)
			{
				case DexcRuntimeUIPage.PanelList:
					return DebugBootMenu.UsesRuntimeMenu ? 2 : 0;
				case DexcRuntimeUIPage.FormDetail:
					return DexcRuntimeFormDetail.IsSelection && DexcRuntimeFormDetail.CanApply ? 1 : 0;
				default:
					return 0;
			}
		}

		private static void ActivateListRow(List<DexcUIRow> rows)
		{
			var rowCount = NavigationCount(rows);
			var selected = DexcRuntimeUIState.ClampSelectedIndex(rowCount + ListFooterItemCount());
			var row = rows.FirstOrDefault(item => item.NavigationIndex >= 0 && item.NavigationIndex == selected);
			if (row != null)
			{
				// A list's search field takes the keyboard, as a text row does on a detail page.
				if (row.Kind == DexcUIRowKind.Input)
					PendingPanelTextFocus = row.Key;
				else
					row.Activated();
				return;
			}

			if (selected < rowCount || ListFooterItemCount() == 0)
			{
				return;
			}

			if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.FormDetail)
			{
				DexcRuntimeFormDetail.Apply();
			}
			else if (selected == rowCount)
			{
				DexcRuntimePanelForm.ResetBootValues();
			}
			else
			{
				DebugBootMenu.Start();
			}
		}

		internal static void ActivateProviderSelection()
		{
			if (!DexcDebugger.IsInitialized)
			{
				return;
			}
			if (DexcRuntimeSplitHome.HandleInput(0, 0, true, false))
				return;

			try
			{
				if (DexcRuntimeUIToolkitRenderer.IsActive)
				{
					DexcRuntimeUIToolkitRenderer.ActivateProviderSelection();
					return;
				}

#if DEXC_HAS_UGUI
				if (DexcRuntimeUGUIRenderer.IsActive)
				{
					DexcRuntimeUGUIRenderer.ActivateSelected();
					return;
				}

#endif
				var listRows = ListPageRows();
				if (listRows != null)
				{
					ActivateListRow(listRows);
					return;
				}

				var detailRows = DetailPageRows();
				var footerIndex = DexcRuntimeUIState.SelectedIndex;
				if (detailRows != null)
				{
					var rowCount = NavigationCount(detailRows);
					var selected = DexcRuntimeUIState.ClampSelectedIndex(ProviderSelectionCount());
					if (selected < rowCount)
					{
						ActivateDetailRow(detailRows.FirstOrDefault(row => row.NavigationIndex == selected));
						return;
					}
					if (StarCount() == 1 && selected == ProviderSelectionCount() - 1)
					{
						DexcRuntimeFavorites.ToggleCurrent(DexcRuntimeUIState.Page);
						return;
					}
					footerIndex = selected - rowCount;
				}

				switch (DexcRuntimeUIState.Page)
				{
					case DexcRuntimeUIPage.PanelDetail:
						var focusedWidget = DexcRuntimePanelForm.FocusedWidget;
						if (focusedWidget is DebugNumberWidget || focusedWidget is DebugTextWidget)
							PendingPanelTextFocus = focusedWidget.Enabled ? PanelTextKey(focusedWidget) : null;
						else
							DexcRuntimePanelForm.Activate();
						break;
					case DexcRuntimeUIPage.CommandDetail:
						if (DexcRuntimeUIState.Confirming)
						{
							if (DexcRuntimeUIState.ClampSelectedIndex(2) == 0)
							{
								DexcRuntimeCommandExecution.Confirm();
							}
							else
							{
								DexcRuntimeCommandExecution.CancelConfirmation();
							}
						}
						else if (DexcRuntimeUIState.AsyncExecution != null)
						{
							DexcRuntimeCommandExecution.CancelExecution();
						}
						else if (!CanExecuteCommand() || footerIndex == 1)
						{
							DexcRuntimeCommandForm.OpenHistory();
						}
						else
						{
							RequestCommandExecution();
						}

						break;
					case DexcRuntimeUIPage.SettingDetail:
						if (CanApplySetting() && footerIndex == 0)
						{
							ApplySetting();
						}
						else
						{
							DexcRuntimeSettingForm.OpenPresets();
						}

						break;
					case DexcRuntimeUIPage.InspectorFields:
						if (footerIndex == 0)
						{
							DexcRuntimeInspectorForm.Apply();
						}
						else
						{
							DexcRuntimeInspectorForm.OpenWatchSelection();
						}

						break;
				}
			}
			catch (Exception exception)
			{
				DexcRuntimeUIState.Notice = exception.Message;
			}
		}

		private static void DrawHeader(Rect physicalHeader)
		{
			var styles = EnsureStyleCache();
			// The place in the menu sits over the page title; a page with none centres its title alone.
			var segments = Breadcrumb(DexcRuntimeUIState.Page);
			// The header can be taller than its buttons, so the row sits in its middle.
			GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
			GUILayout.FlexibleSpace();
			GUILayout.BeginHorizontal();
			GUILayout.Space(4f);
			if (
				HasHeaderBack(DexcRuntimeUIState.Page)
				&& GUILayout.Button("‹", styles.CycleButton, GUILayout.Width(ListRowHeight), GUILayout.Height(ListRowHeight))
			)
				DexcRuntimeUIState.Back();
			GUILayout.Space(4f);
			var reference = CurrentReference();
			var star = DexcRuntimeFavorites.Star(DexcRuntimeUIState.Page);
			// The title gives way to every button beside it, so a long name is cut short instead of pushing them out.
			var buttons =
				(HasHeaderBack(DexcRuntimeUIState.Page) ? ListRowHeight : 0f)
				+ (reference.CanSplit ? SwitchWidth(styles, "Single") + SwitchWidth(styles, "Split") + 4f : 0f)
				+ (HasHeaderHistory() ? SwitchWidth(styles, "History") + 4f : 0f)
				+ (star != null ? ListRowHeight : 0f)
				+ (HasHeaderClose() ? ListRowHeight : 0f);
			var titleWidth = Mathf.Max(1f, physicalHeader.width / Mathf.Max(0.0001f, DrawScale) - buttons - 24f);
			GUILayout.BeginVertical(GUILayout.MinHeight(ListRowHeight), GUILayout.MaxWidth(titleWidth));
			GUILayout.FlexibleSpace();
			if (segments.Count > 1)
				GUILayout.Label(string.Join(" › ", segments.Take(segments.Count - 1)), styles.Breadcrumb, GUILayout.MaxWidth(titleWidth));
			GUILayout.Label(segments[segments.Count - 1], styles.Title, GUILayout.MaxWidth(titleWidth));
			GUILayout.FlexibleSpace();
			GUILayout.EndVertical();
			GUILayout.FlexibleSpace();
			// Where the screen is wide enough, the header switches between one page and side by side.
			if (reference.CanSplit)
			{
				var single = GUILayout.Button(
					"Single",
					reference.Split ? styles.SwitchButton : styles.SelectedSecondaryButton,
					GUILayout.Width(SwitchWidth(styles, "Single")),
					GUILayout.Height(ListRowHeight)
				);
				if (single)
					DexcRuntimeUIState.LayoutOverride = DexcRuntimeUILayoutMode.Single;
				var split = GUILayout.Button(
					"Split",
					reference.Split ? styles.SelectedSecondaryButton : styles.SwitchButton,
					GUILayout.Width(SwitchWidth(styles, "Split")),
					GUILayout.Height(ListRowHeight)
				);
				if (split)
					DexcRuntimeUIState.LayoutOverride = DexcRuntimeUILayoutMode.Split;
				GUILayout.Space(4f);
			}
			// A command's earlier values open from the header. The gamepad still reaches it after Execute, before the star.
			if (HasHeaderHistory())
			{
				var history = GUILayout.Button(
					"History",
					styles.SwitchButton,
					GUILayout.Width(SwitchWidth(styles, "History")),
					GUILayout.Height(ListRowHeight)
				);
				var index = DetailRowCount() + (CanExecuteCommand() ? 1 : 0);
				if (IsNavigationFocused(index) && Event.current.type == EventType.Repaint)
					DrawOutline(GUILayoutUtility.GetLastRect(), styles.Palette.Focus, 2f);
				if (history)
					DexcRuntimeCommandForm.OpenHistory();
				GUILayout.Space(4f);
			}
			// A command or setting group can be starred from its own page; the star fills once it is a favorite.
			if (star != null)
			{
				if (GUILayout.Button(star, styles.CycleButton, GUILayout.Width(ListRowHeight), GUILayout.Height(ListRowHeight)))
					DexcRuntimeFavorites.ToggleCurrent(DexcRuntimeUIState.Page);
				var count = ProviderSelectionCount();
				if (StarCount() == 1 && IsNavigationFocused(count - 1, count) && Event.current.type == EventType.Repaint)
					DrawOutline(GUILayoutUtility.GetLastRect(), styles.Palette.Focus, 2f);
			}
			if (HasHeaderClose())
			{
				var close = GUILayout.Button("×", styles.CycleButton, GUILayout.Width(ListRowHeight), GUILayout.Height(ListRowHeight));
				var closeRect = LastGUILayoutAreaScreenRect(physicalHeader);
#if UNITY_INCLUDE_TESTS
				if (closeRect.width > 0f && closeRect.height > 0f)
					CloseScreenRectForTests = closeRect;
#endif
				if (close)
					DexcRuntimeUIState.Close();
			}
			GUILayout.Space(4f);
			GUILayout.EndHorizontal();
			GUILayout.FlexibleSpace();
			GUILayout.EndVertical();
		}

		/// <summary>A half of the header's Single | Split switch keeps its word whole, whichever half is chosen.</summary>
		private static float SwitchWidth(DexcRuntimeIMGUIStyles styles, string label) =>
			Mathf.Ceil(styles.SwitchButton.CalcSize(new GUIContent(label)).x);

		private static void DrawFooter()
		{
			var styles = EnsureStyleCache();
#if UNITY_INCLUDE_TESTS
			FooterFocusMarkerRectForTests = default(Rect);
			FooterFocusMarkerVisibleForTests = false;
#endif
			// The secondary action keeps a fixed width at the left; the primary one takes the rest, so it is the easy target.
			var secondary = GUILayout.Width(Mathf.Min(120f, Mathf.Max(60f, FormContentWidth * 0.36f)));
			// A detail page numbers its rows before the footer's actions.
			var offset = DetailRowCount();
			GUILayout.BeginHorizontal(styles.Footer, GUILayout.Height(FooterHeight));
			if (DebugBootMenu.UsesRuntimeMenu)
			{
				// Reset and Start are the gamepad's last two items on the panel list, after the panel rows.
				var count = ProviderSelectionCount();
				var onList = DexcRuntimeUIState.Page == DexcRuntimeUIPage.PanelList;
				if (FooterButton("Reset", styles.SecondaryButton, onList && IsNavigationFocused(count - 2, count), secondary))
					DexcRuntimePanelForm.ResetBootValues();
				if (FooterButton("Start", styles.PrimaryButton, onList && IsNavigationFocused(count - 1, count)))
					DebugBootMenu.Start();
			}
			else if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.CommandDetail)
			{
				if (DexcRuntimeUIState.Confirming)
				{
					if (FooterButton("Cancel", styles.SecondaryButton, IsNavigationFocused(1), secondary))
						DexcRuntimeCommandExecution.CancelConfirmation();
					if (FooterButton("Confirm", styles.PrimaryButton, IsNavigationFocused(0)))
						DexcRuntimeCommandExecution.Confirm();
				}
				else if (
					DexcRuntimeUIState.AsyncExecution != null
					&& DexcRuntimeUIState.AsyncTask != null
					&& !DexcRuntimeUIState.AsyncTask.IsCompleted
				)
				{
					if (FooterButton("Cancel running command", styles.PrimaryButton, IsNavigationFocused(0)))
						DexcRuntimeCommandExecution.CancelExecution();
				}
				else
				{
					// History is in the header; the footer is Execute alone.
					var enabled = CanExecuteCommand();
					using (new GUIEnabledScope(enabled))
					{
						if (FooterButton("Execute", enabled ? styles.PrimaryButton : styles.DisabledButton, enabled && IsNavigationFocused(offset)))
							RequestCommandExecution();
					}
				}
			}
			else if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.SettingDetail)
			{
				var enabled = CanApplySetting();
				if (FooterButton("Presets", styles.SecondaryButton, IsNavigationFocused(offset + (enabled ? 1 : 0)), secondary))
					DexcRuntimeSettingForm.OpenPresets();
				using (new GUIEnabledScope(enabled))
				{
					if (FooterButton("Apply", enabled ? styles.PrimaryButton : styles.DisabledButton, enabled && IsNavigationFocused(offset)))
						ApplySetting();
				}
			}
			else if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.InspectorFields)
			{
				if (FooterButton("Watch", styles.SecondaryButton, IsNavigationFocused(offset + 1), secondary))
					DexcRuntimeInspectorForm.OpenWatchSelection();
				var enabled = DexcRuntimeInspectorForm.CanApply;
				using (new GUIEnabledScope(enabled))
					if (FooterButton("Apply", enabled ? styles.PrimaryButton : styles.DisabledButton, IsNavigationFocused(offset)))
						DexcRuntimeInspectorForm.Apply();
			}
			else if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.FormDetail && DexcRuntimeFormDetail.HasApply)
			{
				var count = ProviderSelectionCount();
				using (new GUIEnabledScope(DexcRuntimeFormDetail.CanApply))
				{
					if (FooterButton("Apply", styles.PrimaryButton, DexcRuntimeFormDetail.CanApply && IsNavigationFocused(count - 1, count)))
						DexcRuntimeFormDetail.Apply();
				}
			}
			GUILayout.EndHorizontal();
		}

		/// <summary>A footer action, outlined in the focus colour while the gamepad is on it.</summary>
		private static bool FooterButton(string label, GUIStyle style, bool focused, params GUILayoutOption[] width)
		{
			var options = new List<GUILayoutOption> { GUILayout.Height(ListRowHeight) };
			options.AddRange(width);
			if (width.Length == 0)
				options.Add(GUILayout.ExpandWidth(true));
			var clicked = GUILayout.Button(label, style, options.ToArray());
			if (focused && Event.current.type == EventType.Repaint)
			{
				var rect = GUILayoutUtility.GetLastRect();
				DrawOutline(rect, EnsureStyleCache().Palette.Focus, 2f);
#if UNITY_INCLUDE_TESTS
				FooterFocusMarkerRectForTests = rect;
				FooterFocusMarkerVisibleForTests = true;
#endif
			}
			return clicked;
		}

		internal static void DrawOutline(Rect rect, Color color, float thickness)
		{
			var previous = GUI.color;
			GUI.color = color;
			GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture);
			GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), Texture2D.whiteTexture);
			GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture);
			GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);
			GUI.color = previous;
		}

		private static bool CanExecuteCommand() => DexcRuntimeCommandExecution.CanExecute();

		private static bool IsNavigationFocused(int index)
		{
			return IsNavigationFocused(index, ProviderSelectionCount());
		}

		/// <summary>Whether the gamepad's focus is on item <paramref name="index"/> of <paramref name="count"/> items.</summary>
		private static bool IsNavigationFocused(int index, int count)
		{
			var navigationVisible =
				Event.current != null
					? string.IsNullOrEmpty(GUI.GetNameOfFocusedControl())
					: DexcRuntimeUIState.InputProvider == null || DexcRuntimeUIState.InputState == DebugMenuInputState.Navigation;
			// While the gamepad is in the column of sections, nothing on the page has its focus.
			return navigationVisible
				&& DexcRuntimeUIState.NavigationFocusVisible
				&& DexcRuntimeSplitHome.SectionFocus < 0
				&& count > 0
				&& DexcRuntimeUIState.ClampSelectedIndex(count) == index;
		}

#if UNITY_INCLUDE_TESTS
		internal static bool IsNavigationFocusedForTests(int index)
		{
			return IsNavigationFocused(index);
		}

		/// <summary>Where the footer's actions start in the gamepad's order on a detail page: after the page's rows.</summary>
		internal static int DetailRowCountForTests => DetailRowCount();
#endif

		internal static bool CanApplySetting() =>
			DexcRuntimeUIState.IsOpen && DexcRuntimeUIState.Page == DexcRuntimeUIPage.SettingDetail && DexcRuntimeSettingForm.CanApply;

		private static void RequestCommandExecution()
		{
			if (CanExecuteCommand())
			{
				DexcRuntimeCommandExecution.Request();
			}
		}

		/// <summary>The list a page belongs to side by side: the list itself, or the list its page was opened from.</summary>
		internal static DexcRuntimeUIPage? SplitListPage(DexcRuntimeUIPage page)
		{
			switch (page)
			{
				case DexcRuntimeUIPage.CommandList:
				case DexcRuntimeUIPage.CommandDetail:
					return DexcRuntimeUIPage.CommandList;
				case DexcRuntimeUIPage.SettingList:
				case DexcRuntimeUIPage.SettingDetail:
					return DexcRuntimeUIPage.SettingList;
				case DexcRuntimeUIPage.PanelList:
				case DexcRuntimeUIPage.PanelDetail:
					return DexcRuntimeUIPage.PanelList;
				default:
					return null;
			}
		}

		/// <summary>The rows of a list page, for showing it beside the page it opened.</summary>
		internal static List<DexcUIRow> ListRowsOf(DexcRuntimeUIPage page) =>
			page == DexcRuntimeUIPage.CommandList ? DexcRuntimeCommandForm.ListRows()
			: page == DexcRuntimeUIPage.SettingList ? DexcRuntimeSettingForm.ListRows()
			: DexcRuntimePanelForm.ListRows();

		/// <summary>The key of the list entry the open page came from, so the list beside it can show which one is open.</summary>
		internal static string OpenEntryKey(DexcRuntimeUIPage page)
		{
			switch (page)
			{
				case DexcRuntimeUIPage.CommandDetail:
					return "command-list:" + DexcRuntimeUIState.CommandId;
				case DexcRuntimeUIPage.SettingDetail:
					return DexcRuntimeSettingForm.Current == null ? null : "setting-list:" + DexcRuntimeSettingForm.Current.Id;
				case DexcRuntimeUIPage.PanelDetail:
					return DexcRuntimePanelForm.Current == null ? null : "panel-list:" + DexcRuntimePanelForm.Current.GetHashCode();
				default:
					return null;
			}
		}

		/// <summary>The Home entry of the section a page belongs to, for the column of sections side by side.</summary>
		internal static string SectionKey(DexcRuntimeUIPage page)
		{
			switch (page)
			{
				case DexcRuntimeUIPage.CommandList:
				case DexcRuntimeUIPage.CommandDetail:
					return "home:" + DexcRuntimeUIPage.CommandList;
				case DexcRuntimeUIPage.SettingList:
				case DexcRuntimeUIPage.SettingDetail:
					return "home:" + DexcRuntimeUIPage.SettingList;
				case DexcRuntimeUIPage.History:
					return "home:" + DexcRuntimeUIPage.History;
				case DexcRuntimeUIPage.Diagnostic:
					return "home:" + DexcRuntimeUIPage.Diagnostic;
				case DexcRuntimeUIPage.PanelList:
				case DexcRuntimeUIPage.PanelDetail:
					return "home:" + DexcRuntimeUIPage.PanelList;
				case DexcRuntimeUIPage.Home:
				case DexcRuntimeUIPage.FormDetail:
					return null;
				default:
					return "home:" + DexcRuntimeUIPage.InspectorHome;
			}
		}

		/// <summary>The sections for the left column side by side: Home's six entries, none of them on the gamepad's path.</summary>
		internal static List<DexcUIRow> SectionRows()
		{
			var rows = DexcUIRows.HomeRows().Where(row => row.Key != null && row.Key.StartsWith("home:", StringComparison.Ordinal) && row.Drill && row.NavigationIndex >= 0 && row.NavigationIndex < 6).ToList();
			foreach (var row in rows)
				row.NavigationIndex = -1;
			return rows;
		}

		/// <summary>
		/// Left or right side by side: from a list, right opens the entry the gamepad is on; from the page it opened, left
		/// goes back to the list. False when the layout is not side by side or the page is not part of a list.
		/// </summary>
		internal static bool TrySplitStep(int direction)
		{
			// Left from a section's top page goes into the column of sections, where Home would be.
			if (direction < 0 && DexcRuntimeSplitHome.Active && BackLeavesSection(DexcRuntimeUIState.Page))
				return DexcRuntimeSplitHome.Enter();
			if (!CurrentReference().Split)
				return false;
			var list = SplitListPage(DexcRuntimeUIState.Page);
			if (list == null)
				return false;
			if (DexcRuntimeUIState.Page == list && direction > 0)
			{
				ActivateProviderSelection();
				return true;
			}
			if (DexcRuntimeUIState.Page != list && direction < 0)
			{
				DexcRuntimeUIState.Back();
				return true;
			}
			return false;
		}

		/// <summary>
		/// Side by side, the widths at the left of the page: the sections (none on the boot screen, which has no sections)
		/// and the list beside a page opened from it. A list's own page takes the list's place, so it has no list beside it.
		/// <paramref name="contentWidth"/> is what the header, the page and the footer have beside the sections.
		/// </summary>
		internal static void SplitColumns(DexcRuntimeUIPage page, float contentWidth, out float sections, out float list)
		{
			sections = DebugBootMenu.UsesRuntimeMenu ? 0f : DexcRuntimeFormLayout.SplitSectionWidth;
			var listPage = SplitListPage(page);
			list = listPage == null || page == listPage ? 0f : Mathf.Min(DexcRuntimeFormLayout.SplitListWidth, contentWidth * 0.5f);
		}

		/// <summary>
		/// Side by side, the logical width of the column of sections, which runs the panel's full height with the header, the
		/// page and the footer beside it. Zero on one page and on the boot screen.
		/// </summary>
		internal static float SectionColumnWidth(DexcRuntimeUIReferenceLayout reference) =>
			reference.Split && !DebugBootMenu.UsesRuntimeMenu ? DexcRuntimeFormLayout.SplitSectionWidth : 0f;

		/// <summary>The reference layout for the current screen, as the menu is drawn.</summary>
		internal static DexcRuntimeUIReferenceLayout CurrentReference()
		{
			var profile = UseProfileOverride ? ProfileOverride : DexcRuntimeUIReferenceResolver.ResolveCurrentProfile(Screen.width, Screen.height);
			return DexcRuntimeUIReferenceResolver.Calculate(Screen.width, Screen.height, Screen.safeArea, profile);
		}

		/// <summary>
		/// Where the page sits in the menu, outermost first. The last segment is the page's own title; the header draws the
		/// others above it. Built from the open command, setting, component or panel, not from the page's rows.
		/// </summary>
		internal static IReadOnlyList<string> Breadcrumb(DexcRuntimeUIPage page)
		{
			var segments = new List<string>();
			if (page == DexcRuntimeUIPage.FormDetail)
			{
				segments.Add(Title(DexcRuntimeFormDetail.ReturnTo));
				segments.Add(string.IsNullOrEmpty(DexcRuntimeFormDetail.Title) ? "Detail" : DexcRuntimeFormDetail.Title);
				return segments;
			}
			segments.Add(Title(page));
			switch (page)
			{
				case DexcRuntimeUIPage.CommandList:
					segments.AddRange(DexcRuntimeCommandForm.FolderSegments);
					break;
				case DexcRuntimeUIPage.SettingList:
					segments.AddRange(DexcRuntimeSettingForm.FolderSegments);
					break;
				case DexcRuntimeUIPage.CommandDetail:
					if (DexcRuntimeUIState.Command != null)
						segments.AddRange(DexcRuntimeUIState.Command.Path.Split('/'));
					break;
				case DexcRuntimeUIPage.SettingDetail:
					if (DexcRuntimeSettingForm.Current != null)
						segments.AddRange(DexcRuntimeSettingForm.Current.Label.Split('/'));
					break;
				case DexcRuntimeUIPage.InspectorRegistered:
					segments.Add("Registered");
					break;
				case DexcRuntimeUIPage.InspectorScene:
					segments.Add("Scene");
					break;
				case DexcRuntimeUIPage.InspectorWatches:
					segments.Add("Watch");
					break;
				case DexcRuntimeUIPage.InspectorComponents:
					segments.Add("Components");
					break;
				case DexcRuntimeUIPage.InspectorFields:
					segments.Add(DexcRuntimeInspectorForm.Title);
					break;
				case DexcRuntimeUIPage.PanelDetail:
					if (DexcRuntimePanelForm.Current != null)
						segments.Add(DexcRuntimePanelForm.Current.Title);
					break;
			}
			return segments;
		}

		/// <summary>The header for renderers that draw rich text: the place in the menu, small, over the page title in bold.</summary>
		internal static string RichHeader(DexcRuntimeUIPage page, Color secondary)
		{
			var segments = Breadcrumb(page);
			var title = "<b>" + segments[segments.Count - 1] + "</b>";
			if (segments.Count < 2)
				return title;
			var place = string.Join(" › ", segments.Take(segments.Count - 1));
			return "<size=" + DexcRuntimeFormLayout.SmallFontSize + "><color=#" + ColorUtility.ToHtmlStringRGB(secondary) + ">" + place + "</color></size>\n" + title;
		}

		internal static string Title(DexcRuntimeUIPage page)
		{
			switch (page)
			{
				case DexcRuntimeUIPage.CommandList:
				case DexcRuntimeUIPage.CommandDetail:
					return "Command";
				case DexcRuntimeUIPage.SettingList:
				case DexcRuntimeUIPage.SettingDetail:
					return "Setting";
				case DexcRuntimeUIPage.InspectorHome:
				case DexcRuntimeUIPage.InspectorRegistered:
				case DexcRuntimeUIPage.InspectorScene:
				case DexcRuntimeUIPage.InspectorComponents:
				case DexcRuntimeUIPage.InspectorFields:
				case DexcRuntimeUIPage.InspectorWatches:
					return "Inspector";
				case DexcRuntimeUIPage.History:
					return "History";
				case DexcRuntimeUIPage.PanelList:
				case DexcRuntimeUIPage.PanelDetail:
					return DebugBootMenu.UsesRuntimeMenu ? DebugBootMenu.RuntimeTitle() : "Panels";
				case DexcRuntimeUIPage.Diagnostic:
					return "Diagnostic";
				default:
					return "Dexc Debug";
			}
		}

		internal static string CollectionWarning()
		{
			if (!DexcDebugger.IsInitialized)
				return string.Empty;
			var commands = DebugCommand
				.Registry.GetList(new DebugListQuery(string.Empty, string.Empty, DexcRuntimeUIState.CreatePageRequest(0, 1)))
				.Summary;
			var settings = DebugSetting
				.GetList(new DebugListQuery(string.Empty, string.Empty, DexcRuntimeUIState.CreatePageRequest(0, 1)))
				.Summary;
			var excluded = commands.ExcludedDefinitionCount + settings.ExcludedDefinitionCount;
			var errors = commands.ErrorCount + settings.ErrorCount;
			return errors == 0 && excluded == 0
				? string.Empty
				: "Collection: " + excluded + " excluded / " + errors + " errors · Diagnostics";
		}

		private static void DrawHome()
		{
			DrawRows(DexcUIRows.HomeRows());
		}

		private static void DrawCommandList()
		{
			DrawRows(DexcRuntimeCommandForm.ListRows());
		}

		internal static void SelectCommand(DebugCommandListItem item)
		{
			if (!DebugCommand.Registry.TryGetDescriptor(item.Id, out var descriptor))
			{
				DexcRuntimeUIState.Notice = "Command descriptor is stale.";
				return;
			}
			var result = DebugCommand.Draft.Create(item.Id);
			if (!result.TryGetDraftId(out var draft))
			{
				DexcRuntimeUIState.Notice = "Command draft could not be created.";
				return;
			}
			DexcRuntimeUIState.DiscardDraft();
			DexcRuntimeUIState.CommandId = item.Id;
			DexcRuntimeUIState.Command = descriptor;
			DexcRuntimeUIState.DraftId = draft;
			DexcRuntimeUIState.HasDraft = true;
			DexcRuntimeUIState.LastCommandResult = null;
			DexcRuntimeCommandForm.ResetResult();
			DexcRuntimeUIState.CommandInputText.Clear();
			DexcRuntimeUIState.CommandInvalidInput.Clear();
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandDetail);
		}

#if UNITY_INCLUDE_TESTS
		internal static void SelectCommandForTests(DebugCommandListItem item)
		{
			SelectCommand(item);
		}
#endif

		private static void DrawFormDetail()
		{
			DrawRows(DexcRuntimeFormDetail.Rows());
		}

		private static void DrawCommandDetail()
		{
			if (DexcRuntimeUIState.Command == null)
			{
				DexcRuntimeUIState.Back();
				return;
			}
			DrawRows(DetailPageRows() ?? DexcRuntimeCommandForm.DetailRows());
		}

		/// <summary>
		/// A detail page's rows numbered for the gamepad, or null on other pages and while a command waits for confirmation
		/// or runs. The fields come first, top to bottom, then the footer's actions; a list page numbers its own rows.
		/// </summary>
		private static List<DexcUIRow> DetailPageRows()
		{
			List<DexcUIRow> rows;
			switch (DexcRuntimeUIState.Page)
			{
				case DexcRuntimeUIPage.CommandDetail:
					if (DexcRuntimeUIState.Command == null || DexcRuntimeUIState.Confirming || DexcRuntimeUIState.AsyncExecution != null)
						return null;
					rows = DexcRuntimeCommandForm.DetailRows();
					break;
				case DexcRuntimeUIPage.SettingDetail:
					rows = DexcRuntimeSettingForm.DetailRows(false);
					break;
				case DexcRuntimeUIPage.InspectorFields:
					rows = DexcRuntimeInspectorForm.FieldRows(false);
					break;
				default:
					return null;
			}
			var navigation = 0;
			foreach (var row in rows)
				if (row.Kind == DexcUIRowKind.Input || row.Kind == DexcUIRowKind.Choice || row.Kind == DexcUIRowKind.Button)
					row.NavigationIndex = navigation++;
			return rows;
		}

		/// <summary>The header's star is the gamepad's last item on a page that can be starred, after the footer's actions.</summary>
		private static int StarCount() => DetailPageRows() != null && DexcRuntimeFavorites.Star(DexcRuntimeUIState.Page) != null ? 1 : 0;

		/// <summary>How many of a detail page's gamepad items are its rows; the footer's actions are numbered after them.</summary>
		private static int DetailRowCount()
		{
			var rows = DetailPageRows();
			return rows == null ? 0 : NavigationCount(rows);
		}

		/// <summary>Submit on a detail page's row: a text field takes the keyboard, a choice or a button does what a press does.</summary>
		private static void ActivateDetailRow(DexcUIRow row)
		{
			if (row == null || !row.IsEnabled)
				return;
			if (row.Kind == DexcUIRowKind.Input)
				PendingPanelTextFocus = row.Key;
			else
				row.Activated?.Invoke();
		}

		/// <summary>Left or right on a choice that steps in place. False when the gamepad is not on such a row.</summary>
		private static bool TryCycleFocusedRow(int direction)
		{
#if DEXC_HAS_UGUI
			if (DexcRuntimeUGUIRenderer.IsActive)
				return DexcRuntimeUGUIRenderer.CycleSelected(direction);
#endif
			if (DexcRuntimeUIToolkitRenderer.IsActive)
				return DexcRuntimeUIToolkitRenderer.CycleSelected(direction);
			if (!UsesIMGUI)
				return false;
			var rows = DexcRuntimeUIState.Page == DexcRuntimeUIPage.PanelDetail ? DexcRuntimePanelForm.DetailRows() : DetailPageRows();
			if (rows == null)
				return false;
			var selected = DexcRuntimeUIState.ClampSelectedIndex(ProviderSelectionCount());
			var row = rows.FirstOrDefault(item => item.NavigationIndex == selected && item.Cycle != null);
			if (row == null || !row.IsEnabled)
				return false;
			row.Cycle(direction);
			return true;
		}

		/// <summary>Command field controls are named per session and draft, so focus left on a stale draft can be found and dropped.</summary>
		internal static string CurrentCommandInputPrefix()
		{
			return CommandInputControlPrefix + DexcDebugger.CurrentSession?.SessionId + ":" + DexcRuntimeUIState.DraftId + ":";
		}

		private static void ClearStaleCommandInputFocus()
		{
			var focused = GUI.GetNameOfFocusedControl();
			if (!focused.StartsWith(CommandInputControlPrefix, StringComparison.Ordinal))
			{
				return;
			}
			if (
				!DexcRuntimeUIState.IsOpen
				|| DexcRuntimeUIState.Page != DexcRuntimeUIPage.CommandDetail
				|| !DexcRuntimeUIState.HasDraft
				|| !focused.StartsWith(CurrentCommandInputPrefix(), StringComparison.Ordinal)
			)
			{
				GUI.FocusControl(null);
			}
		}

		private static Rect LastGUILayoutScreenRect()
		{
			if (Event.current == null || Event.current.type != EventType.Repaint)
				return default(Rect);
			var rect = GUILayoutUtility.GetLastRect();
			var topLeft = GUIUtility.GUIToScreenPoint(rect.position);
			var width = rect.width * DrawScale;
			var height = rect.height * DrawScale;
			return new Rect(topLeft.x * DrawScale, Screen.height - topLeft.y * DrawScale - height, width, height);
		}

		private static Rect LastGUILayoutAreaScreenRect(Rect physicalArea)
		{
			if (Event.current == null || Event.current.type != EventType.Repaint)
				return default(Rect);
			var rect = GUILayoutUtility.GetLastRect();
			var x = physicalArea.x + rect.x * DrawScale;
			var top = physicalArea.y + rect.y * DrawScale;
			var width = rect.width * DrawScale;
			var height = rect.height * DrawScale;
			return new Rect(x, Screen.height - top - height, width, height);
		}

		internal static bool TryDrawRuntimeInputEditor(DebugCommandFieldDescriptor field)
		{
			if (field == null || field.Schema == null || field.InputType == null)
				return false;
			if (!DexcRuntimeUIState.InputEditorSelections.TryGetValue(field.Id, out var selection))
			{
				if (DexcRuntimeUIState.InputEditorProviderTypes == null)
					DexcRuntimeUIState.InputEditorProviderTypes = DexcDefinitions.InputEditors;
				var descriptor = new DebugInputEditorDescriptor(field.InputType, field.Schema, field.Schema.SemanticType, "Runtime");
				DebugInputEditor provider;
				string error;
				try
				{
					DexcDebugInputEditorSelector.TrySelect(
						descriptor,
						DexcRuntimeUIState.InputEditorProviderTypes,
						out provider,
						out error
					);
				}
				catch (Exception exception)
				{
					DexcRuntimeUIBridge.AddDiagnostic(
						"input-editor-selection-failed",
						field.InputType.FullName,
						"The runtime input editor selection failed.",
						exception
					);
					return false;
				}
				selection = new DexcRuntimeUIState.RuntimeInputEditorSelection(descriptor, provider, error);
				DexcRuntimeUIState.InputEditorSelections[field.Id] = selection;
			}

			if (selection.Provider == null)
				return false;
			var current = DexcCommandRuntime.Drafts.GetInputValue(DexcRuntimeUIState.DraftId, field.Id);
			if (current == null)
				return false;
			var context = new DebugInputEditorContext(
				field.Id.ToString(),
				field.Label,
				string.Empty,
				current,
				false,
				null,
				false,
				null,
				null,
				false,
				selection.Descriptor
			);
			var edit = new DebugInputEdit();
			try
			{
				selection.Provider.OnGUI(context, edit);
			}
			catch (Exception exception)
			{
				DexcRuntimeUIBridge.AddDiagnostic(
					"input-editor-render-failed",
					field.Id.ToString(),
					"The runtime input editor failed to render.",
					exception
				);
				return false;
			}
			if (edit.HasValue)
			{
				UpdateCommandInput(field, edit.Value);
				if (!DexcRuntimeUIState.CommandInvalidInput.Contains(field.Id))
					DexcRuntimeUIState.CommandInputText[field.Id] = ValueText(edit.Value);
			}
			return true;
		}

#if UNITY_INCLUDE_TESTS
		internal static bool DrawRuntimeInputEditorForTests(DebugCommandFieldDescriptor field)
		{
			return TryDrawRuntimeInputEditor(field);
		}
#endif

		internal static string GetCommandInputText(DebugCommandFieldDescriptor field, string fallback)
		{
			if (!DexcRuntimeUIState.CommandInputText.TryGetValue(field.Id, out var value))
			{
				value = fallback;
				DexcRuntimeUIState.CommandInputText[field.Id] = value;
			}
			return value;
		}

		internal static void UpdateCommandInputText(DebugCommandFieldDescriptor field, string text)
		{
			DexcRuntimeCommandExecution.CancelConfirmation();
			if (!DexcRuntimeUIState.CommandInputText.TryGetValue(field.Id, out var previous) || previous != text)
				DexcRuntimeCommandForm.MarkEdited();
			DexcRuntimeUIState.CommandInputText[field.Id] = text;
			var parsed = ParseSchemaValue(field.Schema, text);
			if (parsed == null)
			{
				DexcRuntimeUIState.CommandInvalidInput.Add(field.Id);
				// The fixed field error slot owns input errors; do not expand the form with a duplicate notice.
				return;
			}
			UpdateCommandInput(field, parsed);
		}

		internal static void UpdateCommandInput(DebugCommandFieldDescriptor field, DebugValue value)
		{
			DexcRuntimeCommandForm.MarkEdited();
			DexcRuntimeCommandExecution.CancelConfirmation();
			var before = DexcRuntimeCommandForm.CaptureValues();
			var result = DebugCommand.Draft.UpdateInput(DexcRuntimeUIState.DraftId, field.Id, value);
			if (result.Status == DebugCommandDraftOperationStatus.Succeeded)
			{
				DexcRuntimeCommandForm.InvalidateChangedValues(before, field.Id);
				if (!Equals(value, DexcCommandRuntime.Drafts.GetInputValue(DexcRuntimeUIState.DraftId, field.Id)))
				{
					DexcRuntimeUIState.CommandInputText.Remove(field.Id);
					DexcRuntimeCommandFormComponents.Invalidate(field);
				}
			}
			if (result.Status != DebugCommandDraftOperationStatus.Succeeded)
			{
				DexcRuntimeUIState.CommandInvalidInput.Add(field.Id);
				// Rejected input remains in the fixed field error slot.
			}
			else if (DexcRuntimeUIState.CommandInvalidInput.Remove(field.Id) && DexcRuntimeUIState.CommandInvalidInput.Count == 0)
			{
				DexcRuntimeUIState.Notice = string.Empty;
			}
		}

		internal static void ExecuteCommand() => DexcRuntimeCommandExecution.Request();

#if UNITY_INCLUDE_TESTS
		internal static void ExecuteCommandForTests()
		{
			ExecuteCommand();
		}
#endif

		private static void DrawSettingList()
		{
			DrawRows(DexcRuntimeSettingForm.ListRows());
		}

		private static void SelectSetting(DebugSettingListItem item) => DexcRuntimeSettingForm.Open(item);

#if UNITY_INCLUDE_TESTS
		internal static void SelectSettingForTests(DebugSettingListItem item) => SelectSetting(item);
#endif

		private static void DrawSettingDetail()
		{
			DexcRuntimeSettingForm.Tick();
			DrawRows(DetailPageRows());
		}

		/// <summary>Draws a page's rows. IMGUI redraws every frame, so it reads each row's getters as it goes.</summary>
		private static void DrawRows(IReadOnlyList<DexcUIRow> rows)
		{
			var styles = EnsureStyleCache();
			// Counted once per draw: counting reads the page again, which is too much to repeat for every row.
			var navigationCount = ProviderSelectionCount();
			var listRects = Event.current.type == EventType.Repaint && rows.Any(row => row.ListItem) ? new List<Rect>() : null;
			var mainRects = listRects == null ? null : new List<Rect>();
			var detailRects = listRects == null ? null : new List<Rect>();
			var arrangement = DexcRuntimeFormLayout.Arrange(FormContentWidth, null);
			for (var index = 0; index < rows.Count; index++)
			{
				var row = rows[index];
				// A read-only value row carries its label in its text; a project's own editor stands in for it as it is.
				if (row.Kind == DexcUIRowKind.Label && row.Immediate != null && row.Immediate())
				{
					SkipGroup(rows, ref index);
					GUILayout.Space(DexcRuntimeFormLayout.RowGap);
					continue;
				}

				switch (row.Kind)
				{
					case DexcUIRowKind.Header:
						if (index > 0)
							GUILayout.Space(DexcRuntimeFormLayout.SectionGap);
						GUILayout.Label(row.Label, styles.Section);
						GUILayout.Space(DexcRuntimeFormLayout.RowGap);
						break;
					case DexcUIRowKind.Label:
						DrawRowLabel(row);
						// Lines of text keep the same gap as every other row, as they do in UGUI and UI Toolkit.
						GUILayout.Space(DexcRuntimeFormLayout.RowGap);
						break;
					case DexcUIRowKind.Button:
						if (row.ListItem)
						{
							DrawListRow(row, IsRowFocused(row, navigationCount), mainRects, detailRects);
							KeepListRowInView(row, listRects, navigationCount);
							GUILayout.Space(DexcRuntimeFormLayout.RowGap);
							break;
						}
						using (new GUIEnabledScope(row.IsEnabled))
						{
							// An action inside a page, such as Next fields: a secondary button, outlined while the gamepad is on it.
							var style = IsRowFocused(row, navigationCount) ? styles.SelectedRowButton : styles.SecondaryButton;
							if (GUILayout.Button(row.Label, style, GUILayout.MinHeight(ListRowHeight)))
								row.Activated();
						}
						GUILayout.Space(DexcRuntimeFormLayout.RowGap);
						break;
					case DexcUIRowKind.Input:
					case DexcUIRowKind.Choice:
						// The components after a vector's first row line up under its value, so they keep its arrangement.
						if (row.Component == null || !string.IsNullOrEmpty(row.Label))
							arrangement = ArrangeField(row);
						var slider = index + 1 < rows.Count && rows[index + 1].Kind == DexcUIRowKind.Slider ? rows[index + 1] : null;
						if (DrawFieldRow(row, slider, arrangement, IsRowFocused(row, navigationCount)))
							SkipGroup(rows, ref index);
						else if (slider != null)
							index++;
						var componentFollows =
							index + 1 < rows.Count && rows[index + 1].Component != null && string.IsNullOrEmpty(rows[index + 1].Label);
						GUILayout.Space(componentFollows ? 0f : DexcRuntimeFormLayout.RowGap);
						break;
					case DexcUIRowKind.Slider:
						DrawRowSlider(row);
						GUILayout.Space(DexcRuntimeFormLayout.RowGap);
						break;
				}
			}

			if (listRects != null)
			{
#if UNITY_INCLUDE_TESTS
				ListRowRectsForTests = listRects.ToArray();
				ListRowScreenRectsForTests = listRects.Select(rect => ListViewportScreenRect(rect, styles.Panel)).ToArray();
				ListMainRectsForTests = mainRects.ToArray();
				ListMainScreenRectsForTests = mainRects.Select(rect => ListViewportScreenRect(rect, styles.Panel)).ToArray();
				ListDetailRectsForTests = detailRects.ToArray();
				ListDetailScreenRectsForTests = detailRects.Select(rect => ListViewportScreenRect(rect, styles.Panel)).ToArray();
				ListRowsFullyVisibleForTests = listRects
					.Select(rect =>
						rect.yMin >= DexcRuntimeUIState.ScrollPosition.y - 0.1f
						&& rect.yMax <= DexcRuntimeUIState.ScrollPosition.y + ListViewportRect.height + 0.1f
					)
					.ToArray();
#endif
			}

			if (FocusedRow != null && Event.current.type != EventType.Layout && GUI.GetNameOfFocusedControl() != SettingFocusName)
			{
				ConfirmRow(FocusedRow);
				FocusedRow = null;
			}
		}

		/// <summary>Label beside the value, or above it when the row is narrow or the label wraps too far.</summary>
		private static DexcRuntimeFormLayout.Arrangement ArrangeField(DexcUIRow row)
		{
			var styles = EnsureStyleCache();
			var width = Mathf.Max(1f, FormContentWidth - styles.FieldRow.padding.horizontal);
			if (string.IsNullOrEmpty(row.Label))
				return DexcRuntimeFormLayout.Arrange(width, null);
			var line = Mathf.Max(1f, styles.FormLabel.lineHeight);
			return DexcRuntimeFormLayout.Arrange(
				width,
				labelWidth => Mathf.RoundToInt(styles.FormLabel.CalcHeight(new GUIContent(BreakLabel(row.Label, labelWidth)), labelWidth) / line)
			);
		}

		/// <summary>A label broken between its words to fit a column of <paramref name="width"/>.</summary>
		private static string BreakLabel(string label, float width)
		{
			var styles = EnsureStyleCache();
			return DexcRuntimeFormLayout.BreakLabel(label, width, line => styles.FormLabel.CalcSize(new GUIContent(line)).x);
		}

		/// <summary>Skips the rest of the field <paramref name="index"/> starts, after a project's own editor drew it.</summary>
		private static void SkipGroup(IReadOnlyList<DexcUIRow> rows, ref int index)
		{
			var group = rows[index].Group;
			while (index + 1 < rows.Count && group != null && rows[index + 1].Group == group)
				index++;
		}

		/// <summary>
		/// One field: its label and note, its value with any "…" action, its slider, its error and its description. Returns
		/// true when a project's own editor drew the value and stands in for the field's other rows.
		/// </summary>
		private static bool DrawFieldRow(DexcUIRow row, DexcUIRow slider, DexcRuntimeFormLayout.Arrangement arrangement, bool focused)
		{
			var styles = EnsureStyleCache();
			GUILayout.BeginVertical(focused ? styles.FocusedFieldRow : styles.FieldRow);
			var hasLabel = !string.IsNullOrEmpty(row.Label);
			var custom = false;
			if (arrangement.Stacked)
			{
				if (hasLabel)
				{
					DrawFieldLabel(row, row.Label);
					GUILayout.Space(DexcRuntimeFormLayout.LabelGap);
				}
				// The value keeps to the row's width, so a switch or "…" at its right end stays in view.
				GUILayout.BeginVertical(GUILayout.Width(arrangement.ValueWidth));
				custom = row.Immediate != null && row.Immediate();
				if (!custom)
				{
					DrawRowValue(row);
					if (slider != null)
						DrawRowSlider(slider);
				}
				GUILayout.EndVertical();
			}
			else
			{
				GUILayout.BeginHorizontal();
				var labelBlock = 0f;
				if (hasLabel)
				{
					var text = BreakLabel(row.Label, arrangement.LabelWidth);
					GUILayout.BeginVertical(GUILayout.Width(arrangement.LabelWidth));
					// A one-line label sits level with the value beside it.
					var top = Mathf.Max(0f, (ListRowHeight - styles.FormLabel.lineHeight) * 0.5f - 2f);
					GUILayout.Space(top);
					DrawFieldLabel(row, text);
					GUILayout.EndVertical();
					labelBlock = top + styles.FormLabel.CalcHeight(new GUIContent(text), arrangement.LabelWidth)
						+ (string.IsNullOrEmpty(row.Note) ? 0f : styles.Note.CalcHeight(new GUIContent(row.Note), arrangement.LabelWidth));
				}
				else
					GUILayout.Space(arrangement.LabelWidth);
				GUILayout.Space(DexcRuntimeFormLayout.ColumnGap);
				GUILayout.BeginVertical(GUILayout.Width(arrangement.ValueWidth));
				// Beside a label that wraps, the value is centred on the label.
				GUILayout.Space(Mathf.Max(0f, (labelBlock - ListRowHeight) * 0.5f));
				custom = row.Immediate != null && row.Immediate();
				if (!custom)
				{
					DrawRowValue(row);
					if (slider != null)
						DrawRowSlider(slider);
				}
				GUILayout.EndVertical();
				GUILayout.EndHorizontal();
			}

			var error = row.ErrorText;
			if (!string.IsNullOrEmpty(error))
			{
				GUILayout.BeginHorizontal();
				if (!arrangement.Stacked)
					GUILayout.Space(arrangement.ValueX);
				GUILayout.Label(error, styles.FieldError, GUILayout.Width(arrangement.ValueWidth));
				GUILayout.EndHorizontal();
			}
			GUILayout.EndVertical();
			return custom;
		}

		private static void DrawFieldLabel(DexcUIRow row, string text)
		{
			var styles = EnsureStyleCache();
			GUILayout.Label(text, styles.FormLabel);
			if (!string.IsNullOrEmpty(row.Note))
				GUILayout.Label(row.Note.ToUpperInvariant(), styles.Note);
		}

		/// <summary>
		/// A list entry: a mark, the label and a second line on the left, a count or result and a chevron on the right. The
		/// whole row is the button.
		/// </summary>
		private static void DrawListRow(DexcUIRow row, bool focused, List<Rect> mainRects, List<Rect> detailRects)
		{
			var styles = EnsureStyleCache();
			var style = focused ? styles.SelectedRowButton : styles.RowButton;
			var label = string.IsNullOrEmpty(row.Leading) ? row.Label : row.Leading + "  " + row.Label;
			var trailing = row.TrailingText;
			var trailingContent = new GUIContent(trailing ?? string.Empty);
			var trailingWidth = string.IsNullOrEmpty(trailing) ? 0f : styles.Trailing.CalcSize(trailingContent).x + 8f;
			var chevronWidth = row.Drill ? 14f : 0f;
			var textWidth = Mathf.Max(1f, FormContentWidth - style.padding.horizontal - trailingWidth - chevronWidth);
			// A long name such as a type's full name wraps at its dots, not part way through a word.
			var detail = DexcRuntimeFormLayout.BreakLines(row.DetailText, textWidth, line => styles.ListDetail.CalcSize(new GUIContent(line)).x);
			var mainContent = new GUIContent(label);
			var mainHeight = styles.ListMain.CalcHeight(mainContent, textWidth);
			var detailHeight = string.IsNullOrEmpty(detail) ? 0f : styles.ListDetail.CalcHeight(new GUIContent(detail), textWidth);
			var height = Mathf.Max(ListRowHeight, mainHeight + detailHeight + style.padding.vertical);
			var rect = GUILayoutUtility.GetRect(FormContentWidth, height, GUILayout.ExpandWidth(true));
			using (new GUIEnabledScope(row.IsEnabled))
			{
				if (GUI.Button(rect, GUIContent.none, style))
				{
					row.Activated();
					return;
				}
				var inner = new Rect(
					rect.x + style.padding.left,
					rect.y + Mathf.Max(style.padding.top, (rect.height - mainHeight - detailHeight) * 0.5f),
					textWidth,
					mainHeight
				);
				var tint = GUI.color;
				if (row.Tone != DexcUIRowTone.Normal)
					GUI.color = styles.Palette.Warning;
				GUI.Label(inner, mainContent, styles.ListMain);
				GUI.color = tint;
				mainRects?.Add(inner);
				if (detailHeight > 0f)
				{
					var detailRect = new Rect(inner.x, inner.yMax, textWidth, detailHeight);
					GUI.Label(detailRect, detail, styles.ListDetail);
					detailRects?.Add(detailRect);
				}
				var right = rect.xMax - style.padding.right;
				if (row.Drill)
				{
					GUI.Label(new Rect(right - chevronWidth, rect.y, chevronWidth, rect.height), "›", styles.Trailing);
					right -= chevronWidth;
				}
				if (trailingWidth > 0f)
				{
					var color = GUI.color;
					if (row.TrailingTone != DexcUIRowTone.Normal)
						GUI.color = styles.Palette.Warning;
					GUI.Label(new Rect(right - trailingWidth, rect.y, trailingWidth, rect.height), trailingContent, styles.Trailing);
					GUI.color = color;
				}
			}
		}

		/// <summary>Scrolls to the list row the gamepad just moved to, once, and notes where the list rows were drawn.</summary>
		private static void KeepListRowInView(DexcUIRow row, List<Rect> listRects, int navigationCount)
		{
			if (Event.current.type != EventType.Repaint)
				return;
			var rect = GUILayoutUtility.GetLastRect();
			listRects?.Add(rect);
			if (DexcRuntimeUIState.ListSelectionVisibilityRequested && IsRowFocused(row, navigationCount))
			{
				GUI.ScrollTo(rect);
				DexcRuntimeUIState.ListSelectionVisibilityRequested = false;
			}
		}

		private static void DrawRowLabel(DexcUIRow row)
		{
			var styles = EnsureStyleCache();
			var error = row.ErrorText;
			if (row.Striped)
			{
				// A table's banded line is laid out like a plain one, so its text does not shift against the next line.
				var content = new GUIContent(row.Text == null ? row.Label : row.Text());
				var rect = GUILayoutUtility.GetRect(content, styles.SecondaryLabel);
				// The band reaches halfway into the gaps around it, so each band sits evenly between its plain neighbours.
				var gap = DexcRuntimeFormLayout.RowGap / 2f;
				if (Event.current.type == EventType.Repaint)
					styles.FieldRow.Draw(new Rect(rect.x, rect.y - gap, rect.width, rect.height + gap * 2f), false, false, false, false);
				GUI.Label(rect, content, styles.SecondaryLabel);
				return;
			}
			GUILayout.BeginHorizontal(GUIStyle.none);
			GUILayout.Label(
				row.Text == null ? row.Label : row.Text(),
				row.Tone == DexcUIRowTone.Normal ? styles.SecondaryLabel : styles.WarningLabel
			);
			if (!string.IsNullOrEmpty(error))
				DrawFormError(error);
			GUILayout.EndHorizontal();
#if UNITY_INCLUDE_TESTS
			if (row.Key == "command:description")
				DescriptionScreenRectForTests = LastGUILayoutScreenRect();
#endif
		}

		private static bool IsRowFocused(DexcUIRow row, int navigationCount)
		{
			return row.NavigationIndex >= 0 && IsNavigationFocused(row.NavigationIndex, navigationCount);
		}

		private static void ConfirmRow(DexcUIRow row)
		{
			row.Confirmed?.Invoke(row.Text());
		}

		private static void DrawRowSlider(DexcUIRow row)
		{
			var before = row.SliderValue();
			GUILayout.BeginHorizontal();
			float value;
			using (new GUIEnabledScope(row.IsEnabled))
			{
				value = DrawFormSlider(before);
			}

			GUILayout.EndHorizontal();
			if (value != before)
			{
				row.Slid(value);
			}
		}

		private static float DrawFormSlider(float value)
		{
			var styles = EnsureStyleCache();
			var rect = GUILayoutUtility.GetRect(1, ListRowHeight, GUILayout.ExpandWidth(true), GUILayout.Height(ListRowHeight));
			styles.SliderTrack.padding.top = Mathf.FloorToInt((ListRowHeight - 12) / 2);
			styles.SliderTrack.padding.bottom = Mathf.CeilToInt((ListRowHeight - 12) / 2);
			if (Event.current.type == EventType.Repaint)
			{
				var color = GUI.color;
				GUI.color = styles.Palette.Row;
				GUI.DrawTexture(new Rect(rect.x + 6, rect.center.y - 2, Mathf.Max(0, rect.width - 12), 4), Texture2D.whiteTexture);
				GUI.color = color;
			}

			return GUI.HorizontalSlider(rect, value, 0f, 1f, styles.SliderTrack, styles.SliderThumb);
		}

		/// <summary>A choice's value without the chevron some forms still put in the text; the renderer draws its own.</summary>
		internal static string ChoiceText(string text)
		{
			text = text ?? string.Empty;
			return text.EndsWith("  ›", StringComparison.Ordinal) ? text.Substring(0, text.Length - 3) : text;
		}

		/// <summary>
		/// A choice's value: a switch for a boolean, ‹ value › for one that steps in place, or the value with a chevron for
		/// one whose candidates open on their own page. Pressing the value itself does what pressing the row always did.
		/// </summary>
		private static void DrawChoiceValue(DexcUIRow row)
		{
			var styles = EnsureStyleCache();
			var text = ChoiceText(row.Text());
			if (row.Boolean)
			{
				var rect = GUILayoutUtility.GetRect(0f, ListRowHeight, GUILayout.ExpandWidth(true), GUILayout.Height(ListRowHeight));
				if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
					row.Activated();
				DrawSwitch(rect, string.Equals(text, "true", StringComparison.OrdinalIgnoreCase), GUI.enabled);
				return;
			}
			if (row.Cycle != null)
			{
				GUILayout.BeginHorizontal();
				if (GUILayout.Button("‹", styles.CycleButton, GUILayout.Width(ListRowHeight), GUILayout.Height(ListRowHeight)))
					row.Cycle(-1);
				if (GUILayout.Button(text, styles.CycleValue, GUILayout.Height(ListRowHeight), GUILayout.ExpandWidth(true), GUILayout.MinWidth(0f)))
					row.Activated();
				if (GUILayout.Button("›", styles.CycleButton, GUILayout.Width(ListRowHeight), GUILayout.Height(ListRowHeight)))
					row.Cycle(1);
				GUILayout.EndHorizontal();
				return;
			}
			var field = GUILayoutUtility.GetRect(0f, ListRowHeight, GUILayout.ExpandWidth(true), GUILayout.Height(ListRowHeight));
			if (GUI.Button(field, text, styles.TextField))
				row.Activated();
			GUI.Label(new Rect(field.xMax - 20f, field.y, 14f, field.height), "›", styles.Trailing);
		}

		/// <summary>An on/off switch at the right end of <paramref name="area"/>: a track with the knob at the side it is on.</summary>
		internal static void DrawSwitch(Rect area, bool on, bool enabled)
		{
			if (Event.current.type != EventType.Repaint)
				return;
			var styles = EnsureStyleCache();
			var height = Mathf.Min(22f, area.height - 4f);
			var track = new Rect(area.xMax - height * 1.9f - 2f, area.center.y - height * 0.5f, height * 1.9f, height);
			var knob = new Rect(on ? track.xMax - height + 2f : track.x + 2f, track.y + 2f, height - 4f, height - 4f);
			var color = GUI.color;
			var tint = enabled ? 1f : 0.5f;
			GUI.color = (on ? styles.Palette.Accent : styles.Palette.Row) * new Color(1f, 1f, 1f, tint);
			GUI.DrawTexture(track, Texture2D.whiteTexture);
			GUI.color = (on ? styles.Palette.PrimaryText : styles.Palette.SecondaryText) * new Color(1f, 1f, 1f, tint);
			GUI.DrawTexture(knob, Texture2D.whiteTexture);
			GUI.color = color;
		}

		/// <summary>A field's value: the component's name, the control, and the "…" action when there is one.</summary>
		private static void DrawRowValue(DexcUIRow row)
		{
			var styles = EnsureStyleCache();
			GUILayout.BeginHorizontal();
			if (row.Component != null)
			{
				GUILayout.Label(
					row.Component.ToUpperInvariant(),
					styles.ComponentLabel,
					GUILayout.Width(DexcRuntimeFormLayout.ComponentWidth),
					GUILayout.Height(ListRowHeight)
				);
				GUILayout.Space(DexcRuntimeFormLayout.ColumnGap);
			}

			var key = row.Key;
			GUI.SetNextControlName(key);
			using (new GUIEnabledScope(row.IsEnabled))
			{
				if (row.Kind == DexcUIRowKind.Choice)
				{
					DrawChoiceValue(row);
				}
				else if (!row.IsEnabled && !row.Multiline)
				{
					// A value that cannot be edited reads as text, not as a field that looks broken.
					GUILayout.Label(row.Text() ?? string.Empty, styles.SecondaryLabel, GUILayout.Height(ListRowHeight), GUILayout.MinWidth(0f));
				}
				else
				{
					var raw = row.Text();
#if UNITY_INCLUDE_TESTS
					if (row.Tag is DebugCommandFieldId && row.Component == null)
						CommandInputTextForTests = raw;
#endif
					var field = string.IsNullOrEmpty(row.ErrorText) ? styles.TextField : styles.ErrorTextField;
					// A long value is cut at the field's edge; asking for its whole width would push the page past the panel.
					var changed = row.Multiline
						? GUILayout.TextArea(raw ?? string.Empty, styles.MultilineTextField, GUILayout.Height(ListRowHeight * 5f), GUILayout.MinWidth(0f))
						: GUILayout.TextField(raw ?? string.Empty, field, GUILayout.Height(ListRowHeight), GUILayout.MinWidth(0f));
					if (Event.current.type == EventType.Repaint)
					{
#if UNITY_INCLUDE_TESTS
						RecordValueControlForTests(key, GUILayoutUtility.GetLastRect());
#endif
						// Submit on a text row asks for its field to take the keyboard; that happens on the next repaint.
						if (PendingPanelTextFocus == key)
						{
							GUI.FocusControl(key);
							PendingPanelTextFocus = null;
						}
					}

					if (changed != raw)
					{
						row.Changed(changed);
					}

					if (GUI.GetNameOfFocusedControl() == key)
					{
						if (FocusedRow != null && SettingFocusName != key)
						{
							ConfirmRow(FocusedRow);
						}

						FocusedRow = row;
						SettingFocusName = key;
						if (
							!row.Multiline
							&& Event.current.type == EventType.KeyDown
							&& (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
						)
						{
							// Enter applies the text and leaves the field, like Submit does on the gamepad.
							ConfirmRow(row);
							FocusedRow = null;
							GUI.FocusControl(null);
						}
					}
				}
			}

			// The error is written under the row, so the "…" action keeps its place whether or not the value is valid.
			if (row.Auxiliary != null)
			{
				if (
					GUILayout.Button(
						"…",
						styles.SecondaryButton,
						GUILayout.Width(DexcRuntimeFormLayout.ErrorWidth),
						GUILayout.Height(ListRowHeight)
					)
				)
				{
					row.Auxiliary();
				}
			}

			GUILayout.EndHorizontal();
		}

		private static void ApplySetting() => DexcRuntimeSettingForm.Apply();

		internal static void ApplySettingForRenderer() => ApplySetting();

		internal sealed class SettingInputBinding
		{
			readonly DexcRuntimeSettingForm.Field m_Field;
			readonly string m_Component;
			readonly int m_Version;
			bool m_Active = true;
			string m_Text;

			internal SettingInputBinding(DebugSettingListItem item, string component = null)
			{
				m_Field = DexcRuntimeSettingForm.Fields.FirstOrDefault(field => field.Item.Id == item.Id);
				m_Component = component;
				m_Version = DexcRuntimeUIState.SettingBindingVersion;
				m_Text = m_Field == null ? "" : DexcRuntimeSettingForm.Text(m_Field, component);
			}

			bool IsCurrent =>
				m_Active
				&& m_Field != null
				&& DexcRuntimeSettingForm.IsCurrent(m_Field)
				&& m_Version == DexcRuntimeUIState.SettingBindingVersion
				&& DexcRuntimeUIState.Page == DexcRuntimeUIPage.SettingDetail;

			internal void Change(string text)
			{
				if (!IsCurrent)
					return;
				m_Text = text;
				DexcRuntimeSettingForm.Change(m_Field, text, m_Component);
			}

			internal bool Confirm(string text) => IsCurrent && m_Text == text && DexcRuntimeSettingForm.Confirm(m_Field);

			internal void Invalidate() => m_Active = false;
		}

		internal static void SetSettingEditTextForRenderer(string text)
		{
			var field = DexcRuntimeSettingForm.Anchor;
			if (field != null)
				DexcRuntimeSettingForm.Change(field, text);
		}

#if UNITY_INCLUDE_TESTS
		internal static void ApplyPresetForTests(DebugSettingPresetItem preset) => DexcRuntimeSettingForm.ApplyPreset(preset);
#endif

		private static void DrawPanelList()
		{
			DrawRows(DexcRuntimePanelForm.ListRows());
		}

		private static void DrawPanelDetail()
		{
			if (DexcRuntimePanelForm.Current == null)
			{
				DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.PanelList);
				return;
			}
			DrawRows(DexcRuntimePanelForm.DetailRows());
		}

		internal static string PanelTextKey(DebugWidget widget)
		{
			return DexcRuntimePanelForm.Key(widget);
		}

		private static void DrawInspectorHome()
		{
			DrawRows(DexcUIRows.InspectorHomeRows());
		}

		private static void DrawInspectorRegistered() => DrawInspectorList();

		private static void DrawInspectorScene() => DrawInspectorList();

		private static void DrawInspectorComponents() => DrawInspectorList();

		private static void DrawInspectorList()
		{
			DrawRows(DexcRuntimeInspectorNavigation.ListRows());
		}

		private static void DrawInspectorFields()
		{
			DrawRows(DetailPageRows());
		}

		private static bool TryGetInspectorComponents(out DebugInspectorComponentPage page)
		{
			page = DebugInspector.GetComponents(DexcRuntimeUIState.InspectorTarget);
			if (page != null && page.Entries.Count != 0)
				return true;
			DexcRuntimeUIState.ReturnFromStaleInspector("The selected inspector target is no longer available.");
			return false;
		}

		private static bool TryGetInspectorFields(
			out DebugInspectorFieldPage fields,
			out DebugResult<IReadOnlyList<DebugInspectorFieldValue>> values
		)
		{
			fields = DexcRuntimeUIState.InspectorComponent.GetFields(DexcRuntimeUIState.CreatePageRequest(0, DexcRuntimeUIState.PageSize));
			values = DexcRuntimeUIState.InspectorComponent.ReadFields(
				fields == null ? null : fields.Fields.Select(item => item.Id).ToList()
			);
			if (values == null || values.Status == DebugOperationStatus.Stale)
			{
				DexcRuntimeUIState.ReturnFromStaleInspector("The selected inspector component is no longer available.");
				return false;
			}
			return true;
		}

#if UNITY_INCLUDE_TESTS
		internal static void RefreshInspectorDetailForTests()
		{
			try
			{
				if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.InspectorComponents)
					TryGetInspectorComponents(out var components);
				else if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.InspectorFields)
					TryGetInspectorFields(out var fields, out var values);
			}
			catch (Exception exception)
			{
				DexcRuntimeUIState.Notice = exception.Message;
			}
		}
#endif

		private static void DrawInspectorWatches() => DrawInspectorList();

		private static void DrawHistory() => DrawInspectorList();

		private static void DrawDiagnostic() => DrawInspectorList();

		private static Rect ListViewportScreenRect(Rect viewport, GUIStyle panel)
		{
			var x = ContentAreaPhysicalRect.x + (panel.padding.left + viewport.x) * DrawScale;
			var top = ContentAreaPhysicalRect.y + (panel.padding.top + viewport.y - DexcRuntimeUIState.ScrollPosition.y) * DrawScale;
			var width = viewport.width * DrawScale;
			var height = viewport.height * DrawScale;
			return new Rect(x, Screen.height - top - height, width, height);
		}

		internal enum RuntimeValueEditorKind
		{
			Unsupported,
			Text,
			Toggle,
			Number,
			Slider,
			Enum,
			Color,
			Vector,
		}

#if UNITY_INCLUDE_TESTS
		internal static RuntimeValueEditorKind GetSettingEditorKindForTests(DebugSettingListItem item)
		{
			if (item == null || !DebugSetting.TryGetDescriptor(item.Id, out var descriptor))
				return RuntimeValueEditorKind.Unsupported;
			return GetEditorKind(descriptor.ValueType, descriptor.DefaultValue, descriptor.IsSlider);
		}

		internal static RuntimeValueEditorKind GetInspectorEditorKindForTests(DebugInspectorFieldDescriptor field, DebugValue value)
		{
			return field == null ? RuntimeValueEditorKind.Unsupported : GetEditorKind(field.TypeName, value, false);
		}
#endif

		private static RuntimeValueEditorKind GetEditorKind(string typeName, DebugValue value, bool slider)
		{
			var type = ResolveType(typeName);
			if (type == typeof(bool))
				return RuntimeValueEditorKind.Toggle;
			if (type == typeof(string))
				return RuntimeValueEditorKind.Text;
			if (type != null && type.IsEnum)
				return RuntimeValueEditorKind.Enum;
			if (type == typeof(Color))
				return RuntimeValueEditorKind.Color;
			if (
				type == typeof(Vector2)
				|| type == typeof(Vector3)
				|| type == typeof(Vector4)
				|| type == typeof(Vector2Int)
				|| type == typeof(Vector3Int)
			)
				return RuntimeValueEditorKind.Vector;
			if (
				IsNumericType(type)
				|| value != null
					&& (
						value.Kind == DebugValueKind.SignedInteger
						|| value.Kind == DebugValueKind.UnsignedInteger
						|| value.Kind == DebugValueKind.FloatingPoint
					)
			)
				return slider ? RuntimeValueEditorKind.Slider : RuntimeValueEditorKind.Number;
			return RuntimeValueEditorKind.Unsupported;
		}

		private static void DrawFormError(string message)
		{
			GUILayout.Space(DexcRuntimeFormLayout.ColumnGap);
			var rect = GUILayoutUtility.GetRect(
				DexcRuntimeFormLayout.ErrorWidth,
				ListRowHeight,
				GUILayout.Width(DexcRuntimeFormLayout.ErrorWidth),
				GUILayout.Height(ListRowHeight)
			);
			var focus = GUI.GetNameOfFocusedControl();
			if (
				!string.IsNullOrEmpty(message)
				&& GUI.Button(
					rect,
					"!",
					DexcRuntimeCommandForm.IsWarningText(message) ? EnsureStyleCache().WarningButton : EnsureStyleCache().ErrorButton
				)
			)
			{
				DexcRuntimeFormDetail.Open(message, focus);
			}
		}

#if UNITY_INCLUDE_TESTS

		private static void RecordValueControlForTests(string key, Rect rect)
		{
			ValueControlDrawnForTests?.Invoke(key, rect);
			ValueControlScreenRectForTests?.Invoke(key, ListViewportScreenRect(rect, EnsureStyleCache().Panel));
		}
#endif

		private static bool IsNumericType(Type type)
		{
			return type == typeof(sbyte)
				|| type == typeof(byte)
				|| type == typeof(short)
				|| type == typeof(ushort)
				|| type == typeof(int)
				|| type == typeof(uint)
				|| type == typeof(long)
				|| type == typeof(ulong)
				|| type == typeof(float)
				|| type == typeof(double);
		}

		internal static Type ResolveType(string typeName) => DexcDefinitions.ResolveType(typeName);

		private static DebugValue GetValue(DebugResult<DebugValue> result)
		{
			DebugValue value;
			return result != null && result.TryGetValue(out value) ? value : null;
		}

		internal static string ValueText(DebugValue value) => DexcInputText.Format(value);

		private static string SnapshotText(InspectorValueSnapshot snapshot)
		{
			return snapshot == null ? ""
				: snapshot.State == DebugInspectorSnapshotState.Value ? ValueText(snapshot.Value)
				: snapshot.State + (string.IsNullOrEmpty(snapshot.Error) ? string.Empty : ": " + snapshot.Error);
		}

		internal static DebugValue ParseSchemaValue(DebugInputSchema schema, string text) => DexcInputText.Parse(schema, text);

#if UNITY_INCLUDE_TESTS
		internal static DebugDisplayFrameSnapshot ReadDisplayFrameForTests() => DexcDisplayOverlay.ReadFrameForTests();

		internal static int RenderDisplayOverlayForTests() => DexcDisplayOverlay.RenderForTests();
#endif

#if UNITY_INCLUDE_TESTS
		internal static IReadOnlyList<DebugDisplayPrimitiveKind> RenderWorldForTests()
		{
			var renderedKinds = DexcPlayerRenderer.GetWorldPrimitiveKindsForTests();
			WorldVisualDrawCountForTests = renderedKinds.Count;
			return renderedKinds;
		}

		internal static void ResetRenderCountersForTests()
		{
			WorldVisualDrawCountForTests = 0;
			MenuDrawCountForTests = 0;
		}
#endif

		private readonly struct GUIEnabledScope : IDisposable
		{
			private readonly bool m_Previous;

			internal GUIEnabledScope(bool enabled)
			{
				m_Previous = GUI.enabled;
				GUI.enabled = enabled;
			}

			public void Dispose()
			{
				GUI.enabled = m_Previous;
			}
		}
	}
}
