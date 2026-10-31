using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;

namespace Dexc
{
	/// <summary>Controls the boot-time menu. Independent from <see cref="DebugMenu"/> and usable before <see cref="DexcDebugger.Initialize()"/>.</summary>
	public static class DebugBootMenu
	{
		/// <summary>State of this class. Owned by <see cref="DexcUIRoot"/>; nothing here survives a root reset.</summary>
		internal sealed class State
		{
			/// <summary>The set being edited, with its values. Dropped when the menu closes.</summary>
			internal DebugBootSelection Selection;

			/// <summary>How this screen goes to the destination. The screen that opened the menu decides.</summary>
			internal Action<string> LoadScene;
			internal DebugMenuInputProvider InputProvider;
			internal bool IsOpen;
			internal bool UsesRuntimeMenu;
			internal Action Started;
			internal float AutoStartSeconds;
			internal string Title = "Boot Menu";
			internal DexcRuntimeUIAlignment? Alignment;
		}

		internal static State Data => DexcUIRoot.Current.BootMenu;

		/// <summary>The panels of the set being edited, or nothing when the menu is closed.</summary>
		internal static IReadOnlyList<DebugPanel> Panels => Data.Selection == null ? Array.Empty<DebugPanel>() : Data.Selection.Panels;

		internal static DebugMenuInputProvider InputProvider
		{
			get => Data.InputProvider;
			private set => Data.InputProvider = value;
		}
		public static bool IsOpen
		{
			get => Data.IsOpen;
			private set => Data.IsOpen = value;
		}

		/// <summary>True while the boot menu is shown through the Runtime menu (session initialized) instead of the standalone IMGUI renderer.</summary>
		internal static bool UsesRuntimeMenu
		{
			get => Data.UsesRuntimeMenu;
			private set => Data.UsesRuntimeMenu = value;
		}

		/// <summary>Seconds until the menu starts automatically. 0 or less waits for the user. Any navigation input cancels the countdown.</summary>
		public static float AutoStartSeconds
		{
			get => Data.AutoStartSeconds;
			set => Data.AutoStartSeconds = value;
		}

		/// <summary>
		/// Where the boot screen sits, when it should not sit where the debug menu does. Null keeps the Skin's placement for
		/// the current profile.
		/// </summary>
		public static DexcRuntimeUIAlignment? Alignment
		{
			get => Data.Alignment;
			set => Data.Alignment = value;
		}

		/// <summary>Title shown in the header.</summary>
		public static string Title
		{
			get => Data.Title;
			set => Data.Title = value;
		}

		/// <summary>
		/// Raised after the boot menu closes through Start. Subscriptions live on the root, so they are dropped when Play
		/// Mode ends instead of keeping the previous run's objects alive.
		/// </summary>
		public static event Action Started
		{
			add { Data.Started += value; }
			remove { Data.Started -= value; }
		}

		/// <summary>Opens the boot menu. <paramref name="onStart"/> runs once when the user starts the game.</summary>
		[Conditional("DEXC_DEBUGGER")]
		public static void Open(string tag, Action<string> loadScene)
		{
#if DEXC_DEBUGGER
			if (loadScene == null)
				throw new ArgumentNullException(nameof(loadScene));
			Data.Selection = DebugBootSelection.Open(tag);
			Data.LoadScene = loadScene;
			IsOpen = true;
			DexcBootMenuState.Reset();
			// The driver polls input and draws only in standalone mode; in Runtime-menu mode it just runs the auto-start timer.
			DexcBootMenuDriver.Ensure();
			if (DexcDebugger.IsInitialized)
			{
				// A session exists: render through the Runtime menu so the selected renderer (IMGUI/uGUI/UI Toolkit) is used.
				UsesRuntimeMenu = true;
				DexcRuntimeUIState.Open();
				DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.PanelList);
				return;
			}
			UsesRuntimeMenu = false;
#endif
		}

		/// <summary>Closes the menu without starting.</summary>
		[Conditional("DEXC_DEBUGGER")]
		public static void Close()
		{
#if DEXC_DEBUGGER
			IsOpen = false;
			Data.Selection = null;
			Data.LoadScene = null;
			if (UsesRuntimeMenu)
			{
				UsesRuntimeMenu = false;
				if (DexcRuntimeUIState.IsOpen)
					DexcRuntimeUIState.Close();
			}
			DexcBootMenuDriver.Destroy();
#endif
		}

		/// <summary>Hands the values to the game, goes to the destination and closes the menu.</summary>
		[Conditional("DEXC_DEBUGGER")]
		public static void Start()
		{
#if DEXC_DEBUGGER
			if (!IsOpen)
				return;
			var selection = Data.Selection;
			var loadScene = Data.LoadScene;
			IsOpen = false;
			Data.Selection = null;
			Data.LoadScene = null;
			if (UsesRuntimeMenu)
			{
				UsesRuntimeMenu = false;
				if (DexcRuntimeUIState.IsOpen)
					DexcRuntimeUIState.Close();
			}
			DexcBootMenuDriver.Destroy();
			try
			{
				if (selection != null && loadScene != null)
					selection.Start(loadScene);
			}
			catch (Exception exception)
			{
				UnityEngine.Debug.LogException(exception);
			}
			try
			{
				Data.Started?.Invoke();
			}
			catch (Exception exception)
			{
				UnityEngine.Debug.LogException(exception);
			}
#endif
		}

