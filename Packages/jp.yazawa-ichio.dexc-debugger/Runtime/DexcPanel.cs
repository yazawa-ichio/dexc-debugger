using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Dexc
{
	/// <summary>
	/// Renderer-neutral widget shown by the Runtime menu "Panels" page and the Boot menu.
	/// Widgets read and write through the delegates they were built with; nothing is cached.
	/// </summary>
	public abstract class DebugWidget
	{
		protected DebugWidget(string label)
		{
			Label = label ?? string.Empty;
		}

		public string Label { get; set; }
		public string Description { get; set; }

		/// <summary>Optional storage key. When set the value is saved through <see cref="DebugStorage"/> and restored on registration.</summary>
		public string Key { get; set; }
		public Func<bool> EnabledProvider { get; set; }
		public bool Enabled => EnabledProvider == null || EnabledProvider();

		/// <summary>True for widgets a user can focus and operate (everything except labels).</summary>
		public virtual bool IsInteractive => true;

		/// <summary>Whether the value differs from the one it starts with, for widgets that know it, such as boot items.</summary>
		internal Func<bool> IsChanged;

		internal virtual string SerializeValue()
		{
			return null;
		}

		internal virtual void DeserializeValue(string text) { }

		protected void Persist()
		{
			if (string.IsNullOrEmpty(Key))
				return;
			var text = SerializeValue();
			if (text != null)
				DebugStorage.Save(Key, text);
		}

		internal void Restore()
		{
			if (string.IsNullOrEmpty(Key) || !DebugStorage.TryLoad(Key, out var text))
				return;
			try
			{
				DeserializeValue(text);
			}
			catch (Exception exception)
			{
				UnityEngine.Debug.LogWarning("Dexc widget restore failed for '" + Key + "': " + exception.Message);
			}
		}
	}

	public sealed class DebugToggleWidget : DebugWidget
	{
		readonly Func<bool> m_Get;
		readonly Action<bool> m_Set;

		public DebugToggleWidget(string label, Func<bool> get, Action<bool> set)
			: base(label)
		{
			m_Get = get ?? throw new ArgumentNullException(nameof(get));
			m_Set = set ?? throw new ArgumentNullException(nameof(set));
		}

		public bool Value
		{
			get { return m_Get(); }
			set
			{
				m_Set(value);
				Persist();
			}
		}

		public void Toggle()
		{
			Value = !Value;
		}

		internal override string SerializeValue()
		{
			return Value ? "1" : "0";
		}

		internal override void DeserializeValue(string text)
		{
			m_Set(text == "1" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase));
		}
	}

	public sealed class DebugChoiceWidget : DebugWidget
	{
		readonly Func<int> m_Get;
		readonly Action<int> m_Set;

		public DebugChoiceWidget(string label, IEnumerable<string> options, Func<int> get, Action<int> set)
			: base(label)
		{
			Options = new ReadOnlyCollection<string>((options ?? throw new ArgumentNullException(nameof(options))).ToList());
			if (Options.Count == 0)
				throw new ArgumentException("Choice requires at least one option.", nameof(options));
			m_Get = get ?? throw new ArgumentNullException(nameof(get));
			m_Set = set ?? throw new ArgumentNullException(nameof(set));
		}

		public IReadOnlyList<string> Options { get; }

		public int Index
		{
			get { return Mathf.Clamp(m_Get(), 0, Options.Count - 1); }
			set
			{
				m_Set(Mathf.Clamp(value, 0, Options.Count - 1));
				Persist();
			}
		}

		public string Selected => Options[Index];

		public void Move(int delta)
		{
			Index = (Index + delta % Options.Count + Options.Count) % Options.Count;
		}

		internal override string SerializeValue()
		{
			return Selected;
		}

		internal override void DeserializeValue(string text)
		{
			var index = ((IList<string>)Options).IndexOf(text);
			if (index >= 0)
				m_Set(index);
		}
	}

	public sealed class DebugNumberWidget : DebugWidget
	{
		readonly Func<double> m_Get;
		readonly Action<double> m_Set;

		public DebugNumberWidget(
			string label,
			Func<double> get,
			Action<double> set,
			double minimum = double.NegativeInfinity,
			double maximum = double.PositiveInfinity,
			double step = 0d,
			bool integer = false
		)
			: base(label)
		{
			m_Get = get ?? throw new ArgumentNullException(nameof(get));
			m_Set = set ?? throw new ArgumentNullException(nameof(set));
			Minimum = minimum;
			Maximum = maximum;
			Integer = integer;
			Step =
				step > 0d ? step
				: integer ? 1d
				: double.IsInfinity(maximum) || double.IsInfinity(minimum) ? 1d
				: Math.Max((maximum - minimum) / 20d, 0.01d);
		}

		public double Minimum { get; }
		public double Maximum { get; }
		public double Step { get; set; }
		public bool Integer { get; }

		public double Value
		{
			get { return m_Get(); }
			set
			{
				// NaN passes through the clamp, and neither it nor an infinity is a number the field can hold or show.
				if (double.IsNaN(value) || double.IsInfinity(value))
					return;
				var clamped = Math.Min(Maximum, Math.Max(Minimum, value));
				if (Integer)
					clamped = Math.Round(clamped);
				m_Set(clamped);
				Persist();
			}
		}

		public void Move(int delta)
		{
			Value = Value + delta * Step;
		}

		public string Text =>
			Integer ? ((long)Value).ToString(CultureInfo.InvariantCulture) : Value.ToString("0.###", CultureInfo.InvariantCulture);

		public bool TrySetText(string text)
		{
			if (
				!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
				|| double.IsNaN(number)
				|| double.IsInfinity(number)
			)
				return false;
			Value = number;
			return true;
		}

		internal override string SerializeValue()
		{
			return Value.ToString("R", CultureInfo.InvariantCulture);
		}

		internal override void DeserializeValue(string text)
		{
			if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
				Value = number;
		}
	}

	public sealed class DebugTextWidget : DebugWidget
	{
		readonly Func<string> m_Get;
		readonly Action<string> m_Set;

		public DebugTextWidget(string label, Func<string> get, Action<string> set)
			: base(label)
		{
			m_Get = get ?? throw new ArgumentNullException(nameof(get));
			m_Set = set ?? throw new ArgumentNullException(nameof(set));
		}

		public string Value
		{
			get { return m_Get() ?? string.Empty; }
			set
			{
				m_Set(value ?? string.Empty);
				Persist();
			}
		}

		internal override string SerializeValue()
		{
			return Value;
		}

		internal override void DeserializeValue(string text)
		{
			m_Set(text ?? string.Empty);
		}
	}

	public sealed class DebugButtonWidget : DebugWidget
	{
		readonly Action m_Action;

		public DebugButtonWidget(string label, Action action)
			: base(label)
		{
			m_Action = action;
		}

		/// <summary>Boot menu only: activating the button also starts the game.</summary>
		public void Invoke()
		{
			m_Action?.Invoke();
		}
	}

	public sealed class DebugLabelWidget : DebugWidget
	{
		readonly Func<string> m_Text;

		public DebugLabelWidget(Func<string> text)
			: base(string.Empty)
		{
			m_Text = text ?? throw new ArgumentNullException(nameof(text));
		}

		public DebugLabelWidget(string text)
			: this(() => text) { }

		public string Text => m_Text() ?? string.Empty;
		public override bool IsInteractive => false;
	}

	/// <summary>A titled list of widgets. Register with <see cref="DebugPanels.Register"/> to show it on the Runtime menu "Panels" page.</summary>
	public sealed class DebugPanel
	{
		readonly List<DebugWidget> m_Widgets = new List<DebugWidget>();

		public DebugPanel(string title)
		{
			Title = title ?? string.Empty;
		}

		public string Title { get; }
		public int Order { get; set; }
		public IReadOnlyList<DebugWidget> Widgets => m_Widgets;

		public DebugPanel Add(DebugWidget widget)
		{
			if (widget == null)
				throw new ArgumentNullException(nameof(widget));
			m_Widgets.Add(widget);
			return this;
		}

		public DebugPanel Toggle(string label, Func<bool> get, Action<bool> set, string key = null)
		{
			return Add(new DebugToggleWidget(label, get, set) { Key = key });
		}

		public DebugPanel Choice(string label, IEnumerable<string> options, Func<int> get, Action<int> set, string key = null)
		{
			return Add(new DebugChoiceWidget(label, options, get, set) { Key = key });
		}

		public DebugPanel Number(
			string label,
			Func<double> get,
			Action<double> set,
			double minimum = double.NegativeInfinity,
			double maximum = double.PositiveInfinity,
			double step = 0d,
			bool integer = false,
			string key = null
		)
		{
			return Add(new DebugNumberWidget(label, get, set, minimum, maximum, step, integer) { Key = key });
		}

		public DebugPanel Text(string label, Func<string> get, Action<string> set, string key = null)
		{
			return Add(new DebugTextWidget(label, get, set) { Key = key });
		}

		public DebugPanel Button(string label, Action action)
		{
			return Add(new DebugButtonWidget(label, action));
		}

		public DebugPanel Label(string text)
		{
			return Add(new DebugLabelWidget(text));
		}

		public DebugPanel Label(Func<string> text)
		{
			return Add(new DebugLabelWidget(text));
		}

		internal void RestoreAll()
		{
			foreach (var widget in m_Widgets)
				widget.Restore();
		}
	}

	/// <summary>
	/// Project-defined panels shown on the Runtime menu "Panels" page. Boot declares its own panels and keeps them in its
	/// own place, so nothing from the boot screen appears here.
	/// Does not require <see cref="DexcDebugger.Initialize()"/> to register.
	/// </summary>
	public static class DebugPanels
	{
		static List<DebugPanel> Registered => DexcRoot.Current.RegisteredPanels;

		/// <summary>Registers a panel. Widget values with a storage key are restored immediately.</summary>
		public static void Register(DebugPanel panel)
		{
			if (panel == null)
				throw new ArgumentNullException(nameof(panel));
			if (Registered.Contains(panel))
				return;
			panel.RestoreAll();
			Registered.Add(panel);
		}

		public static void Unregister(DebugPanel panel)
		{
			Registered.Remove(panel);
		}

		/// <summary>
		/// The panel the menu opens on when it shows its pages side by side, in place of the last section opened. It shows
		/// only while it is registered; null, the default, leaves the menu on the last section.
		/// </summary>
		public static DebugPanel Home
		{
			get => DexcRoot.Current.HomePanel;
			set => DexcRoot.Current.HomePanel = value;
		}

		/// <summary>Registered panels, sorted by Order then Title.</summary>
		public static IReadOnlyList<DebugPanel> All
		{
			get
			{
#if DEXC_DEBUGGER
				return Registered.OrderBy(panel => panel.Order).ThenBy(panel => panel.Title, StringComparer.Ordinal).ToList();
#else
				return Array.Empty<DebugPanel>();
#endif
			}
		}

#if UNITY_INCLUDE_TESTS
		internal static void ResetForTests()
		{
			Registered.Clear();
			Home = null;
		}
#endif
	}
}
