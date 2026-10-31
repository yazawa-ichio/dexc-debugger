#if DEXC_HAS_UGUI
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

namespace Dexc
{
	internal readonly struct DexcRuntimeUGUILayout
	{
		internal DexcRuntimeUGUILayout(
			Rect screen,
			Rect safeArea,
			Rect panel,
			Rect header,
			Rect content,
			Rect footer,
			float rowHeight,
			float hitHeight,
			DexcRuntimeUIProfileKind profile,
			Vector2 referenceResolution,
			float scale
		)
		{
			Screen = screen;
			SafeArea = safeArea;
			Panel = panel;
			Header = header;
			Content = content;
			Footer = footer;
			RowHeight = rowHeight;
			HitHeight = hitHeight;
			Profile = profile;
			ReferenceResolution = referenceResolution;
			Scale = scale;
		}

		internal Rect Screen { get; }
		internal Rect SafeArea { get; }
		internal Rect Panel { get; }
		internal Rect Header { get; }
		internal Rect Content { get; }
		internal Rect Footer { get; }
		internal float RowHeight { get; }
		internal float HitHeight { get; }
		internal DexcRuntimeUIProfileKind Profile { get; }
		internal Vector2 ReferenceResolution { get; }
		internal float Scale { get; }
	}

	internal sealed class DexcRuntimeUGUIRenderer : MonoBehaviour
	{
		private const float HeaderHeight = 44f;
		private const float FooterHeight = 46f;
		private const int MaximumRows = DexcRuntimeUIState.PageSize;

		/// <summary>One line of a label, for counting how far a label wraps.</summary>
		private const float LabelLineHeight = DexcRuntimeFormLayout.FontSize + 2f;

		/// <summary>State of this class. Owned by <see cref="DexcUIRoot"/>; nothing here survives a root reset.</summary>
		internal sealed class State
		{
			internal DexcRuntimeUGUIRenderer Instance;
		}

		internal static State Data => DexcUIRoot.Current.UGUIRenderer;
		private static DexcRuntimeUGUIRenderer Instance
		{
			get => Data.Instance;
			set => Data.Instance = value;
		}

		private Canvas m_Canvas;
		private Component m_CanvasScaler;
		private RectTransform m_SafeAreaRoot;
		private RectTransform m_Panel;
		private RectTransform m_Header;
		private RectTransform m_ScrollView;
		private RectTransform m_Viewport;
		private RectTransform m_Content;
		private RectTransform m_Footer;
		private Component m_Title;
		private Component m_ScrollRect;
		private RowSlot m_BackButton;
		private RowSlot m_CloseButton;
		private RowSlot m_FavoriteButton;
		private RowSlot m_HistoryButton;
		private RowSlot m_SingleButton;
		private RowSlot m_SplitButton;
		private readonly List<RowSlot> m_Rows = new List<RowSlot>();
		private readonly List<RowSlot> m_FooterButtons = new List<RowSlot>();
		private readonly List<Component> m_Selectables = new List<Component>();
		private readonly List<Component> m_Texts = new List<Component>();
		private int m_LastSignature = int.MinValue;
		private int m_SettingVersion = -1;
		private int m_InspectorVersion = -1;
		private Vector2 m_FormReturnScroll;
		private DebugCommandDraftId m_FormReturnDraft;
		private int m_ProfileWidth;
		private int m_ProfileHeight;
		private Rect m_ProfileSafeArea;
		private bool m_UseProfileOverride;
		private DexcRuntimeUIProfileKind m_ProfileOverride;
		private DexcRuntimeUGUILayout m_Layout;
		private DexcRuntimeFormLayout.Arrangement m_Arrangement;

		// Side by side: the sections and the list beside a page, pointer only, and how much width they take from the page.
		private RectTransform m_SplitSections;
		private RectTransform m_SplitList;
		private readonly List<RowSlot> m_SectionSlots = new List<RowSlot>();
		private readonly List<RowSlot> m_ListSlots = new List<RowSlot>();
		private float m_SectionWidth;
		private float m_ListWidth;
		private int m_LastSelectionIndex = -1;
		private Component m_LastSelected;

		/// <summary>InputField whose activation was requested this frame; uGUI activates it in LateUpdate.</summary>
		private Component m_ActivatingInput;
		private DexcRuntimeUIPage m_SelectionPage;
		private DebugSessionId m_SelectionSession;
		private DebugCommandDraftId m_SelectionDraft;

		internal static bool IsActive => DexcRuntimeUIRendererSelection.UseUGUI && Instance != null;
#if UNITY_INCLUDE_TESTS
		internal static GameObject RootForTests => Instance == null ? null : Instance.gameObject;
		internal static RectTransform SafeAreaRootForTests => Instance == null ? null : Instance.m_SafeAreaRoot;
		internal static RectTransform HeaderForTests => Instance == null ? null : Instance.m_Header;
		internal static RectTransform ViewportForTests => Instance == null ? null : Instance.m_Viewport;
		internal static RectTransform ContentForTests => Instance == null ? null : Instance.m_Content;
		internal static RectTransform FooterForTests => Instance == null ? null : Instance.m_Footer;
		internal static Component ScrollRectForTests => Instance == null ? null : Instance.m_ScrollRect;
#endif
		internal static int VisibleRowCount => Instance == null ? 0 : Instance.m_Rows.Count(row => row.Root.gameObject.activeSelf);
		internal static int SelectableCount => Instance == null ? 0 : Instance.m_Selectables.Count;

#if UNITY_INCLUDE_TESTS
		/// <summary>Whether the footer shows an Execute button that can be pressed.</summary>
		internal static bool ExecuteInteractableForTests => Instance != null && Instance.ExecuteInteractable();

		private bool ExecuteInteractable()
		{
			if (m_FooterButtons.Count < 2)
				return false;
			var execute = m_FooterButtons[1];
			return execute.Root.gameObject.activeSelf
				&& Equals(DexcUGUIApi.Get(execute.Label, "text"), "Execute")
				&& Equals(DexcUGUIApi.Get(execute.Button, "interactable"), true);
		}

		internal static int RendererCountForTests => Resources.FindObjectsOfTypeAll<DexcRuntimeUGUIRenderer>().Length;
		internal static Rect PanelScreenRectForTests => ScreenRect(Instance == null ? null : Instance.m_Panel);
		internal static Rect HeaderScreenRectForTests => ScreenRect(Instance == null ? null : Instance.m_Header);
		internal static Rect ContentScreenRectForTests => ScreenRect(Instance == null ? null : Instance.m_ScrollView);
		internal static Rect FooterScreenRectForTests =>
			ScreenRect(Instance == null || !Instance.m_Footer.gameObject.activeInHierarchy ? null : Instance.m_Footer);
		internal static Rect CloseScreenRectForTests =>
			ScreenRect(Instance == null || Instance.m_CloseButton == null ? null : Instance.m_CloseButton.Root);
		internal static Rect CancelBoundsForTests
		{
			get
			{
				if (
					Instance == null
					|| Instance.m_FooterButtons.Count == 0
					|| !Instance.m_FooterButtons[0].Root.gameObject.activeInHierarchy
				)
					return default(Rect);
				return ScreenRect(Instance.m_FooterButtons[0].Root);
			}
		}
		internal static DexcRuntimeUGUILayout CurrentLayoutForTests =>
			Instance == null ? default(DexcRuntimeUGUILayout) : Instance.m_Layout;
		internal static bool HasHorizontalOverflowForTests =>
			Instance != null && Instance.m_Content.rect.width > Instance.m_Viewport.rect.width + 0.01f;
		internal static bool HasVerticalOverflowForTests =>
			Instance != null && Instance.m_Content.rect.height > Instance.m_Viewport.rect.height + 0.01f;
		internal static float VerticalNormalizedPositionForTests =>
			Instance == null ? 1f : Convert.ToSingle(DexcUGUIApi.Get(Instance.m_ScrollRect, "verticalNormalizedPosition"));
		internal static DexcRuntimeUISemanticPalette SemanticPaletteForTests =>
			DexcRuntimeUISemanticPalette.FromSnapshot(Instance == null ? null : Instance.Skin);
		internal static int FocusMarkerCountForTests =>
			Instance == null ? 0 : Instance.AllSlots.Count(slot => slot.FocusMarker.activeSelf);
		internal static int PrimaryActionIndexForTests =>
			Instance == null || Instance.m_FooterButtons.Count < 2
				? -1
				: Instance.m_Selectables.IndexOf(Instance.m_FooterButtons[1].Button);

		internal static Component SettingInputForTests(DebugSettingId id, string component = null) =>
			Instance
				?.m_Rows.FirstOrDefault(row =>
					row.Root.gameObject.activeSelf
					&& row.Source != null
					&& row.Source.Key == DexcRuntimeSettingForm.Key(id, component)
					&& row.InputRoot.activeSelf
				)
				?.Input;

		internal static Component SelectableForTests(int index)
		{
			return Instance == null || index < 0 || index >= Instance.m_Selectables.Count ? null : Instance.m_Selectables[index];
		}

		internal static Rect SelectableScreenRectForTests(int index)
		{
			var selectable = SelectableForTests(index);
			return ScreenRect(selectable == null ? null : selectable.transform as RectTransform);
		}

		internal static string RowTextForTests(int index)
		{
			if (Instance == null || index < 0 || index >= Instance.m_Rows.Count)
				return string.Empty;
			return Convert.ToString(DexcUGUIApi.Get(Instance.m_Rows[index].Label, "text"));
		}
#endif

		private DexcRuntimeUISkinSnapshot Skin =>
			DexcDebugger.CurrentSession == null ? null : DexcDebugger.CurrentSession.Options.RuntimeUISkin;
		private DexcRuntimeUISemanticPalette Palette => DexcRuntimeUISemanticPalette.FromSnapshot(Skin);
		private IEnumerable<RowSlot> AllSlots =>
			m_Rows.Concat(m_FooterButtons).Concat(new[] { m_BackButton, m_CloseButton, m_HistoryButton, m_FavoriteButton }).Where(slot => slot != null);
		private Color PanelColor => Palette.Panel;
		private Color HeaderColor => Palette.Header;
		private Color RowColor => Palette.Row;
		private Color AccentColor => Palette.Accent;
		private Color ErrorColor => Palette.Error;

		internal static void CreateForBootstrap()
		{
			if (Instance != null)
				return;
			var root = new GameObject("DexcRuntimeUGUI", typeof(RectTransform));
			// No hide flags: Play Mode owns the lifetime, so leaving it destroys this with everything else.
			DontDestroyOnLoad(root);
			Instance = root.AddComponent<DexcRuntimeUGUIRenderer>();
			Instance.BuildHierarchy();
		}

		internal static void TickActive()
		{
			if (Instance != null)
				Instance.Tick();
		}

		/// <summary>Destroys the renderer objects so a new root does not adopt the previous run's Canvas.</summary>
		internal static void DestroyInstance()
		{
			// The fallback EventSystem exists only for this renderer, and its Tick is what used to release it.
			DexcUGUIApi.ReleaseFallbackEventSystem();
			foreach (var instance in Resources.FindObjectsOfTypeAll<DexcRuntimeUGUIRenderer>())
			{
				if (instance == null)
					continue;
				if (Application.isPlaying)
					Destroy(instance.gameObject);
				else
					DestroyImmediate(instance.gameObject);
			}
			Instance = null;
		}

#if UNITY_INCLUDE_TESTS
		internal static void DestroyForTests() => DestroyInstance();

		internal static DexcRuntimeUGUILayout CalculateLayoutForTests(int width, int height, Rect safeArea, DexcRuntimeUIPage page)
		{
			return CalculateLayout(width, height, safeArea, page, DexcRuntimeUIReferenceResolver.LegacyProfileForTests(width, height));
		}
#endif

		internal static DexcRuntimeUGUILayout CalculateLayoutForProfileTests(
			int width,
			int height,
			Rect safeArea,
			DexcRuntimeUIPage page,
			DexcRuntimeUIProfileKind profile
		)
		{
			return CalculateLayout(width, height, safeArea, page, profile);
		}

#if UNITY_INCLUDE_TESTS
		internal static void RenderProfileForTests(int width, int height, Rect safeArea)
		{
			RenderProfileForTests(width, height, safeArea, DexcRuntimeUIReferenceResolver.LegacyProfileForTests(width, height));
		}

		internal static void RenderProfileForTests(int width, int height, Rect safeArea, DexcRuntimeUIProfileKind profile)
		{
			CreateForBootstrap();
			DexcRuntimeUIRendererSelection.SetUseUGUIForTests(true);
			Instance.m_ProfileWidth = width;
			Instance.m_ProfileHeight = height;
			Instance.m_ProfileSafeArea = safeArea;
			Instance.m_UseProfileOverride = true;
			Instance.m_ProfileOverride = profile;
			Instance.Rebuild(true);
			Instance.Tick();
		}

		internal static void ClearProfileOverrideForTests()
		{
			if (Instance == null)
				return;
			Instance.m_UseProfileOverride = false;
			Instance.m_LastSignature = int.MinValue;
		}

		internal static void ActivateSelectedForTests() => ActivateSelected();

		internal static void SelectForTests(int index)
		{
			if (Instance == null || Instance.m_Selectables.Count == 0)
				return;
			index = Mathf.Clamp(index, 0, Instance.m_Selectables.Count - 1);
			DexcRuntimeUIState.SetSelectedIndex(index);
			DexcUGUIApi.SetSelected(Instance.m_Selectables[index].gameObject);
		}

		internal static void RefreshForTests()
		{
			if (Instance != null)
				Instance.Rebuild(true);
		}
#endif

		/// <summary>Submit on the current selection: invokes buttons, enters editing on input fields.</summary>
		internal static void ActivateSelected()
		{
			if (Instance == null || Instance.m_Selectables.Count == 0)
				return;
			var index = Mathf.Clamp(DexcRuntimeUIState.SelectedIndex, 0, Instance.m_Selectables.Count - 1);
			var selectable = Instance.m_Selectables[index];
			var slot = Instance.AllSlots.FirstOrDefault(item => selectable == item.Button || selectable == item.Input);
			if (selectable == slot?.Button && slot.ButtonAction != null)
				slot.ButtonAction.Invoke();
			else
			{
				if (DexcUGUIApi.InputFieldType.IsInstanceOfType(selectable))
					Instance.m_ActivatingInput = selectable;
				DexcUGUIApi.Activate(selectable);
			}
		}

		/// <summary>Left or right on a choice that steps in place. False when the selection is not on such a choice.</summary>
		internal static bool CycleSelected(int direction)
		{
			if (Instance == null || Instance.m_Selectables.Count == 0)
				return false;
			var selectable = Instance.m_Selectables[Mathf.Clamp(DexcRuntimeUIState.SelectedIndex, 0, Instance.m_Selectables.Count - 1)];
			var slot = Instance.AllSlots.FirstOrDefault(item => item.Choice != null && selectable == item.Choice);
			var row = slot?.Source;
			if (row?.Cycle == null || !row.IsEnabled || !row.Cycle(direction))
				return false;
			Instance.RefreshRows();
			return true;
		}

		/// <summary>True while the selected InputField is being edited (or its activation is pending this frame).</summary>
		internal static bool IsTextFocused
		{
			get
			{
				if (Instance == null)
					return false;
				var selected = DexcUGUIApi.CurrentSelected;
				if (selected == null || !DexcUGUIApi.InputFieldType.IsInstanceOfType(selected))
				{
					Instance.m_ActivatingInput = null;
					return false;
				}
				if (Equals(DexcUGUIApi.Get(selected, "isFocused"), true))
				{
					Instance.m_ActivatingInput = null;
					return true;
				}
				return ReferenceEquals(Instance.m_ActivatingInput, selected);
			}
		}

		/// <summary>Deactivates the InputField being edited; the value is committed through onEndEdit.</summary>
		internal static void ReleaseTextFocus()
		{
			if (Instance == null)
				return;
			Instance.m_ActivatingInput = null;
			var selected = DexcUGUIApi.CurrentSelected;
			if (selected == null || !DexcUGUIApi.InputFieldType.IsInstanceOfType(selected))
				return;
			selected.GetType().GetMethod("DeactivateInputField", Type.EmptyTypes)?.Invoke(selected, null);
			DexcRuntimeUIState.NotifyInputState(DebugMenuInputState.Navigation);
		}