		/// <summary>Optional gamepad provider. When unset, Input System gamepads are read directly if the package is present.</summary>
		public static void SetInputProvider(DebugMenuInputProvider provider)
		{
			InputProvider = provider;
		}

		/// <summary>Any user interaction cancels the auto-start countdown. Called by the boot menu and the Runtime menu.</summary>
		internal static void NoteInteraction()
		{
#if DEXC_DEBUGGER
			if (IsOpen)
				DexcBootMenuState.CancelAutoStart();
#endif
		}

		/// <summary>Whole seconds until auto start, or 0 when no countdown is running.</summary>
		internal static int AutoStartRemainingSeconds
		{
			get
			{
#if DEXC_DEBUGGER
				if (!IsOpen || DexcBootMenuState.AutoStartAt <= 0f || DexcBootMenuState.AutoStartCancelled)
					return 0;
				return Mathf.Max(0, Mathf.CeilToInt(DexcBootMenuState.AutoStartAt - Time.realtimeSinceStartup));
#else
				return 0;
#endif
			}
		}

		/// <summary>Header shown by the Runtime menu in boot mode: title, renderer and the auto-start countdown.</summary>
		internal static string RuntimeTitle()
		{
			var remaining = AutoStartRemainingSeconds;
			return (string.IsNullOrEmpty(Title) ? "Boot Menu" : Title)
				+ " · "
				+ DebugMenu.RendererName(DebugMenu.Renderer)
				+ (remaining > 0 ? " · " + remaining + "s" : string.Empty);
		}
	}

