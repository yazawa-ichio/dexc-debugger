using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dexc
{
	internal readonly struct DexcRuntimeUIToolkitLayout
	{
		internal DexcRuntimeUIToolkitLayout(
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

	internal sealed class DexcRuntimeUIToolkitRenderer : MonoBehaviour
	{
		private const float HeaderHeight = 44f;
		private const float FooterHeight = 46f;
		private const int MaximumRows = DexcRuntimeUIState.PageSize;

		/// <summary>State of this class. Owned by <see cref="DexcUIRoot"/>; nothing here survives a root reset.</summary>
		internal sealed class State
		{
			internal DexcRuntimeUIToolkitRenderer Instance;
			internal DexcRuntimeUISkinSnapshot FailedSkin;
			internal bool FailedWithoutSkin;
			internal string LastDependencyDiagnosticSession = string.Empty;
		}

		internal static State Data => DexcUIRoot.Current.UIToolkitRenderer;
		private static DexcRuntimeUIToolkitRenderer Instance
		{
			get => Data.Instance;
			set => Data.Instance = value;
		}
		private static DexcRuntimeUISkinSnapshot FailedSkin
		{
			get => Data.FailedSkin;
			set => Data.FailedSkin = value;
		}
		private static bool FailedWithoutSkin
		{
			get => Data.FailedWithoutSkin;
			set => Data.FailedWithoutSkin = value;
		}
		private static string LastDependencyDiagnosticSession
		{
			get => Data.LastDependencyDiagnosticSession;
			set => Data.LastDependencyDiagnosticSession = value;
		}

		private readonly struct StyleSnapshot
		{
			internal StyleSnapshot(DexcRuntimeUISemanticPalette palette, Font font, Texture2D texture, Sprite panelSprite)
			{
				Palette = palette;
				Font = font;
				Texture = texture;
				PanelSprite = panelSprite;
			}

			internal DexcRuntimeUISemanticPalette Palette { get; }
			internal Font Font { get; }
			internal Texture2D Texture { get; }
			internal Sprite PanelSprite { get; }
		}

		private enum RowKind
		{
			Label,
			Button,
			Input,
			Choice,
			Slider,
		}

		private sealed class RowModel
		{
			internal RowKind Kind;
			internal string Text;
			internal string Value;
			internal string Component;
			internal Action Action;
			internal Action Auxiliary;
			internal Action<string> ValueChanged;
			internal Color Color;
			internal Color TextColor;
			internal bool Enabled;
			internal bool Multiline;
			internal bool Boolean;

			/// <summary>The shared row this item draws, for pages built from rows.</summary>
			internal DexcUIRow Source;

			/// <summary>The list entry a button row draws, for its mark, second line and right-end text.</summary>
			internal DexcUIRow Entry;
			internal string NavigationKey;
		}

		private sealed class Selection
		{
			internal RowModel Row;
			internal Button Button;
			internal Action Action;
			internal Action Auxiliary;
			internal bool IsText;
			internal bool IsError;
		}

		private UIDocument m_Document;
		private DexcRuntimeUISkinSnapshot m_SkinSnapshot;
		private PanelSettings m_PanelSettingsTemplate;
		private PanelSettings m_PanelSettings;
		private VisualElement m_Root;
		private VisualElement m_SafeAreaRoot;
		private VisualElement m_Panel;
		private VisualElement m_Header;
		private Button m_Back;
		private Label m_Title;
		private Button m_Close;
		private Button m_Favorite;
		private Button m_History;

		// Side by side: the sections and the list beside a page, pointer only, laid over the panel's left.
		private VisualElement m_SplitSections;
		private Button m_Single;
		private float m_SideWidth;
		private Button m_Split;
		private bool m_SplitShown;
		private ScrollView m_SplitList;
		private ListView m_ContentList;
		private Label m_Measure;
		private bool m_Remeasure;
		private VisualElement m_Footer;
		private Button m_FooterSecondary;
		private Button m_FooterPrimary;
		private readonly List<RowModel> m_Items = new List<RowModel>();
		private readonly List<Selection> m_Selectables = new List<Selection>();
		private DexcRuntimeUIToolkitLayout m_Layout;
		private Action m_FooterSecondaryAction;
		private Action m_FooterPrimaryAction;
		private int m_LastSignature = int.MinValue;
		private int m_SettingVersion = -1;
		private int m_InspectorVersion = -1;
		private DexcRuntimeUIPage m_LastBuiltPage;
		private Vector2 m_FormReturnScroll;
		private DebugCommandDraftId m_FormReturnDraft;
		private int m_ProfileWidth;
		private int m_ProfileHeight;
		private Rect m_ProfileSafeArea;
		private bool m_UseProfileOverride;
		private DexcRuntimeUIProfileKind m_ProfileOverride;
		private bool m_CaptureVisible = true;
		private TextField m_FocusedTextField;
		private StyleSnapshot m_Style;
		private VisualElement m_HoveredRow;
		private VisualElement m_PressedRow;
		private const float PointerDragThresholdSquared = 100f;
		private int m_PointerDownId = -1;
		private Vector2 m_PointerDownPosition;
		private bool m_PointerDragged;

		internal static bool IsActive => DexcRuntimeUIRendererSelection.UseUIToolkit && Instance != null;

		/// <summary>Whether the last build failed for the current Skin, so IMGUI draws in its place until a Skin that works arrives.</summary>
		internal static bool HasFailed => FailedWithoutSkin || FailedSkin != null;
#if UNITY_INCLUDE_TESTS
		internal static GameObject RootForTests => Instance == null ? null : Instance.gameObject;
		internal static UIDocument DocumentForTests => Instance == null ? null : Instance.m_Document;
		internal static PanelSettings PanelSettingsTemplateForTests => Instance == null ? null : Instance.m_PanelSettingsTemplate;
		internal static PanelSettings PanelSettingsForTests => Instance == null ? null : Instance.m_PanelSettings;
		internal static VisualElement SafeAreaRootForTests => Instance == null ? null : Instance.m_SafeAreaRoot;
		internal static VisualElement HeaderForTests => Instance == null ? null : Instance.m_Header;
		internal static ListView ContentListForTests => Instance == null ? null : Instance.m_ContentList;
		internal static VisualElement FooterForTests => Instance == null ? null : Instance.m_Footer;
#endif
		internal static int VisibleRowCount => Instance == null ? 0 : Instance.m_Items.Count;
#if UNITY_INCLUDE_TESTS
		internal static int ResolvedRowCountForTests =>
			Instance == null || Instance.m_ContentList == null
				? 0
				: Instance.m_ContentList.Query<VisualElement>(className: "dexc-uitk-row").ToList().Count;
#endif
		internal static int SelectionCount => Instance == null ? 0 : Instance.m_Selectables.Count;
		internal static int SelectableCount => SelectionCount;
#if UNITY_INCLUDE_TESTS
		internal static int RendererCountForTests => Resources.FindObjectsOfTypeAll<DexcRuntimeUIToolkitRenderer>().Length;

		/// <summary>Whether the footer shows an Execute button that can be pressed.</summary>
		internal static bool ExecuteInteractableForTests =>
			Instance != null
			&& Instance.m_FooterPrimary != null
			&& Instance.m_FooterPrimary.style.display != DisplayStyle.None
			&& Instance.m_FooterPrimary.text == "Execute"
			&& Instance.m_FooterPrimary.enabledSelf;
		internal static TextField FocusedTextFieldForTests => Instance == null ? null : Instance.m_FocusedTextField;
		internal static bool HasHorizontalOverflowForTests => Instance != null && Instance.HasHorizontalRowOverflow();
		internal static bool HasVerticalOverflowForTests =>
			Instance != null
			&& Instance.ContentScrollView != null
			&& Instance.ContentScrollView.contentContainer.layout.height
				> Instance.ContentScrollView.contentViewport.layout.height + 0.01f;
		internal static float VerticalScrollOffsetForTests =>
			Instance == null || Instance.ContentScrollView == null ? 0f : Instance.ContentScrollView.scrollOffset.y;
		internal static DexcRuntimeUIToolkitLayout CurrentLayoutForTests =>
			Instance == null ? default(DexcRuntimeUIToolkitLayout) : Instance.m_Layout;
		internal static Rect PanelScreenRectForTests => ScreenRect(Instance == null ? null : Instance.m_Panel);
		internal static Rect HeaderScreenRectForTests => ScreenRect(Instance == null ? null : Instance.m_Header);
		internal static Rect ContentScreenRectForTests => ScreenRect(Instance == null ? null : Instance.m_ContentList);
		internal static Rect FooterScreenRectForTests =>
			ScreenRect(Instance == null || Instance.m_Footer.resolvedStyle.display == DisplayStyle.None ? null : Instance.m_Footer);
		internal static Rect CloseScreenRectForTests => ScreenRect(Instance == null ? null : Instance.m_Close);
		internal static Rect CancelBoundsForTests =>
			ScreenRect(
				Instance == null || Instance.m_FooterPrimary.resolvedStyle.display == DisplayStyle.None
					? null
					: Instance.m_FooterPrimary
			);
		internal static DexcRuntimeUISemanticPalette SemanticPaletteForTests =>
			Instance == null ? DexcRuntimeUISemanticPalette.FromSnapshot(null) : Instance.m_Style.Palette;
		internal static int FocusMarkerCountForTests =>
			Instance == null || Instance.m_ContentList == null
				? 0
				: Instance
					.m_ContentList.Query<VisualElement>(className: "dexc-uitk-focus-marker")
					.ToList()
					.Count(marker => marker.style.display.value != DisplayStyle.None);
		internal static int PrimaryActionIndexForTests =>
			Instance == null
				? -1
				: Instance.m_Selectables.FindIndex(selection => ReferenceEquals(selection.Button, Instance.m_FooterPrimary));

		// A validation message is the error carried by a field row; the renderer shows it as the row's "!" marker and
		// opens the text from there, so the marker is what the capture tests measure.
		internal static int ValidationMessageItemIndexForTests =>
			Instance == null ? -1 : Instance.m_Items.IndexOf(Instance.ValidationMessageItem());
		internal static string ValidationMessageForTests =>
			Instance == null || Instance.ValidationMessageItem() == null
				? string.Empty
				: InputError(Instance.ValidationMessageItem());
		internal static string ValidationMessageTailForTests => ValidationMessageForTests;
		internal static Rect ValidationMessageScreenRectForTests =>
			Instance == null ? default(Rect) : Instance.ValidationMessageScreenRect();
		internal static Rect ValidationMessageTailScreenRectForTests => ValidationMessageScreenRectForTests;
		internal static bool ValidationMessageFullyVisibleForTests => Instance != null && Instance.ValidationMessageFullyVisible();
		internal static DebugCommandFieldId[] VisibleCommandFieldIdsForTests =>
			Instance == null
				? Array.Empty<DebugCommandFieldId>()
				: Instance
					.m_Items.Where(item => item.Source?.Tag is DebugCommandFieldId)
					.Select(item => (DebugCommandFieldId)item.Source.Tag)
					.Distinct()
					.ToArray();
#endif

		private DexcRuntimeUISkinSnapshot Skin =>
			DexcDebugger.CurrentSession == null ? null : DexcDebugger.CurrentSession.Options.RuntimeUISkin;
		private ScrollView ContentScrollView => m_ContentList == null ? null : m_ContentList.Q<ScrollView>();

		private bool HasHorizontalRowOverflow()
		{
			var scrollView = ContentScrollView;
			if (scrollView == null)
				return false;
			var viewport = scrollView.contentViewport.worldBound;
			if (viewport.width <= 0f)
				return false;
			var rows = m_ContentList.Query<VisualElement>(className: "dexc-uitk-row").ToList();
			return rows.Any(row => row.worldBound.xMin < viewport.xMin - 0.01f || row.worldBound.xMax > viewport.xMax + 0.01f);
		}

		private RowModel ValidationMessageItem()
		{
			return m_Items.FirstOrDefault(item => (InputError(item) ?? string.Empty).StartsWith("Error:", StringComparison.Ordinal))
				?? m_Items.FirstOrDefault(item => !string.IsNullOrEmpty(InputError(item)));
		}

		private RowModel ValidationMessageItem(DebugCommandFieldId fieldId)
		{
			return m_Items.FirstOrDefault(item =>
				item.Source?.Tag is DebugCommandFieldId id && id == fieldId && !string.IsNullOrEmpty(InputError(item))
			);
		}

		private VisualElement ValidationMessageRow()
		{
			return ValidationMessageRow(ValidationMessageItem());
		}

		private VisualElement ValidationMessageRow(RowModel item)
		{
			return item == null || m_ContentList == null
				? null
				: m_ContentList
					.Query<VisualElement>(className: "dexc-uitk-row")
					.ToList()
					.FirstOrDefault(row => ReferenceEquals(row.userData, item));
		}

		private Rect ValidationMessageScreenRect()
		{
			var row = ValidationMessageRow();
			return ScreenRect(row == null ? null : row.Q<Button>("FieldError"));
		}

		private bool ValidationMessageFullyVisible()
		{
			var row = ValidationMessageRow();
			var marker = row == null ? null : row.Q<Button>("FieldError");
			return marker != null
				&& marker.panel != null
				&& marker.style.visibility.value == Visibility.Visible
				&& Contains(ScreenRect(m_ContentList), ScreenRect(marker), 1f)
				&& !HasHorizontalRowOverflow();
		}

		private static bool Contains(Rect outer, Rect inner, float tolerance)
		{
			return inner.width > 0f
				&& inner.height > 0f
				&& inner.xMin >= outer.xMin - tolerance
				&& inner.xMax <= outer.xMax + tolerance
				&& inner.yMin >= outer.yMin - tolerance
				&& inner.yMax <= outer.yMax + tolerance;
		}

		private DexcRuntimeUISemanticPalette Palette => m_Style.Palette;
		private Color PanelColor => Palette.Panel;
		private Color HeaderColor => Palette.Header;
		private Color RowColor => Palette.Row;
		private Color AccentColor => Palette.Accent;
		private Color ErrorColor => Palette.Error;

		internal static void CreateForBootstrap()
		{
			if (!DexcRuntimeUIRendererSelection.UseUIToolkit)
				return;
			var skin = DexcDebugger.CurrentSession == null ? null : DexcDebugger.CurrentSession.Options.RuntimeUISkin;
			if (FailedWithoutSkin && skin == null || skin != null && ReferenceEquals(FailedSkin, skin))
				return;
			var template = skin == null ? null : skin.UIToolkitPanelSettingsTemplate as PanelSettings;
			if (template == null || template.themeStyleSheet == null)
			{
				DestroyInstance();
				FailedSkin = skin;
				FailedWithoutSkin = skin == null;
				RecordDependencyDiagnostic(
					skin == null || skin.UIToolkitPanelSettingsTemplate == null
						? "runtime-uitoolkit-panel-settings-missing"
						: "runtime-uitoolkit-panel-settings-invalid"
				);
				return;
			}
			FailedSkin = null;
			FailedWithoutSkin = false;
			if (
				Instance != null
				&& ReferenceEquals(Instance.m_SkinSnapshot, skin)
				&& ReferenceEquals(Instance.m_PanelSettingsTemplate, template)
			)
				return;
			DestroyInstance();

			var root = new GameObject("DexcRuntimeUIToolkit");
			// No hide flags: Play Mode owns the lifetime, so leaving it destroys this with everything else.
			DontDestroyOnLoad(root);
			Instance = root.AddComponent<DexcRuntimeUIToolkitRenderer>();
			Instance.m_SkinSnapshot = skin;
			Instance.BuildHierarchy(template);
		}

		private static void RecordDependencyDiagnostic(string code)
		{
			if (!DexcRuntimeUIBridge.TryGetSession(out var sessionId))
				return;
			var key = sessionId + ":" + code;
			if (string.Equals(LastDependencyDiagnosticSession, key, StringComparison.Ordinal))
				return;
			LastDependencyDiagnosticSession = key;
			DexcRuntimeUIBridge.AddDiagnostic(
				code,
				"DexcRuntimeUISkin",
				"UI Toolkit requires a Skin snapshot with a valid PanelSettings template and ThemeStyleSheet. Falling back to the default IMGUI Runtime UI."
			);
		}

		/// <summary>Destroys the renderer objects and the PanelSettings clone it instantiated.</summary>
		internal static void DestroyInstance()
		{
			var instances = Resources.FindObjectsOfTypeAll<DexcRuntimeUIToolkitRenderer>();
			foreach (var instance in instances)
			{
				if (instance == null)
					continue;
				var settings = instance.m_PanelSettings;
				DestroyImmediate(instance.gameObject);
				if (settings != null)
					DestroyImmediate(settings);
			}
			Instance = null;
		}

#if UNITY_INCLUDE_TESTS
		internal static void DestroyForTests()
		{
			DestroyInstance();
			FailedSkin = null;
			FailedWithoutSkin = false;
			LastDependencyDiagnosticSession = string.Empty;
		}
#endif

		internal static void TickActive()
		{
			CreateForBootstrap();
			if (Instance != null)
				Instance.Tick();
		}

#if UNITY_INCLUDE_TESTS
		internal static DexcRuntimeUIToolkitLayout CalculateLayoutForTests(int width, int height, Rect safeArea, DexcRuntimeUIPage page)
		{
			return CalculateLayout(width, height, safeArea, page, DexcRuntimeUIReferenceResolver.LegacyProfileForTests(width, height));
		}
#endif

		internal static DexcRuntimeUIToolkitLayout CalculateLayoutForProfileTests(
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
			DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(true);
			CreateForBootstrap();
			if (Instance == null)
				throw new InvalidOperationException("The Runtime UI Toolkit renderer is unavailable for the current Skin snapshot.");
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

		internal static void RefreshForTests()
		{
			if (Instance != null)
				Instance.Rebuild(true);
		}

		internal static void ScrollValidationMessageIntoViewForTests()
		{
			if (Instance == null || Instance.m_ContentList == null)
				return;
			var index = ValidationMessageItemIndexForTests;
			Instance.ScrollItemIntoViewForTests(index);
		}

		internal static void ScrollValidationMessageIntoViewForTests(DebugCommandFieldId fieldId)
		{
			if (Instance == null || Instance.m_ContentList == null)
				return;
			Instance.ScrollItemIntoViewForTests(Instance.m_Items.IndexOf(Instance.ValidationMessageItem(fieldId)));
		}

		internal static void ScrollCommandFieldForTests(DebugCommandFieldId fieldId)
		{
			if (Instance == null)
			{
				return;
			}

			Instance.ScrollItemIntoViewForTests(
				Instance.m_Items.FindIndex(item => item.Source?.Tag is DebugCommandFieldId id && id == fieldId)
			);
		}

		private void ScrollItemIntoViewForTests(int index)
		{
			if (index < 0)
				return;
			m_ContentList.ScrollToItem(index);
		}
#endif

		internal static void SetDocumentVisibleForCaptureTests(bool visible)
		{
			if (Instance == null)
				return;
			Instance.m_CaptureVisible = visible;
			Instance.UpdateVisibility();
		}

#if UNITY_INCLUDE_TESTS
		internal static TextField SettingInputForTests(DebugSettingId id, string component = null) =>
			Instance
				?.m_ContentList.Query<TextField>()
				.ToList()
				.FirstOrDefault(input =>
					input.userData is RowModel item && item.Source != null && item.Source.Key == DexcRuntimeSettingForm.Key(id, component)
				);

		internal static void ActivateSelectedForTests()
		{
			ActivateProviderSelection();
		}
#endif

		internal static void ActivateProviderSelection()
		{
			if (Instance != null)
				Instance.ActivateCurrentSelection();
		}

		/// <summary>True while a TextField of the menu has keyboard focus.</summary>
		internal static bool IsTextFocused =>
			Instance != null && Instance.m_FocusedTextField != null && Instance.m_FocusedTextField.panel != null;

		/// <summary>Blurs the TextField being edited and returns keyboard focus to the menu root.</summary>
		/// <summary>Left or right on a choice that steps in place. False when the selection is not on such a choice.</summary>
		internal static bool CycleSelected(int direction)
		{
			if (Instance == null || Instance.m_Selectables.Count == 0)
				return false;
			var selection = Instance.m_Selectables[DexcRuntimeUIState.ClampSelectedIndex(Instance.m_Selectables.Count)];
			var row = selection.IsError ? null : selection.Row?.Source;
			if (row == null || row.Kind != DexcUIRowKind.Choice || row.Cycle == null || !row.IsEnabled || !row.Cycle(direction))
				return false;
			Instance.RefreshRows();
			return true;
		}

		internal static void ReleaseTextFocus()
		{
			if (Instance == null)
				return;
			Instance.BlurFocused();
			Instance.m_Root?.Focus();
			Instance.RefreshFocusVisuals();
		}

#if UNITY_INCLUDE_TESTS
		internal static void SelectForTests(int index)
		{
			if (Instance == null || Instance.m_Selectables.Count == 0)
				return;
			DexcRuntimeUIState.SetSelectedIndex(Mathf.Clamp(index, 0, Instance.m_Selectables.Count - 1));
			Instance.RefreshFocusVisuals();
			Instance.FocusSelected();
		}

		internal static bool SendClickForTests(int index)
		{
			if (Instance == null || index < 0 || index >= Instance.m_Selectables.Count)
				return false;
			var selection = Instance.m_Selectables[index];
			if (selection.IsText)
				Instance.ResolveSelectionElement(selection)?.Focus();
			else
				selection.Action?.Invoke();
			return true;
		}

		internal static void SendKeyForTests(KeyCode keyCode, char character = '\0')
		{
			if (Instance == null || Instance.m_Root == null)
				return;
			using (var key = KeyDownEvent.GetPooled(character, keyCode, EventModifiers.None))
				Instance.m_Root.SendEvent(key);
		}

		internal static TextField FirstTextFieldForTests()
		{
			return Instance == null
				? null
				: Instance
					.m_ContentList.Query<TextField>(className: "dexc-uitk-input")
					.ToList()
					.FirstOrDefault(field => field.resolvedStyle.display != DisplayStyle.None);
		}

		internal static string RowTextForTests(int index)
		{
			return Instance == null || index < 0 || index >= Instance.m_Items.Count ? string.Empty : Instance.m_Items[index].Text;
		}

		internal static string SelectableTextForTests(int index)
		{
			if (Instance == null || index < 0 || index >= Instance.m_Selectables.Count)
				return string.Empty;
			var selection = Instance.m_Selectables[index];
			return selection.Row == null
				? selection.Button == null
					? string.Empty
					: selection.Button.text
				: selection.Row.Text;
		}

		internal static Rect SelectableScreenRectForTests(int index)
		{
			if (Instance == null || index < 0 || index >= Instance.m_Selectables.Count)
				return default(Rect);
			return ScreenRect(Instance.ResolveSelectionElement(Instance.m_Selectables[index]));
		}
#endif

		private static Rect ScreenRect(VisualElement element)
		{
			if (element == null || element.panel == null || element.resolvedStyle.display == DisplayStyle.None)
				return default(Rect);
			var bounds = element.worldBound;
			var height = Instance != null && Instance.m_UseProfileOverride ? Instance.m_ProfileHeight : Screen.height;
			var scale = Instance == null ? 1f : Instance.m_Layout.Scale;
			return new Rect(bounds.x * scale, height - bounds.yMax * scale, bounds.width * scale, bounds.height * scale);
		}

		private static DexcRuntimeUIToolkitLayout CalculateLayout(int width, int height, Rect safeArea, DexcRuntimeUIPage page)
		{
			return CalculateLayout(width, height, safeArea, page, DexcRuntimeUIReferenceResolver.ResolveCurrentProfile(width, height));
		}

		private static DexcRuntimeUIToolkitLayout CalculateLayout(
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
			return new DexcRuntimeUIToolkitLayout(
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

		private void BuildHierarchy(PanelSettings template)
		{
			m_PanelSettingsTemplate = template;
			m_PanelSettings = Instantiate(template);
			m_PanelSettings.hideFlags = HideFlags.HideAndDontSave;
			m_Document = gameObject.AddComponent<UIDocument>();
			m_Document.panelSettings = m_PanelSettings;
			m_Document.sortingOrder = short.MaxValue;
			m_Root = m_Document.rootVisualElement;
			m_Root.focusable = true;
			m_Root.tabIndex = -1;
			m_Root.Clear();
			BuildProductionHierarchy();
			BindProductionCallbacks();
			ConfigureVirtualizedContent();
			m_Root.style.display = DisplayStyle.None;
			ApplyStyle(ResolveStyleSnapshot());
		}

		private void BindProductionCallbacks()
		{
			m_Root.RegisterCallback<PointerDownEvent>(
				pointer =>
				{
					m_PointerDownId = pointer.pointerId;
					m_PointerDownPosition = pointer.position;
					m_PointerDragged = false;
				},
				TrickleDown.TrickleDown
			);
			m_Root.RegisterCallback<PointerMoveEvent>(
				pointer =>
				{
					// Match the UI Toolkit ScrollView touch-slop in panel coordinates.
					if (
						pointer.pointerId == m_PointerDownId
						&& ((Vector2)pointer.position - m_PointerDownPosition).sqrMagnitude >= PointerDragThresholdSquared
					)
					{
						m_PointerDragged = true;
					}
				},
				TrickleDown.TrickleDown
			);
			m_Root.RegisterCallback<PointerCancelEvent>(
				pointer =>
				{
					if (pointer.pointerId == m_PointerDownId)
					{
						m_PointerDragged = true;
					}
				},
				TrickleDown.TrickleDown
			);
			m_Back.RegisterCallback<PointerUpEvent>(pointer => InvokePointerRelease(pointer, m_Back, ActivateBack));
			m_Close.RegisterCallback<PointerUpEvent>(pointer => InvokePointerRelease(pointer, m_Close, ActivateClose));
			foreach (var toggle in new[] { m_Single, m_Split })
			{
				var mode = toggle == m_Split ? DexcRuntimeUILayoutMode.Split : DexcRuntimeUILayoutMode.Single;
				toggle.RegisterCallback<PointerUpEvent>(pointer =>
					InvokePointerRelease(
						pointer,
						toggle,
						() =>
						{
							DexcRuntimeUIState.LayoutOverride = mode;
							Rebuild(true, false);
						}
					)
				);
			}
			m_History.RegisterCallback<PointerUpEvent>(pointer => InvokePointerRelease(pointer, m_History, OpenCommandHistory));
			m_Favorite.RegisterCallback<PointerUpEvent>(pointer =>
				InvokePointerRelease(
					pointer,
					m_Favorite,
					() =>
					{
						DexcRuntimeFavorites.ToggleCurrent(DexcRuntimeUIState.Page);
						Rebuild(true, false);
					}
				)
			);
			m_FooterSecondary.RegisterCallback<PointerUpEvent>(pointer =>
				InvokePointerRelease(pointer, m_FooterSecondary, () => m_FooterSecondaryAction?.Invoke())
			);
			m_FooterPrimary.RegisterCallback<PointerUpEvent>(pointer =>
				InvokePointerRelease(
					pointer,
					m_FooterPrimary,
					() =>
					{
						if (m_FooterPrimary.enabledSelf)
							m_FooterPrimaryAction?.Invoke();
					}
				)
			);
			RegisterButtonVisualEvents(m_Back, () => RowColor, false);
			RegisterButtonVisualEvents(m_Close, () => RowColor, false);
			RegisterButtonVisualEvents(m_History, () => RowColor, false);
			RegisterButtonVisualEvents(m_Favorite, () => RowColor, false);
			RegisterButtonVisualEvents(m_Single, () => SwitchColor(m_Single), false);
			RegisterButtonVisualEvents(m_Split, () => SwitchColor(m_Split), false);
			RegisterButtonVisualEvents(m_FooterSecondary, () => RowColor, false);
			RegisterButtonVisualEvents(m_FooterPrimary, () => m_FooterPrimary.enabledSelf ? AccentColor : Palette.Disabled, true);
			m_Root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
		}

		private void ConfigureVirtualizedContent()
		{
			m_ContentList.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
			m_ContentList.selectionType = SelectionType.None;
			m_ContentList.horizontalScrollingEnabled = false;
			m_ContentList.itemsSource = m_Items;
			m_ContentList.makeItem = MakeRow;
			m_ContentList.bindItem = BindRow;
			m_ContentList.unbindItem = UnbindRow;
			// The rows keep clear of the panel's sides, as the other renderers' rows do.
			var rows = m_ContentList.Q<ScrollView>().contentContainer;
			rows.style.paddingLeft = rows.style.paddingRight = ListInset;
		}

		private void BuildProductionHierarchy()
		{
			m_Root.AddToClassList("dexc-uitk-root");
			m_SafeAreaRoot = new VisualElement { name = "SafeAreaRoot" };
			m_SafeAreaRoot.AddToClassList("dexc-uitk-safe-area");
			m_Panel = new VisualElement { name = "Panel" };
			m_Panel.AddToClassList("dexc-uitk-panel");
			m_Header = new VisualElement { name = "Header" };
			m_Header.AddToClassList("dexc-uitk-header");
			m_Back = new Button { name = "Back", text = "‹" };
			m_Header.Add(m_Back);
			m_Title = new Label("Dexc Debug") { name = "Title" };
			m_Title.AddToClassList("dexc-uitk-title");
			m_Header.Add(m_Title);
			// Where the screen is wide enough, the header switches between one page and side by side.
			m_Single = new Button { name = "Single", text = "Single" };
			m_Header.Add(m_Single);
			m_Split = new Button { name = "Split", text = "Split" };
			m_Header.Add(m_Split);
			// A command's earlier values open from the header, so the footer is Execute alone.
			m_History = new Button { name = "History", text = "History" };
			m_Header.Add(m_History);
			// The star sits before Close and only shows on a page that can be starred.
			m_Favorite = new Button { name = "Favorite", text = "☆" };
			m_Header.Add(m_Favorite);
			m_Close = new Button { name = "Close", text = "×" };
			m_Header.Add(m_Close);
			m_Panel.Add(m_Header);
			m_ContentList = new ListView { name = "ContentList" };
			m_Panel.Add(m_ContentList);
			m_Footer = new VisualElement { name = "Footer" };
			m_Footer.AddToClassList("dexc-uitk-footer");
			m_FooterSecondary = new Button { name = "FooterSecondary", text = "Back" };
			m_FooterPrimary = new Button { name = "FooterPrimary", text = "Execute" };
			m_Footer.Add(m_FooterSecondary);
			m_Footer.Add(m_FooterPrimary);
			m_Panel.Add(m_Footer);
			m_SplitSections = new VisualElement { name = "SplitSections" };
			m_SplitList = new ScrollView(ScrollViewMode.Vertical) { name = "SplitList" };
			foreach (var column in new[] { m_SplitSections, (VisualElement)m_SplitList })
			{
				column.style.position = Position.Absolute;
				column.style.left = 0f;
				column.style.display = DisplayStyle.None;
				m_Panel.Add(column);
			}
			m_SafeAreaRoot.Add(m_Panel);
			m_Root.Add(m_SafeAreaRoot);
			// A row's label has no font until the row is styled, after it is bound; labels are measured in the panel's font
			// by this one, which stays in the panel unseen.
			m_Measure = new Label { name = "Measure", pickingMode = PickingMode.Ignore };
			m_Measure.style.position = Position.Absolute;
			m_Measure.style.visibility = Visibility.Hidden;
			m_Measure.style.whiteSpace = WhiteSpace.Normal;
			m_Root.Add(m_Measure);
			ApplyProductionShellStyle();
		}

		private void ApplyProductionShellStyle()
		{
			m_Root.style.position = Position.Absolute;
			m_Root.style.left = 0f;
			m_Root.style.top = 0f;
			m_Root.style.right = 0f;
			m_Root.style.bottom = 0f;
			m_SafeAreaRoot.style.position = Position.Absolute;
			m_Panel.style.position = Position.Absolute;
			m_Panel.style.flexDirection = FlexDirection.Column;
			m_Panel.style.overflow = Overflow.Hidden;
			m_Header.style.height = HeaderHeight;
			m_Header.style.minHeight = HeaderHeight;
			m_Header.style.maxHeight = HeaderHeight;
			m_Header.style.flexDirection = FlexDirection.Row;
			m_Header.style.alignItems = Align.Center;
			m_Title.style.flexGrow = 1f;
			// A long name is cut short; the buttons beside it keep their size and stay in the header.
			m_Title.style.flexShrink = 1f;
			m_Title.style.minWidth = 0f;
			m_Title.style.overflow = Overflow.Hidden;
			m_Title.style.whiteSpace = WhiteSpace.NoWrap;
			m_Title.style.unityTextAlign = TextAnchor.MiddleLeft;
			m_Title.style.fontSize = DexcRuntimeFormLayout.TitleFontSize;
			m_ContentList.style.flexGrow = 1f;
			m_ContentList.style.flexShrink = 1f;
			m_Footer.style.height = FooterHeight;
			m_Footer.style.minHeight = FooterHeight;
			m_Footer.style.maxHeight = FooterHeight;
			m_Footer.style.flexDirection = FlexDirection.Row;
			foreach (var button in m_Header.Query<Button>().ToList())
			{
				SetButtonShellStyle(button);
				button.style.flexShrink = 0f;
			}
			foreach (var button in m_Footer.Query<Button>().ToList())
			{
				SetButtonShellStyle(button);
				button.style.flexGrow = 1f;
			}
		}

		private static void SetButtonShellStyle(Button button)
		{
			button.style.unityTextAlign = TextAnchor.MiddleCenter;
			button.style.height = 36f;
			button.style.minWidth = 36f;
			button.style.paddingLeft = button.style.paddingRight = 8f;
			button.style.paddingTop = button.style.paddingBottom = 4f;
			button.style.marginLeft = 4f;
			button.style.marginRight = 4f;
		}

		private void RegisterButtonVisualEvents(Button button, Func<Color> normal, bool primary)
		{
			button.RegisterCallback<PointerEnterEvent>(_ =>
			{
				if (button.enabledSelf)
					button.style.backgroundColor = Palette.Selection;
			});
			button.RegisterCallback<PointerDownEvent>(_ =>
			{
				if (button.enabledSelf)
					button.style.backgroundColor = primary ? Palette.Focus : Palette.Accent;
			});
			button.RegisterCallback<PointerLeaveEvent>(_ => ApplyButtonVisual(button, normal(), primary));
			button.RegisterCallback<PointerUpEvent>(_ => ApplyButtonVisual(button, normal(), primary));
			button.RegisterCallback<FocusInEvent>(_ => button.schedule.Execute(() => RefreshFocusVisuals()));
			button.RegisterCallback<FocusOutEvent>(_ => button.schedule.Execute(() => RefreshFocusVisuals()));
		}

		/// <summary>The chosen half of Single | Split is filled; neither is outlined unless it has the focus.</summary>
		private Color SwitchColor(Button toggle) => (toggle == m_Split) == m_SplitShown ? Palette.Selection : RowColor;

		private void ApplyButtonVisual(Button button, Color normal, bool primary)
		{
			if (button == null)
				return;
			var enabled = button.enabledSelf;
			var logicalIndex = m_Selectables.FindIndex(selection => ReferenceEquals(selection.Button, button));
			// While the gamepad is in the column of sections, nothing on the page has its focus.
			var logicalFocus =
				DexcRuntimeSplitHome.SectionFocus < 0
				&& logicalIndex >= 0
				&& logicalIndex == DexcRuntimeUIState.ClampSelectedIndex(m_Selectables.Count);
			var focused = logicalFocus || button.panel != null && ReferenceEquals(button.panel.focusController.focusedElement, button);
			button.style.backgroundColor = enabled
				? focused && !primary
					? Palette.Selection
					: normal
				: Palette.Disabled;
			button.style.color = enabled ? Palette.PrimaryText : Palette.SecondaryText;
			var width = 1f;
			button.style.borderLeftWidth = width;
			button.style.borderTopWidth = width;
			button.style.borderRightWidth = width;
			button.style.borderBottomWidth = width;
			var border = focused ? Palette.Focus : normal;
			button.style.borderLeftColor = border;
			button.style.borderTopColor = border;
			button.style.borderRightColor = border;
			button.style.borderBottomColor = border;
		}

		private VisualElement MakeRow()
		{
			var row = new VisualElement();
			row.AddToClassList("dexc-uitk-row");
			row.style.flexDirection = FlexDirection.Column;
			row.style.justifyContent = Justify.Center;
			row.style.paddingLeft = 8f;
			row.style.paddingRight = 8f;
			row.style.overflow = Overflow.Hidden;
			// ListView zeroes an item's margins, so the gap under a row is its own bottom padding, painted the panel's colour.
			var gap = new VisualElement { name = "RowGap", pickingMode = PickingMode.Ignore };
			gap.style.position = Position.Absolute;
			gap.style.left = 0f;
			gap.style.right = 0f;
			gap.style.bottom = 0f;
			// The focused row is outlined above its gap, like the other renderers' focused rows.
			var focusMarker = new VisualElement { name = "FocusMarker", pickingMode = PickingMode.Ignore };
			focusMarker.AddToClassList("dexc-uitk-focus-marker");
			focusMarker.style.position = Position.Absolute;
			focusMarker.style.left = 0f;
			focusMarker.style.right = 0f;
			focusMarker.style.top = 0f;
			focusMarker.style.borderLeftWidth = focusMarker.style.borderRightWidth = 2f;
			focusMarker.style.borderTopWidth = focusMarker.style.borderBottomWidth = 2f;
			focusMarker.style.display = DisplayStyle.None;
			var label = new Label { name = "RowLabel" };
			label.AddToClassList("dexc-uitk-row-label");
			label.style.whiteSpace = WhiteSpace.Normal;
			label.style.overflow = Overflow.Hidden;
			label.style.textOverflow = TextOverflow.Ellipsis;
			// The theme pads every label above and below; a row sets its own spacing, as the other renderers do.
			label.style.marginTop = label.style.paddingTop = label.style.paddingBottom = 0f;
			var input = new TextField { name = "RowInput" };
			input.AddToClassList("dexc-uitk-input");
			input.style.display = DisplayStyle.None;
			input.RegisterCallback<FocusInEvent>(_ =>
			{
				m_FocusedTextField = input;
				if (input.userData is RowModel focused)
				{
					SelectRow(focused);
				}

				m_LastSignature = Signature();
				DexcRuntimeUIState.NotifyInputState(DebugMenuInputState.TextInput);
			});
			input.RegisterCallback<FocusOutEvent>(_ =>
			{
				var item = input.userData as RowModel;
				if (item?.Source?.Confirmed != null && item.Source.Confirmed(input.value))
				{
					if (item.Source.Rebuilds)
					{
						// A confirmed search changes the list itself; rebuild once the focus change has finished.
						var page = DexcRuntimeUIState.Page;
						m_Root.schedule.Execute(() =>
						{
							if (DexcRuntimeUIState.Page == page && m_Items.Contains(item))
							{
								Rebuild(true, false);
							}
						});
					}
					else
					{
						RefreshRows();
					}
				}

				if (ReferenceEquals(m_FocusedTextField, input))
				{
					m_FocusedTextField = null;
				}

				if (DexcRuntimeUIState.IsOpen)
				{
					DexcRuntimeUIState.NotifyInputState(DebugMenuInputState.Navigation);
				}
			});
			input.RegisterCallback<KeyDownEvent>(evt =>
			{
				if (
					(evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
					&& (
						DexcRuntimeUIState.Page == DexcRuntimeUIPage.FormDetail
						|| (
							DexcRuntimeUIState.Page == DexcRuntimeUIPage.CommandList
							|| DexcRuntimeUIState.Page == DexcRuntimeUIPage.SettingList
						)
					)
					&& (input.userData as RowModel)?.Text == "Search"
				)
				{
					input.Blur();
					Rebuild(true, false);
					evt.StopPropagation();
				}
			});
			row.Add(gap);
			row.Add(focusMarker);
			// The label and the value sit in a body, beside or above each other; a field's error is written under the body.
			var body = new VisualElement { name = "RowBody" };
			body.style.flexDirection = FlexDirection.Column;
			row.Add(body);
			body.Add(label);
			body.Add(input);
			var slider = new Slider(0f, 1f) { name = "RowSlider" };
			slider.style.marginLeft = 6f;
			slider.style.marginRight = 0f;
			slider.RegisterValueChangedCallback(change =>
			{
				if (row.userData is RowModel item && item.Kind == RowKind.Slider && item.Enabled)
				{
					item.Source.Slid(change.newValue);
					RefreshRows();
				}
			});
			body.Add(slider);
			var choice = new Button { name = "RowChoice" };
			choice.RegisterCallback<PointerUpEvent>(evt =>
				InvokePointerRelease(
					evt,
					choice,
					() =>
					{
						if (row.userData is RowModel item && item.Enabled)
						{
							SelectRow(item);
							item.Action?.Invoke();
						}
					}
				)
			);
			choice.style.marginLeft = 0f;
			choice.style.marginRight = 0f;
			choice.style.marginTop = DexcRuntimeFormLayout.LabelGap;
			choice.style.marginBottom = 0f;
			choice.style.paddingLeft = choice.style.paddingRight = DexcRuntimeFormLayout.HorizontalPadding;
			choice.style.paddingTop = choice.style.paddingBottom = DexcRuntimeFormLayout.VerticalPadding;
			choice.style.borderLeftWidth =
				choice.style.borderRightWidth =
				choice.style.borderTopWidth =
				choice.style.borderBottomWidth =
					0f;
			choice.style.unityTextAlign = TextAnchor.MiddleLeft;
			choice.style.whiteSpace = WhiteSpace.NoWrap;
			choice.style.textOverflow = TextOverflow.Ellipsis;
			choice.style.overflow = Overflow.Hidden;
			body.Add(choice);
			// A choice that steps in place gets ‹ and › at its ends; a boolean shows a switch instead of its text.
			var previous = StepButton("RowPrevious", "‹", row, -1);
			body.Insert(body.IndexOf(choice), previous);
			body.Add(StepButton("RowNext", "›", row, 1));
			var track = new VisualElement { name = "Switch", pickingMode = PickingMode.Ignore };
			track.style.position = Position.Absolute;
			track.style.right = 4f;
			track.style.top = new Length(50f, LengthUnit.Percent);
			var knob = new VisualElement { name = "Knob", pickingMode = PickingMode.Ignore };
			knob.style.position = Position.Absolute;
			track.Add(knob);
			choice.Add(track);
			var chevron = new Label("›") { name = "Chevron", pickingMode = PickingMode.Ignore };
			chevron.style.position = Position.Absolute;
			chevron.style.right = 8f;
			chevron.style.top = 0f;
			chevron.style.bottom = 0f;
			chevron.style.unityTextAlign = TextAnchor.MiddleRight;
			choice.Add(chevron);
			// A list entry's second line under its label and its count or result at the right end.
			var detail = new Label { name = "RowDetail", pickingMode = PickingMode.Ignore };
			detail.style.fontSize = DexcRuntimeFormLayout.SmallFontSize;
			detail.style.whiteSpace = WhiteSpace.Normal;
			detail.style.marginTop = detail.style.marginBottom = detail.style.paddingTop = detail.style.paddingBottom = 0f;
			body.Insert(body.IndexOf(label) + 1, detail);
			var trailing = new Label { name = "RowTrailing", pickingMode = PickingMode.Ignore };
			trailing.style.position = Position.Absolute;
			trailing.style.right = 8f;
			trailing.style.unityTextAlign = TextAnchor.MiddleRight;
			trailing.style.whiteSpace = WhiteSpace.NoWrap;
			trailing.style.fontSize = DexcRuntimeFormLayout.TrailingFontSize;
			trailing.style.marginTop = trailing.style.marginBottom = trailing.style.paddingTop = trailing.style.paddingBottom = 0f;
			row.Add(trailing);
			// The right-end text belongs to the label's first line, wherever the row puts it.
			label.RegisterCallback<GeometryChangedEvent>(_ => trailing.style.top = body.layout.y + label.layout.y);
			// A field's message, under its value as in IMGUI.
			var errorLine = new Label { name = "RowError", pickingMode = PickingMode.Ignore };
			errorLine.style.fontSize = DexcRuntimeFormLayout.SmallFontSize;
			errorLine.style.whiteSpace = WhiteSpace.Normal;
			errorLine.style.marginLeft = errorLine.style.marginRight = errorLine.style.marginBottom = 0f;
			errorLine.style.marginTop = 2f;
			errorLine.style.paddingLeft = errorLine.style.paddingRight = errorLine.style.paddingTop = errorLine.style.paddingBottom = 0f;
			errorLine.style.display = DisplayStyle.None;
			row.Add(errorLine);
			// The "…" action at the value's right end, and the "!" of a line of text that has an error.
			var error = new Button { name = "FieldError", text = "…" };
			error.RegisterCallback<PointerUpEvent>(pointer =>
				InvokePointerRelease(pointer, error, () => OpenInputError(row.userData as RowModel))
			);
			error.style.position = Position.Absolute;
			error.style.right = 0f;
			error.style.bottom = 0f;
			error.style.width = DexcRuntimeFormLayout.ErrorWidth;
			error.style.marginLeft = 0f;
			error.style.marginRight = 0f;
			error.style.marginTop = 0f;
			error.style.marginBottom = 0f;
			error.style.unityTextAlign = TextAnchor.MiddleCenter;
			error.style.paddingLeft = error.style.paddingRight = 0f;
			error.style.paddingTop = error.style.paddingBottom = 4f;
			error.style.borderLeftWidth = error.style.borderRightWidth = error.style.borderTopWidth = error.style.borderBottomWidth = 0f;
			body.Add(error);
			row.RegisterCallback<PointerEnterEvent>(_ =>
			{
				m_HoveredRow = row;
				ApplyRowVisual(row);
			});
			row.RegisterCallback<PointerDownEvent>(_ =>
			{
				m_PressedRow = row;
				ApplyRowVisual(row);
			});
			row.RegisterCallback<PointerLeaveEvent>(_ =>
			{
				if (ReferenceEquals(m_HoveredRow, row))
				{
					m_HoveredRow = null;
				}

				if (ReferenceEquals(m_PressedRow, row))
				{
					m_PressedRow = null;
				}

				ApplyRowVisual(row);
			});
			row.RegisterCallback<FocusInEvent>(_ => row.schedule.Execute(RefreshFocusVisuals));
			row.RegisterCallback<FocusOutEvent>(_ => row.schedule.Execute(RefreshFocusVisuals));
			row.RegisterCallback<PointerUpEvent>(OnRowPointerUp);
			return row;
		}

		private Button StepButton(string name, string symbol, VisualElement row, int direction)
		{
			var step = new Button { name = name, text = symbol };
			step.RegisterCallback<PointerUpEvent>(evt =>
				InvokePointerRelease(
					evt,
					step,
					() =>
					{
						if (row.userData is RowModel item && item.Enabled && item.Source?.Cycle != null && item.Source.Cycle(direction))
							RefreshRows();
					}
				)
			);
			step.style.marginLeft = step.style.marginRight = step.style.marginTop = step.style.marginBottom = 0f;
			step.style.paddingLeft = step.style.paddingRight = 0f;
			step.style.borderLeftWidth = step.style.borderRightWidth = step.style.borderTopWidth = step.style.borderBottomWidth = 0f;
			step.style.unityTextAlign = TextAnchor.MiddleCenter;
			step.style.fontSize = DexcRuntimeFormLayout.SymbolFontSize;
			step.style.display = DisplayStyle.None;
			return step;
		}

		/// <summary>The width a row's content has: the list's width less the row's own padding.</summary>
		private float RowWidth()
		{
			// The panel's logical width less the scrollbar and the list's sides: the list's own width is only known after its
			// first layout.
			var width = m_Layout.Panel.width / Mathf.Max(0.0001f, m_Layout.Scale) - 14f - m_SideWidth - ListInset * 2f;
			return Mathf.Max(1f, width - 16f);
		}

		/// <summary>Label beside the value, or above it when the row is narrow or the label wraps too far.</summary>
		private DexcRuntimeFormLayout.Arrangement Arrange(string text)
		{
			if (string.IsNullOrEmpty(text))
				return DexcRuntimeFormLayout.Arrange(RowWidth(), null);
			return DexcRuntimeFormLayout.Arrange(
				RowWidth(),
				width =>
				{
					var size = m_Measure.MeasureTextSize(
						BreakLabel(text, width),
						width,
						VisualElement.MeasureMode.AtMost,
						0f,
						VisualElement.MeasureMode.Undefined
					);
					if (float.IsNaN(size.y))
						m_Remeasure = true;
					return float.IsNaN(size.y) || size.y <= 0f ? 1 : Mathf.Max(1, Mathf.RoundToInt(size.y / (DexcRuntimeFormLayout.FontSize + 2f)));
				}
			);
		}

		/// <summary>A label broken between its words to fit a column of <paramref name="width"/>.</summary>
		private string BreakLabel(string text, float width) =>
			DexcRuntimeFormLayout.BreakLabel(
				text,
				width - LabelInset,
				line =>
				{
					var size = m_Measure.MeasureTextSize(line, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
					if (float.IsNaN(size.x))
						m_Remeasure = true;
					return size.x;
				}
			);

		/// <summary>A field's label with its note under it in small capitals.</summary>
		private static string LabelWithNote(string label, string note) =>
			string.IsNullOrEmpty(label) || string.IsNullOrEmpty(note) ? label : label + "\n<size=" + DexcRuntimeFormLayout.NoteFontSize + ">" + note.ToUpperInvariant() + "</size>";

		private void BindRow(VisualElement row, int index)
		{
			if (index < 0 || index >= m_Items.Count)
			{
				return;
			}

			// ListView recycles views while scrolling, so a row stays bound to its item until the page is rebuilt.
			var item = m_Items[index];
			row.userData = item;
			row.focusable = item.Kind == RowKind.Button;
			// A read-only value keeps its row readable; only the control inside it is disabled.
			row.SetEnabled(item.Kind == RowKind.Label || item.Enabled || item.Source != null);
			row.style.height = StyleKeyword.Auto;
			// Every row keeps the gap under it; a line of text splits it above and below so a striped band stays centred on
			// the text, and the components of one vector share a band with no gap between them.
			var textLine = item.Kind == RowKind.Label;
			var shared = !string.IsNullOrEmpty(item.Component)
				&& index + 1 < m_Items.Count
				&& !string.IsNullOrEmpty(m_Items[index + 1].Component)
				&& string.IsNullOrEmpty(m_Items[index + 1].Text);
			// A field's band keeps an inset above and below its value, as in the other renderers.
			var band = item.Kind == RowKind.Input || item.Kind == RowKind.Choice || item.Kind == RowKind.Slider ? FieldInset : 0f;
			// The space above is a border in the row's own colour: ListView leaves a row's top padding out of its height.
			row.style.paddingTop = 0f;
			row.style.borderTopWidth = textLine ? DexcRuntimeFormLayout.RowGap * 0.5f : band;
			row.style.paddingBottom = textLine ? DexcRuntimeFormLayout.RowGap * 0.5f : band + DexcRuntimeFormLayout.RowGap;
			row.style.minHeight = item.Kind == RowKind.Label ? 0f : ItemHeight() + band * 2f + DexcRuntimeFormLayout.RowGap;
			var gap = row.Q<VisualElement>("RowGap");
			gap.style.height = DexcRuntimeFormLayout.RowGap;
			gap.style.backgroundColor = PanelColor;
			gap.style.display = textLine || shared ? DisplayStyle.None : DisplayStyle.Flex;
			row.Q<VisualElement>("FocusMarker").style.bottom = shared ? 0f : DexcRuntimeFormLayout.RowGap;
			var label = row.Q<Label>("RowLabel");
			var input = row.Q<TextField>("RowInput");
			input.UnregisterValueChangedCallback(OnInputChanged);
			label.text = item.Text ?? string.Empty;
			label.style.color = item.TextColor;
			label.style.whiteSpace = WhiteSpace.Normal;
			label.style.textOverflow = TextOverflow.Clip;
			label.style.unityTextAlign = item.Kind == RowKind.Button ? TextAnchor.MiddleLeft : TextAnchor.UpperLeft;
			label.style.display =
				item.Kind == RowKind.Input && (string.IsNullOrEmpty(item.Component) || string.IsNullOrEmpty(item.Text))
					? DisplayStyle.None
					: DisplayStyle.Flex;
			label.style.marginBottom =
				item.Kind == RowKind.Input && !string.IsNullOrEmpty(item.Component) && !string.IsNullOrEmpty(item.Text)
					? DexcRuntimeFormLayout.LabelGap
					: 0f;
			input.style.display = item.Kind == RowKind.Input ? DisplayStyle.Flex : DisplayStyle.None;
			input.userData = item.Kind == RowKind.Input ? item : null;
			if (item.Kind == RowKind.Input)
			{
				input.label = CommandInputLabel(item);
				input.SetEnabled(item.Enabled);
				input.multiline = item.Multiline;
				input.style.flexDirection = string.IsNullOrEmpty(item.Component) ? FlexDirection.Column : FlexDirection.Row;
				input.style.marginLeft = 0f;
				input.style.marginRight = ActionWidth(item);
				input.style.marginTop = 0f;
				input.style.marginBottom = 0f;
				input.style.whiteSpace = item.Multiline ? WhiteSpace.Normal : WhiteSpace.NoWrap;
				input.SetValueWithoutNotify(item.Value ?? string.Empty);
				input.RegisterValueChangedCallback(OnInputChanged);
				ApplyTextFieldVisual(input);
				var inputLabel = input.Q<Label>();
				if (inputLabel != null)
				{
					inputLabel.style.color = item.TextColor;
				}
			}

			var slider = row.Q<Slider>("RowSlider");
			slider.style.display = item.Kind == RowKind.Slider ? DisplayStyle.Flex : DisplayStyle.None;
			slider.style.height = DexcRuntimeFormLayout.RowHeight(m_Layout.RowHeight, m_Layout.HitHeight);
			slider.style.marginRight = ActionWidth(item);
			if (item.Kind == RowKind.Slider)
			{
				slider.SetEnabled(item.Enabled);
				label.style.display = DisplayStyle.None;
				slider.SetValueWithoutNotify(item.Source.SliderValue());
				StyleSlider(slider, false);
			}

			var choice = row.Q<Button>("RowChoice");
			choice.style.display = item.Kind == RowKind.Choice ? DisplayStyle.Flex : DisplayStyle.None;
			choice.SetEnabled(item.Enabled);
			SetChoiceVisual(choice, item);
			choice.style.height = DexcRuntimeFormLayout.RowHeight(m_Layout.RowHeight, m_Layout.HitHeight);
			choice.style.minHeight = choice.style.height;
			choice.style.maxHeight = choice.style.height;
			choice.style.backgroundColor = item.Boolean ? Color.clear : item.Enabled ? RowColor : Palette.Disabled;
			choice.style.color = Palette.PrimaryText;
			LayOutRow(row, item, label, input, choice);
			ApplyRowVisual(row);
			BindError(row, item);
		}

		/// <summary>
		/// Puts a field's label beside its value or above it, adds the steps of a choice that steps in place, and gives a list
		/// entry its mark, second line and right-end text. Everything is reset first: ListView reuses rows for other items.
		/// </summary>
		private void LayOutRow(VisualElement row, RowModel item, Label label, TextField input, Button choice)
		{
			var previous = row.Q<Button>("RowPrevious");
			var next = row.Q<Button>("RowNext");
			var detail = row.Q<Label>("RowDetail");
			var trailing = row.Q<Label>("RowTrailing");
			var error = row.Q<Button>("FieldError");
			var body = row.Q<VisualElement>("RowBody");
			var errorLine = row.Q<Label>("RowError");
			body.style.flexDirection = FlexDirection.Column;
			body.style.alignItems = Align.Stretch;
			errorLine.style.marginLeft = 0f;
			label.style.width = StyleKeyword.Auto;
			label.style.minWidth = StyleKeyword.Auto;
			label.style.maxWidth = StyleKeyword.None;
			label.style.flexShrink = 1f;
			label.style.marginRight = 0f;
			input.style.marginLeft = 0f;
			input.style.flexGrow = 0f;
			input.style.alignItems = Align.Stretch;
			// The "…" button sits level with the value: at the bottom when the label is above it.
			error.style.top = StyleKeyword.Auto;
			error.style.bottom = 0f;
			error.style.marginTop = 0f;
			choice.style.flexGrow = 0f;
			choice.style.marginTop = DexcRuntimeFormLayout.LabelGap;
			choice.style.marginRight = ActionWidth(item);
			previous.style.display = DisplayStyle.None;
			next.style.display = DisplayStyle.None;
			detail.style.display = DisplayStyle.None;
			trailing.style.display = DisplayStyle.None;
			var entry = item.Entry;
			if (item.Kind == RowKind.Button && entry != null)
			{
				if (!string.IsNullOrEmpty(entry.Leading))
					label.text = entry.Leading + "  " + item.Text;
				var second = entry.DetailText;
				if (!string.IsNullOrEmpty(second))
				{
					// A long name such as a type's full name wraps at its dots, not part way through a word.
					detail.text = DexcRuntimeFormLayout.BreakLines(
						second,
						RowWidth() - LabelInset,
						line => detail.MeasureTextSize(line, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).x
					);
					detail.style.color = Palette.SecondaryText;
					detail.style.display = DisplayStyle.Flex;
					row.style.borderTopWidth = 3f;
					row.style.paddingBottom = DexcRuntimeFormLayout.RowGap + 3f;
				}
				var end = ((entry.TrailingText ?? string.Empty) + (entry.Drill ? "  ›" : string.Empty)).Trim();
				if (end.Length > 0)
				{
					trailing.text = end;
					trailing.style.color = entry.TrailingTone == DexcUIRowTone.Normal ? Palette.SecondaryText : Palette.Warning;
					trailing.style.display = DisplayStyle.Flex;
					var size = trailing.MeasureTextSize(end, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
					var line = label.MeasureTextSize("A", 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).y;
					label.style.marginRight = (float.IsNaN(size.x) ? 24f : size.x) + 12f;
					trailing.style.height = float.IsNaN(line) || line <= 0f ? DexcRuntimeFormLayout.FontSize + 5f : line;
				}
				return;
			}
			var source = item.Source;
			if (source == null || item.Kind != RowKind.Input && item.Kind != RowKind.Choice)
				return;
			var arrangement = Arrange(source.Label);
			var text = LabelWithNote(
				arrangement.Stacked ? source.Label : BreakLabel(source.Label, arrangement.LabelWidth),
				source.Note
			);
			var rowHeight = DexcRuntimeFormLayout.RowHeight(m_Layout.RowHeight, m_Layout.HitHeight);
			if (item.Kind == RowKind.Choice)
			{
				label.text = text ?? string.Empty;
				if (arrangement.Stacked)
					return;
				body.style.flexDirection = FlexDirection.Row;
				body.style.alignItems = Align.Center;
				SetLabelColumn(label, arrangement.LabelWidth);
				CenterOnValue(error, rowHeight);
				errorLine.style.marginLeft = arrangement.ValueX;
				choice.style.flexGrow = 1f;
				choice.style.marginTop = 0f;
				if (source.Cycle == null || item.Boolean)
					return;
				choice.style.marginRight = 0f;
				foreach (var step in new[] { previous, next })
				{
					step.style.display = DisplayStyle.Flex;
					step.style.width = step.style.height = rowHeight;
					step.style.backgroundColor = RowColor;
					step.style.color = Palette.PrimaryText;
					step.SetEnabled(item.Enabled);
				}
				next.style.marginRight = ActionWidth(item);
				return;
			}
			if (string.IsNullOrEmpty(source.Component))
			{
				input.label = text;
				if (arrangement.Stacked)
					return;
				input.style.flexDirection = FlexDirection.Row;
				input.style.alignItems = Align.Center;
				CenterOnValue(error, rowHeight);
				errorLine.style.marginLeft = arrangement.ValueX;
				var inputLabel = input.Q<Label>();
				if (inputLabel != null)
				{
					SetLabelColumn(inputLabel, arrangement.LabelWidth);
					inputLabel.style.marginBottom = 0f;
				}
				var textInput = input.Q<VisualElement>(className: "unity-base-text-field__input");
				if (textInput != null)
				{
					textInput.style.flexGrow = 1f;
					textInput.style.flexShrink = 1f;
					textInput.style.flexBasis = 0f;
				}
			}
			else if (!string.IsNullOrEmpty(item.Text))
			{
				// A vector's first component: the field's label on the left, the component's own row on the right.
				label.text = text;
				if (arrangement.Stacked)
					return;
				body.style.flexDirection = FlexDirection.Row;
				body.style.alignItems = Align.Center;
				SetLabelColumn(label, arrangement.LabelWidth);
				CenterOnValue(error, rowHeight);
				errorLine.style.marginLeft = arrangement.ValueX;
				label.style.marginBottom = 0f;
				input.style.flexGrow = 1f;
			}
			else if (!arrangement.Stacked)
			{
				input.style.marginLeft = arrangement.LabelWidth + DexcRuntimeFormLayout.ColumnGap;
				errorLine.style.marginLeft = arrangement.ValueX;
			}
		}

		/// <summary>The width a row gives up at the value's right end: only a row with a "…" action keeps a place for it.</summary>
		private static float ActionWidth(RowModel item) =>
			item.Auxiliary == null ? 0f : DexcRuntimeFormLayout.ErrorWidth + DexcRuntimeFormLayout.ColumnGap;

		/// <summary>Beside a label that wraps, the value is centred on the body; the button at its end follows it.</summary>
		private static void CenterOnValue(Button error, float rowHeight)
		{
			error.style.top = new Length(50f, LengthUnit.Percent);
			error.style.bottom = StyleKeyword.Auto;
			error.style.marginTop = -rowHeight * 0.5f;
		}

		private static void SetLabelColumn(Label label, float width)
		{
			label.style.width = width;
			label.style.minWidth = width;
			label.style.maxWidth = width;
			label.style.flexShrink = 0f;
			label.style.marginRight = DexcRuntimeFormLayout.ColumnGap;
		}

		private void UnbindRow(VisualElement row, int index)
		{
			row.userData = null;
			row.focusable = false;
			var input = row.Q<TextField>("RowInput");
			if (input != null)
			{
				input.UnregisterValueChangedCallback(OnInputChanged);
				if (ReferenceEquals(m_FocusedTextField, input))
					m_FocusedTextField = null;
				input.userData = null;
			}
		}

		private void ApplyTextFieldVisual(TextField input)
		{
			input.style.backgroundColor = Color.clear;
			input.style.color = Palette.PrimaryText;
			var label = input.Q<Label>();
			if (label != null)
			{
				label.style.color = Palette.PrimaryText;
				label.style.unityTextAlign = TextAnchor.UpperLeft;
				label.style.whiteSpace = WhiteSpace.Normal;
				label.style.textOverflow = TextOverflow.Clip;
				label.style.minWidth = 0f;
				label.style.width = StyleKeyword.Auto;
				label.style.marginLeft = 0f;
				label.style.marginRight = 0f;
				label.style.marginBottom = string.IsNullOrEmpty(input.label) ? 0f : DexcRuntimeFormLayout.LabelGap;
				var component = (input.userData as RowModel)?.Component;
				if (!string.IsNullOrEmpty(component))
				{
					label.style.width = DexcRuntimeFormLayout.ComponentWidth;
					label.style.minWidth = DexcRuntimeFormLayout.ComponentWidth;
					label.style.maxWidth = DexcRuntimeFormLayout.ComponentWidth;
					label.style.flexShrink = 0f;
					label.style.height = DexcRuntimeFormLayout.RowHeight(m_Layout.RowHeight, m_Layout.HitHeight);
					label.style.marginRight = DexcRuntimeFormLayout.ColumnGap;
					label.style.marginBottom = 0f;
					label.style.unityTextAlign = TextAnchor.MiddleLeft;
				}
				else
				{
					label.style.maxWidth = StyleKeyword.None;
					label.style.height = StyleKeyword.Auto;
				}
			}

			var textInput = input.Q<VisualElement>(className: "unity-base-text-field__input");
			if (textInput == null)
			{
				return;
			}

			textInput.style.whiteSpace = input.multiline ? WhiteSpace.Normal : WhiteSpace.NoWrap;
			textInput.style.overflow = Overflow.Hidden;
			var inlineComponent = !string.IsNullOrEmpty((input.userData as RowModel)?.Component);
			textInput.style.flexGrow = inlineComponent ? 1f : 0f;
			textInput.style.flexShrink = inlineComponent ? 1f : 0f;
			textInput.style.flexBasis = inlineComponent ? new StyleLength(0f) : new StyleLength(StyleKeyword.Auto);
			textInput.style.minWidth = 0f;
			var height = DexcRuntimeFormLayout.RowHeight(m_Layout.RowHeight, m_Layout.HitHeight) * (input.multiline ? 3f : 1f);
			textInput.style.height = height;
			textInput.style.minHeight = height;
			textInput.style.maxHeight = height;
			textInput.style.unityTextAlign = input.multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
			textInput.style.paddingLeft = DexcRuntimeFormLayout.HorizontalPadding;
			textInput.style.paddingRight = DexcRuntimeFormLayout.HorizontalPadding;
			textInput.style.paddingTop = DexcRuntimeFormLayout.VerticalPadding;
			textInput.style.paddingBottom = DexcRuntimeFormLayout.VerticalPadding;
			textInput.style.borderLeftWidth = 0f;
			textInput.style.borderRightWidth = 0f;
			textInput.style.borderTopWidth = 0f;
			textInput.style.borderBottomWidth = 0f;
			foreach (var text in textInput.Query<TextElement>().ToList())
			{
				text.style.unityTextAlign = input.multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
			}

			// A value that cannot be edited reads as text, not as a field that looks broken.
			textInput.style.backgroundColor = input.enabledSelf ? RowColor : Color.clear;
			textInput.style.color = Palette.PrimaryText;
			ApplyFieldErrorOutline(input);
		}

		/// <summary>A field that is not valid is outlined in the error text's colour, as IMGUI outlines it.</summary>
		private void ApplyFieldErrorOutline(TextField input)
		{
			var textInput = input.Q<VisualElement>(className: "unity-base-text-field__input");
			if (textInput == null)
				return;
			var invalid = input.userData is RowModel item && !string.IsNullOrEmpty(InputError(item));
			var width = invalid ? 1f : 0f;
			var color = invalid ? Palette.FieldErrorText : Palette.Focus;
			textInput.style.borderLeftWidth = textInput.style.borderRightWidth = width;
			textInput.style.borderTopWidth = textInput.style.borderBottomWidth = width;
			textInput.style.borderLeftColor = textInput.style.borderRightColor = color;
			textInput.style.borderTopColor = textInput.style.borderBottomColor = color;
		}

		private void ApplyRowVisual(VisualElement row)
		{
			var item = row == null ? null : row.userData as RowModel;
			if (item == null)
			{
				return;
			}

			var focusedElement = row.panel == null ? null : row.panel.focusController.focusedElement as VisualElement;
			var input = row.Q<TextField>("RowInput");
			var logicalIndex = m_Selectables.FindIndex(selection => ReferenceEquals(selection.Row, item));
			// While the gamepad is in the column of sections, nothing on the page has its focus.
			var logicalFocus =
				DexcRuntimeSplitHome.SectionFocus < 0
				&& logicalIndex >= 0
				&& logicalIndex == DexcRuntimeUIState.ClampSelectedIndex(m_Selectables.Count);
			var focused =
				logicalFocus
				|| ReferenceEquals(focusedElement, row)
				|| input != null && (ReferenceEquals(focusedElement, input) || focusedElement != null && input.Contains(focusedElement));
			var interactive = item.Kind != RowKind.Label && item.Enabled;
			var preservesError = item.Color == Palette.Error;
			// A field sits on a band a step lighter than the panel, as in the other renderers; the focused row is filled.
			var field = item.Kind == RowKind.Input || item.Kind == RowKind.Choice || item.Kind == RowKind.Slider;
			row.style.backgroundColor =
				item.Kind == RowKind.Label && item.Source != null && item.Source.Striped ? HeaderColor
				: item.Kind == RowKind.Label ? PanelColor
				: field && focused ? Palette.Selection
				: field ? HeaderColor
				: !item.Enabled ? Palette.Disabled
				: interactive && !preservesError && ReferenceEquals(m_PressedRow, row) ? Palette.Accent
				: interactive && !preservesError && (ReferenceEquals(m_HoveredRow, row) || focused) ? Palette.Selection
				: item.Color;
			// Only the top border is used, as the space above the row's content; it takes the row's colour.
			row.style.borderLeftWidth = 0f;
			row.style.borderRightWidth = 0f;
			row.style.borderBottomWidth = 0f;
			row.style.borderTopColor = row.style.backgroundColor;
			var marker = row.Q<VisualElement>("FocusMarker");
			if (marker != null)
			{
				// The outline starts above the top border, at the row's own top.
				marker.style.top = -row.style.borderTopWidth.value;
				marker.style.borderLeftColor = marker.style.borderRightColor = Palette.Focus;
				marker.style.borderTopColor = marker.style.borderBottomColor = Palette.Focus;
				marker.style.display = focused && item.Kind != RowKind.Label ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		private void OnRowPointerUp(PointerUpEvent pointer)
		{
			var row = pointer.currentTarget as VisualElement;
			if (ReferenceEquals(m_PressedRow, row))
				m_PressedRow = null;
			ApplyRowVisual(row);
			if (!IsPointerReleaseInside(pointer, row))
				return;
			var item = row == null ? null : row.userData as RowModel;
			if (item == null || item.Kind != RowKind.Button || !item.Enabled)
				return;
			SelectRow(item);
			item.Action?.Invoke();
		}

		private void InvokePointerRelease(PointerUpEvent pointer, VisualElement target, Action action)
		{
			if (!IsPointerReleaseInside(pointer, target))
				return;
			action?.Invoke();
		}

		private bool IsPointerReleaseInside(PointerUpEvent pointer, VisualElement target)
		{
			// Native touch has panel-space position but no original IMGUI mouse event.
			return pointer != null
				&& pointer.button == 0
				&& target != null
				&& target.enabledInHierarchy
				&& !(pointer.pointerId == m_PointerDownId && m_PointerDragged)
				&& target.worldBound.Contains(pointer.position);
		}

		private void ActivateBack()
		{
			DexcRuntimeUIState.Back();
			Rebuild(true);
		}

		private void ActivateClose()
		{
			DexcRuntimeUIState.Close();
			BlurFocused();
			UpdateVisibility();
		}

		private static string CommandInputLabel(RowModel item)
		{
			return string.IsNullOrEmpty(item.Component) ? item.Text ?? string.Empty : item.Component.ToUpperInvariant();
		}

		private static string InputError(RowModel item)
		{
			return item.Source?.ErrorText;
		}

		private void BindError(VisualElement row, RowModel item)
		{
			var error = row.Q<Button>("FieldError");
			if (error == null)
			{
				return;
			}

			var message = InputError(item);
			// A field writes its message under the value, as IMGUI does; only a line of text keeps the "!" that opens it.
			var field = item.Kind == RowKind.Input || item.Kind == RowKind.Choice;
			var errorLine = row.Q<Label>("RowError");
			var written = field && !string.IsNullOrEmpty(message);
			errorLine.text = written ? message : string.Empty;
			errorLine.style.color = Palette.FieldErrorText;
			errorLine.style.display = written ? DisplayStyle.Flex : DisplayStyle.None;
			if (item.Kind == RowKind.Input)
				ApplyFieldErrorOutline(row.Q<TextField>("RowInput"));
			var marked = !field && !string.IsNullOrEmpty(message);
			var visible = marked || field && item.Auxiliary != null;
			error.text = marked ? "!" : "…";
			error.style.height = DexcRuntimeFormLayout.RowHeight(m_Layout.RowHeight, m_Layout.HitHeight);
			error.style.visibility = visible ? Visibility.Visible : Visibility.Hidden;
			error.SetEnabled(visible);
			error.style.backgroundColor = marked && !DexcRuntimeCommandForm.IsWarningText(message) ? ErrorColor : RowColor;
			error.style.color = marked && DexcRuntimeCommandForm.IsWarningText(message) ? Palette.Warning : Palette.PrimaryText;
		}

		private void OnInputChanged(ChangeEvent<string> change)
		{
			var input = change.currentTarget as TextField;
			var item = input == null ? null : input.userData as RowModel;
			if (item == null || item.Kind != RowKind.Input || !item.Enabled)
				return;
			var expected = item.Value ?? string.Empty;
			if (!string.Equals(change.previousValue ?? string.Empty, expected, StringComparison.Ordinal))
			{
				input.SetValueWithoutNotify(expected);
				return;
			}
			item.Value = change.newValue ?? string.Empty;
			if (item.Source != null)
			{
				item.Source.Changed(item.Value);
				RefreshRows();
				m_Selectables.Clear();
				BuildSelections();
			}
			else
				item.ValueChanged?.Invoke(item.Value);
			if (item.Source != null)
				input.label = CommandInputLabel(item);
			// The field sits in its row's body; the error line and the "…" belong to the row.
			BindError(input.parent.parent, item);
			input.schedule.Execute(() =>
			{
				if (
					DexcRuntimeUIState.IsOpen
					&& input.panel != null
					&& ReferenceEquals(input.userData, item)
					&& ReferenceEquals(m_FocusedTextField, input)
				)
					input.Focus();
			});
		}

		private void OnKeyDown(KeyDownEvent key)
		{
			var textField = m_FocusedTextField;
			if (textField != null && textField.panel != null)
			{
				if (key.keyCode == KeyCode.Escape || key.keyCode == KeyCode.Return || key.keyCode == KeyCode.KeypadEnter)
				{
					textField.Blur();
					m_Root.Focus();
					key.StopPropagation();
				}

				return;
			}

			if (key.keyCode == KeyCode.Escape || key.keyCode == KeyCode.Backspace)
			{
				DexcRuntimeUIState.Back();
				Rebuild(true);
				key.StopPropagation();
				return;
			}

			// In the column of sections the keys pick and open a section.
			var submit = key.keyCode == KeyCode.Return || key.keyCode == KeyCode.KeypadEnter || key.keyCode == KeyCode.Space;
			if (
				DexcRuntimeSplitHome.HandleInput(
					key.keyCode == KeyCode.UpArrow ? -1 : key.keyCode == KeyCode.DownArrow ? 1 : 0,
					key.keyCode == KeyCode.LeftArrow ? -1 : key.keyCode == KeyCode.RightArrow ? 1 : 0,
					submit,
					false
				)
			)
			{
				Rebuild(true);
				key.StopPropagation();
				return;
			}

			if (key.keyCode == KeyCode.UpArrow || key.keyCode == KeyCode.LeftArrow)
			{
				MoveSelection(-1);
				key.StopPropagation();
				return;
			}

			if (key.keyCode == KeyCode.DownArrow || key.keyCode == KeyCode.RightArrow)
			{
				MoveSelection(1);
				key.StopPropagation();
				return;
			}

			if (key.keyCode == KeyCode.Return || key.keyCode == KeyCode.KeypadEnter || key.keyCode == KeyCode.Space)
			{
				ActivateCurrentSelection();
				key.StopPropagation();
			}
		}

		private void Tick()
		{
			DexcRuntimeSettingForm.Tick();
			DexcRuntimeInspectorForm.Tick();
			if (m_InspectorVersion != DexcRuntimeInspectorForm.Version)
				RefreshRows();
			if (DexcRuntimeInspectorNavigation.IsList(DexcRuntimeUIState.Page))
				DexcRuntimeInspectorNavigation.Rows();
			if (m_SettingVersion != DexcRuntimeSettingForm.Version)
				RefreshRows();
			UpdateVisibility();
			if (!IsActive || !DexcRuntimeUIState.IsOpen)
			{
				if (!DexcRuntimeUIState.IsOpen)
				{
					BlurFocused();
					// Reopening on Home matches the signature it closed with, but the counts, favorites and focus are not
					// what they were: the menu is built again when it opens, as uGUI does.
					m_LastSignature = int.MinValue;
				}
				return;
			}

			DexcRuntimeUIRenderer.HandleProviderInput();
			// Rows bound before the measuring label had its font are bound again, now that it has one.
			if (m_Remeasure)
			{
				m_Remeasure = false;
				m_ContentList.RefreshItems();
			}
			var signature = Signature();
			if (
				signature != m_LastSignature
				&& !(DexcRuntimeInspectorNavigation.Searchable && m_FocusedTextField != null && m_FocusedTextField.panel != null)
			)
				Rebuild(false);
			DexcRuntimeUIState.NotifyInputState(
				m_FocusedTextField != null && m_FocusedTextField.panel != null
					? DebugMenuInputState.TextInput
					: DebugMenuInputState.Navigation
			);
		}

		private void UpdateVisibility()
		{
			if (m_Root == null)
				return;
			m_Root.style.display = IsActive && DexcRuntimeUIState.IsOpen && m_CaptureVisible ? DisplayStyle.Flex : DisplayStyle.None;
		}

		private int Signature()
		{
			var hash = new HashCode();
			hash.Add(DexcRuntimeUIState.Page);
			if (DexcRuntimeInspectorNavigation.IsList(DexcRuntimeUIState.Page))
				hash.Add(DexcRuntimeInspectorNavigation.Version);
			hash.Add(m_UseProfileOverride ? m_ProfileWidth : Screen.width);
			hash.Add(m_UseProfileOverride ? m_ProfileHeight : Screen.height);
			hash.Add(DexcRuntimeUIState.SelectedIndex);
			hash.Add(DexcRuntimeSplitHome.SectionFocus);
			hash.Add(DexcRuntimeUIState.LastCommandResult?.Status);
			hash.Add(DexcRuntimeUIState.AsyncExecution != null);
			hash.Add(DexcRuntimeUIState.Confirming);
			hash.Add(DexcRuntimeUIState.Notice);
			hash.Add(DebugBootMenu.AutoStartRemainingSeconds);
			return hash.ToHashCode();
		}

		private void Rebuild(bool force, bool focusSelection = true)
		{
			if (!force && !DexcRuntimeUIState.IsOpen)
			{
				return;
			}

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
			ApplyStyle(ResolveStyleSnapshot());
			BindState(focusSelection);
		}

		private void BindState(bool focusSelection)
		{
			var selected = DexcRuntimeUIState.SelectedIndex;
			var navigationKey =
				m_LastBuiltPage == DexcRuntimeUIState.Page
				&& DexcRuntimeInspectorNavigation.IsList(DexcRuntimeUIState.Page)
				&& selected >= 0
				&& selected < m_Selectables.Count
					? m_Selectables[selected].Row?.NavigationKey
					: null;
			var returningFromDetail =
				m_LastBuiltPage == DexcRuntimeUIPage.FormDetail && DexcRuntimeUIState.Page != DexcRuntimeUIPage.FormDetail;
			if (m_LastBuiltPage != DexcRuntimeUIPage.FormDetail && DexcRuntimeUIState.Page == DexcRuntimeUIPage.FormDetail)
			{
				m_FormReturnScroll = ContentScrollView?.scrollOffset ?? Vector2.zero;
				m_FormReturnDraft = DexcRuntimeUIState.DraftId;
			}

			m_LastBuiltPage = DexcRuntimeUIState.Page;
			foreach (var item in m_Items)
			{
				item.Source?.Unbound?.Invoke();
			}

			m_Items.Clear();
			m_Selectables.Clear();
			m_Title.text = DexcRuntimeUIRenderer.RichHeader(DexcRuntimeUIState.Page, Palette.SecondaryText);
			BuildSplitColumns();
			if (!DexcDebugger.IsInitialized)
			{
				AddLabel("Dexc Debugger is enabled but not initialized.", ErrorColor);
			}
			else
			{
				BuildPage();
			}

			if (!string.IsNullOrEmpty(DexcRuntimeUIState.Notice))
			{
				AddLabel(DexcRuntimeUIState.Notice, RowColor, Palette.Warning);
			}

			BuildFooter();
			m_Back.style.display = DexcRuntimeUIRenderer.HasHeaderBack(DexcRuntimeUIState.Page) ? DisplayStyle.Flex : DisplayStyle.None;
			m_Close.style.display = DexcRuntimeUIRenderer.HasHeaderClose() ? DisplayStyle.Flex : DisplayStyle.None;
			var star = DexcRuntimeFavorites.Star(DexcRuntimeUIState.Page);
			m_Favorite.style.display = star == null ? DisplayStyle.None : DisplayStyle.Flex;
			m_Favorite.text = star ?? "☆";
			m_History.style.display = DexcRuntimeUIRenderer.HasHeaderHistory() ? DisplayStyle.Flex : DisplayStyle.None;
			BuildSelections();
			if (navigationKey != null)
			{
				var restored = m_Selectables.FindIndex(selection => selection.Row?.NavigationKey == navigationKey);
				if (restored >= 0)
				{
					DexcRuntimeUIState.SetSelectedIndex(restored);
				}
			}

			m_ContentList.fixedItemHeight = ItemHeight();
			m_ContentList.RefreshItems();
			m_LastSignature = Signature();
			UpdateVisibility();
			if (DexcRuntimeSplitHome.SectionFocus >= 0)
				m_Root.Focus();
			else if (focusSelection)
			{
				// Returning from an input's error detail resumes editing that input; plain navigation only marks text rows.
				FocusSelected(returningFromDetail && m_FormReturnDraft == DexcRuntimeUIState.DraftId);
			}

			if (returningFromDetail && m_FormReturnDraft == DexcRuntimeUIState.DraftId)
			{
				m_Root.schedule.Execute(() =>
				{
					if (ContentScrollView != null)
					{
						ContentScrollView.scrollOffset = m_FormReturnScroll;
					}
				});
			}
		}

		private void ApplyLayout()
		{
			var scale = m_Layout.Scale > 0f ? m_Layout.Scale : 1f;
			m_PanelSettings.scale = scale;
			var safeTop = (m_Layout.Screen.height - m_Layout.SafeArea.yMax) / scale;
			m_SafeAreaRoot.style.left = m_Layout.SafeArea.x / scale;
			m_SafeAreaRoot.style.top = safeTop;
			m_SafeAreaRoot.style.width = m_Layout.SafeArea.width / scale;
			m_SafeAreaRoot.style.height = m_Layout.SafeArea.height / scale;
			m_Panel.style.left = (m_Layout.Panel.x - m_Layout.SafeArea.x) / scale;
			m_Panel.style.top = (m_Layout.SafeArea.yMax - m_Layout.Panel.yMax) / scale;
			m_Panel.style.width = m_Layout.Panel.width / scale;
			m_Panel.style.height = m_Layout.Panel.height / scale;
			m_Header.style.height = m_Layout.Header.height / scale;
			m_Header.style.minHeight = m_Layout.Header.height / scale;
			m_Header.style.maxHeight = m_Layout.Header.height / scale;
			m_Footer.style.height = m_Layout.Footer.height / scale;
			m_Footer.style.minHeight = m_Layout.Footer.height / scale;
			m_Footer.style.maxHeight = m_Layout.Footer.height / scale;
			m_Footer.style.display = m_Layout.Footer.height > 0f ? DisplayStyle.Flex : DisplayStyle.None;
			m_Footer.style.alignItems = Align.Center;
			foreach (var button in m_Header.Query<Button>().ToList().Concat(m_Footer.Query<Button>().ToList()))
			{
				button.style.height = m_Layout.HitHeight;
				button.style.minHeight = m_Layout.HitHeight;
				button.style.minWidth = m_Layout.HitHeight;
			}
		}

		/// <summary>
		/// Side by side, fills the sections and the list beside a page and moves the page right of them. The columns are for
		/// the pointer; the gamepad keeps to the page and goes back to the list with Back.
		/// </summary>
		private void BuildSplitColumns()
		{
			var scale = m_Layout.Scale > 0f ? m_Layout.Scale : 1f;
			var width = (int)m_Layout.Screen.width;
			var height = (int)m_Layout.Screen.height;
			var profile = m_UseProfileOverride ? m_ProfileOverride : DexcRuntimeUIReferenceResolver.ResolveCurrentProfile(width, height);
			var reference = DexcRuntimeUIReferenceResolver.Calculate(width, height, m_Layout.SafeArea, profile);
			var split = DexcDebugger.IsInitialized && reference.Split;
			m_SplitShown = reference.Split;
			foreach (var toggle in new[] { m_Single, m_Split })
			{
				toggle.style.display = reference.CanSplit ? DisplayStyle.Flex : DisplayStyle.None;
				toggle.style.width = 64f;
				ApplyButtonVisual(toggle, SwitchColor(toggle), false);
			}
			// The layout has already put the header, the page and the footer beside the sections.
			var sections = (m_Layout.Header.x - m_Layout.Panel.x) / scale;
			var list = 0f;
			if (split)
				DexcRuntimeUIRenderer.SplitColumns(DexcRuntimeUIState.Page, m_Layout.Content.width / scale, out _, out list);
			m_SideWidth = sections + list;
			m_ContentList.style.marginLeft = m_SideWidth;
			m_Header.style.marginLeft = sections;
			m_Footer.style.marginLeft = sections;
			var top = m_Layout.Header.height / scale;
			var bottom = m_Layout.Footer.height / scale;
			FillColumn(
				m_SplitSections,
				sections,
				0f,
				0f,
				0f,
				split && sections > 0f ? DexcRuntimeUIRenderer.SectionRows() : null,
				DexcRuntimeUIRenderer.SectionKey(DexcRuntimeUIState.Page)
			);
			var listPage = DexcRuntimeUIRenderer.SplitListPage(DexcRuntimeUIState.Page);
			FillColumn(
				m_SplitList,
				list,
				sections,
				top,
				bottom,
				list > 0f ? DexcRuntimeUIRenderer.ListRowsOf(listPage.Value).Where(row => row.ListItem).ToList() : null,
				DexcRuntimeUIRenderer.OpenEntryKey(DexcRuntimeUIState.Page)
			);
		}

		private void FillColumn(VisualElement column, float width, float left, float top, float bottom, List<DexcUIRow> rows, string current)
		{
			var container = column is ScrollView scroll ? scroll.contentContainer : column;
			container.Clear();
			column.style.display = rows == null ? DisplayStyle.None : DisplayStyle.Flex;
			if (rows == null)
				return;
			column.style.left = left;
			column.style.width = width;
			column.style.top = top;
			column.style.bottom = bottom;
			column.style.paddingLeft = column.style.paddingRight = 6f;
			column.style.paddingTop = 6f;
			column.style.backgroundColor = PanelColor;
			var sectionFocus = column == m_SplitSections ? DexcRuntimeSplitHome.SectionFocus : -1;
			for (var index = 0; index < rows.Count; index++)
			{
				var row = rows[index];
				var target = row;
				var end = ((row.TrailingText ?? string.Empty) + (row.Drill ? "  ›" : string.Empty)).Trim();
				var button = new Button();
				// The name keeps one line clear of the right-end text; a group's second line goes under it.
				var main = new Label((string.IsNullOrEmpty(row.Leading) ? string.Empty : row.Leading + "  ") + row.Label)
				{
					pickingMode = PickingMode.Ignore,
				};
				main.style.whiteSpace = WhiteSpace.NoWrap;
				main.style.overflow = Overflow.Hidden;
				main.style.textOverflow = TextOverflow.Ellipsis;
				main.style.unityTextAlign = TextAnchor.MiddleLeft;
				main.style.paddingLeft = main.style.paddingRight = main.style.paddingTop = main.style.paddingBottom = 0f;
				main.style.marginLeft = main.style.marginTop = main.style.marginBottom = 0f;
				button.Add(main);
				var second = row.DetailText;
				if (!string.IsNullOrEmpty(second))
				{
					var detail = new Label(second) { pickingMode = PickingMode.Ignore };
					detail.style.fontSize = DexcRuntimeFormLayout.SmallFontSize;
					detail.style.color = Palette.SecondaryText;
					detail.style.whiteSpace = WhiteSpace.NoWrap;
					detail.style.overflow = Overflow.Hidden;
					detail.style.textOverflow = TextOverflow.Ellipsis;
					detail.style.unityTextAlign = TextAnchor.MiddleLeft;
					detail.style.paddingLeft = detail.style.paddingRight = detail.style.paddingTop = detail.style.paddingBottom = 0f;
					detail.style.marginLeft = detail.style.marginTop = detail.style.marginBottom = 0f;
					button.Add(detail);
				}
				button.RegisterCallback<PointerUpEvent>(pointer =>
					InvokePointerRelease(
						pointer,
						button,
						() =>
						{
							target.Activated();
							Rebuild(true);
						}
					)
				);
				button.style.flexDirection = FlexDirection.Column;
				button.style.justifyContent = Justify.Center;
				button.style.minHeight = m_Layout.HitHeight;
				button.style.paddingTop = button.style.paddingBottom = 2f;
				button.style.marginLeft = button.style.marginRight = 0f;
				button.style.marginBottom = DexcRuntimeFormLayout.RowGap;
				// The gamepad's place when it has gone into the column of sections, where it would have gone back to Home.
				var marked = index == sectionFocus && DexcRuntimeUIState.NavigationFocusVisible;
				button.style.borderLeftWidth = button.style.borderRightWidth = button.style.borderTopWidth = button.style.borderBottomWidth =
					marked ? 2f : 0f;
				button.style.borderLeftColor = button.style.borderRightColor = Palette.Focus;
				button.style.borderTopColor = button.style.borderBottomColor = Palette.Focus;
				button.style.backgroundColor = row.Key == current ? Palette.Selection : RowColor;
				button.style.color = Palette.PrimaryText;
				if (end.Length > 0)
				{
					var trailing = new Label(end) { pickingMode = PickingMode.Ignore };
					trailing.style.position = Position.Absolute;
					trailing.style.right = 6f;
					trailing.style.top = 0f;
					trailing.style.bottom = 0f;
					trailing.style.unityTextAlign = TextAnchor.MiddleRight;
					trailing.style.whiteSpace = WhiteSpace.NoWrap;
					trailing.style.color = row.TrailingTone == DexcUIRowTone.Normal ? Palette.SecondaryText : Palette.Warning;
					button.Add(trailing);
					var size = trailing.MeasureTextSize(end, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
					main.style.marginRight = (float.IsNaN(size.x) ? 24f : size.x) + 8f;
				}
				container.Add(button);
			}
		}

		private StyleSnapshot ResolveStyleSnapshot()
		{
			var skin = Skin;
			return new StyleSnapshot(
				DexcRuntimeUISemanticPalette.FromSnapshot(skin),
				skin == null ? null : skin.Font,
				skin == null ? null : skin.Texture,
				skin == null ? null : skin.PanelSprite
			);
		}

		private void SetChoiceVisual(Button choice, RowModel item)
		{
			var on = string.Equals(item.Value, "true", StringComparison.OrdinalIgnoreCase);
			var steps = item.Source?.Cycle != null && !item.Boolean;
			choice.text = item.Boolean ? string.Empty : DexcRuntimeUIRenderer.ChoiceText(item.Value);
			choice.style.unityTextAlign = steps ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
			choice.style.paddingLeft = 6f;
			choice.style.paddingRight = steps ? 6f : 20f;
			var chevron = choice.Q<Label>("Chevron");
			chevron.style.display = item.Boolean || steps ? DisplayStyle.None : DisplayStyle.Flex;
			chevron.style.color = Palette.SecondaryText;
			// A boolean is a switch at the right end: an accent track when on, the knob at the side it is on.
			var track = choice.Q<VisualElement>("Switch");
			track.style.display = item.Boolean ? DisplayStyle.Flex : DisplayStyle.None;
			if (!item.Boolean)
				return;
			var size = Mathf.Min(22f, DexcRuntimeFormLayout.RowHeight(m_Layout.RowHeight, m_Layout.HitHeight) - 4f);
			track.style.width = size * 1.9f;
			track.style.height = size;
			track.style.marginTop = -size * 0.5f;
			track.style.backgroundColor = on ? AccentColor : RowColor;
			var knob = track.Q<VisualElement>("Knob");
			knob.style.width = knob.style.height = size - 4f;
			knob.style.top = 2f;
			knob.style.left = on ? size * 1.9f - size + 2f : 2f;
			knob.style.backgroundColor = on ? Palette.PrimaryText : Palette.SecondaryText;
		}

		private void StyleScrollAndSlider()
		{
			var scroll = ContentScrollView;
			if (scroll == null)
			{
				return;
			}

			scroll.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
			StyleScroller(scroll.verticalScroller);
			// The list beside a page scrolls with the same bar, shown only when its rows overflow.
			StyleScroller(m_SplitList.verticalScroller);
		}

		private void StyleScroller(Scroller scroller)
		{
			scroller.style.width = scroller.style.minWidth = scroller.style.maxWidth = 14f;
			scroller.style.backgroundColor = HeaderColor;
			scroller.lowButton.style.display = scroller.highButton.style.display = DisplayStyle.None;
			StyleSlider(scroller.slider, true);
		}

		private void StyleSlider(Slider slider, bool vertical)
		{
			var track = slider.Q<VisualElement>(className: Slider.trackerUssClassName);
			var thumb = slider.Q<VisualElement>(className: Slider.draggerUssClassName);
			if (track != null)
			{
				track.style.backgroundColor = HeaderColor;
				track.style.borderLeftWidth =
					track.style.borderRightWidth =
					track.style.borderTopWidth =
					track.style.borderBottomWidth =
						0f;
			}

			if (thumb != null)
			{
				thumb.style.backgroundColor = vertical ? Palette.SecondaryText : AccentColor;
				thumb.style.backgroundImage = StyleKeyword.None;
				thumb.style.borderLeftWidth =
					thumb.style.borderRightWidth =
					thumb.style.borderTopWidth =
					thumb.style.borderBottomWidth =
						0f;
			}
		}

		private void ApplyStyle(StyleSnapshot style)
		{
			if (m_Panel == null)
			{
				return;
			}

			m_Style = style;
			m_Panel.style.backgroundColor = PanelColor;
			m_Header.style.backgroundColor = HeaderColor;
			m_Footer.style.backgroundColor = HeaderColor;
			if (style.Texture != null)
			{
				m_Panel.style.backgroundImage = new StyleBackground(style.Texture);
			}
			else if (style.PanelSprite != null)
			{
				m_Panel.style.backgroundImage = new StyleBackground(style.PanelSprite);
			}
			else
			{
				m_Panel.style.backgroundImage = StyleKeyword.None;
			}

			if (style.Font != null)
			{
				m_Root.style.unityFont = style.Font;
			}
			else
			{
				m_Root.style.unityFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			}

			m_Root.style.color = Palette.PrimaryText;
			m_Root.style.fontSize = DexcRuntimeFormLayout.FontSize;
			StyleScrollAndSlider();
			m_Title.style.color = Palette.PrimaryText;
			ApplyButtonVisual(m_Back, RowColor, false);
			ApplyButtonVisual(m_Close, RowColor, false);
			ApplyButtonVisual(m_History, RowColor, false);
			ApplyButtonVisual(m_Favorite, RowColor, false);
			ApplyButtonVisual(m_FooterSecondary, RowColor, false);
			ApplyButtonVisual(m_FooterPrimary, AccentColor, true);
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
					BuildInspectorRegistered();
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

		/// <summary>Turns a page's rows into items. Each item keeps its row so <see cref="RefreshRows"/> can update it.</summary>
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
							label.Source = row;
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
							null,
							ToneTextColor(row)
						);
						if (button != null)
						{
							button.NavigationKey = row.Key;
							button.Entry = row.ListItem ? row : null;
						}
						break;
					case DexcUIRowKind.Input:
					case DexcUIRowKind.Choice:
						BuildRowInput(row);
						break;
					case DexcUIRowKind.Slider:
						m_Items.Add(
							new RowModel
							{
								Kind = RowKind.Slider,
								Source = row,
								Enabled = row.IsEnabled,
								Color = RowColor,
								TextColor = Palette.PrimaryText,
							}
						);
						break;
				}
			}
		}

		private Color ToneColor(DexcUIRow row) => row.Tone == DexcUIRowTone.Error ? ErrorColor : RowColor;

		private Color? ToneTextColor(DexcUIRow row) => row.Tone == DexcUIRowTone.Warning ? Palette.Warning : (Color?)null;

		private void BuildRowInput(DexcUIRow row)
		{
			var item = AddInput(row.Label, row.Text(), null, ToneColor(row), ToneTextColor(row), row.Multiline, row.Component);
			if (item == null)
				return;
			item.Source = row;
			item.NavigationKey = row.Key;
			item.Enabled = row.IsEnabled;
			if (row.Kind == DexcUIRowKind.Choice)
			{
				item.Kind = RowKind.Choice;
				item.Boolean = row.Boolean;
				item.Action = () =>
				{
					row.Activated();
					Rebuild(true);
				};
			}

			if (row.Auxiliary != null)
				item.Auxiliary = () => row.Auxiliary();
		}

		/// <summary>Re-reads every row in place, so typing keeps its focus and caret while values and errors follow the form.</summary>
		private void RefreshRows()
		{
			m_SettingVersion = DexcRuntimeSettingForm.Version;
			m_InspectorVersion = DexcRuntimeInspectorForm.Version;
			// Rows read the session; after it ends they wait for the page to be rebuilt.
			if (m_ContentList == null || !DexcDebugger.IsInitialized)
			{
				return;
			}

			foreach (var item in m_Items.Where(item => item.Source != null))
			{
				if (item.Kind == RowKind.Label && item.Source.Text != null)
					item.Text = item.Source.Text();
				if (item.Kind == RowKind.Input || item.Kind == RowKind.Choice)
					item.Value = item.Source.Text();
			}

			foreach (var row in m_ContentList.Query<VisualElement>(className: "dexc-uitk-row").ToList())
			{
				if (!(row.userData is RowModel item) || item.Source == null)
				{
					continue;
				}

				switch (item.Kind)
				{
					case RowKind.Label:
						row.Q<Label>("RowLabel").text = item.Text;
						break;
					case RowKind.Input:
						row.Q<TextField>("RowInput").SetValueWithoutNotify(item.Value);
						break;
					case RowKind.Choice:
						SetChoiceVisual(row.Q<Button>("RowChoice"), item);
						break;
					case RowKind.Slider:
						row.Q<Slider>("RowSlider").SetValueWithoutNotify(item.Source.SliderValue());
						break;
				}

				BindError(row, item);
			}

			AfterRowsRefreshed();
		}

		/// <summary>What a refresh changes beyond the rows: the page's Apply state, and for fields the selectable error marks.</summary>
		private void AfterRowsRefreshed()
		{
			if (m_FooterPrimary == null)
			{
				return;
			}

			if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.SettingDetail)
			{
				m_FooterPrimary.SetEnabled(DexcRuntimeSettingForm.CanApply);
				ApplyButtonVisual(m_FooterPrimary, DexcRuntimeSettingForm.CanApply ? AccentColor : Palette.Disabled, true);
			}
			else if (
				DexcRuntimeUIState.Page == DexcRuntimeUIPage.CommandDetail
				&& !DexcRuntimeUIState.Confirming
				&& DexcRuntimeUIState.AsyncExecution == null
			)
			{
				var enabled = CanExecute();
				m_FooterPrimary.SetEnabled(enabled);
				ApplyButtonVisual(m_FooterPrimary, enabled ? AccentColor : Palette.Disabled, true);
				m_Selectables.Clear();
				BuildSelections();
				if (m_FocusedTextField?.userData is RowModel focusedRow)
					SelectRow(focusedRow);
				if (m_FocusedTextField != null && m_FocusedTextField.panel != null)
					RefreshFocusVisuals();
				else
					FocusSelected();
			}
			else if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.InspectorFields)
			{
				m_FooterPrimary.SetEnabled(DexcRuntimeInspectorForm.CanApply);
				ApplyButtonVisual(m_FooterPrimary, DexcRuntimeInspectorForm.CanApply ? AccentColor : Palette.Disabled, true);
				m_Selectables.Clear();
				BuildSelections();
				if (m_FocusedTextField?.userData is RowModel focused)
				{
					SelectRow(focused);
				}
			}
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
		internal static TextField InspectorInputForTests(DebugInspectorFieldId id, string component = null) =>
			Instance
				?.m_ContentList.Query<TextField>()
				.ToList()
				.FirstOrDefault(input =>
					input.userData is RowModel item
					&& item.Source != null
					&& item.Source.Key == DexcRuntimeInspectorForm.Key(id, component)
					&& item.Component == component
				);
#endif

		private void BuildInspectorFields()
		{
			BuildRows(DexcRuntimeInspectorForm.FieldRows(true));
		}

		private void BuildInspectorWatches() => BuildInspectorList();

		private void BuildHistory() => BuildInspectorList();

		private void BuildFooter()
		{
			m_FooterSecondaryAction = null;
			m_FooterPrimaryAction = null;
			m_FooterSecondary.style.display = DisplayStyle.None;
			m_FooterPrimary.style.display = DisplayStyle.None;
			if (m_Layout.Footer.height <= 0f)
				return;
			if (DebugBootMenu.UsesRuntimeMenu)
			{
				ConfigureFooter(
					m_FooterSecondary,
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
				ConfigureFooter(m_FooterPrimary, "Start", () => DebugBootMenu.Start(), true, Palette.Accent, true);
				return;
			}
			if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.SettingDetail)
			{
				ConfigureFooter(
					m_FooterSecondary,
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
				ConfigureFooter(
					m_FooterPrimary,
					"Apply",
					() =>
					{
						if (DexcRuntimeUIRenderer.CanApplySetting())
							DexcRuntimeUIRenderer.ApplySettingForRenderer();
						Rebuild(true);
					},
					DexcRuntimeUIRenderer.CanApplySetting(),
					AccentColor,
					true
				);
				return;
			}
			if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.InspectorFields)
			{
				ConfigureFooter(
					m_FooterSecondary,
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
					m_FooterPrimary,
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
					m_FooterPrimary,
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
					m_FooterSecondary,
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
					m_FooterPrimary,
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
				ConfigureFooter(
					m_FooterPrimary,
					"▶ Cancel request",
					() => DexcRuntimeCommandExecution.CancelExecution(),
					true,
					AccentColor,
					true
				);
				return;
			}
			var enabled = CanExecute();
			ConfigureFooter(
				m_FooterPrimary,
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

		private void ConfigureFooter(Button button, string text, Action action, bool enabled, Color color, bool primary)
		{
			button.text = text;
			button.style.display = DisplayStyle.Flex;
			// The secondary action keeps a fixed width at the left; the primary one takes the rest.
			var panelWidth = m_Layout.Panel.width / Mathf.Max(0.0001f, m_Layout.Scale);
			button.style.flexGrow = primary ? 1f : 0f;
			button.style.width = primary ? StyleKeyword.Auto : new StyleLength(Mathf.Min(120f, Mathf.Max(60f, panelWidth * 0.36f)));
			button.SetEnabled(enabled);
			ApplyButtonVisual(button, enabled ? color : Palette.Disabled, primary);
			if (primary)
				m_FooterPrimaryAction = action;
			else
				m_FooterSecondaryAction = action;
		}

		private static bool CanExecute() => DexcRuntimeCommandExecution.CanExecute();

		/// <summary>Adds a label row and returns it, or null once the row cap is reached and the row is dropped.</summary>
		private RowModel AddLabel(string text, Color color, Color? textColor = null)
		{
			if (m_Items.Count >= MaximumRows)
				return null;
			var row = new RowModel
			{
				Kind = RowKind.Label,
				Text = text ?? string.Empty,
				Color = color,
				TextColor = textColor ?? Palette.SecondaryText,
				Enabled = false,
			};
			m_Items.Add(row);
			return row;
		}

		private RowModel AddButton(string text, Action action, Color? color = null, Color? textColor = null)
		{
			if (m_Items.Count >= MaximumRows)
				return null;
			var row = new RowModel
			{
				Kind = RowKind.Button,
				Text = text ?? string.Empty,
				Action = action,
				Color = color ?? RowColor,
				TextColor = textColor ?? Palette.PrimaryText,
				Enabled = true,
			};
			m_Items.Add(row);
			return row;
		}

		private RowModel AddInput(
			string text,
			string value,
			Action<string> changed,
			Color color,
			Color? textColor = null,
			bool multiline = false,
			string component = null
		)
		{
			if (m_Items.Count >= MaximumRows)
				return null;
			var row = new RowModel
			{
				Kind = RowKind.Input,
				Text = text ?? string.Empty,
				Value = value ?? string.Empty,
				ValueChanged = changed,
				Color = color,
				// A field's name reads in the main text colour, as in the other renderers.
				TextColor = textColor ?? Palette.PrimaryText,
				Enabled = true,
				Multiline = multiline,
				Component = component,
			};
			m_Items.Add(row);
			return row;
		}

		private void BuildSelections()
		{
			foreach (var item in m_Items)
			{
				if (
					item.Enabled
					&& (
						item.Kind == RowKind.Button
						|| item.Kind == RowKind.Input
						|| item.Kind == RowKind.Choice
						|| item.Kind == RowKind.Slider
					)
				)
					m_Selectables.Add(
						new Selection
						{
							Row = item,
							Action = item.Action,
							IsText = item.Kind == RowKind.Input,
						}
					);
				if (
					(item.Enabled || item.Source != null)
					&& (item.Kind == RowKind.Input || item.Kind == RowKind.Choice)
					&& item.Auxiliary != null
				)
					m_Selectables.Add(
						new Selection
						{
							Row = item,
							IsError = true,
							Action = () => OpenInputError(item),
						}
					);
			}
			if (m_FooterSecondary.style.display.value != DisplayStyle.None && m_FooterSecondaryAction != null)
				m_Selectables.Add(new Selection { Button = m_FooterSecondary, Action = m_FooterSecondaryAction });
			if (m_FooterPrimary.style.display.value != DisplayStyle.None && m_FooterPrimaryAction != null && m_FooterPrimary.enabledSelf)
				m_Selectables.Add(new Selection { Button = m_FooterPrimary, Action = m_FooterPrimaryAction });
			// History follows Execute on the gamepad's path, as it did when it sat beside it in the footer.
			if (m_History.style.display.value != DisplayStyle.None)
				m_Selectables.Add(new Selection { Button = m_History, Action = OpenCommandHistory });
			// Only the header buttons that are drawn: on the boot screen a hidden close would start the game.
			if (DexcRuntimeUIRenderer.HasHeaderBack(DexcRuntimeUIState.Page))
				m_Selectables.Add(
					new Selection
					{
						Button = m_Back,
						Action = () =>
						{
							DexcRuntimeUIState.Back();
							Rebuild(true);
						},
					}
				);
			if (DexcRuntimeUIRenderer.HasHeaderClose())
				m_Selectables.Add(
					new Selection
					{
						Button = m_Close,
						Action = () =>
						{
							DexcRuntimeUIState.Close();
							BlurFocused();
							UpdateVisibility();
						},
					}
				);
			// The star comes last, so the gamepad reaches it without moving anything it already knew.
			if (m_Favorite.style.display.value != DisplayStyle.None)
				m_Selectables.Add(
					new Selection
					{
						Button = m_Favorite,
						Action = () =>
						{
							DexcRuntimeFavorites.ToggleCurrent(DexcRuntimeUIState.Page);
							Rebuild(true, false);
						},
					}
				);
			DexcRuntimeUIState.ClampSelectedIndex(m_Selectables.Count);
		}

		private void OpenCommandHistory()
		{
			DexcRuntimeCommandForm.OpenHistory();
			Rebuild(true);
		}

		/// <summary>Runs a field's "…" action, or opens the whole message of a line of text's "!".</summary>
		private void OpenInputError(RowModel item)
		{
			var message = item == null || item.Kind != RowKind.Label ? null : InputError(item);
			if (item == null || (string.IsNullOrEmpty(message) && item.Auxiliary == null))
				return;
			m_FormReturnScroll = ContentScrollView == null ? Vector2.zero : ContentScrollView.scrollOffset;
			SelectRow(item);
			if (!string.IsNullOrEmpty(message))
				DexcRuntimeFormDetail.Open(message);
			else
				item.Auxiliary?.Invoke();
			Rebuild(true);
		}

		private void SelectRow(RowModel row)
		{
			var index = m_Selectables.FindIndex(selection => ReferenceEquals(selection.Row, row));
			if (index >= 0)
				DexcRuntimeUIState.SetSelectedIndex(index);
		}

		private void MoveSelection(int delta)
		{
			if (m_Selectables.Count == 0)
				return;
			var selected = DexcRuntimeUIState.ClampSelectedIndex(m_Selectables.Count);
			DexcRuntimeUIState.SetSelectedIndex(Mathf.Clamp(selected + delta, 0, m_Selectables.Count - 1));
			FocusSelected();
		}

		private void ActivateCurrentSelection()
		{
			if (m_Selectables.Count == 0)
				return;
			var selection = m_Selectables[DexcRuntimeUIState.ClampSelectedIndex(m_Selectables.Count)];
			if (selection.IsText)
			{
				var element = ResolveSelectionElement(selection);
				element?.Focus();
				return;
			}
			selection.Action?.Invoke();
		}

		private void FocusSelected(bool editText = false)
		{
			if (m_Selectables.Count == 0 || m_ContentList == null)
				return;
			var selection = m_Selectables[DexcRuntimeUIState.ClampSelectedIndex(m_Selectables.Count)];
			if (selection.Row != null)
			{
				var itemIndex = m_Items.IndexOf(selection.Row);
				if (itemIndex >= 0)
					m_ContentList.ScrollToItem(itemIndex);
			}
			m_Root.schedule.Execute(() =>
			{
				// A later navigation/rebuild supersedes this queued focus request.
				if (
					DexcRuntimeUIState.SelectedIndex >= m_Selectables.Count
					|| !ReferenceEquals(m_Selectables[DexcRuntimeUIState.SelectedIndex], selection)
				)
					return;
				if (selection.IsText && !editText)
				{
					// Landing on a text row only marks it; Submit (or a click) enters editing.
					if (m_FocusedTextField != null)
						BlurFocused();
					m_Root.Focus();
				}
				else
					ResolveSelectionElement(selection)?.Focus();
				RefreshFocusVisuals();
			});
		}

		private void RefreshFocusVisuals()
		{
			if (m_ContentList != null)
			{
				foreach (var row in m_ContentList.Query<VisualElement>(className: "dexc-uitk-row").ToList())
					ApplyRowVisual(row);
			}
			ApplyButtonVisual(m_Back, RowColor, false);
			ApplyButtonVisual(m_Close, RowColor, false);
			ApplyButtonVisual(m_History, RowColor, false);
			ApplyButtonVisual(m_Favorite, RowColor, false);
			ApplyButtonVisual(m_FooterSecondary, RowColor, false);
			ApplyButtonVisual(m_FooterPrimary, m_FooterPrimary.enabledSelf ? AccentColor : Palette.Disabled, true);
		}

		private VisualElement ResolveSelectionElement(Selection selection)
		{
			if (selection == null)
				return null;
			if (selection.Button != null)
				return selection.Button;
			var row = m_ContentList
				.Query<VisualElement>(className: "dexc-uitk-row")
				.ToList()
				.FirstOrDefault(element => ReferenceEquals(element.userData, selection.Row));
			if (row == null)
				return null;
			return selection.IsError ? row.Q<Button>("FieldError")
				: selection.IsText ? row.Q<TextField>("RowInput")
				: selection.Row?.Kind == RowKind.Slider ? row.Q<Slider>("RowSlider")
				: selection.Row?.Kind == RowKind.Choice ? row.Q<Button>("RowChoice")
				: row;
		}

		/// <summary>The inset of a field's band above and below its value.</summary>
		private const float FieldInset = 4f;

		/// <summary>The space between the list's rows and the panel's sides.</summary>
		private const float ListInset = 8f;

		/// <summary>What the theme's label margins and padding take from a column, so a line broken to fit it does fit.</summary>
		private const float LabelInset = 12f;

		private float ItemHeight()
		{
			return m_Layout.HitHeight;
		}

		private void Navigate(DexcRuntimeUIPage page)
		{
			DexcRuntimeUIState.Navigate(page);
			Rebuild(true);
		}

		private void BlurFocused()
		{
			var focused =
				m_FocusedTextField != null ? m_FocusedTextField
				: m_Root == null || m_Root.panel == null ? null
				: m_Root.panel.focusController.focusedElement as VisualElement;
			focused?.Blur();
			m_FocusedTextField = null;
		}
	}
}