		internal static void SetCanvasVisibleForCaptureTests(bool visible)
		{
			if (Instance == null)
				return;
			Instance.m_Canvas.enabled = visible && DexcRuntimeUIRendererSelection.UseUGUI && DexcRuntimeUIState.IsOpen;
		}

		private static Rect ScreenRect(RectTransform rect)
		{
			if (rect == null || !rect.gameObject.activeInHierarchy)
				return default(Rect);
			var corners = new Vector3[4];
			rect.GetWorldCorners(corners);
			var bottomLeft = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
			var topRight = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
			return Rect.MinMaxRect(bottomLeft.x, bottomLeft.y, topRight.x, topRight.y);
		}

		private static DexcRuntimeUGUILayout CalculateLayout(int width, int height, Rect safeArea, DexcRuntimeUIPage page)
		{
			return CalculateLayout(width, height, safeArea, page, DexcRuntimeUIReferenceResolver.ResolveCurrentProfile(width, height));
		}

		private static DexcRuntimeUGUILayout CalculateLayout(
			int width,
			int height,
			Rect safeArea,
			DexcRuntimeUIPage page,
			DexcRuntimeUIProfileKind profile
		)
		{
			var reference = DexcRuntimeUIReferenceResolver.Calculate(width, height, safeArea, profile);
			var panel = reference.Panel;
			var headerHeight = Mathf.Min(DexcRuntimeFormLayout.HeaderHeight(reference.HitHeight) * reference.Scale, panel.height);
			var footerHeight = DexcRuntimeUIRenderer.HasFooter(page)
				? Mathf.Min((reference.HitHeight + 12f) * reference.Scale, Mathf.Max(0f, panel.height - headerHeight))
				: 0f;
			// Side by side the sections run the panel's full height at its left; the header, the page and the footer are beside them.
			var side = DexcRuntimeUIRenderer.SectionColumnWidth(reference) * reference.Scale;
			var header = new Rect(panel.x + side, panel.yMax - headerHeight, panel.width - side, headerHeight);
			var footer = new Rect(panel.x + side, panel.y, panel.width - side, footerHeight);
			var content = new Rect(panel.x + side, panel.y + footerHeight, panel.width - side, Mathf.Max(0f, panel.height - headerHeight - footerHeight));
			return new DexcRuntimeUGUILayout(
				reference.Screen,
				reference.SafeArea,
				panel,
				header,
				content,
				footer,
				reference.RowHeight,
				reference.HitHeight,
				reference.Profile,
				reference.ReferenceResolution,
				reference.Scale
			);
		}

		private void BuildHierarchy()
		{
			m_Canvas = gameObject.AddComponent<Canvas>();
			m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			m_Canvas.sortingOrder = short.MaxValue;
			m_CanvasScaler = DexcUGUIApi.Add(gameObject, DexcUGUIApi.CanvasScalerType);
			DexcUGUIApi.ConfigureCanvasScaler(m_CanvasScaler);
			DexcUGUIApi.Add(gameObject, DexcUGUIApi.GraphicRaycasterType);
			m_SafeAreaRoot = CreateRect("SafeAreaRoot", transform);
			m_Panel = CreateRect("Panel", m_SafeAreaRoot);
			DexcUGUIApi.SetImage(m_Panel.gameObject, PanelColor, false);
			m_Header = CreateRect("Header", m_Panel);
			DexcUGUIApi.SetImage(m_Header.gameObject, HeaderColor, false);
			var headerLayout = DexcUGUIApi.Add(m_Header.gameObject, DexcUGUIApi.HorizontalLayoutGroupType);
			DexcUGUIApi.ConfigureHorizontalLayout(headerLayout, 6, 6, false);
			m_BackButton = CreateHeaderButton(
				"Back",
				"‹",
				() =>
				{
					DexcRuntimeUIState.Back();
					Rebuild(true);
				}
			);
			// The place and the title keep one line each and are cut at the buttons, as a viewport cuts the page's rows.
			var titleClip = CreateRect("TitleClip", m_Header);
			DexcUGUIApi.SetImage(titleClip.gameObject, new Color(0f, 0f, 0f, 0.01f), false);
			DexcUGUIApi.Add(titleClip.gameObject, DexcUGUIApi.MaskType);
			var titleLayout = DexcUGUIApi.Add(titleClip.gameObject, DexcUGUIApi.LayoutElementType);
			DexcUGUIApi.Set(titleLayout, "flexibleWidth", 1f);
			DexcUGUIApi.Set(titleLayout, "minWidth", 0f);
			m_Title = CreateText("Title", titleClip, "Dexc Debug", DexcRuntimeFormLayout.TitleFontSize, TextAnchor.MiddleLeft);
			DexcUGUIApi.Set(m_Title, "horizontalOverflow", HorizontalWrapMode.Overflow);
			DexcUGUIApi.Set(m_Title, "verticalOverflow", VerticalWrapMode.Overflow);
			// Where the screen is wide enough, the header switches between one page and side by side.
			m_SingleButton = CreateHeaderButton("Single", "Single", () => SwitchLayout(DexcRuntimeUILayoutMode.Single));
			m_SplitButton = CreateHeaderButton("Split", "Split", () => SwitchLayout(DexcRuntimeUILayoutMode.Split));
			// A command's earlier values open from the header, so the footer is Execute alone.
			m_HistoryButton = CreateHeaderButton(
				"History",
				"History",
				() =>
				{
					DexcRuntimeCommandForm.OpenHistory();
					Rebuild(true);
				}
			);
			// The star sits before Close and only shows on a page that can be starred.
			m_FavoriteButton = CreateHeaderButton(
				"Favorite",
				"☆",
				() =>
				{
					DexcRuntimeFavorites.ToggleCurrent(DexcRuntimeUIState.Page);
					Rebuild(true);
				}
			);
			m_CloseButton = CreateHeaderButton(
				"Close",
				"×",
				() =>
				{
					DexcRuntimeUIState.Close();
					DexcUGUIApi.SetSelected(null);
					Tick();
				}
			);
			m_ScrollView = CreateRect("ScrollView", m_Panel);
			m_ScrollRect = DexcUGUIApi.Add(m_ScrollView.gameObject, DexcUGUIApi.ScrollRectType);
			m_Viewport = CreateRect("Viewport", m_ScrollView);
			DexcUGUIApi.SetImage(m_Viewport.gameObject, new Color(0f, 0f, 0f, 0.01f), false);
			DexcUGUIApi.Add(m_Viewport.gameObject, DexcUGUIApi.MaskType);
			m_Content = CreateRect("Content", m_Viewport);
			var vertical = DexcUGUIApi.Add(m_Content.gameObject, DexcUGUIApi.VerticalLayoutGroupType);
			DexcUGUIApi.ConfigureVerticalLayout(vertical, 8, 4);
			var fitter = DexcUGUIApi.Add(m_Content.gameObject, DexcUGUIApi.ContentSizeFitterType);
			DexcUGUIApi.SetEnum(fitter, "horizontalFit", "Unconstrained");
			DexcUGUIApi.SetEnum(fitter, "verticalFit", "PreferredSize");
			SetStretch(m_Viewport, 0f);
			m_Viewport.offsetMax = new Vector2(-16f, 0f);
			var scrollBar = CreateRect("VerticalScrollbar", m_ScrollView);
			scrollBar.anchorMin = new Vector2(1f, 0f);
			scrollBar.anchorMax = Vector2.one;
			scrollBar.offsetMin = new Vector2(-14f, 0f);
			scrollBar.offsetMax = Vector2.zero;
			DexcUGUIApi.SetImage(scrollBar.gameObject, HeaderColor, true);
			var scrollbar = DexcUGUIApi.Add(scrollBar.gameObject, DexcUGUIApi.ScrollbarType);
			var thumb = CreateRect("Thumb", scrollBar);
			SetStretch(thumb, 2f);
			var thumbImage = DexcUGUIApi.SetImage(thumb.gameObject, Palette.SecondaryText, true);
			DexcUGUIApi.Set(scrollbar, "handleRect", thumb);
			DexcUGUIApi.Set(scrollbar, "targetGraphic", thumbImage);
			DexcUGUIApi.SetEnum(scrollbar, "direction", "BottomToTop");
			DexcUGUIApi.Set(m_ScrollRect, "verticalScrollbar", scrollbar);
			m_Content.anchorMin = new Vector2(0f, 1f);
			m_Content.anchorMax = new Vector2(1f, 1f);
			m_Content.pivot = new Vector2(0.5f, 1f);
			m_Content.offsetMin = Vector2.zero;
			m_Content.offsetMax = Vector2.zero;
			DexcUGUIApi.Set(m_ScrollRect, "viewport", m_Viewport);
			DexcUGUIApi.Set(m_ScrollRect, "content", m_Content);
			DexcUGUIApi.Set(m_ScrollRect, "horizontal", false);
			DexcUGUIApi.Set(m_ScrollRect, "vertical", true);
			DexcUGUIApi.Set(m_ScrollRect, "movementType", Enum.Parse(DexcUGUIApi.ScrollRectType.GetNestedType("MovementType"), "Clamped"));
			m_Footer = CreateRect("Footer", m_Panel);
			DexcUGUIApi.SetImage(m_Footer.gameObject, HeaderColor, false);
			var footerLayout = DexcUGUIApi.Add(m_Footer.gameObject, DexcUGUIApi.HorizontalLayoutGroupType);
			// Widths come from each action's layout element: the primary one flexes, the secondary one keeps its width.
			DexcUGUIApi.ConfigureHorizontalLayout(footerLayout, 6, 6, false);
			for (var index = 0; index < 2; index++)
			{
				m_FooterButtons.Add(CreateRow("FooterAction" + index, m_Footer));
			}

			m_SplitSections = SplitColumn("SplitSections", false);
			m_SplitList = SplitColumn("SplitList", true);
			m_Canvas.enabled = false;
			ApplySkin();
		}

		/// <summary>A column side by side. The list's column scrolls: its rows sit in a content rect a ScrollRect moves.</summary>
		private RectTransform SplitColumn(string name, bool scrolls)
		{
			var column = CreateRect(name, m_Panel);
			DexcUGUIApi.SetImage(column.gameObject, PanelColor, scrolls);
			DexcUGUIApi.Add(column.gameObject, DexcUGUIApi.MaskType);
			var rows = column;
			if (scrolls)
			{
				rows = CreateRect("Content", column);
				rows.anchorMin = new Vector2(0f, 1f);
				rows.anchorMax = Vector2.one;
				rows.pivot = new Vector2(0.5f, 1f);
				rows.offsetMin = Vector2.zero;
				rows.offsetMax = Vector2.zero;
				var fitter = DexcUGUIApi.Add(rows.gameObject, DexcUGUIApi.ContentSizeFitterType);
				DexcUGUIApi.SetEnum(fitter, "verticalFit", "PreferredSize");
				var scroll = DexcUGUIApi.Add(column.gameObject, DexcUGUIApi.ScrollRectType);
				DexcUGUIApi.Set(scroll, "viewport", column);
				DexcUGUIApi.Set(scroll, "content", rows);
				DexcUGUIApi.Set(scroll, "horizontal", false);
				DexcUGUIApi.Set(scroll, "vertical", true);
				DexcUGUIApi.Set(scroll, "movementType", Enum.Parse(DexcUGUIApi.ScrollRectType.GetNestedType("MovementType"), "Clamped"));
			}
			var layout = DexcUGUIApi.Add(rows.gameObject, DexcUGUIApi.VerticalLayoutGroupType);
			DexcUGUIApi.ConfigureVerticalLayout(layout, 8, 4);
			DexcUGUIApi.Set(layout, "childForceExpandHeight", false);
			DexcUGUIApi.Set(layout, "childControlHeight", true);
			column.gameObject.SetActive(false);
			return column;
		}

		private RowSlot CreateHeaderButton(string name, string label, UnityAction action)
		{
			var slot = CreateRow(name, m_Header);
			slot.ConfigureButton(label, action, true, 36f, RowColor, Palette, false, true);
			SetStretch((RectTransform)slot.Label.transform, 4f);
			var layout = slot.Root.GetComponent(DexcUGUIApi.LayoutElementType);
			DexcUGUIApi.Set(layout, "preferredWidth", 36f);
			DexcUGUIApi.Set(layout, "flexibleWidth", 0f);
			return slot;
		}

		private static RectTransform CreateRect(string name, Transform parent)
		{
			var gameObject = new GameObject(name, typeof(RectTransform));
			gameObject.transform.SetParent(parent, false);
			return (RectTransform)gameObject.transform;
		}

		private Component CreateText(string name, Transform parent, string value, int fontSize, TextAnchor alignment)
		{
			var rect = CreateRect(name, parent);
			SetStretch(rect, 2f);
			var text = DexcUGUIApi.Add(rect.gameObject, DexcUGUIApi.TextType);
			DexcUGUIApi.Set(text, "font", Skin == null || Skin.Font == null ? DexcUGUIApi.DefaultFont : Skin.Font);
			DexcUGUIApi.Set(text, "fontSize", fontSize);
			DexcUGUIApi.Set(text, "fontStyle", FontStyle.Normal);
			DexcUGUIApi.Set(text, "color", Palette.PrimaryText);
			DexcUGUIApi.Set(text, "alignment", alignment);
			DexcUGUIApi.Set(text, "horizontalOverflow", HorizontalWrapMode.Wrap);
			DexcUGUIApi.Set(text, "verticalOverflow", VerticalWrapMode.Truncate);
			DexcUGUIApi.Set(text, "text", value ?? string.Empty);
			DexcUGUIApi.Set(text, "raycastTarget", false);
			m_Texts.Add(text);
			return text;
		}

		private static void SetStretch(RectTransform rect, float inset)
		{
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.pivot = new Vector2(0.5f, 0.5f);
			rect.offsetMin = new Vector2(inset, inset);
			rect.offsetMax = new Vector2(-inset, -inset);
		}