#if DEXC_DEBUGGER
	internal static class DexcBootMenuState
	{
		internal sealed class Row
		{
			internal DebugWidget Widget;
			internal bool Focusable => !(Widget is DebugLabelWidget);
			internal string ControlName;
		}

		/// <summary>State of this class. Owned by <see cref="DexcUIRoot"/>; nothing here survives a root reset.</summary>
		internal sealed class State
		{
			internal readonly List<Row> Rows = new List<Row>();
			internal readonly List<DebugPanel> Groups = new List<DebugPanel>();
			internal int Focus;
			internal Vector2 Scroll;
			internal bool ScrollToFocus;
			internal float AutoStartAt;
			internal bool AutoStartCancelled;
			internal string EditText;
			internal string EditingControl;
			internal string PendingFocusControl;
			internal Vector2 RepeatDirection;
			internal float RepeatAt;
			internal int ActionFrame = -1;
			internal string Notice = string.Empty;
			internal bool ReleaseFocusRequested;
			internal bool FocusVisible = true;
		}

		internal static State Data => DexcUIRoot.Current.BootMenuState;
		internal static List<Row> Rows => Data.Rows;
		internal static List<DebugPanel> Groups => Data.Groups;
		internal static int Focus
		{
			get => Data.Focus;
			set => Data.Focus = value;
		}
		internal static Vector2 Scroll
		{
			get => Data.Scroll;
			set => Data.Scroll = value;
		}
		internal static bool ScrollToFocus
		{
			get => Data.ScrollToFocus;
			set => Data.ScrollToFocus = value;
		}
		internal static float AutoStartAt
		{
			get => Data.AutoStartAt;
			set => Data.AutoStartAt = value;
		}
		internal static bool AutoStartCancelled
		{
			get => Data.AutoStartCancelled;
			set => Data.AutoStartCancelled = value;
		}
		internal static string EditText
		{
			get => Data.EditText;
			set => Data.EditText = value;
		}
		internal static string EditingControl
		{
			get => Data.EditingControl;
			set => Data.EditingControl = value;
		}
		internal static string PendingFocusControl
		{
			get => Data.PendingFocusControl;
			set => Data.PendingFocusControl = value;
		}
		internal static Vector2 RepeatDirection
		{
			get => Data.RepeatDirection;
			set => Data.RepeatDirection = value;
		}
		internal static float RepeatAt
		{
			get => Data.RepeatAt;
			set => Data.RepeatAt = value;
		}
		internal static int ActionFrame
		{
			get => Data.ActionFrame;
			set => Data.ActionFrame = value;
		}
		internal static string Notice
		{
			get => Data.Notice;
			set => Data.Notice = value;
		}
		internal static bool ReleaseFocusRequested
		{
			get => Data.ReleaseFocusRequested;
			set => Data.ReleaseFocusRequested = value;
		}
		internal static bool FocusVisible
		{
			get => Data.FocusVisible;
			set => Data.FocusVisible = value;
		}
		internal const int FooterReset = -2;
		internal const int FooterStart = -1;

		internal static void Reset()
		{
			Focus = 0;
			Scroll = Vector2.zero;
			EditText = null;
			EditingControl = null;
			PendingFocusControl = null;
			Notice = string.Empty;
			ReleaseFocusRequested = false;
			FocusVisible = true;
			AutoStartCancelled = false;
			AutoStartAt = DebugBootMenu.AutoStartSeconds > 0f ? Time.realtimeSinceStartup + DebugBootMenu.AutoStartSeconds : 0f;
			Rebuild();
		}

		internal static void Rebuild()
		{
			Rows.Clear();
			Groups.Clear();
			var index = 0;
			foreach (var group in DebugBootMenu.Panels)
			{
				Groups.Add(group);
				foreach (var widget in group.Widgets)
					Rows.Add(new Row { Widget = widget, ControlName = "dexc-boot:" + index++ });
			}
			ClampFocus();
		}

		internal static int FocusableCount
		{
			get
			{
				var count = 0;
				foreach (var row in Rows)
					if (row.Focusable)
						count++;
				return count + 2;
			}
		}

		/// <summary>Focus order: rows (focusable only) then Reset then Start.</summary>
		internal static Row FocusedRow
		{
			get
			{
				var i = 0;
				foreach (var row in Rows)
				{
					if (!row.Focusable)
						continue;
					if (i == Focus)
						return row;
					i++;
				}
				return null;
			}
		}

		internal static int FocusedFooter =>
			Focus == FocusableCount - 2 ? FooterReset
			: Focus == FocusableCount - 1 ? FooterStart
			: 0;

		internal static void ClampFocus()
		{
			Focus = Mathf.Clamp(Focus, 0, Mathf.Max(0, FocusableCount - 1));
		}

		internal static void Move(int delta)
		{
			CancelAutoStart();
			Focus = Mathf.Clamp(Focus + delta, 0, FocusableCount - 1);
			ScrollToFocus = true;
		}

		internal static void CancelAutoStart()
		{
			AutoStartCancelled = true;
		}

		internal static bool ConsumeAction()
		{
			if (ActionFrame == Time.frameCount)
				return false;
			ActionFrame = Time.frameCount;
			return true;
		}

		internal static void Adjust(int delta)
		{
			CancelAutoStart();
			var row = FocusedRow;
			if (row == null || !row.Widget.Enabled)
				return;
			try
			{
				switch (row.Widget)
				{
					case DebugToggleWidget toggle:
						toggle.Toggle();
						break;
					case DebugChoiceWidget choice:
						choice.Move(delta);
						break;
					case DebugNumberWidget number:
						number.Move(delta);
						break;
				}
			}
			catch (Exception exception)
			{
				Notice = exception.Message;
			}
		}

		internal static void Activate()
		{
			CancelAutoStart();
			if (FocusedFooter == FooterStart)
			{
				DebugBootMenu.Start();
				return;
			}
			if (FocusedFooter == FooterReset)
			{
				ResetAll();
				return;
			}
			var row = FocusedRow;
			if (row == null || !row.Widget.Enabled)
				return;
			try
			{
				switch (row.Widget)
				{
					case DebugToggleWidget toggle:
						toggle.Toggle();
						break;
					case DebugChoiceWidget choice:
						choice.Move(1);
						break;
					case DebugNumberWidget _:
					case DebugTextWidget _:
						PendingFocusControl = row.ControlName;
						break;
					case DebugButtonWidget button:
						button.Invoke();
						break;
				}
			}
			catch (Exception exception)
			{
				Notice = exception.Message;
			}
		}

		internal static void ResetAll()
		{
			try
			{
				DebugBootMenu.Data.Selection?.Reset();
				Rebuild();
				Notice = "Reset to defaults.";
			}
			catch (Exception exception)
			{
				Notice = exception.Message;
			}
			Rebuild();
		}

		internal static void CommitEdit()
		{
			if (EditingControl == null)
				return;
			foreach (var row in Rows)
			{
				if (row.ControlName != EditingControl)
					continue;
				try
				{
					if (row.Widget is DebugNumberWidget number)
					{
						if (!number.TrySetText(EditText ?? string.Empty))
							Notice = "Invalid number: " + EditText;
					}
					else if (row.Widget is DebugTextWidget text)
						text.Value = EditText ?? string.Empty;
				}
				catch (Exception exception)
				{
					Notice = exception.Message;
				}
				break;
			}
			EditingControl = null;
			EditText = null;
		}
	}

	/// <summary>Reads gamepad navigation from a provider or, by default, from Input System's Gamepad.current through reflection.</summary>
	internal static class DexcBootMenuInput
	{
		/// <summary>State of this class. Owned by <see cref="DexcUIRoot"/>; nothing here survives a root reset.</summary>
		internal sealed class State
		{
			internal readonly Type GamepadType;
			internal readonly PropertyInfo Current;
			internal readonly PropertyInfo Dpad;
			internal readonly PropertyInfo ButtonSouth;
			internal readonly PropertyInfo ButtonEast;
			internal readonly PropertyInfo StartButton;
			internal MethodInfo ReadVectorMethod;
			internal PropertyInfo WasPressedProperty;
			internal readonly HashSet<string> DiagnosticKeys = new HashSet<string>(StringComparer.Ordinal);

			internal State()
			{
				GamepadType = Type.GetType("UnityEngine.InputSystem.Gamepad, Unity.InputSystem", false);
				if (GamepadType == null)
					return;
				Current = GamepadType.GetProperty("current", BindingFlags.Public | BindingFlags.Static);
				Dpad = GamepadType.GetProperty("dpad", BindingFlags.Public | BindingFlags.Instance);
				ButtonSouth = GamepadType.GetProperty("buttonSouth", BindingFlags.Public | BindingFlags.Instance);
				ButtonEast = GamepadType.GetProperty("buttonEast", BindingFlags.Public | BindingFlags.Instance);
				StartButton = GamepadType.GetProperty("startButton", BindingFlags.Public | BindingFlags.Instance);
			}
		}

		internal static State Data => DexcUIRoot.Current.BootMenuInput;
		static Type GamepadType => Data.GamepadType;
		static PropertyInfo Current => Data.Current;
		static PropertyInfo Dpad => Data.Dpad;
		static PropertyInfo ButtonSouth => Data.ButtonSouth;
		static PropertyInfo ButtonEast => Data.ButtonEast;
		static PropertyInfo StartButton => Data.StartButton;
		static MethodInfo ReadVectorMethod
		{
			get => Data.ReadVectorMethod;
			set => Data.ReadVectorMethod = value;
		}
		static PropertyInfo WasPressedProperty
		{
			get => Data.WasPressedProperty;
			set => Data.WasPressedProperty = value;
		}

		internal static DebugMenuInputFrame Read()
		{
			var provider = DebugBootMenu.InputProvider;
			if (provider != null)
			{
				try
				{
					return provider.ReadInput();
				}
				catch (Exception exception)
				{
					DexcBootMenuState.Notice = exception.Message;
					// The same failure repeats every frame; the Runtime menu records it once, and so does this.
					var key = provider.GetType().FullName + ":" + exception.GetType().FullName + ":" + exception.Message;
					if (Data.DiagnosticKeys.Add(key))
						DexcRuntimeUIBridge.AddDiagnostic(
							"input-provider-read-failed",
							provider.GetType().FullName,
							"The boot menu input provider failed to read input.",
							exception
						);
					return default(DebugMenuInputFrame);
				}
			}
			return ReadGamepad();
		}

		/// <summary>Reads the current Input System gamepad through reflection; empty when the package or a gamepad is absent.</summary>
		internal static DebugMenuInputFrame ReadGamepad()
		{
			if (Current == null)
				return default(DebugMenuInputFrame);
			try
			{
				var gamepad = Current.GetValue(null, null);
				if (gamepad == null)
					return default(DebugMenuInputFrame);
				var navigation = ReadVector(Dpad?.GetValue(gamepad, null));
				var submit = WasPressed(ButtonSouth?.GetValue(gamepad, null)) || WasPressed(StartButton?.GetValue(gamepad, null));
				var cancel = WasPressed(ButtonEast?.GetValue(gamepad, null));
				return new DebugMenuInputFrame(navigation, submit, cancel);
			}
			catch (Exception)
			{
				return default(DebugMenuInputFrame);
			}
		}

		static Vector2 ReadVector(object control)
		{
			if (control == null)
				return Vector2.zero;
			if (ReadVectorMethod == null || ReadVectorMethod.DeclaringType == null || !ReadVectorMethod.DeclaringType.IsInstanceOfType(control))
				ReadVectorMethod = control
					.GetType()
					.GetMethod("ReadValue", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
			return ReadVectorMethod == null ? Vector2.zero : (Vector2)ReadVectorMethod.Invoke(control, null);
		}

		static bool WasPressed(object button)
		{
			if (button == null)
				return false;
			if (WasPressedProperty == null)
				WasPressedProperty = button.GetType().GetProperty("wasPressedThisFrame", BindingFlags.Public | BindingFlags.Instance);
			return WasPressedProperty != null && (bool)WasPressedProperty.GetValue(button, null);
		}

		internal static void Poll()
		{
			var input = Read();
			if (DexcBootMenuState.EditingControl != null)
			{
				// Back or Submit leaves the text field; the value is committed on the next draw.
				if ((input.CancelPressed || input.SubmitPressed) && DexcBootMenuState.ConsumeAction())
					DexcBootMenuState.ReleaseFocusRequested = true;
				return;
			}
			if (input.CancelPressed && DexcBootMenuState.ConsumeAction())
			{
				DexcBootMenuState.CancelAutoStart();
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
				changed = Vector2.Dot(normalized, DexcBootMenuState.RepeatDirection) < 0.8f;
				move = changed || now >= DexcBootMenuState.RepeatAt;
			}
			else
				DexcBootMenuState.RepeatDirection = Vector2.zero;
			if ((!move && !input.SubmitPressed) || !DexcBootMenuState.ConsumeAction())
				return;
			DexcBootMenuState.FocusVisible = true;
			if (move)
			{
				DexcBootMenuState.RepeatDirection = normalized;
				DexcBootMenuState.RepeatAt = now + (changed ? 0.35f : 0.08f);
				Apply(normalized);
			}
			if (input.SubmitPressed)
				DexcBootMenuState.Activate();
		}

		internal static void Apply(Vector2 direction)
		{
			if (Mathf.Abs(direction.y) >= Mathf.Abs(direction.x))
				DexcBootMenuState.Move(direction.y > 0f ? -1 : 1);
			else
				DexcBootMenuState.Adjust(direction.x > 0f ? 1 : -1);
		}
	}

	internal static class DexcBootMenuRenderer
	{
		const float LabelFraction = DexcRuntimeFormLayout.LabelFraction;

		/// <summary>State of this class. Owned by <see cref="DexcUIRoot"/>; nothing here survives a root reset.</summary>
		internal sealed class State
		{
			internal float RowHeight = DexcRuntimeFormLayout.DefaultRowHeight;
			internal readonly Dictionary<string, Rect> RowRects = new Dictionary<string, Rect>(StringComparer.Ordinal);
		}

		internal static State Data => DexcUIRoot.Current.BootMenuRenderer;
		static float RowHeight
		{
			get => Data.RowHeight;
			set => Data.RowHeight = value;
		}
		static Dictionary<string, Rect> RowRects => Data.RowRects;

		internal static void Draw()
		{
			var styles = DexcRuntimeUIRenderer.EnsureStyleCache();
			if (DexcBootMenuState.ReleaseFocusRequested)
			{
				DexcBootMenuState.CommitEdit();
				GUI.FocusControl(null);
				DexcBootMenuState.ReleaseFocusRequested = false;
			}
			if (Event.current.type == EventType.MouseDown)
				DexcBootMenuState.FocusVisible = false;
			HandleKeyboard();
			if (!DebugBootMenu.IsOpen)
				return;
			var profile = DexcRuntimeUIReferenceResolver.ResolveCurrentProfile(Screen.width, Screen.height);
			var reference = DexcRuntimeUIReferenceResolver.Calculate(Screen.width, Screen.height, Screen.safeArea, profile);
			RowHeight = DexcRuntimeFormLayout.RowHeight(reference.RowHeight, reference.HitHeight);
			var scale = reference.Scale > 0f ? reference.Scale : 1f;
			var panelPhysical = new Rect(
				reference.Panel.x,
				reference.Screen.height - reference.Panel.yMax,
				reference.Panel.width,
				reference.Panel.height
			);
			var panel = new Rect(
				panelPhysical.x / scale,
				panelPhysical.y / scale,
				panelPhysical.width / scale,
				panelPhysical.height / scale
			);
			var barHeight = Mathf.Min(reference.HitHeight + 12f, panel.height);
			var header = new Rect(panel.x, panel.y, panel.width, barHeight);
			var footer = new Rect(panel.x, panel.yMax - barHeight, panel.width, barHeight);
			var content = new Rect(panel.x, panel.y + barHeight, panel.width, Mathf.Max(0f, panel.height - barHeight * 2f));

			var previousMatrix = GUI.matrix;
			var origin = GUIUtility.GUIToScreenPoint(Vector2.zero);
			var unit = GUIUtility.GUIToScreenPoint(Vector2.one);
			var pixelsPerPoint = Mathf.Max(1f, (Mathf.Abs(unit.x - origin.x) + Mathf.Abs(unit.y - origin.y)) * 0.5f);
			var matrixScale = scale / pixelsPerPoint;
			GUI.matrix = Matrix4x4.Scale(new Vector3(matrixScale, matrixScale, 1f));
			try
			{
				GUI.Box(panel, GUIContent.none, styles.Panel);
				GUI.Box(header, GUIContent.none, styles.Header);
				GUI.Box(footer, GUIContent.none, styles.Footer);
				GUILayout.BeginArea(header);
				DrawHeader(styles);
				GUILayout.EndArea();

				// Side by side the groups are listed at the left; choosing one brings its first item into view.
				if (reference.Split && DexcBootMenuState.Groups.Count > 1)
				{
					var index = new Rect(content.x, content.y, DexcRuntimeFormLayout.SplitListWidth, content.height);
					DrawGroupIndex(styles, index);
					content = new Rect(index.xMax, content.y, content.width - index.width, content.height);
				}
				GUILayout.BeginArea(content);
				GUILayout.BeginVertical(styles.Panel, GUILayout.Width(content.width), GUILayout.Height(content.height));
				var viewWidth = Mathf.Max(1f, content.width - styles.Panel.padding.horizontal);
				var viewHeight = Mathf.Max(1f, content.height - styles.Panel.padding.vertical);
				var contentWidth = Mathf.Max(1f, viewWidth - styles.ScrollbarWidth - 4f);
				DexcBootMenuState.Scroll = styles.BeginScrollView(DexcBootMenuState.Scroll, viewWidth, viewHeight);
				DexcBootMenuState.Data.Scroll.x = 0f;
				GUILayout.BeginVertical(GUILayout.Width(contentWidth), GUILayout.ExpandWidth(false));
				DrawGroups(styles, contentWidth);
				if (!string.IsNullOrEmpty(DexcBootMenuState.Notice))
					GUILayout.Label(DexcBootMenuState.Notice, styles.WarningBox);
				GUILayout.EndVertical();
				DexcBootMenuState.Scroll = styles.EndScrollView(DexcBootMenuState.Scroll);
				GUILayout.EndVertical();
				GUILayout.EndArea();

				GUILayout.BeginArea(footer);
				DrawFooter(styles);
				GUILayout.EndArea();

				if (Event.current.type == EventType.Repaint)
				{
					if (DexcBootMenuState.ScrollToFocus)
					{
						EnsureFocusVisible(viewHeight);
						DexcBootMenuState.ScrollToFocus = false;
					}
					if (DexcBootMenuState.PendingFocusControl != null)
					{
						BeginEdit(DexcBootMenuState.PendingFocusControl);
						GUI.FocusControl(DexcBootMenuState.PendingFocusControl);
						DexcBootMenuState.PendingFocusControl = null;
					}
				}
				if (
					DexcBootMenuState.EditingControl != null
					&& Event.current.type != EventType.Layout
					&& GUI.GetNameOfFocusedControl() != DexcBootMenuState.EditingControl
				)
					DexcBootMenuState.CommitEdit();
			}
			finally
			{
				GUI.matrix = previousMatrix;
			}
		}

		static void BeginEdit(string control)
		{
			foreach (var row in DexcBootMenuState.Rows)
			{
				if (row.ControlName != control)
					continue;
				DexcBootMenuState.EditingControl = control;
				DexcBootMenuState.EditText =
					row.Widget is DebugNumberWidget number ? number.Text
					: row.Widget is DebugTextWidget text ? text.Value
					: string.Empty;
				return;
			}
		}

		static void HandleKeyboard()
		{
			var current = Event.current;
			if (current == null || current.type != EventType.KeyDown)
				return;
			var editing = !string.IsNullOrEmpty(GUI.GetNameOfFocusedControl());
			if (editing)
			{
				if (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter || current.keyCode == KeyCode.Escape)
				{
					DexcBootMenuState.CommitEdit();
					GUI.FocusControl(null);
					current.Use();
				}
				return;
			}
			var vertical = 0;
			var horizontal = 0;
			var submit = false;
			if (!current.alt && !current.control && !current.command)
			{
				switch (current.keyCode)
				{
					case KeyCode.Tab:
						vertical = current.shift ? -1 : 1;
						break;
					case KeyCode.UpArrow:
						vertical = -1;
						break;
					case KeyCode.DownArrow:
						vertical = 1;
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
					case KeyCode.Escape:
						DexcBootMenuState.CancelAutoStart();
						current.Use();
						return;
				}
			}
			if (vertical == 0 && horizontal == 0 && !submit)
				return;
			DexcBootMenuState.FocusVisible = true;
			if (DexcBootMenuState.ConsumeAction())
			{
				if (vertical != 0)
					DexcBootMenuState.Move(vertical);
				else if (horizontal != 0)
					DexcBootMenuState.Adjust(horizontal);
				else
					DexcBootMenuState.Activate();
			}
			current.Use();
		}

		static void DrawHeader(DexcRuntimeIMGUIStyles styles)
		{
			GUILayout.BeginVertical();
			GUILayout.Space(6f);
			GUILayout.BeginHorizontal();
			GUILayout.Space(4f);
			GUILayout.Label(DebugBootMenu.Title, styles.PrimaryLabel, GUILayout.Height(RowHeight));
			GUILayout.FlexibleSpace();
			// Where the screen is wide enough, the header switches between one column and the groups beside the rows.
			var profile = DexcRuntimeUIReferenceResolver.ResolveCurrentProfile(Screen.width, Screen.height);
			var reference = DexcRuntimeUIReferenceResolver.Calculate(Screen.width, Screen.height, Screen.safeArea, profile);
			if (reference.CanSplit)
			{
				if (GUILayout.Button("Single", reference.Split ? styles.SwitchButton : styles.SelectedSecondaryButton, GUILayout.Height(RowHeight)))
					DexcRuntimeUIState.LayoutOverride = DexcRuntimeUILayoutMode.Single;
				if (GUILayout.Button("Split", reference.Split ? styles.SelectedSecondaryButton : styles.SwitchButton, GUILayout.Height(RowHeight)))
					DexcRuntimeUIState.LayoutOverride = DexcRuntimeUILayoutMode.Split;
				GUILayout.Space(4f);
			}
			if (DexcBootMenuState.AutoStartAt > 0f && !DexcBootMenuState.AutoStartCancelled)
			{
				var remaining = Mathf.Max(0f, DexcBootMenuState.AutoStartAt - Time.realtimeSinceStartup);
				GUILayout.Label("Auto start " + Mathf.CeilToInt(remaining) + "s", styles.SecondaryLabel, GUILayout.Height(RowHeight));
			}
			GUILayout.Space(4f);
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
		}

		static void DrawFooter(DexcRuntimeIMGUIStyles styles)
		{
			GUILayout.BeginHorizontal(styles.Footer, GUILayout.Height(RowHeight + 12f));
			// Reset keeps a fixed width at the left; Start takes the rest. The focused one is outlined.
			var focus = DexcBootMenuState.FocusVisible ? DexcBootMenuState.FocusedFooter : 0;
			if (GUILayout.Button("Reset", styles.SecondaryButton, GUILayout.Height(RowHeight), GUILayout.Width(96f)))
			{
				DexcBootMenuState.Focus = DexcBootMenuState.FocusableCount - 2;
				DexcBootMenuState.ResetAll();
			}
			OutlineIf(focus == DexcBootMenuState.FooterReset, styles);
			if (GUILayout.Button("Start", styles.PrimaryButton, GUILayout.Height(RowHeight), GUILayout.ExpandWidth(true)))
				DebugBootMenu.Start();
			OutlineIf(focus == DexcBootMenuState.FooterStart, styles);
			GUILayout.EndHorizontal();
		}

		/// <summary>The groups as a list beside the rows; the one holding the focused item is marked.</summary>
		static void DrawGroupIndex(DexcRuntimeIMGUIStyles styles, Rect area)
		{
			GUILayout.BeginArea(area);
			GUILayout.BeginVertical(styles.Panel, GUILayout.Width(area.width), GUILayout.Height(area.height));
			var focused = DexcBootMenuState.FocusedRow;
			var rowIndex = 0;
			foreach (var group in DexcBootMenuState.Groups)
			{
				var first = rowIndex;
				var current = false;
				foreach (var widget in group.Widgets)
				{
					current |= ReferenceEquals(DexcBootMenuState.Rows[rowIndex], focused);
					rowIndex++;
				}
				if (GUILayout.Button(group.Title, current ? styles.SelectedRowButton : styles.RowButton, GUILayout.MinHeight(RowHeight)))
				{
					DexcBootMenuState.Row target = null;
					for (var candidate = first; candidate < rowIndex && target == null; candidate++)
						if (DexcBootMenuState.Rows[candidate].Focusable)
							target = DexcBootMenuState.Rows[candidate];
					if (target != null)
					{
						FocusRow(target);
						DexcBootMenuState.ScrollToFocus = true;
					}
				}
				GUILayout.Space(DexcRuntimeFormLayout.RowGap);
			}
			GUILayout.EndVertical();
			GUILayout.EndArea();
		}

		static void OutlineIf(bool focused, DexcRuntimeIMGUIStyles styles)
		{
			if (focused && Event.current.type == EventType.Repaint)
				DexcRuntimeUIRenderer.DrawOutline(GUILayoutUtility.GetLastRect(), styles.Palette.Focus, 2f);
		}

		static void DrawGroups(DexcRuntimeIMGUIStyles styles, float width)
		{
			if (DexcBootMenuState.Rows.Count == 0)
			{
				GUILayout.Label(
					"No boot items. Add a class marked [DebugBootMenu] deriving DebugBootSet, or register a DebugPanel.",
					styles.SecondaryLabel
				);
				return;
			}
			var focused = DexcBootMenuState.FocusVisible ? DexcBootMenuState.FocusedRow : null;
			var rowIndex = 0;
			foreach (var group in DexcBootMenuState.Groups)
			{
				GUILayout.Label(group.Title, styles.Section);
				foreach (var widget in group.Widgets)
				{
					var row = DexcBootMenuState.Rows[rowIndex++];
					DrawRow(styles, row, ReferenceEquals(row, focused), width);
					GUILayout.Space(DexcRuntimeFormLayout.RowGap);
				}
				GUILayout.Space(DexcRuntimeFormLayout.RowGap);
			}
		}

		static void DrawRow(DexcRuntimeIMGUIStyles styles, DexcBootMenuState.Row row, bool focused, float width)
		{
			var widget = row.Widget;
			if (widget is DebugLabelWidget label)
			{
				GUILayout.Label(label.Text, styles.SecondaryLabel);
				return;
			}
			var enabled = widget.Enabled;
			var hasDescription = !string.IsNullOrEmpty(widget.Description);
			var description = hasDescription ? new GUIContent(widget.Description) : null;
			var descriptionHeight = hasDescription ? styles.SecondaryLabel.CalcHeight(description, width - 12f) : 0f;
			var height = RowHeight + descriptionHeight;
			var rect = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(true));
			if (Event.current.type == EventType.Repaint)
				RowRects[row.ControlName] = rect;
			if (focused && Event.current.type == EventType.Repaint)
				styles.SelectedRowButton.Draw(
					new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f),
					GUIContent.none,
					false,
					false,
					false,
					false
				);
			var rowRect = new Rect(rect.x, rect.y, rect.width, RowHeight);
			if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
				FocusRow(row);

			if (widget is DebugButtonWidget button)
			{
				using (new EnabledScope(enabled))
				{
					if (
						GUI.Button(
							rowRect,
							button.Label,
							focused ? styles.SelectedRowButton : styles.RowButton
						)
					)
					{
						FocusRow(row);
						DexcBootMenuState.Activate();
					}
				}
			}
			else
			{
				var labelWidth = Mathf.Floor(rect.width * LabelFraction);
				GUI.Label(
					new Rect(rowRect.x + 6f, rowRect.y + 4f, labelWidth - 6f, rowRect.height - 4f),
					DexcRuntimePanelForm.MarkChanged(widget, widget.Label),
					styles.FormLabel
				);
				var valueRect = new Rect(rowRect.x + labelWidth, rowRect.y, rowRect.width - labelWidth, rowRect.height);
				using (new EnabledScope(enabled))
					DrawValue(styles, row, valueRect, focused);
			}
			if (hasDescription)
				GUI.Label(new Rect(rect.x + 6f, rect.y + RowHeight, rect.width - 12f, descriptionHeight), description, styles.SecondaryLabel);
		}

		static void FocusRow(DexcBootMenuState.Row target)
		{
			var i = 0;
			foreach (var row in DexcBootMenuState.Rows)
			{
				if (!row.Focusable)
					continue;
				if (ReferenceEquals(row, target))
				{
					DexcBootMenuState.Focus = i;
					DexcBootMenuState.CancelAutoStart();
					return;
				}
				i++;
			}
		}

		static void DrawValue(DexcRuntimeIMGUIStyles styles, DexcBootMenuState.Row row, Rect rect, bool focused)
		{
			var arrow = RowHeight;
			var left = new Rect(rect.x, rect.y, arrow, rect.height);
			var middle = new Rect(rect.x + arrow + 2f, rect.y, Mathf.Max(0f, rect.width - arrow * 2f - 4f), rect.height);
			var right = new Rect(rect.xMax - arrow, rect.y, arrow, rect.height);
			switch (row.Widget)
			{
				case DebugToggleWidget toggle:
					// A switch at the right end; pressing anywhere on the value turns it over.
					if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
					{
						FocusRow(row);
						toggle.Toggle();
					}
					DexcRuntimeUIRenderer.DrawSwitch(rect, toggle.Value, GUI.enabled);
					break;
				case DebugChoiceWidget choice:
					if (GUI.Button(left, "‹", styles.SecondaryButton))
					{
						FocusRow(row);
						choice.Move(-1);
					}
					if (GUI.Button(right, "›", styles.SecondaryButton))
					{
						FocusRow(row);
						choice.Move(1);
					}
					if (GUI.Button(middle, choice.Selected, styles.CycleValue))
					{
						FocusRow(row);
						choice.Move(1);
					}
					break;
				case DebugNumberWidget number:
					if (GUI.Button(left, "‹", styles.SecondaryButton))
					{
						FocusRow(row);
						number.Move(-1);
					}
					if (GUI.Button(right, "›", styles.SecondaryButton))
					{
						FocusRow(row);
						number.Move(1);
					}
					DrawTextField(styles, row, middle, number.Text);
					break;
				case DebugTextWidget text:
					DrawTextField(styles, row, new Rect(rect.x, rect.y, rect.width, rect.height), text.Value);
					break;
			}
		}

		static void DrawTextField(DexcRuntimeIMGUIStyles styles, DexcBootMenuState.Row row, Rect rect, string current)
		{
			var editing = DexcBootMenuState.EditingControl == row.ControlName;
			GUI.SetNextControlName(row.ControlName);
			var shown = editing ? DexcBootMenuState.EditText ?? string.Empty : current;
			var changed = GUI.TextField(rect, shown, styles.TextField);
			if (!editing && GUI.GetNameOfFocusedControl() == row.ControlName)
			{
				FocusRow(row);
				BeginEdit(row.ControlName);
				if (changed != shown)
					DexcBootMenuState.EditText = changed;
				return;
			}
			if (editing && changed != shown)
				DexcBootMenuState.EditText = changed;
		}

		static void EnsureFocusVisible(float viewHeight)
		{
			var row = DexcBootMenuState.FocusedRow;
			if (row == null)
			{
				if (DexcBootMenuState.FocusedFooter != 0)
					DexcBootMenuState.Data.Scroll.y = float.MaxValue;
				return;
			}
			if (!RowRects.TryGetValue(row.ControlName, out var rect))
				return;
			var scroll = DexcBootMenuState.Scroll;
			if (rect.y < scroll.y)
				scroll.y = rect.y - 4f;
			else if (rect.yMax > scroll.y + viewHeight)
				scroll.y = rect.yMax - viewHeight + 4f;
			DexcBootMenuState.Scroll = scroll;
		}

		readonly struct EnabledScope : IDisposable
		{
			readonly bool m_Previous;

			internal EnabledScope(bool enabled)
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
#endif
}