		private RowSlot CreateRow(string name, Transform parent)
		{
			var root = CreateRect(name, parent);
			var image = DexcUGUIApi.SetImage(root.gameObject, RowColor, true);
			var button = DexcUGUIApi.Add(root.gameObject, DexcUGUIApi.ButtonType);
			DexcUGUIApi.Set(button, "targetGraphic", image);
			var layout = DexcUGUIApi.Add(root.gameObject, DexcUGUIApi.LayoutElementType);
			// The focused row is outlined, as in the other renderers: four thin edges over the row's own colour.
			var focusMarker = CreateRect("FocusMarker", root);
			SetStretch(focusMarker, 0f);
			foreach (var side in new[] { Vector2.zero, Vector2.up, Vector2.right, Vector2.one })
			{
				var vertical = side == Vector2.zero || side == Vector2.one;
				var edge = CreateRect("Edge", focusMarker);
				edge.anchorMin = vertical ? new Vector2(side.x, 0f) : new Vector2(0f, side.x);
				edge.anchorMax = vertical ? new Vector2(side.x, 1f) : new Vector2(1f, side.x);
				edge.pivot = vertical ? new Vector2(side.x, 0.5f) : new Vector2(0.5f, side.x);
				edge.anchoredPosition = Vector2.zero;
				edge.sizeDelta = vertical ? new Vector2(2f, 0f) : new Vector2(0f, 2f);
				DexcUGUIApi.SetImage(edge.gameObject, Palette.Focus, false);
			}
			focusMarker.gameObject.SetActive(false);
			var label = CreateText("Label", root, string.Empty, DexcRuntimeFormLayout.FontSize, TextAnchor.MiddleLeft);
			var inputRoot = CreateRect("Input", root);
			SetStretch(inputRoot, 0f);
			DexcUGUIApi.SetImage(inputRoot.gameObject, Color.clear, true);
			var input = DexcUGUIApi.Add(inputRoot.gameObject, DexcUGUIApi.InputFieldType);
			// Landing on a field with navigation only selects it; Submit or a click starts editing.
			DexcUGUIApi.Set(input, "shouldActivateOnSelect", false);
			var inputVisual = CreateRect("Visual", inputRoot);
			inputVisual.anchorMin = Vector2.zero;
			inputVisual.anchorMax = new Vector2(1f, 0.52f);
			inputVisual.offsetMin = new Vector2(6f, 4f);
			inputVisual.offsetMax = new Vector2(-6f, -2f);
			var inputVisualImage = DexcUGUIApi.SetImage(inputVisual.gameObject, Color.white, false);
			var inputText = CreateText("Text", inputVisual, string.Empty, DexcRuntimeFormLayout.FontSize, TextAnchor.MiddleLeft);
			DexcUGUIApi.Set(input, "targetGraphic", inputVisualImage);
			DexcUGUIApi.Set(input, "textComponent", inputText);
			inputRoot.gameObject.SetActive(false);
			var errorRoot = CreateRect("Error", root);
			errorRoot.anchorMin = new Vector2(1f, 0f);
			errorRoot.anchorMax = new Vector2(1f, 0f);
			errorRoot.pivot = new Vector2(1f, 0f);
			errorRoot.anchoredPosition = Vector2.zero;
			// The "…" action at the right end of the rows that have one. The button's colours tint the image, so it is white.
			var errorImage = DexcUGUIApi.SetImage(errorRoot.gameObject, Color.white, true);
			var errorButton = DexcUGUIApi.Add(errorRoot.gameObject, DexcUGUIApi.ButtonType);
			DexcUGUIApi.Set(errorButton, "targetGraphic", errorImage);
			CreateText("Symbol", errorRoot, "…", DexcRuntimeFormLayout.FontSize, TextAnchor.MiddleCenter);
			errorRoot.gameObject.SetActive(false);
			var slot = new RowSlot(
				root,
				button,
				label,
				focusMarker.gameObject,
				inputRoot.gameObject,
				inputVisual.gameObject,
				input,
				inputText,
				layout,
				errorButton,
				CreateText("ComponentLabel", root, string.Empty, DexcRuntimeFormLayout.FontSize, TextAnchor.MiddleLeft)
			);
			// The parts a row shows only for some rows: a note under the label, a list entry's second line and right-end
			// text, a choice's step buttons and chevron, and a boolean's switch.
			slot.Note = CreateText("Note", root, string.Empty, DexcRuntimeFormLayout.NoteFontSize, TextAnchor.UpperLeft);
			slot.Detail = CreateText("Detail", root, string.Empty, DexcRuntimeFormLayout.SmallFontSize, TextAnchor.UpperLeft);
			slot.Trailing = CreateText("Trailing", root, string.Empty, DexcRuntimeFormLayout.TrailingFontSize, TextAnchor.MiddleRight);
			slot.Previous = CreateStepButton("Previous", inputRoot, "‹");
			slot.Next = CreateStepButton("Next", inputRoot, "›");
			slot.Chevron = CreateText("Chevron", inputRoot, "›", DexcRuntimeFormLayout.FontSize, TextAnchor.MiddleRight);
			var track = CreateRect("Switch", inputRoot);
			DexcUGUIApi.SetImage(track.gameObject, Palette.Row, false);
			var knob = CreateRect("Knob", track);
			DexcUGUIApi.SetImage(knob.gameObject, Palette.PrimaryText, false);
			slot.Switch = track;
			slot.Knob = knob;
			// A field that is not valid is outlined, and its message is written under the value, as in IMGUI.
			var outline = CreateRect("ErrorOutline", inputVisual);
			SetStretch(outline, 0f);
			foreach (var side in new[] { Vector2.zero, Vector2.up, Vector2.right, Vector2.one })
			{
				var vertical = side == Vector2.zero || side == Vector2.one;
				var edge = CreateRect("Edge", outline);
				edge.anchorMin = vertical ? new Vector2(side.x, 0f) : new Vector2(0f, side.x);
				edge.anchorMax = vertical ? new Vector2(side.x, 1f) : new Vector2(1f, side.x);
				edge.pivot = vertical ? new Vector2(side.x, 0.5f) : new Vector2(0.5f, side.x);
				edge.anchoredPosition = Vector2.zero;
				edge.sizeDelta = vertical ? new Vector2(1f, 0f) : new Vector2(0f, 1f);
				DexcUGUIApi.SetImage(edge.gameObject, Palette.FieldErrorText, false);
			}
			slot.ErrorOutline = outline;
			slot.ErrorLine = CreateText("ErrorLine", root, string.Empty, DexcRuntimeFormLayout.SmallFontSize, TextAnchor.UpperLeft);
			// The components of one vector share a band: this strip fills the layout's gap under the row.
			var bridge = CreateRect("Bridge", root);
			bridge.anchorMin = Vector2.zero;
			bridge.anchorMax = new Vector2(1f, 0f);
			bridge.pivot = new Vector2(0.5f, 1f);
			bridge.anchoredPosition = Vector2.zero;
			bridge.sizeDelta = new Vector2(0f, DexcRuntimeFormLayout.RowGap);
			DexcUGUIApi.SetImage(bridge.gameObject, HeaderColor, false);
			bridge.gameObject.SetActive(false);
			slot.Bridge = bridge;
			slot.HideOptionalParts();
			return slot;
		}

		/// <summary>A ‹ or › button at one end of a choice: steps the value without opening its page.</summary>
		private Component CreateStepButton(string name, Transform parent, string symbol)
		{
			var rect = CreateRect(name, parent);
			// The button's colours tint the image, so the image itself is white.
			var image = DexcUGUIApi.SetImage(rect.gameObject, Color.white, true);
			var button = DexcUGUIApi.Add(rect.gameObject, DexcUGUIApi.ButtonType);
			DexcUGUIApi.Set(button, "targetGraphic", image);
			CreateText("Symbol", rect, symbol, DexcRuntimeFormLayout.SymbolFontSize, TextAnchor.MiddleCenter);
			return button;
		}

		private void Tick()
		{
			DexcRuntimeSettingForm.Tick();
			DexcRuntimeInspectorForm.Tick();
			if (m_InspectorVersion != DexcRuntimeInspectorForm.Version)
				RefreshRows();
			if (DexcRuntimeInspectorNavigation.IsList(DexcRuntimeUIState.Page))
				DexcRuntimeInspectorNavigation.Rows();
			if (m_Content != null && m_SettingVersion != DexcRuntimeSettingForm.Version)
				RefreshRows();
			var active = DexcRuntimeUIRendererSelection.UseUGUI;
			m_Canvas.enabled = active && DexcRuntimeUIState.IsOpen;
			if (!active || !DexcRuntimeUIState.IsOpen)
			{
				if (!DexcRuntimeUIState.IsOpen)
				{
					DexcUGUIApi.SetSelected(null);
					m_LastSelectionIndex = -1;
					m_LastSelected = null;
					m_LastSignature = int.MinValue;
				}
				m_ActivatingInput = null;
				DexcUGUIApi.ReleaseFallbackEventSystem();
				return;
			}

			DexcUGUIApi.EnsureEventSystem();
			DexcRuntimeUIRenderer.HandleProviderInput();
			var signature = Signature();
			if (
				signature != m_LastSignature
				&& !(
					DexcRuntimeInspectorNavigation.Searchable
					&& DexcUGUIApi.CurrentSelected != null
					&& DexcUGUIApi.InputFieldType.IsInstanceOfType(DexcUGUIApi.CurrentSelected)
					&& Equals(DexcUGUIApi.Get(DexcUGUIApi.CurrentSelected, "isFocused"), true)
				)
			)
				Rebuild(false);
			else
				SyncSelection();
			DexcRuntimeUIState.NotifyInputState(IsTextFocused ? DebugMenuInputState.TextInput : DebugMenuInputState.Navigation);
		}

		private int Signature()
		{
			var hash = new HashCode();
			hash.Add(DexcRuntimeUIState.Page);
			if (DexcRuntimeInspectorNavigation.IsList(DexcRuntimeUIState.Page))
				hash.Add(DexcRuntimeInspectorNavigation.Version);
			hash.Add(m_UseProfileOverride ? m_ProfileWidth : Screen.width);
			hash.Add(m_UseProfileOverride ? m_ProfileHeight : Screen.height);
			hash.Add(DexcRuntimeUIState.LastCommandResult?.Status);
			hash.Add(DexcRuntimeUIState.AsyncExecution != null);
			hash.Add(DexcRuntimeUIState.Confirming);
			hash.Add(DexcRuntimeUIState.Notice);
			hash.Add(DebugBootMenu.AutoStartRemainingSeconds);
			return hash.ToHashCode();
		}

		private void Rebuild(bool force)
		{
			if (!force && !DexcRuntimeUIState.IsOpen)
				return;
			var width = m_UseProfileOverride ? m_ProfileWidth : Screen.width;
			var height = m_UseProfileOverride ? m_ProfileHeight : Screen.height;
			var safe = m_UseProfileOverride ? m_ProfileSafeArea : Screen.safeArea;
			// Side by side Home gives way to the home panel or the last section.
			DexcRuntimeSplitHome.Shown = DexcRuntimeUIReferenceResolver
				.Calculate(width, height, safe, m_UseProfileOverride ? m_ProfileOverride : DexcRuntimeUIReferenceResolver.ResolveCurrentProfile(width, height))
				.Split;
			DexcRuntimeSplitHome.Apply();
			m_Layout = m_UseProfileOverride
				? CalculateLayout(width, height, safe, DexcRuntimeUIState.Page, m_ProfileOverride)
				: CalculateLayout(width, height, safe, DexcRuntimeUIState.Page);
			ApplyLayout();
			ApplySkin();
			if (m_SelectionPage != DexcRuntimeUIPage.FormDetail && DexcRuntimeUIState.Page == DexcRuntimeUIPage.FormDetail)
			{
				m_FormReturnScroll = m_Content.anchoredPosition;
				m_FormReturnDraft = m_SelectionDraft;
			}
			var currentNavigationSelection = DexcUGUIApi.CurrentSelected;
			var navigationKey =
				m_SelectionPage == DexcRuntimeUIState.Page && DexcRuntimeInspectorNavigation.IsList(DexcRuntimeUIState.Page)
					? m_Rows
						.FirstOrDefault(row =>
							row.Root.gameObject.activeSelf
							&& (row.Button == currentNavigationSelection || row.Input == currentNavigationSelection)
						)
						?.NavigationKey
					: null;
			m_Selectables.Clear();
			DeactivateRows();
			DexcUGUIApi.Set(m_Title, "text", DexcRuntimeUIRenderer.RichHeader(DexcRuntimeUIState.Page, Palette.SecondaryText));
			BuildSplitColumns();
			if (!DexcDebugger.IsInitialized)
				AddLabel("Dexc Debugger is enabled but not initialized.", ErrorColor);
			else
				BuildPage();
			BridgeVectors();
			BuildFooter();
			// History follows Execute on the gamepad's path, as it did when it sat beside it in the footer.
			var history = DexcRuntimeUIRenderer.HasHeaderHistory();
			m_HistoryButton.Root.gameObject.SetActive(history);
			DexcUGUIApi.Set(m_HistoryButton.Layout, "minWidth", 72f);
			DexcUGUIApi.Set(m_HistoryButton.Layout, "preferredWidth", 72f);
			if (history)
				m_Selectables.Add(m_HistoryButton.Button);
			m_BackButton.Root.gameObject.SetActive(DexcRuntimeUIRenderer.HasHeaderBack(DexcRuntimeUIState.Page));
			if (m_BackButton.Root.gameObject.activeSelf)
				m_Selectables.Add(m_BackButton.Button);
			var reference = CurrentReference();
			foreach (var toggle in new[] { m_SingleButton, m_SplitButton })
			{
				toggle.Root.gameObject.SetActive(reference.CanSplit);
				var on = (toggle == m_SplitButton) == reference.Split;
				DexcUGUIApi.SetColorBlock(toggle.Button, Palette, on ? Palette.Selection : RowColor, false, true);
				// The header's buttons keep their width; a long title is what gives way.
				DexcUGUIApi.Set(toggle.Layout, "minWidth", 64f);
				DexcUGUIApi.Set(toggle.Layout, "preferredWidth", 64f);
			}
			var star = DexcRuntimeFavorites.Star(DexcRuntimeUIState.Page);
			m_FavoriteButton.Root.gameObject.SetActive(star != null);
			if (star != null)
				DexcUGUIApi.Set(m_FavoriteButton.Label, "text", star);
			m_CloseButton.Root.gameObject.SetActive(DexcRuntimeUIRenderer.HasHeaderClose());
			if (m_CloseButton.Root.gameObject.activeSelf)
				m_Selectables.Add(m_CloseButton.Button);
			// The star comes last, so the gamepad reaches it without moving anything it already knew.
			if (star != null)
				m_Selectables.Add(m_FavoriteButton.Button);
			if (navigationKey != null)
			{
				var restored = m_Rows.FirstOrDefault(row => row.Root.gameObject.activeSelf && row.NavigationKey == navigationKey);
				var selection =
					restored == null
						? -1
						: m_Selectables.IndexOf(restored.Source?.Kind == DexcUIRowKind.Input ? restored.Input : restored.Button);
				if (selection >= 0)
					DexcRuntimeUIState.SetSelectedIndex(selection);
			}
			WireNavigation();
			var returningFromDetail =
				m_SelectionPage == DexcRuntimeUIPage.FormDetail && DexcRuntimeUIState.Page != DexcRuntimeUIPage.FormDetail;
			var session = DexcDebugger.CurrentSession == null ? default(DebugSessionId) : DexcDebugger.CurrentSession.SessionId;
			if (
				m_LastSelectionIndex < 0
				|| m_SelectionPage != DexcRuntimeUIState.Page
				|| m_SelectionSession != session
				|| m_SelectionDraft != DexcRuntimeUIState.DraftId
			)
			{
				m_LastSelectionIndex = -1;
				m_LastSelected = null;
				DexcUGUIApi.Set(m_ScrollRect, "verticalNormalizedPosition", 1f);
			}
			m_SelectionPage = DexcRuntimeUIState.Page;
			m_SelectionSession = session;
			m_SelectionDraft = DexcRuntimeUIState.DraftId;
			m_LastSignature = Signature();
			if (returningFromDetail && m_FormReturnDraft == DexcRuntimeUIState.DraftId)
			{
				Canvas.ForceUpdateCanvases();
				m_Content.anchoredPosition = m_FormReturnScroll;
			}
			SyncSelection();
		}

		private void ApplySkin()
		{
			DexcUGUIApi.SetImageColor(m_Panel.gameObject, PanelColor);
			DexcUGUIApi.SetImageSprite(m_Panel.gameObject, Skin == null ? null : Skin.PanelSprite);
			DexcUGUIApi.SetImageColor(m_Header.gameObject, HeaderColor);
			DexcUGUIApi.SetImageColor(m_Footer.gameObject, HeaderColor);
			var font = Skin == null || Skin.Font == null ? DexcUGUIApi.DefaultFont : Skin.Font;
			foreach (var text in m_Texts)
			{
				DexcUGUIApi.Set(text, "font", font);
				DexcUGUIApi.Set(text, "color", Palette.PrimaryText);
			}
			foreach (var slot in AllSlots)
			{
				foreach (Transform edge in slot.FocusMarker.transform)
					DexcUGUIApi.SetImageColor(edge.gameObject, Palette.Focus);
				if (slot.Bridge != null)
					DexcUGUIApi.SetImageColor(slot.Bridge.gameObject, HeaderColor);
				if (slot.ErrorOutline != null)
				{
					foreach (Transform edge in slot.ErrorOutline)
						DexcUGUIApi.SetImageColor(edge.gameObject, Palette.FieldErrorText);
				}
				DexcUGUIApi.SetImageColor(slot.InputVisual, Color.white);
			}
		}

		private void ApplyLayout()
		{
			var safe = m_Layout.SafeArea;
			var scale = m_Layout.Scale > 0f ? m_Layout.Scale : 1f;
			DexcUGUIApi.Set(m_CanvasScaler, "scaleFactor", 1f);
			m_Canvas.scaleFactor = 1f;
			// Keep legacy Text and InputField geometry in the same logical units.
			m_Panel.localScale = new Vector3(scale, scale, 1f);
			m_SafeAreaRoot.anchorMin = new Vector2(
				safe.xMin / Mathf.Max(1f, m_Layout.Screen.width),
				safe.yMin / Mathf.Max(1f, m_Layout.Screen.height)
			);
			m_SafeAreaRoot.anchorMax = new Vector2(
				safe.xMax / Mathf.Max(1f, m_Layout.Screen.width),
				safe.yMax / Mathf.Max(1f, m_Layout.Screen.height)
			);
			m_SafeAreaRoot.offsetMin = Vector2.zero;
			m_SafeAreaRoot.offsetMax = Vector2.zero;
			SetBottomLeftRect(
				m_Panel,
				new Rect(m_Layout.Panel.x - safe.x, m_Layout.Panel.y - safe.y, m_Layout.Panel.width / scale, m_Layout.Panel.height / scale)
			);
			var side = (m_Layout.Header.x - m_Layout.Panel.x) / scale;
			SetBottomLeftRect(
				m_Header,
				new Rect(
					side,
					(m_Layout.Panel.height - m_Layout.Header.height) / scale,
					m_Layout.Header.width / scale,
					m_Layout.Header.height / scale
				)
			);
			SetBottomLeftRect(
				m_ScrollView,
				new Rect(side, m_Layout.Footer.height / scale, m_Layout.Content.width / scale, m_Layout.Content.height / scale)
			);
			SetBottomLeftRect(m_Footer, new Rect(side, 0f, m_Layout.Footer.width / scale, m_Layout.Footer.height / scale));
			m_Footer.gameObject.SetActive(m_Layout.Footer.height > 0f);
			foreach (var slot in new[] { m_BackButton, m_FavoriteButton, m_CloseButton })
			{
				DexcUGUIApi.Set(slot.Layout, "minWidth", m_Layout.HitHeight);
				DexcUGUIApi.Set(slot.Layout, "preferredWidth", m_Layout.HitHeight);
			}
			// Every header button is as tall as a row; Single and Split keep their own width.
			foreach (var slot in new[] { m_BackButton, m_SingleButton, m_SplitButton, m_HistoryButton, m_FavoriteButton, m_CloseButton })
			{
				DexcUGUIApi.Set(slot.Layout, "minHeight", m_Layout.HitHeight);
				DexcUGUIApi.Set(slot.Layout, "preferredHeight", m_Layout.HitHeight);
			}
		}

		private static void SetBottomLeftRect(RectTransform rect, Rect value)
		{
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.zero;
			rect.pivot = Vector2.zero;
			rect.anchoredPosition = value.position;
			rect.sizeDelta = value.size;
		}

		private void BuildPage()
		{
			switch (DexcRuntimeUIState.Page)
			{
				case DexcRuntimeUIPage.FormDetail:
					BuildFormDetail();
					break;
				case DexcRuntimeUIPage.Home:
					BuildHome();
					break;
				case DexcRuntimeUIPage.CommandList:
					BuildCommandList();
					break;
				case DexcRuntimeUIPage.CommandDetail:
					BuildCommandDetail();
					break;
				case DexcRuntimeUIPage.SettingList:
					BuildSettingList();
					break;
				case DexcRuntimeUIPage.SettingDetail:
					BuildSettingDetail();
					break;
				case DexcRuntimeUIPage.InspectorHome:
					BuildInspectorHome();
					break;
				case DexcRuntimeUIPage.InspectorRegistered:
					BuildInspectorRegistered();
					break;
				case DexcRuntimeUIPage.InspectorScene:
					BuildInspectorScene();
					break;
				case DexcRuntimeUIPage.InspectorComponents:
					BuildInspectorComponents();
					break;
				case DexcRuntimeUIPage.InspectorFields:
					BuildInspectorFields();
					break;
				case DexcRuntimeUIPage.InspectorWatches:
					BuildInspectorWatches();
					break;
				case DexcRuntimeUIPage.History:
					BuildHistory();
					break;
				case DexcRuntimeUIPage.Diagnostic:
					BuildInspectorList();
					break;
				case DexcRuntimeUIPage.PanelList:
					BuildPanelList();
					break;
				case DexcRuntimeUIPage.PanelDetail:
					BuildPanelDetail();
					break;
				default:
					AddLabel(DexcRuntimeUIRenderer.Title(DexcRuntimeUIState.Page) + " uses the shared Runtime UI state.", RowColor);
					break;
			}

			if (!string.IsNullOrEmpty(DexcRuntimeUIState.Notice))
			{
				AddLabel(DexcRuntimeUIState.Notice, RowColor, Palette.Warning);
			}
		}

		private void BuildHome()
		{
			BuildRows(DexcUIRows.HomeRows());
		}

		private void BuildPanelList()
		{
			BuildRows(DexcRuntimePanelForm.ListRows());
		}

		private void BuildPanelDetail()
		{
			if (DexcRuntimePanelForm.Current == null)
			{
				Navigate(DexcRuntimeUIPage.PanelList);
				return;
			}
			BuildRows(DexcRuntimePanelForm.DetailRows());
		}

		private void BuildCommandList()
		{
			BuildRows(DexcRuntimeCommandForm.ListRows());
		}

		private void BuildFormDetail()
		{
			BuildRows(DexcRuntimeFormDetail.Rows());
		}

		private void BuildCommandDetail()
		{
			if (DexcRuntimeUIState.Command == null)
			{
				Navigate(DexcRuntimeUIPage.CommandList);
				return;
			}
			BuildRows(DexcRuntimeCommandForm.DetailRows(MaximumRows));
		}

		private void BuildSettingList()
		{
			BuildRows(DexcRuntimeSettingForm.ListRows());
		}

		private void BuildSettingDetail()
		{
			if (DexcRuntimeSettingForm.Current == null)
			{
				Navigate(DexcRuntimeUIPage.SettingList);
				return;
			}
			DexcRuntimeSettingForm.Tick();
			BuildRows(DexcRuntimeSettingForm.DetailRows(true));
		}

		/// <summary>Turns a page's rows into pooled slots. Each slot keeps its row so <see cref="RefreshRows"/> can update it.</summary>
		private void BuildRows(IReadOnlyList<DexcUIRow> rows)
		{
			foreach (var row in rows)
			{
				switch (row.Kind)
				{
					case DexcUIRowKind.Header:
						AddLabel(row.Label, HeaderColor);
						break;
					case DexcUIRowKind.Label:
						var label = AddLabel(row.Text == null ? row.Label : row.Text(), ToneColor(row), ToneTextColor(row));
						if (label != null)
						{
							label.Source = row;
							// Every other line of a table sits on a band so the line can be followed across.
							if (row.Striped)
								DexcUGUIApi.SetImageColor(label.Root.gameObject, HeaderColor);
						}
						break;
					case DexcUIRowKind.Button:
						var target = row;
						var button = AddButton(
							row.Label,
							() =>
							{
								target.Activated();
								Rebuild(true);
							},
							textColor: ToneTextColor(row)
						);
						if (button != null)
						{
							button.NavigationKey = row.Key;
							if (row.ListItem)
								DecorateListRow(button, row);
						}
						break;
					case DexcUIRowKind.Input:
					case DexcUIRowKind.Choice:
						BuildRowInput(row);
						break;
					case DexcUIRowKind.Slider:
						var slider = AddLabel(string.Empty, RowColor, height: m_Layout.HitHeight);
						if (slider == null)
							break;
						slider.Source = row;
						slider.ConfigureSlider(row, Palette, RefreshRows);
						if (row.IsEnabled)
							m_Selectables.Add(slider.Slider);
						break;
				}
			}
		}

		/// <summary>The components of one vector share a band: each row but the last fills the gap under it.</summary>
		private void BridgeVectors()
		{
			var live = m_Rows.Where(slot => slot.Root.gameObject.activeSelf).ToList();
			for (var index = 0; index < live.Count; index++)
			{
				var next = index + 1 < live.Count ? live[index + 1] : null;
				var shared = !string.IsNullOrEmpty(live[index].CommandComponent)
					&& next != null
					&& !string.IsNullOrEmpty(next.CommandComponent)
					&& string.IsNullOrEmpty(next.Source?.Label);
				live[index].Bridge.gameObject.SetActive(shared);
			}
		}

		/// <summary>
		/// A list entry's extras: a mark before the label, a second line under it, and a count or result with a chevron at
		/// the right end. The label keeps its own text, so a row is still found by what it says.
		/// </summary>
		private void DecorateListRow(RowSlot slot, DexcUIRow row)
		{
			if (!string.IsNullOrEmpty(row.Leading))
				DexcUGUIApi.Set(slot.Label, "text", row.Leading + "  " + row.Label);
			var trailing = (row.TrailingText ?? string.Empty) + (row.Drill ? "  ›" : string.Empty);
			var trailingWidth = 0f;
			if (trailing.Length > 0)
			{
				slot.Trailing.gameObject.SetActive(true);
				DexcUGUIApi.Set(slot.Trailing, "text", trailing.Trim());
				DexcUGUIApi.Set(slot.Trailing, "horizontalOverflow", HorizontalWrapMode.Overflow);
				DexcUGUIApi.Set(slot.Trailing, "color", row.TrailingTone == DexcUIRowTone.Normal ? Palette.SecondaryText : Palette.Warning);
				trailingWidth = Mathf.Ceil(Convert.ToSingle(DexcUGUIApi.Get(slot.Trailing, "preferredWidth"))) + 8f;
				var rect = (RectTransform)slot.Trailing.transform;
				rect.anchorMin = new Vector2(1f, 0f);
				rect.anchorMax = Vector2.one;
				rect.pivot = new Vector2(1f, 0.5f);
				rect.anchoredPosition = new Vector2(-8f, 0f);
				rect.sizeDelta = new Vector2(trailingWidth, 0f);
			}
			var labelRect = (RectTransform)slot.Label.transform;
			labelRect.offsetMax = new Vector2(-8f - trailingWidth, labelRect.offsetMax.y);
			var detail = row.DetailText;
			if (string.IsNullOrEmpty(detail))
			{
				// A label that wraps beside the right-end text makes the row taller rather than losing its last line.
				var fitted = Mathf.Max(m_Layout.HitHeight, Mathf.Ceil(Convert.ToSingle(DexcUGUIApi.Get(slot.Label, "preferredHeight"))) + 8f);
				slot.Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, fitted);
				DexcUGUIApi.Set(slot.Layout, "minHeight", fitted);
				DexcUGUIApi.Set(slot.Layout, "preferredHeight", fitted);
				return;
			}
			// Two lines: the label at the top, the second line under it, the row as tall as both.
			slot.Detail.gameObject.SetActive(true);
			DexcUGUIApi.Set(slot.Detail, "color", Palette.SecondaryText);
			var width = Mathf.Max(1f, slot.Root.rect.width - 16f - trailingWidth);
			// A long name such as a type's full name wraps at its dots, not part way through a word.
			DexcUGUIApi.Set(
				slot.Detail,
				"text",
				DexcRuntimeFormLayout.BreakLines(
					detail,
					width,
					line =>
					{
						DexcUGUIApi.Set(slot.Detail, "text", line);
						return Convert.ToSingle(DexcUGUIApi.Get(slot.Detail, "preferredWidth"));
					}
				)
			);
			labelRect.anchorMin = labelRect.anchorMax = new Vector2(0f, 1f);
			labelRect.pivot = new Vector2(0f, 1f);
			labelRect.sizeDelta = new Vector2(width, 20f);
			var labelHeight = Mathf.Ceil(Convert.ToSingle(DexcUGUIApi.Get(slot.Label, "preferredHeight")));
			labelRect.sizeDelta = new Vector2(width, labelHeight);
			labelRect.anchoredPosition = new Vector2(8f, -4f);
			var detailRect = (RectTransform)slot.Detail.transform;
			detailRect.anchorMin = detailRect.anchorMax = new Vector2(0f, 1f);
			detailRect.pivot = new Vector2(0f, 1f);
			detailRect.sizeDelta = new Vector2(width, 20f);
			var detailHeight = Mathf.Ceil(Convert.ToSingle(DexcUGUIApi.Get(slot.Detail, "preferredHeight")));
			detailRect.sizeDelta = new Vector2(width, detailHeight);
			detailRect.anchoredPosition = new Vector2(8f, -4f - labelHeight);
			var height = Mathf.Max(m_Layout.HitHeight, labelHeight + detailHeight + 8f);
			slot.Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
			DexcUGUIApi.Set(slot.Layout, "minHeight", height);
			DexcUGUIApi.Set(slot.Layout, "preferredHeight", height);
		}

		private Color ToneColor(DexcUIRow row) => row.Tone == DexcUIRowTone.Error ? ErrorColor : RowColor;

		private Color? ToneTextColor(DexcUIRow row) => row.Tone == DexcUIRowTone.Warning ? Palette.Warning : (Color?)null;

		private void BuildRowInput(DexcUIRow row)
		{
			var slot = AddInput(
				row.Label,
				row.Text(),
				text =>
				{
					row.Changed?.Invoke(text);
					RefreshRows();
				},
				ToneColor(row),
				ToneTextColor(row),
				row.Multiline,
				confirmed: text =>
				{
					// A confirmed search changes the list itself, so that page is built again.
					if (row.Confirmed != null && row.Confirmed(text) && row.Rebuilds)
						Rebuild(true);
					else
						RefreshRows();
				},
				unbind: row.Unbound,
				interactable: row.IsEnabled,
				component: row.Component?.ToUpperInvariant(),
				note: row.Note
			);
			if (slot == null)
				return;
			slot.Source = row;
			slot.NavigationKey = row.Key;
			slot.CommandComponent = row.Component;
			if (row.Kind == DexcUIRowKind.Choice)
			{
				m_Selectables.Remove(slot.Input);
				slot.ConfigureChoice(
					row.Text(),
					() =>
					{
						row.Activated();
						Rebuild(true);
					},
					row.IsEnabled,
					Palette,
					row.Boolean,
					row.Cycle == null
						? (Action<int>)null
						: direction =>
						{
							if (row.Cycle(direction))
								RefreshRows();
						}
				);
				if (row.IsEnabled)
					m_Selectables.Add(slot.Choice);
			}

			if (row.Auxiliary != null)
				slot.Auxiliary = () => row.Auxiliary();
			// A value that cannot be edited reads as text, not as a field that looks broken.
			if (row.Kind == DexcUIRowKind.Input && !row.IsEnabled)
			{
				// The field's disabled tint would fade the box back in, so the tint itself is clear.
				DexcUGUIApi.SetColorBlock(slot.Input, Palette, Color.clear, false, false, Color.clear);
				DexcUGUIApi.SetCanvasRendererColor(slot.InputVisual, Color.clear);
				DexcUGUIApi.Set(slot.InputText, "color", Palette.SecondaryText);
			}
			SetInputError(slot, row.ErrorText);
		}

		/// <summary>Re-reads every live row in place, so typing keeps its focus and caret while values and errors follow the form.</summary>
		private void RefreshRows()
		{
			m_SettingVersion = DexcRuntimeSettingForm.Version;
			m_InspectorVersion = DexcRuntimeInspectorForm.Version;
			// Rows read the session; after it ends they wait for the page to be rebuilt.
			if (!DexcDebugger.IsInitialized)
				return;
			foreach (var slot in m_Rows.Where(slot => slot.Root.gameObject.activeSelf && slot.Source != null))
			{
				var row = slot.Source;
				switch (row.Kind)
				{
					case DexcUIRowKind.Label:
						if (row.Text != null)
							DexcUGUIApi.Set(slot.Label, "text", row.Text());
						break;
					case DexcUIRowKind.Slider:
						slot.Slider.GetType().GetMethod("SetValueWithoutNotify").Invoke(slot.Slider, new object[] { row.SliderValue() });
						break;
					case DexcUIRowKind.Choice:
						slot.SetChoiceValue(row.Text(), Palette);
						SetInputError(slot, row.ErrorText);
						break;
					case DexcUIRowKind.Input:
						var value = row.Text();
						if (!string.Equals((string)DexcUGUIApi.Get(slot.Input, "text"), value, StringComparison.Ordinal))
						{
							slot.Input.GetType()
								.GetMethod("SetTextWithoutNotify", new[] { typeof(string) })
								?.Invoke(slot.Input, new object[] { value });
						}
						SetInputError(slot, row.ErrorText);
						break;
				}
			}

			RefreshSettingApply();
			RefreshInspectorApply();
			RefreshCommandExecute();
		}

		private void RefreshSettingApply()
		{
			if (DexcRuntimeUIState.Page != DexcRuntimeUIPage.SettingDetail || m_FooterButtons.Count < 2)
				return;
			m_Selectables.Remove(m_FooterButtons[1].Button);
			var enabled = DexcRuntimeUIRenderer.CanApplySetting();
			ConfigureFooter(
				1,
				"Apply",
				() =>
				{
					if (DexcRuntimeUIRenderer.CanApplySetting())
						DexcRuntimeUIRenderer.ApplySettingForRenderer();
					Rebuild(true);
				},
				enabled,
				enabled ? AccentColor : Palette.Disabled,
				true
			);
			WireNavigation();
		}

		private void BuildInspectorHome()
		{
			BuildRows(DexcUIRows.InspectorHomeRows());
		}

		private void BuildInspectorRegistered() => BuildInspectorList();

		private void BuildInspectorScene() => BuildInspectorList();

		private void BuildInspectorComponents() => BuildInspectorList();

		private void BuildInspectorList()
		{
			BuildRows(DexcRuntimeInspectorNavigation.ListRows());
		}

#if UNITY_INCLUDE_TESTS
		internal static Component InspectorInputForTests(DebugInspectorFieldId id, string component = null) =>
			Instance
				?.m_Rows.FirstOrDefault(row =>
					row.Root.gameObject.activeSelf && row.Source != null && row.Source.Key == DexcRuntimeInspectorForm.Key(id, component)
				)
				?.Input;
#endif

		private void BuildInspectorFields()
		{
			BuildRows(DexcRuntimeInspectorForm.FieldRows(true));
		}

		private void RefreshInspectorApply()
		{
			if (DexcRuntimeUIState.Page != DexcRuntimeUIPage.InspectorFields || m_FooterButtons.Count < 2)
				return;
			m_Selectables.Remove(m_FooterButtons[1].Button);
			ConfigureFooter(
				1,
				"Apply",
				() =>
				{
					DexcRuntimeInspectorForm.Apply();
					RefreshRows();
				},
				DexcRuntimeInspectorForm.CanApply,
				AccentColor,
				true
			);
			WireNavigation();
		}

		private void BuildInspectorWatches() => BuildInspectorList();

		private void BuildHistory() => BuildInspectorList();

		private void BuildFooter()
		{
			foreach (var slot in m_FooterButtons)
				slot.Root.gameObject.SetActive(false);
			if (!m_Footer.gameObject.activeSelf)
				return;
			if (DebugBootMenu.UsesRuntimeMenu)
			{
				ConfigureFooter(
					0,
					"Reset",
					() =>
					{
						DexcRuntimePanelForm.ResetBootValues();
						Rebuild(true);
					},
					true,
					RowColor,
					false
				);
				ConfigureFooter(1, "Start", () => DebugBootMenu.Start(), true, Palette.Accent, true);
				return;
			}
			if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.SettingDetail)
			{
				ConfigureFooter(
					0,
					"Presets",
					() =>
					{
						DexcRuntimeSettingForm.OpenPresets();
						Rebuild(true);
					},
					true,
					RowColor,
					false
				);
				RefreshSettingApply();
				return;
			}
			if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.InspectorFields)
			{
				ConfigureFooter(
					0,
					"Watch",
					() =>
					{
						DexcRuntimeInspectorForm.OpenWatchSelection();
						Rebuild(true);
					},
					true,
					RowColor,
					false
				);
				ConfigureFooter(
					1,
					"Apply",
					() =>
					{
						DexcRuntimeInspectorForm.Apply();
						RefreshRows();
					},
					DexcRuntimeInspectorForm.CanApply,
					AccentColor,
					true
				);
				return;
			}
			if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.FormDetail && DexcRuntimeFormDetail.HasApply)
			{
				ConfigureFooter(
					1,
					"Apply",
					() =>
					{
						DexcRuntimeFormDetail.Apply();
						Rebuild(true);
					},
					DexcRuntimeFormDetail.CanApply,
					AccentColor,
					true
				);
				return;
			}
			if (DexcRuntimeUIState.Page != DexcRuntimeUIPage.CommandDetail)
				return;
			if (DexcRuntimeUIState.Confirming)
			{
				ConfigureFooter(
					0,
					"Cancel",
					() =>
					{
						DexcRuntimeCommandExecution.CancelConfirmation();
						Rebuild(true);
					},
					true,
					RowColor,
					false
				);
				ConfigureFooter(
					1,
					"Confirm",
					() =>
					{
						DexcRuntimeCommandExecution.Confirm();
						Rebuild(true);
					},
					DexcRuntimeCommandExecution.CanExecute(),
					AccentColor,
					true
				);
				return;
			}
			if (DexcRuntimeUIState.AsyncExecution != null)
			{
				ConfigureFooter(0, "▶ Cancel request", () => DexcRuntimeCommandExecution.CancelExecution(), true, AccentColor, true);
				return;
			}
			// History is in the header: the secondary place stays empty and Execute takes the footer's width.
			var enabled = DexcRuntimeCommandExecution.CanExecute();
			ConfigureFooter(
				1,
				"Execute",
				() =>
				{
					DexcRuntimeCommandExecution.Request();
					Rebuild(true);
				},
				enabled,
				enabled ? AccentColor : Palette.Disabled,
				true
			);
		}

		private void ConfigureFooter(int index, string label, UnityAction action, bool interactable, Color color, bool primary)
		{
			var slot = m_FooterButtons[index];
			slot.ConfigureButton(label, action, interactable, m_Layout.HitHeight, color, Palette, primary, true);
			// The secondary action keeps a fixed width at the left; the primary one takes the rest.
			var panelWidth = m_Layout.Panel.width / Mathf.Max(0.0001f, m_Layout.Scale);
			DexcUGUIApi.Set(slot.Layout, "flexibleWidth", primary ? 1f : 0f);
			DexcUGUIApi.Set(slot.Layout, "preferredWidth", primary ? -1f : Mathf.Min(120f, Mathf.Max(60f, panelWidth * 0.36f)));
			if (interactable)
				m_Selectables.Add(slot.Button);
		}

		private void RefreshCommandExecute()
		{
			if (
				DexcRuntimeUIState.Confirming
				|| DexcRuntimeUIState.Page != DexcRuntimeUIPage.CommandDetail
				|| DexcRuntimeUIState.AsyncExecution != null
				|| m_FooterButtons.Count < 2
			)
				return;
			var enabled = DexcRuntimeCommandExecution.CanExecute();

			var execute = m_FooterButtons[1];
			if (execute.Root.gameObject.activeInHierarchy)
			{
				DexcUGUIApi.Set(execute.Button, "interactable", enabled);
				DexcUGUIApi.SetImageColor(execute.Root.gameObject, Color.white);
				DexcUGUIApi.Set(execute.Label, "color", enabled ? Palette.PrimaryText : Palette.SecondaryText);
				DexcUGUIApi.SetColorBlock(execute.Button, Palette, enabled ? AccentColor : Palette.Disabled, true, enabled);
				DexcUGUIApi.SetCanvasRendererColor(execute.Root.gameObject, enabled ? AccentColor : Palette.Disabled);
				if (enabled && !m_Selectables.Contains(execute.Button))
					m_Selectables.Add(execute.Button);
				else if (!enabled)
					m_Selectables.Remove(execute.Button);
				DexcRuntimeUIState.ClampSelectedIndex(m_Selectables.Count);
				WireNavigation();
			}
		}

		private void Navigate(DexcRuntimeUIPage page)
		{
			DexcRuntimeUIState.Navigate(page);
			Rebuild(true);
		}

		/// <summary>Adds a label row and returns it, or null once the row cap is reached and the row is dropped.</summary>
		private RowSlot AddLabel(string label, Color color, Color? textColor = null, float? height = null, int fontSize = DexcRuntimeFormLayout.FontSize)
		{
			var slot = NextRow();
			if (slot == null)
			{
				return null;
			}

			var rowHeight = height ?? 0f;
			slot.Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rowHeight);
			slot.ConfigureLabel(label, rowHeight, PanelColor, textColor ?? Palette.PrimaryText, Palette);
			DexcUGUIApi.Set(slot.Label, "fontSize", fontSize);
			// LayoutGroup controls width, but not height. Measure the actual font at that width
			// before the next layout pass; a minimum hit height is not a two-line text budget.
			var padding = (RectOffset)DexcUGUIApi.Get(m_Content.GetComponent(DexcUGUIApi.VerticalLayoutGroupType), "padding");
			var width = MainWidth() - 16f - padding.horizontal;
			slot.Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(1f, width));
			var textRect = (RectTransform)slot.Label.transform;
			var textInset = textRect.offsetMin.y - textRect.offsetMax.y;
			var preferred = Convert.ToSingle(DexcUGUIApi.Get(slot.Label, "preferredHeight"));
			var fittedHeight = Mathf.Max(rowHeight, Mathf.Ceil(preferred + textInset));
			slot.Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, fittedHeight);
			DexcUGUIApi.Set(slot.Layout, "minHeight", fittedHeight);
			DexcUGUIApi.Set(slot.Layout, "preferredHeight", fittedHeight);
			return slot;
		}

		private RowSlot AddButton(string label, UnityAction action, Color? color = null, Color? textColor = null)
		{
			var slot = NextRow();
			if (slot == null)
			{
				return null;
			}

			slot.Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, m_Layout.HitHeight);
			slot.ConfigureButton(label, action, true, m_Layout.HitHeight, color ?? RowColor, Palette, false);
			var padding = (RectOffset)DexcUGUIApi.Get(m_Content.GetComponent(DexcUGUIApi.VerticalLayoutGroupType), "padding");
			var width = MainWidth() - 16f - padding.horizontal;
			slot.Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
			var fitted = Mathf.Max(m_Layout.HitHeight, Mathf.Ceil(Convert.ToSingle(DexcUGUIApi.Get(slot.Label, "preferredHeight"))) + 8f);
			slot.Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, fitted);
			DexcUGUIApi.Set(slot.Layout, "minHeight", fitted);
			DexcUGUIApi.Set(slot.Layout, "preferredHeight", fitted);
			if (textColor.HasValue)
			{
				DexcUGUIApi.Set(slot.Label, "color", textColor.Value);
			}

			m_Selectables.Add(slot.Button);
			return slot;
		}

		private RowSlot AddInput(
			string label,
			string value,
			UnityAction<string> action,
			Color color,
			Color? labelColor = null,
			bool multiline = false,
			UnityAction<string> confirmed = null,
			Action unbind = null,
			bool interactable = true,
			string component = null,
			string note = null
		)
		{
			var slot = NextRow();
			if (slot == null)
			{
				return null;
			}

			var height = DexcRuntimeFormLayout.RowHeight(m_Layout.RowHeight, m_Layout.HitHeight);
			if (multiline)
			{
				height *= 3f;
			}

			var padding = (RectOffset)DexcUGUIApi.Get(m_Content.GetComponent(DexcUGUIApi.VerticalLayoutGroupType), "padding");
			var width = MainWidth() - 16f - padding.horizontal;
			slot.Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(1f, width));
			// The components after a vector's first row keep its arrangement, so they line up under its value. The row's band
			// keeps an inset around its content.
			var content = Mathf.Max(1f, width - RowSlot.BandLeft - RowSlot.BandRight);
			if (!string.IsNullOrEmpty(label) || string.IsNullOrEmpty(component))
				m_Arrangement = ArrangeInput(slot, label, content);
			slot.ConfigureInput(
				m_Arrangement.Stacked ? label : BreakLabel(slot, label, m_Arrangement.LabelWidth),
				value,
				action,
				height,
				HeaderColor,
				RowColor,
				Palette,
				multiline,
				confirmed,
				unbind,
				component,
				m_Arrangement,
				note
			);
			slot.CommandComponent = component;
			DexcUGUIApi.Set(slot.Input, "interactable", interactable);
			if (labelColor.HasValue)
			{
				DexcUGUIApi.Set(slot.Label, "color", labelColor.Value);
			}

			if (interactable)
			{
				m_Selectables.Add(slot.Input);
			}

			return slot;
		}

		private void SwitchLayout(DexcRuntimeUILayoutMode mode)
		{
			DexcRuntimeUIState.LayoutOverride = mode;
			Rebuild(true);
		}

		/// <summary>The reference layout of the screen this renderer draws on, for whether it is side by side.</summary>
		private DexcRuntimeUIReferenceLayout CurrentReference()
		{
			var width = (int)m_Layout.Screen.width;
			var height = (int)m_Layout.Screen.height;
			var profile = m_UseProfileOverride ? m_ProfileOverride : DexcRuntimeUIReferenceResolver.ResolveCurrentProfile(width, height);
			return DexcRuntimeUIReferenceResolver.Calculate(width, height, m_Layout.SafeArea, profile);
		}

		/// <summary>The page's logical width: the panel's, less the columns side by side at its left.</summary>
		private float MainWidth() => m_Layout.Panel.width / Mathf.Max(0.0001f, m_Layout.Scale) - m_SectionWidth - m_ListWidth;

		/// <summary>
		/// Side by side, fills the sections and the list beside a page, and moves the page right of them. The columns are for
		/// the pointer; the gamepad keeps to the page and goes back to the list with Back.
		/// </summary>
		private void BuildSplitColumns()
		{
			var scale = Mathf.Max(0.0001f, m_Layout.Scale);
			var split = DexcDebugger.IsInitialized && CurrentReference().Split;
			// The layout has already put the header, the page and the footer beside the sections.
			m_SectionWidth = (m_Layout.Header.x - m_Layout.Panel.x) / scale;
			var contentWidth = m_Layout.Content.width / scale;
			var contentHeight = m_Layout.Content.height / scale;
			m_ListWidth = 0f;
			if (split)
				DexcRuntimeUIRenderer.SplitColumns(DexcRuntimeUIState.Page, contentWidth, out _, out m_ListWidth);
			var top = m_Layout.Footer.height / scale;
			SetBottomLeftRect(m_ScrollView, new Rect(m_SectionWidth + m_ListWidth, top, contentWidth - m_ListWidth, contentHeight));
			FillColumn(
				m_SplitSections,
				m_SectionSlots,
				m_SectionWidth,
				0f,
				0f,
				m_Layout.Panel.height / scale,
				split && m_SectionWidth > 0f ? DexcRuntimeUIRenderer.SectionRows() : null,
				DexcRuntimeUIRenderer.SectionKey(DexcRuntimeUIState.Page)
			);
			var listPage = DexcRuntimeUIRenderer.SplitListPage(DexcRuntimeUIState.Page);
			FillColumn(
				m_SplitList,
				m_ListSlots,
				m_ListWidth,
				top,
				m_SectionWidth,
				contentHeight,
				m_ListWidth > 0f ? DexcRuntimeUIRenderer.ListRowsOf(listPage.Value).Where(row => row.ListItem).ToList() : null,
				DexcRuntimeUIRenderer.OpenEntryKey(DexcRuntimeUIState.Page)
			);
		}

		private void FillColumn(
			RectTransform column,
			List<RowSlot> slots,
			float width,
			float top,
			float left,
			float height,
			List<DexcUIRow> rows,
			string current
		)
		{
			column.gameObject.SetActive(rows != null);
			foreach (var slot in slots)
				slot.Root.gameObject.SetActive(false);
			if (rows == null)
				return;
			SetBottomLeftRect(column, new Rect(left, top, width, height));
			var parent = column.Find("Content") ?? column;
			for (var index = 0; index < rows.Count; index++)
			{
				while (slots.Count <= index)
					slots.Add(CreateRow(column.name + slots.Count, parent));
				var slot = slots[index];
				var row = rows[index];
				slot.Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(1f, width - 16f));
				slot.ConfigureButton(
					row.Label,
					() =>
					{
						row.Activated();
						Rebuild(true);
					},
					true,
					m_Layout.HitHeight,
					row.Key == current ? Palette.Selection : RowColor,
					Palette,
					false
				);
				DecorateListRow(slot, row);
			}
		}

		/// <summary>Label beside the value, or above it when the row is narrow or the label wraps too far.</summary>
		private static DexcRuntimeFormLayout.Arrangement ArrangeInput(RowSlot slot, string label, float width)
		{
			if (string.IsNullOrEmpty(label))
				return DexcRuntimeFormLayout.Arrange(width, null);
			DexcUGUIApi.Set(slot.Label, "text", label);
			DexcUGUIApi.Set(slot.Label, "fontSize", DexcRuntimeFormLayout.FontSize);
			var rect = (RectTransform)slot.Label.transform;
			rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
			return DexcRuntimeFormLayout.Arrange(
				width,
				labelWidth =>
				{
					var broken = BreakLabel(slot, label, labelWidth);
					DexcUGUIApi.Set(slot.Label, "text", broken);
					rect.sizeDelta = new Vector2(labelWidth, 20f);
					var preferred = Convert.ToSingle(DexcUGUIApi.Get(slot.Label, "preferredHeight"));
					return Mathf.Max(1, Mathf.RoundToInt(preferred / LabelLineHeight));
				}
			);
		}

		/// <summary>A label broken between its words to fit a column of <paramref name="width"/>, measured with the row's label.</summary>
		private static string BreakLabel(RowSlot slot, string label, float width) =>
			DexcRuntimeFormLayout.BreakLabel(
				label,
				width,
				line =>
				{
					DexcUGUIApi.Set(slot.Label, "text", line);
					return Convert.ToSingle(DexcUGUIApi.Get(slot.Label, "preferredWidth"));
				}
			);

		private void SetInputError(RowSlot slot, string message)
		{
			if (slot == null)
			{
				return;
			}

			var selectedIndex = DexcRuntimeUIState.SelectedIndex;
			var selected = selectedIndex < 0 || selectedIndex >= m_Selectables.Count ? null : m_Selectables[selectedIndex];
			// As in IMGUI, the message is written under the value and the "…" action keeps the right end, so the value only
			// gives up width on the rows that have that action.
			slot.ShowError(message, slot.Source == null || slot.Source.Kind == DexcUIRowKind.Input, Palette);
			var auxiliary = slot.Auxiliary;
			DexcUGUIApi.SetColorBlock(slot.Error, Palette, RowColor, false, true);
			DexcUGUIApi.Set(slot.Error.GetComponentInChildren(DexcUGUIApi.TextType), "color", Palette.PrimaryText);
			slot.SetAuxiliary(
				auxiliary == null
					? null
					: () =>
					{
						m_FormReturnScroll = m_Content.anchoredPosition;
						DexcRuntimeUIState.SetSelectedIndex(
							m_Selectables.IndexOf(m_Selectables.Contains(slot.Primary) ? slot.Primary : slot.Error)
						);
						auxiliary();
						Rebuild(true);
					}
			);
			m_Selectables.Remove(slot.Error);
			if (auxiliary != null && m_Selectables.Contains(slot.Primary))
			{
				m_Selectables.Insert(m_Selectables.IndexOf(slot.Primary) + 1, slot.Error);
			}
			else if (auxiliary != null && slot.Source != null)
			{
				m_Selectables.Add(slot.Error);
			}

			if (selected != null && m_Selectables.Contains(selected))
			{
				DexcRuntimeUIState.SetSelectedIndex(m_Selectables.IndexOf(selected));
			}

			WireNavigation();
		}

		private RowSlot NextRow()
		{
			var index = m_Rows.Count(row => row.Root.gameObject.activeSelf);
			if (index >= MaximumRows)
				return null;
			while (m_Rows.Count <= index)
				m_Rows.Add(CreateRow("Row" + m_Rows.Count, m_Content));
			return m_Rows[index];
		}

		private void DeactivateRows()
		{
			foreach (var row in m_Rows)
			{
				row.UnbindInput();
				row.SetFocused(false);
				row.Root.gameObject.SetActive(false);
			}
		}

		private void WireNavigation()
		{
			for (var index = 0; index < m_Selectables.Count; index++)
			{
				var up = index == 0 ? m_Selectables[0] : m_Selectables[index - 1];
				var down = index == m_Selectables.Count - 1 ? m_Selectables[index] : m_Selectables[index + 1];
				DexcUGUIApi.SetExplicitNavigation(m_Selectables[index], up, down);
				var selectable = m_Selectables[index];
				DexcUGUIApi.SetCancelHandler(
					selectable,
					OnNativeCancel,
					DexcUGUIApi.ButtonType.IsInstanceOfType(selectable) && selectable.transform.IsChildOf(m_Content) ? m_ScrollRect : null
				);
			}
		}

		private void OnNativeCancel(object eventData)
		{
			if (!IsActive || !DexcRuntimeUIState.IsOpen || !DexcRuntimeUIState.ConsumeInputAction())
				return;
			var selected = DexcUGUIApi.CurrentSelected;
			if (
				selected != null
				&& DexcUGUIApi.InputFieldType.IsInstanceOfType(selected)
				&& Equals(DexcUGUIApi.Get(selected, "isFocused"), true)
			)
			{
				selected.GetType().GetMethod("DeactivateInputField", Type.EmptyTypes)?.Invoke(selected, null);
				DexcRuntimeUIState.NotifyInputState(DebugMenuInputState.Navigation);
			}
			else
			{
				DexcRuntimeUIState.Back();
				Rebuild(true);
			}
			eventData.GetType().GetMethod("Use", Type.EmptyTypes)?.Invoke(eventData, null);
		}

		private void SyncSelection()
		{
			foreach (var slot in AllSlots)
				slot.SetFocused(false);
			// While the gamepad is in the column of sections it marks a section; nothing on the page is selected.
			var sectionFocus = DexcRuntimeSplitHome.SectionFocus;
			for (var section = 0; section < m_SectionSlots.Count; section++)
				m_SectionSlots[section].SetFocused(section == sectionFocus && DexcRuntimeUIState.NavigationFocusVisible);
			if (sectionFocus >= 0)
			{
				DexcUGUIApi.SetSelected(null);
				m_LastSelectionIndex = -1;
				m_LastSelected = null;
				return;
			}
			if (m_Selectables.Count == 0)
				return;
			var index = Mathf.Clamp(DexcRuntimeUIState.SelectedIndex, 0, m_Selectables.Count - 1);
			var current = DexcUGUIApi.CurrentSelected;
			var currentIndex = current == null ? -1 : m_Selectables.FindIndex(selectable => ReferenceEquals(selectable, current));
			if (index != m_LastSelectionIndex || currentIndex < 0)
			{
				DexcUGUIApi.SetSelected(m_Selectables[index].gameObject);
			}
			else
			{
				index = currentIndex;
				DexcRuntimeUIState.SetSelectedIndex(index);
			}
			var selected = DexcUGUIApi.CurrentSelected ?? m_Selectables[index];
			var selectedSlot = AllSlots.FirstOrDefault(slot => selected != null && (selected == slot.Button || selected == slot.Primary));
			selectedSlot?.SetFocused(true);
			DexcUGUIApi.ApplySelectedColor(selected);
			var selectionChanged = index != m_LastSelectionIndex || !ReferenceEquals(selected, m_LastSelected);
			m_LastSelectionIndex = index;
			m_LastSelected = selected;
			if (selectionChanged && selected != null && selected.transform.IsChildOf(m_Content))
			{
				ScrollSelectionIntoView(selected.transform as RectTransform);
			}
		}

		private void ScrollSelectionIntoView(RectTransform selected)
		{
			if (selected == null)
			{
				return;
			}
			Canvas.ForceUpdateCanvases();
			var range = m_Content.rect.height - m_Viewport.rect.height;
			if (range <= 0f)
			{
				return;
			}
			var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(m_Viewport, selected);
			var viewport = m_Viewport.rect;
			var delta =
				bounds.max.y > viewport.yMax ? viewport.yMax - bounds.max.y
				: bounds.min.y < viewport.yMin ? viewport.yMin - bounds.min.y
				: 0f;
			if (Mathf.Abs(delta) < 0.01f)
			{
				return;
			}
			// Idle pointer drag, wheel and inertia own the position until selection changes.
			var normalized = 1f - (m_Content.anchoredPosition.y + delta) / range;
			DexcUGUIApi.Set(m_ScrollRect, "verticalNormalizedPosition", Mathf.Clamp01(normalized));
		}

		private sealed class RowSlot
		{
			internal RowSlot(
				RectTransform root,
				Component button,
				Component label,
				GameObject focusMarker,
				GameObject inputRoot,
				GameObject inputVisual,
				Component input,
				Component inputText,
				Component layout,
				Component error,
				Component componentLabel
			)
			{
				Root = root;
				Button = button;
				Label = label;
				FocusMarker = focusMarker;
				InputRoot = inputRoot;
				InputVisual = inputVisual;
				Input = input;
				InputText = inputText;
				Layout = layout;
				Error = error;
				ComponentLabel = componentLabel;
			}

			internal RectTransform Root { get; }
			internal Component Button { get; }
			internal Component Label { get; }
			internal GameObject FocusMarker { get; }
			internal GameObject InputRoot { get; }
			internal GameObject InputVisual { get; }
			internal Component Input { get; }
			internal Component InputText { get; }
			internal Component Layout { get; }
			internal Component Error { get; }
			internal Component ComponentLabel { get; }
			internal UnityAction ButtonAction { get; private set; }
			internal Component Choice { get; private set; }
			internal Component Primary => Choice != null && ((Behaviour)Choice).enabled ? Choice : Input;
			internal UnityAction Auxiliary;
			internal string CommandComponent;

			/// <summary>The shared row this slot draws, for pages built from rows.</summary>
			internal DexcUIRow Source;
			internal string NavigationKey;
			internal Component Slider;
			internal Component Note;
			internal Component Detail;
			internal Component Trailing;
			internal Component Previous;
			internal Component Next;
			internal Component Chevron;
			internal RectTransform Switch;
			internal RectTransform Knob;
			internal RectTransform Bridge;
			internal RectTransform ErrorOutline;
			internal Component ErrorLine;

			/// <summary>A field's band keeps this inset around the label and value, as the IMGUI field row does.</summary>
			internal const float BandLeft = 8f;
			internal const float BandRight = 6f;
			internal const float BandVertical = 4f;

			/// <summary>The band a field's row sits on, filled with the selection colour while the row has the focus.</summary>
			private Color? m_Band;
			private Color m_Selection;
			private RectTransform m_Checkbox;
			private RectTransform m_CheckboxFill;
			private bool m_IsBoolean;
			private bool m_Steps;
			private Action m_UnbindInput;

			// Where ConfigureInput put the value, so an error or a "…" action can be fitted around it afterwards.
			private float m_ValueX;
			private float m_ContentBottom;
			private float m_ErrorLineHeight;

			internal void HideOptionalParts()
			{
				foreach (var part in new[] { Note, Detail, Trailing, Chevron, ErrorLine })
					part?.gameObject.SetActive(false);
				ErrorOutline?.gameObject.SetActive(false);
				m_ErrorLineHeight = 0f;
				foreach (var step in new[] { Previous, Next })
				{
					if (step == null)
						continue;
					DexcUGUIApi.SetEvent(step, "onClick", null);
					step.gameObject.SetActive(false);
				}
				Switch?.gameObject.SetActive(false);
			}

			internal void UnbindInput()
			{
				HideOptionalParts();
				Auxiliary = null;
				m_IsBoolean = false;
				m_Steps = false;
				if (m_Checkbox != null)
				{
					m_Checkbox.gameObject.SetActive(false);
				}

				Source = null;
				NavigationKey = null;
				CommandComponent = null;
				if (Slider != null)
				{
					DexcUGUIApi.SetEvent(Slider, "onValueChanged", null);
					Slider.gameObject.SetActive(false);
				}

				if (Choice != null)
				{
					Choice.gameObject.SetActive(false);
					DexcUGUIApi.Enable(Choice, false);
					DexcUGUIApi.SetEvent(Choice, "onClick", null);
				}

				m_UnbindInput?.Invoke();
				m_UnbindInput = null;
				DexcUGUIApi.SetEvent(Input, "onEndEdit", null);
				DexcUGUIApi.SetEvent(Input, "onValueChanged", null);
			}

			internal void ConfigureButton(
				string text,
				UnityAction action,
				bool interactable,
				float height,
				Color color,
				DexcRuntimeUISemanticPalette palette,
				bool primary,
				bool centered = false
			)
			{
				UnbindInput();
				m_Band = null;
				Bridge?.gameObject.SetActive(false);
				ButtonAction = interactable ? action : null;
				SetAuxiliary(null);
				ComponentLabel.gameObject.SetActive(false);
				Root.gameObject.SetActive(true);
				SetFocused(false);
				InputRoot.SetActive(false);
				InputVisual.SetActive(true);
				Label.gameObject.SetActive(true);
				SetStretch((RectTransform)Label.transform, 0f);
				((RectTransform)Label.transform).offsetMin = new Vector2(8f, 4f);
				((RectTransform)Label.transform).offsetMax = new Vector2(-8f, -4f);
				DexcUGUIApi.Set(Label, "alignment", centered ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft);
				DexcUGUIApi.SetRaycastTarget(Root.gameObject, true);
				DexcUGUIApi.SetRaycastTarget(InputRoot, true);
				DexcUGUIApi.SetRaycastTarget(InputVisual, false);
				DexcUGUIApi.SetImageColor(InputRoot, Color.clear);
				DexcUGUIApi.SetImageColor(InputVisual, Color.white);
				DexcUGUIApi.Enable(Button, true);
				DexcUGUIApi.Set(Button, "targetGraphic", Root.GetComponent(DexcUGUIApi.ImageType));
				DexcUGUIApi.Set(Button, "interactable", interactable);
				DexcUGUIApi.Enable(Input, false);
				DexcUGUIApi.Set(Input, "interactable", false);
				DexcUGUIApi.SetEvent(Input, "onValueChanged", null);
				DexcUGUIApi.Set(Input, "text", string.Empty);
				DexcUGUIApi.Set(Label, "text", text ?? string.Empty);
				DexcUGUIApi.Set(Label, "fontSize", DexcRuntimeFormLayout.FontSize);
				DexcUGUIApi.Set(Label, "color", interactable ? palette.PrimaryText : palette.SecondaryText);
				DexcUGUIApi.SetImageColor(Root.gameObject, Color.white);
				DexcUGUIApi.SetColorBlock(Button, palette, color, primary, interactable);
				DexcUGUIApi.SetCanvasRendererColor(Root.gameObject, interactable ? color : palette.Disabled);
				DexcUGUIApi.Set(Layout, "minHeight", height);
				DexcUGUIApi.Set(Layout, "preferredHeight", height);
				DexcUGUIApi.Set(Layout, "flexibleWidth", 1f);
				DexcUGUIApi.SetEvent(Button, "onClick", action);
			}

			internal void ConfigureSlider(DexcUIRow row, DexcRuntimeUISemanticPalette palette, Action refresh)
			{
				if (Slider == null)
				{
					var rect = CreateRect("Slider", Root);
					SetStretch(rect, 8f);
					Slider = rect.gameObject.AddComponent(DexcUGUIApi.SliderType);
					var track = CreateRect("Track", rect);
					track.anchorMin = new Vector2(0f, .5f);
					track.anchorMax = new Vector2(1f, .5f);
					track.offsetMin = new Vector2(0f, -2f);
					track.offsetMax = new Vector2(0f, 2f);
					DexcUGUIApi.SetImage(track.gameObject, palette.Header, true);
					var handle = CreateRect("Handle", rect);
					handle.sizeDelta = new Vector2(12f, 0f);
					DexcUGUIApi.SetImage(handle.gameObject, palette.Accent, true);
					DexcUGUIApi.Set(Slider, "handleRect", handle);
					DexcUGUIApi.Set(Slider, "targetGraphic", handle.GetComponent(DexcUGUIApi.ImageType));
				}

				Slider.gameObject.SetActive(true);
				DexcUGUIApi.Set(Slider, "interactable", row.IsEnabled);
				DexcUGUIApi.Set(Slider, "value", row.SliderValue());
				DexcUGUIApi.SetEvent(
					Slider,
					"onValueChanged",
					new UnityAction<float>(value =>
					{
						// A pooled slot may already draw another row; only its current row takes the value.
						if (!ReferenceEquals(Source, row))
						{
							return;
						}

						row.Slid(value);
						refresh();
					})
				);
			}

			internal void ConfigureLabel(string text, float height, Color color, Color textColor, DexcRuntimeUISemanticPalette palette)
			{
				ConfigureButton(text, null, false, height, color, palette, false);
				DexcUGUIApi.Set(Button, "targetGraphic", null);
				DexcUGUIApi.SetImageColor(Root.gameObject, color);
				DexcUGUIApi.ResetGraphicTint(Root.gameObject);
				SetStretch((RectTransform)Label.transform, 0f);
				((RectTransform)Label.transform).offsetMin = new Vector2(2f, 0f);
				((RectTransform)Label.transform).offsetMax = new Vector2(-2f, 0f);
				DexcUGUIApi.Set(Label, "color", textColor);
				DexcUGUIApi.Set(Label, "alignment", TextAnchor.UpperLeft);
				DexcUGUIApi.Enable(Button, false);
				DexcUGUIApi.Set(Button, "interactable", false);
				DexcUGUIApi.SetRaycastTarget(Root.gameObject, false);
			}

			internal void ConfigureInput(
				string label,
				string value,
				UnityAction<string> action,
				float height,
				Color color,
				Color inputColor,
				DexcRuntimeUISemanticPalette palette,
				bool multiline,
				UnityAction<string> confirmed,
				Action unbind,
				string component,
				DexcRuntimeFormLayout.Arrangement arrangement,
				string note
			)
			{
				UnbindInput();
				m_UnbindInput = unbind;
				m_Band = color;
				m_Selection = palette.Selection;
				Bridge?.gameObject.SetActive(false);
				SetAuxiliary(null);
				ButtonAction = null;
				Root.gameObject.SetActive(true);
				SetFocused(false);
				DexcUGUIApi.Set(Button, "targetGraphic", null);
				DexcUGUIApi.Enable(Button, false);
				DexcUGUIApi.Set(Button, "interactable", false);
				DexcUGUIApi.SetEvent(Button, "onClick", null);
				DexcUGUIApi.SetRaycastTarget(Root.gameObject, false);
				DexcUGUIApi.SetImageColor(Root.gameObject, color);
				DexcUGUIApi.ResetGraphicTint(Root.gameObject);
				Label.gameObject.SetActive(true);
				InputRoot.SetActive(true);
				InputVisual.SetActive(true);
				InputText.gameObject.SetActive(true);
				DexcUGUIApi.SetRaycastTarget(InputRoot, true);
				DexcUGUIApi.SetRaycastTarget(InputVisual, false);
				DexcUGUIApi.SetImageColor(InputRoot, Color.clear);
				DexcUGUIApi.SetImageColor(InputVisual, Color.white);
				DexcUGUIApi.Set(Label, "text", label ?? string.Empty);
				DexcUGUIApi.Set(Label, "fontSize", DexcRuntimeFormLayout.FontSize);
				DexcUGUIApi.Set(Label, "color", palette.PrimaryText);
				DexcUGUIApi.Set(InputText, "fontSize", DexcRuntimeFormLayout.FontSize);
				DexcUGUIApi.Set(InputText, "color", palette.PrimaryText);
				DexcUGUIApi.Set(Label, "alignment", TextAnchor.UpperLeft);
				DexcUGUIApi.Set(InputText, "alignment", multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
				var labelRect = (RectTransform)Label.transform;
				var hasLabel = !string.IsNullOrEmpty(label);
				var labelWidth = arrangement.Stacked ? Root.rect.width - BandLeft - BandRight : arrangement.LabelWidth;
				labelRect.anchorMin = labelRect.anchorMax = new Vector2(0f, 1f);
				labelRect.pivot = new Vector2(0f, 1f);
				labelRect.sizeDelta = new Vector2(labelWidth, 20f);
				var labelHeight = hasLabel ? Mathf.Ceil(Convert.ToSingle(DexcUGUIApi.Get(Label, "preferredHeight"))) : 0f;
				labelRect.sizeDelta = new Vector2(labelWidth, labelHeight);
				var hasNote = hasLabel && !string.IsNullOrEmpty(note);
				var noteHeight = hasNote ? DexcRuntimeFormLayout.NoteFontSize + 3f : 0f;
				Note.gameObject.SetActive(hasNote);
				if (hasNote)
				{
					DexcUGUIApi.Set(Note, "text", note.ToUpperInvariant());
					DexcUGUIApi.Set(Note, "color", palette.SecondaryText);
					var noteRect = (RectTransform)Note.transform;
					noteRect.anchorMin = noteRect.anchorMax = new Vector2(0f, 1f);
					noteRect.pivot = new Vector2(0f, 1f);
					noteRect.sizeDelta = new Vector2(labelWidth, noteHeight);
				}
				var errorRect = (RectTransform)Error.transform;
				errorRect.sizeDelta = new Vector2(DexcRuntimeFormLayout.ErrorWidth, height);
				var inputRect = (RectTransform)InputRoot.transform;
				var componentRect = (RectTransform)ComponentLabel.transform;
				var componentInset = string.IsNullOrEmpty(component) ? 0f : DexcRuntimeFormLayout.ComponentWidth + DexcRuntimeFormLayout.ColumnGap;
				float valueTop;
				if (arrangement.Stacked)
				{
					// The label is on its own line above the value, and the value spans the row.
					var gap = labelHeight > 0f ? DexcRuntimeFormLayout.LabelGap : 0f;
					valueTop = BandVertical + labelHeight + noteHeight + gap;
					m_ValueX = 0f;
					m_ContentBottom = valueTop + height;
					labelRect.anchoredPosition = new Vector2(BandLeft, -BandVertical);
					if (hasNote)
						((RectTransform)Note.transform).anchoredPosition = new Vector2(BandLeft, -BandVertical - labelHeight);
				}
				else
				{
					// The label column on the left and the value beside it, each centred on the taller of the two.
					var content = Mathf.Max(height, labelHeight + noteHeight);
					var labelTop = BandVertical + Mathf.Max(0f, (content - labelHeight - noteHeight) * 0.5f);
					valueTop = BandVertical + Mathf.Max(0f, (content - height) * 0.5f);
					m_ValueX = arrangement.ValueX;
					m_ContentBottom = BandVertical + content;
					labelRect.anchoredPosition = new Vector2(BandLeft, -labelTop);
					if (hasNote)
						((RectTransform)Note.transform).anchoredPosition = new Vector2(BandLeft, -labelTop - labelHeight);
				}
				// The value reaches the band's right edge; a "…" action takes the end of it only on the rows that have one.
				inputRect.anchorMin = new Vector2(0f, 1f);
				inputRect.anchorMax = Vector2.one;
				inputRect.pivot = new Vector2(0.5f, 1f);
				inputRect.offsetMin = new Vector2(BandLeft + m_ValueX + componentInset, -valueTop - height);
				inputRect.offsetMax = new Vector2(-BandRight, -valueTop);
				componentRect.anchorMin = componentRect.anchorMax = new Vector2(0f, 1f);
				componentRect.pivot = new Vector2(0f, 1f);
				componentRect.anchoredPosition = new Vector2(BandLeft + m_ValueX, -valueTop);
				errorRect.anchorMin = errorRect.anchorMax = Vector2.one;
				errorRect.pivot = Vector2.one;
				errorRect.anchoredPosition = new Vector2(-BandRight, -valueTop);
				var totalHeight = m_ContentBottom + BandVertical;
				Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, totalHeight);
				ComponentLabel.gameObject.SetActive(!string.IsNullOrEmpty(component));
				DexcUGUIApi.Set(ComponentLabel, "text", component ?? string.Empty);
				componentRect.sizeDelta = new Vector2(DexcRuntimeFormLayout.ComponentWidth, height);
				SetStretch((RectTransform)InputVisual.transform, 0f);
				var textRect = (RectTransform)InputText.transform;
				DexcUGUIApi.Set(InputText, "horizontalOverflow", HorizontalWrapMode.Wrap);
				SetStretch(textRect, 0f);
				textRect.offsetMin = new Vector2(DexcRuntimeFormLayout.HorizontalPadding, DexcRuntimeFormLayout.VerticalPadding);
				textRect.offsetMax = -textRect.offsetMin;
				DexcUGUIApi.Enable(Input, true);
				DexcUGUIApi.Set(Input, "interactable", true);
				// The field keeps its colour when its row is selected: the row's fill and outline show the focus.
				DexcUGUIApi.SetColorBlock(Input, palette, inputColor, false, true, keepsSelected: true);
				DexcUGUIApi.SetCanvasRendererColor(InputVisual, inputColor);
				DexcUGUIApi.SetEvent(Input, "onValueChanged", null);
				DexcUGUIApi.SetEnum(Input, "lineType", multiline ? "MultiLineNewline" : "SingleLine");
				DexcUGUIApi.Set(Input, "text", value ?? string.Empty);
				DexcUGUIApi.SetEvent(Input, "onValueChanged", action);
				DexcUGUIApi.SetEvent(Input, "onEndEdit", confirmed);
				DexcUGUIApi.Set(Layout, "minHeight", totalHeight);
				DexcUGUIApi.Set(Layout, "preferredHeight", totalHeight);
				DexcUGUIApi.Set(Layout, "flexibleWidth", 1f);
			}

			internal void ConfigureChoice(
				string value,
				UnityAction action,
				bool enabled,
				DexcRuntimeUISemanticPalette palette,
				bool boolean = false,
				Action<int> cycle = null
			)
			{
				DexcUGUIApi.Enable(Input, false);
				m_IsBoolean = boolean;
				m_Steps = !boolean && cycle != null;
				DexcUGUIApi.Set(InputText, "horizontalOverflow", HorizontalWrapMode.Overflow);
				if (Choice == null)
				{
					var rect = CreateRect("ChoiceButton", InputRoot.transform);
					SetStretch(rect, 0f);
					DexcUGUIApi.Add(rect.gameObject, DexcUGUIApi.ImageType);
					DexcUGUIApi.SetImageColor(rect.gameObject, Color.clear);
					Choice = DexcUGUIApi.Add(rect.gameObject, DexcUGUIApi.ButtonType);
				}

				DexcUGUIApi.Enable(Choice, true);
				Choice.gameObject.SetActive(true);
				DexcUGUIApi.Set(Choice, "interactable", enabled);
				DexcUGUIApi.Set(Choice, "targetGraphic", InputVisual.GetComponent(DexcUGUIApi.ImageType));
				// A switch is drawn on its own, so the field behind it only shows while the row is pressed or selected.
				DexcUGUIApi.SetColorBlock(Choice, palette, boolean ? Color.clear : palette.Row, false, enabled, keepsSelected: true);
				DexcUGUIApi.SetCanvasRendererColor(InputVisual, boolean ? Color.clear : palette.Row);
				DexcUGUIApi.SetEvent(Choice, "onClick", action);
				var height = InputRoot.GetComponent<RectTransform>().rect.height;
				var choiceRect = (RectTransform)Choice.transform;
				SetStretch(choiceRect, 0f);
				if (m_Steps)
				{
					// The steps sit at both ends, above the value; pressing the value itself still opens its page.
					choiceRect.offsetMin = new Vector2(height, 0f);
					choiceRect.offsetMax = new Vector2(-height, 0f);
					PlaceStep(Previous, 0f, height, () => cycle(-1), enabled, palette);
					PlaceStep(Next, 1f, height, () => cycle(1), enabled, palette);
				}
				else if (!boolean)
				{
					Chevron.gameObject.SetActive(true);
					Chevron.transform.SetAsLastSibling();
					var chevron = (RectTransform)Chevron.transform;
					SetStretch(chevron, 0f);
					chevron.offsetMax = new Vector2(-8f, 0f);
					DexcUGUIApi.Set(Chevron, "color", palette.SecondaryText);
				}
				SetChoiceValue(value, palette);
			}

			void PlaceStep(Component step, float side, float size, Action action, bool enabled, DexcRuntimeUISemanticPalette palette)
			{
				step.gameObject.SetActive(true);
				step.transform.SetAsLastSibling();
				var rect = (RectTransform)step.transform;
				rect.anchorMin = new Vector2(side, 0f);
				rect.anchorMax = new Vector2(side, 1f);
				rect.pivot = new Vector2(side, 0.5f);
				rect.anchoredPosition = Vector2.zero;
				rect.sizeDelta = new Vector2(size, 0f);
				DexcUGUIApi.Set(step, "interactable", enabled);
				DexcUGUIApi.SetColorBlock(step, palette, palette.Row, false, enabled);
				DexcUGUIApi.SetEvent(step, "onClick", new UnityAction(() => action()));
			}

			internal void SetChoiceValue(string value, DexcRuntimeUISemanticPalette palette)
			{
				var on = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
				DexcUGUIApi.Set(InputText, "text", m_IsBoolean ? string.Empty : DexcRuntimeUIRenderer.ChoiceText(value));
				DexcUGUIApi.Set(InputText, "alignment", m_Steps ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft);
				var textRect = (RectTransform)InputText.transform;
				var height = InputRoot.GetComponent<RectTransform>().rect.height;
				textRect.offsetMin = new Vector2(m_Steps ? height : 6f, 4f);
				textRect.offsetMax = new Vector2(m_Steps ? -height : -20f, -4f);
				if (m_Checkbox != null)
					m_Checkbox.gameObject.SetActive(false);
				Switch.gameObject.SetActive(m_IsBoolean);
				if (!m_IsBoolean)
					return;

				// The switch sits at the right end: a track in the accent colour when on, the knob at the side it is on.
				var size = Mathf.Min(22f, height - 4f);
				Switch.SetAsLastSibling();
				Switch.anchorMin = Switch.anchorMax = new Vector2(1f, 0.5f);
				Switch.pivot = new Vector2(1f, 0.5f);
				Switch.anchoredPosition = new Vector2(-2f, 0f);
				Switch.sizeDelta = new Vector2(size * 1.9f, size);
				Knob.anchorMin = Knob.anchorMax = new Vector2(on ? 1f : 0f, 0.5f);
				Knob.pivot = new Vector2(on ? 1f : 0f, 0.5f);
				Knob.anchoredPosition = new Vector2(on ? -2f : 2f, 0f);
				Knob.sizeDelta = new Vector2(size - 4f, size - 4f);
				DexcUGUIApi.SetImageColor(Switch.gameObject, on ? palette.Accent : palette.Row);
				DexcUGUIApi.SetImageColor(Knob.gameObject, on ? palette.PrimaryText : palette.SecondaryText);
			}

			/// <summary>Shows the "…" action at the value's right end, or hides it and gives the value the whole width.</summary>
			internal void SetAuxiliary(UnityAction action)
			{
				var visible = action != null;
				Error.gameObject.SetActive(visible);
				DexcUGUIApi.Set(Error, "interactable", visible);
				DexcUGUIApi.SetEvent(Error, "onClick", action);
				if (!InputRoot.activeSelf)
					return;
				var inputRect = (RectTransform)InputRoot.transform;
				var end = visible ? DexcRuntimeFormLayout.ErrorWidth + DexcRuntimeFormLayout.ColumnGap : 0f;
				inputRect.offsetMax = new Vector2(-BandRight - end, inputRect.offsetMax.y);
			}

			/// <summary>
			/// Outlines a field that is not valid and writes its message under the value, growing the row to fit it, as IMGUI
			/// does. A choice is not outlined: its message alone tells what is wrong.
			/// </summary>
			internal void ShowError(string message, bool outlined, DexcRuntimeUISemanticPalette palette)
			{
				var visible = !string.IsNullOrEmpty(message);
				ErrorOutline.gameObject.SetActive(visible && outlined);
				ErrorOutline.SetAsLastSibling();
				ErrorLine.gameObject.SetActive(visible);
				var height = 0f;
				if (visible)
				{
					var rect = (RectTransform)ErrorLine.transform;
					var width = Mathf.Max(1f, Root.rect.width - BandLeft - BandRight - m_ValueX);
					DexcUGUIApi.Set(ErrorLine, "text", message);
					DexcUGUIApi.Set(ErrorLine, "color", palette.FieldErrorText);
					DexcUGUIApi.Set(ErrorLine, "horizontalOverflow", HorizontalWrapMode.Wrap);
					rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
					rect.pivot = new Vector2(0f, 1f);
					rect.sizeDelta = new Vector2(width, 14f);
					height = Mathf.Ceil(Convert.ToSingle(DexcUGUIApi.Get(ErrorLine, "preferredHeight"))) + 2f;
					rect.sizeDelta = new Vector2(width, height);
					rect.anchoredPosition = new Vector2(BandLeft + m_ValueX, -m_ContentBottom - 2f);
				}
				if (Mathf.Approximately(height, m_ErrorLineHeight))
					return;
				m_ErrorLineHeight = height;
				var total = m_ContentBottom + height + BandVertical;
				Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, total);
				DexcUGUIApi.Set(Layout, "minHeight", total);
				DexcUGUIApi.Set(Layout, "preferredHeight", total);
			}

			internal void SetFocused(bool focused)
			{
				var shown = focused && Root.gameObject.activeInHierarchy;
				FocusMarker.SetActive(shown);
				// A field's band fills while its row has the focus; a button row's own colours already show it.
				if (m_Band.HasValue)
					DexcUGUIApi.SetImageColor(Root.gameObject, shown ? m_Selection : m_Band.Value);
			}
		}
	}

	internal static class DexcUGUIApi
	{
		/// <summary>State of this class. Owned by <see cref="DexcUIRoot"/>; nothing here survives a root reset.</summary>
		internal sealed class State
		{
			internal readonly Type ScrollbarType = Resolve("UnityEngine.UI.Scrollbar");
			internal readonly Type SelectableType = Resolve("UnityEngine.UI.Selectable");
			internal readonly Type SliderType = Resolve("UnityEngine.UI.Slider");
			internal readonly Type CanvasScalerType = Resolve("UnityEngine.UI.CanvasScaler");
			internal readonly Type GraphicRaycasterType = Resolve("UnityEngine.UI.GraphicRaycaster");
			internal readonly Type ImageType = Resolve("UnityEngine.UI.Image");
			internal readonly Type TextType = Resolve("UnityEngine.UI.Text");
			internal readonly Type ButtonType = Resolve("UnityEngine.UI.Button");
			internal readonly Type InputFieldType = Resolve("UnityEngine.UI.InputField");
			internal readonly Type MaskType = Resolve("UnityEngine.UI.Mask");
			internal readonly Type ScrollRectType = Resolve("UnityEngine.UI.ScrollRect");
			internal readonly Type VerticalLayoutGroupType = Resolve("UnityEngine.UI.VerticalLayoutGroup");
			internal readonly Type HorizontalLayoutGroupType = Resolve("UnityEngine.UI.HorizontalLayoutGroup");
			internal readonly Type ContentSizeFitterType = Resolve("UnityEngine.UI.ContentSizeFitter");
			internal readonly Type LayoutElementType = Resolve("UnityEngine.UI.LayoutElement");
			internal readonly Type EventSystemType = Resolve("UnityEngine.EventSystems.EventSystem");
			internal readonly Type StandaloneInputModuleType = Resolve("UnityEngine.EventSystems.StandaloneInputModule");
			internal readonly Type InputSystemUIInputModuleType = Type.GetType(
				"UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem",
				false
			);
			internal GameObject FallbackEventSystem;
			internal readonly Font DefaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
		}

		internal static State Data => DexcUIRoot.Current.UGUIApi;
		internal static Type ScrollbarType => Data.ScrollbarType;
		internal static Type SelectableType => Data.SelectableType;
		internal static Type SliderType => Data.SliderType;
		internal static Type CanvasScalerType => Data.CanvasScalerType;
		internal static Type GraphicRaycasterType => Data.GraphicRaycasterType;
		internal static Type ImageType => Data.ImageType;
		internal static Type TextType => Data.TextType;
		internal static Type ButtonType => Data.ButtonType;
		internal static Type InputFieldType => Data.InputFieldType;
		internal static Type MaskType => Data.MaskType;
		internal static Type ScrollRectType => Data.ScrollRectType;
		internal static Type VerticalLayoutGroupType => Data.VerticalLayoutGroupType;
		internal static Type HorizontalLayoutGroupType => Data.HorizontalLayoutGroupType;
		internal static Type ContentSizeFitterType => Data.ContentSizeFitterType;
		internal static Type LayoutElementType => Data.LayoutElementType;
		internal static Type EventSystemType => Data.EventSystemType;
		private static Type StandaloneInputModuleType => Data.StandaloneInputModuleType;
		private static Type InputSystemUIInputModuleType => Data.InputSystemUIInputModuleType;
		private static GameObject FallbackEventSystem
		{
			get => Data.FallbackEventSystem;
			set => Data.FallbackEventSystem = value;
		}
		internal static Font DefaultFont => Data.DefaultFont;

		/// <summary>
		/// Creates a pointer-only EventSystem while the uGUI menu is open and the scene has none, so the menu can be
		/// clicked and touched. Gamepad and keyboard navigation stay with the <see cref="DebugMenuInputProvider"/>.
		/// </summary>
		internal static void EnsureEventSystem()
		{
			if (EventSystemType == null)
				return;
			if (FallbackEventSystem != null)
			{
				// A scene that brings its own EventSystem takes over; two active EventSystems make Unity warn every frame.
				if (UnityEngine.Object.FindObjectsByType(EventSystemType, FindObjectsSortMode.None).Length > 1)
					ReleaseFallbackEventSystem();
				return;
			}
			var current = EventSystemType.GetProperty("current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
			if (current != null)
				return;
			var gameObject = new GameObject("DexcEventSystem");
			UnityEngine.Object.DontDestroyOnLoad(gameObject);
			gameObject.AddComponent(EventSystemType);
#if ENABLE_INPUT_SYSTEM
			var moduleType = InputSystemUIInputModuleType;
#else
			var moduleType = StandaloneInputModuleType;
#endif
			if (moduleType != null)
			{
				var module = gameObject.AddComponent(moduleType);
				// The module assigns default actions on enable; drop navigation so only the pointer is native.
				foreach (var action in new[] { "move", "submit", "cancel" })
					Set(module, action, null);
			}
			FallbackEventSystem = gameObject;
		}

		/// <summary>Removes the fallback EventSystem when the uGUI menu is closed or another EventSystem became current.</summary>
		internal static void ReleaseFallbackEventSystem()
		{
			if (FallbackEventSystem == null)
				return;
			var fallback = FallbackEventSystem;
			FallbackEventSystem = null;
			if (Application.isPlaying)
				UnityEngine.Object.Destroy(fallback);
			else
				UnityEngine.Object.DestroyImmediate(fallback);
		}

		internal static bool HasFallbackEventSystem => FallbackEventSystem != null;

		internal static void ConfigureCanvasScaler(Component scaler)
		{
			SetEnum(scaler, "uiScaleMode", "ConstantPixelSize");
			Set(scaler, "scaleFactor", 1f);
			Set(scaler, "referencePixelsPerUnit", 100f);
		}

		internal static Component CurrentSelected
		{
			get
			{
				var current = EventSystemType.GetProperty("current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
				var gameObject =
					current == null
						? null
						: EventSystemType.GetProperty("currentSelectedGameObject")?.GetValue(current, null) as GameObject;
				return gameObject == null ? null : gameObject.GetComponent(SelectableType);
			}
		}

		internal static Component Add(GameObject gameObject, Type type)
		{
			if (type == null)
				throw new InvalidOperationException("The optional com.unity.ugui package is unavailable.");
			return gameObject.AddComponent(type);
		}

		internal static Component SetImage(GameObject gameObject, Color color, bool raycastTarget)
		{
			var image = gameObject.GetComponent(ImageType) ?? Add(gameObject, ImageType);
			Set(image, "color", color);
			Set(image, "raycastTarget", raycastTarget);
			return image;
		}

		internal static void SetImageColor(GameObject gameObject, Color color)
		{
			var image = gameObject.GetComponent(ImageType);
			if (image != null)
				Set(image, "color", color);
		}

		internal static void SetImageSprite(GameObject gameObject, Sprite sprite)
		{
			var image = gameObject.GetComponent(ImageType);
			if (image != null)
				Set(image, "sprite", sprite);
		}

		internal static void SetRaycastTarget(GameObject gameObject, bool value)
		{
			var image = gameObject.GetComponent(ImageType);
			if (image != null)
				Set(image, "raycastTarget", value);
		}

		internal static void ConfigureVerticalLayout(Component layout, int padding, float spacing)
		{
			Set(layout, "padding", new RectOffset(padding, padding, padding, padding));
			Set(layout, "spacing", spacing);
			Set(layout, "childControlWidth", true);
			Set(layout, "childControlHeight", false);
			Set(layout, "childForceExpandWidth", true);
			Set(layout, "childForceExpandHeight", false);
		}

		internal static void ConfigureHorizontalLayout(Component layout, int padding, float spacing, bool forceExpandWidth = true)
		{
			Set(layout, "padding", new RectOffset(padding, padding, 4, 4));
			Set(layout, "spacing", spacing);
			Set(layout, "childControlWidth", true);
			Set(layout, "childControlHeight", true);
			Set(layout, "childForceExpandWidth", forceExpandWidth);
			Set(layout, "childForceExpandHeight", true);
		}

		internal static void SetExplicitNavigation(Component selectable, Component up, Component down)
		{
			if (selectable == null)
				return;
			var property = selectable.GetType().GetProperty("navigation");
			if (property == null)
				return;
			var navigation = property.GetValue(selectable, null);
			var type = navigation.GetType();
			type.GetProperty("mode")?.SetValue(navigation, Enum.Parse(type.GetNestedType("Mode"), "Explicit"), null);
			type.GetProperty("selectOnUp")?.SetValue(navigation, up, null);
			type.GetProperty("selectOnDown")?.SetValue(navigation, down, null);
			property.SetValue(selectable, navigation, null);
		}

		internal static void SetColorBlock(
			Component selectable,
			DexcRuntimeUISemanticPalette palette,
			Color normal,
			bool primary,
			bool interactable,
			Color? disabled = null,
			bool keepsSelected = false
		)
		{
			if (selectable == null)
				return;
			var property = selectable.GetType().GetProperty("colors");
			if (property == null)
				return;
			var colors = property.GetValue(selectable, null);
			if (colors == null)
				return;
			var preservesError = normal == palette.Error;
			Set(colors, "normalColor", interactable ? normal : palette.Disabled);
			Set(
				colors,
				"highlightedColor",
				interactable
					? preservesError
						? normal
						: palette.Selection
					: palette.Disabled
			);
			Set(
				colors,
				"pressedColor",
				interactable
					? preservesError
						? normal
						: primary
							? palette.Focus
							: palette.Accent
					: palette.Disabled
			);
			Set(
				colors,
				"selectedColor",
				interactable
					? preservesError || primary || keepsSelected
						? normal
						: palette.Selection
					: palette.Disabled
			);
			Set(colors, "disabledColor", disabled ?? palette.Disabled);
			Set(colors, "colorMultiplier", 1f);
			Set(colors, "fadeDuration", 0.05f);
			property.SetValue(selectable, colors, null);
		}

		internal static void SetCanvasRendererColor(GameObject gameObject, Color color)
		{
			var renderer = gameObject == null ? null : gameObject.GetComponent<CanvasRenderer>();
			if (renderer != null)
				renderer.SetColor(color);
		}

		internal static void ResetGraphicTint(GameObject gameObject)
		{
			var graphic = gameObject.GetComponent(ImageType);
			graphic
				?.GetType()
				.GetMethod("CrossFadeColor", new[] { typeof(Color), typeof(float), typeof(bool), typeof(bool) })
				.Invoke(graphic, new object[] { Color.white, 0f, true, true });
			SetCanvasRendererColor(gameObject, Color.white);
		}

		internal static void ApplySelectedColor(Component selectable)
		{
			var colors = Get(selectable, "colors");
			var selectedColor = Get(colors, "selectedColor");
			var targetGraphic = Get(selectable, "targetGraphic") as Component;
			if (selectedColor is Color color && targetGraphic != null)
				SetCanvasRendererColor(targetGraphic.gameObject, color);
		}

		internal static void SetSelected(GameObject gameObject)
		{
			var current = EventSystemType.GetProperty("current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
			if (current == null)
				return;
			EventSystemType.GetMethod("SetSelectedGameObject", new[] { typeof(GameObject) })?.Invoke(current, new object[] { gameObject });
		}

		internal static void SetCancelHandler(Component selectable, Action<object> action, Component scroll)
		{
			if (selectable == null)
				return;
			// Keep the optional uGUI assembly boundary: bind its public EventTrigger
			// API just like the existing reflected Button/InputField events.
			var triggerType = Resolve("UnityEngine.EventSystems.EventTrigger");
			var trigger = selectable.GetComponent(triggerType) ?? Add(selectable.gameObject, triggerType);
			SetTrigger(trigger, "Cancel", action);
			// EventTrigger is also a drag receiver. Preserve the existing parent
			// ScrollRect route for buttons; InputField keeps its own text-drag route.
			if (scroll == null)
				return;
			foreach (var name in new[] { "InitializePotentialDrag", "BeginDrag", "Drag", "EndDrag", "Scroll" })
			{
				var method = scroll.GetType().GetMethod("On" + name);
				SetTrigger(
					trigger,
					name,
					value =>
					{
						if (scroll == null)
							return;
						// The trigger is the drag receiver as well as the button, so the input module would still click the row
						// the finger lifts on. A drag that scrolls the list is not a press.
						if (name == "BeginDrag")
							Set(value, "eligibleForClick", false);
						method.Invoke(scroll, new[] { value });
					}
				);
			}
		}

		private static void SetTrigger(Component trigger, string eventName, Action<object> action)
		{
			var triggerType = trigger.GetType();
			var entries = (System.Collections.IList)Get(trigger, "triggers");
			var entryType = triggerType.GetNestedType("Entry");
			var eventId = entryType.GetField("eventID");
			var cancel = Enum.Parse(eventId.FieldType, eventName);
			object entry = null;
			foreach (var candidate in entries)
				if (Equals(eventId.GetValue(candidate), cancel))
				{
					entry = candidate;
					break;
				}
			if (entry == null)
			{
				entry = Activator.CreateInstance(entryType);
				eventId.SetValue(entry, cancel);
				entries.Add(entry);
			}
			var callback = entryType.GetField("callback").GetValue(entry);
			// This Core API must remain reachable under IL2CPP stripping. A reflected
			// lookup alone can disappear even while EventTrigger itself is preserved.
			((UnityEventBase)callback).RemoveAllListeners();
			var add = callback.GetType().GetMethod("AddListener");
			Action<object> guarded = value =>
			{
				if (trigger != null)
					action(value);
			};
			var listener = Delegate.CreateDelegate(add.GetParameters()[0].ParameterType, guarded.Target, guarded.Method);
			add.Invoke(callback, new object[] { listener });
		}

		internal static void InvokeButton(Component button)
		{
			if (button == null || !ButtonType.IsInstanceOfType(button))
				return;
			(Get(button, "onClick") as UnityEvent)?.Invoke();
		}

		internal static void Activate(Component selectable)
		{
			if (selectable == null)
				return;
			if (ButtonType.IsInstanceOfType(selectable))
			{
				InvokeButton(selectable);
				return;
			}
			if (InputFieldType.IsInstanceOfType(selectable))
			{
				SetSelected(selectable.gameObject);
				selectable.GetType().GetMethod("ActivateInputField", Type.EmptyTypes)?.Invoke(selectable, null);
			}
		}

		internal static object Get(object target, string propertyName)
		{
			return !IsAlive(target) ? null : target.GetType().GetProperty(propertyName)?.GetValue(target, null);
		}

		internal static void SetEvent(Component component, string propertyName, Delegate listener)
		{
			var value = Get(component, propertyName) as UnityEventBase;
			if (value == null)
				return;
			value.RemoveAllListeners();
			if (listener == null)
				return;
			// The owner can be destroyed while a previously obtained event is retained.
			// Direct Core UnityEvent calls also keep these generic bindings visible to IL2CPP.
			if (value is UnityEvent buttonEvent && listener is UnityAction buttonAction)
			{
				buttonEvent.AddListener(() =>
				{
					if (component != null)
						buttonAction();
				});
			}
			else if (value is UnityEvent<float> sliderEvent && listener is UnityAction<float> sliderAction)
			{
				sliderEvent.AddListener(number =>
				{
					if (component != null)
						sliderAction(number);
				});
			}
			else if (value is UnityEvent<string> inputEvent && listener is UnityAction<string> inputAction)
			{
				inputEvent.AddListener(text =>
				{
					if (component != null)
						inputAction(text);
				});
			}
			else
			{
				throw new ArgumentException("Unsupported uGUI event/listener pair: " + propertyName, nameof(listener));
			}
		}

		internal static void Enable(Component component, bool enabled)
		{
			if (component != null && component is Behaviour behaviour)
				behaviour.enabled = enabled;
		}

		internal static void SetEnum(Component component, string propertyName, string value)
		{
			if (component == null)
				return;
			var property = component.GetType().GetProperty(propertyName);
			if (property != null)
				property.SetValue(component, Enum.Parse(property.PropertyType, value), null);
		}

		internal static void Set(object target, string propertyName, object value)
		{
			if (!IsAlive(target))
				return;
			var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
			if (property != null && property.CanWrite)
				property.SetValue(target, value, null);
		}

		private static Type Resolve(string fullName)
		{
			return Type.GetType(fullName + ", UnityEngine.UI", false);
		}

		private static bool IsAlive(object target)
		{
			return target != null && (!(target is UnityEngine.Object unityObject) || unityObject != null);
		}
	}
}
#endif
