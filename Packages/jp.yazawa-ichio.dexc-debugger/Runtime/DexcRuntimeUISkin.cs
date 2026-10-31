using System;
using UnityEngine;

namespace Dexc
{
	/// <summary>Defines the responsive panel alignment for a Runtime UI profile.</summary>
	/// <summary>
	/// Where the panel sits in the safe area. The values keep their order because skins serialize them by index; new
	/// placements are appended.
	/// </summary>
	public enum DexcRuntimeUIAlignment
	{
		/// <summary>Left and bottom.</summary>
		Bottom,
		RightTop,
		RightCenter,
		LeftTop,
		LeftCenter,
		RightBottom,
	}

	/// <summary>
	/// How the Runtime menu uses a wide screen. The values keep their order because skins serialize them by index; a skin
	/// saved before this setting existed reads as <see cref="Single"/>.
	/// </summary>
	public enum DexcRuntimeUILayoutMode
	{
		/// <summary>One page at a time in the profile's panel.</summary>
		Single,

		/// <summary>Side by side whenever the safe area is wide enough for <see cref="DexcRuntimeUISkin.LayoutProfile.SplitPanelSize"/>.</summary>
		Auto,

		/// <summary>Starts side by side where it fits; the header switches back to one page.</summary>
		Split,
	}

	/// <summary>
	/// Stores renderer-neutral Runtime UI layout, palette, and Unity asset references.
	/// Values are validated and copied when <see cref="DexcDebugger.Initialize()"/> starts a session.
	/// </summary>
	public sealed class DexcRuntimeUISkin : ScriptableObject, ISerializationCallbackReceiver
	{
		const int CurrentSerializationVersion = 1;

		[Serializable]
		public sealed class LayoutProfile
		{
			[SerializeField]
			Vector2 m_PanelSize;

			[SerializeField]
			float m_SafeAreaMargin;

			[SerializeField]
			DexcRuntimeUIAlignment m_Alignment;

			[SerializeField]
			float m_RowHeight;

			[SerializeField]
			float m_MinimumHitHeight;

			[SerializeField]
			DexcRuntimeUILayoutMode m_Layout;

			[SerializeField]
			Vector2 m_SplitPanelSize;

			internal LayoutProfile(
				Vector2 panelSize,
				float safeAreaMargin,
				DexcRuntimeUIAlignment alignment,
				float rowHeight,
				float minimumHitHeight
			)
			{
				m_PanelSize = panelSize;
				m_SafeAreaMargin = safeAreaMargin;
				m_Alignment = alignment;
				m_RowHeight = rowHeight;
				m_MinimumHitHeight = minimumHitHeight;
			}

			/// <summary>Gets the target panel size before safe-area shrinking.</summary>
			public Vector2 PanelSize => m_PanelSize;

			/// <summary>Gets the margin applied inside the current safe area.</summary>
			public float SafeAreaMargin => m_SafeAreaMargin;

			/// <summary>Gets the panel alignment.</summary>
			public DexcRuntimeUIAlignment Alignment => m_Alignment;

			/// <summary>Gets the visual row height.</summary>
			public float RowHeight => m_RowHeight;

			/// <summary>Gets the minimum selectable hit height.</summary>
			public float MinimumHitHeight => m_MinimumHitHeight;

			/// <summary>Gets whether the menu shows its sections, a list and a page side by side on a wide screen.</summary>
			public DexcRuntimeUILayoutMode Layout => m_Layout;

			/// <summary>Gets the panel size side by side; zero means the default, 960 by 560.</summary>
			public Vector2 SplitPanelSize => m_SplitPanelSize;
		}

		[SerializeField]
		LayoutProfile m_AndroidPortrait = new LayoutProfile(new Vector2(320f, 520f), 12f, DexcRuntimeUIAlignment.Bottom, 28f, 28f);

		[SerializeField]
		LayoutProfile m_AndroidLandscape = new LayoutProfile(new Vector2(480f, 366f), 12f, DexcRuntimeUIAlignment.LeftTop, 28f, 28f);

		[SerializeField]
		LayoutProfile m_Windows = new LayoutProfile(new Vector2(360f, 520f), 12f, DexcRuntimeUIAlignment.LeftCenter, 28f, 28f);

		[SerializeField]
		Color m_PanelColor = new Color(0.04f, 0.06f, 0.10f, 0.98f);

		[SerializeField]
		Color m_HeaderColor = new Color(0.10f, 0.13f, 0.19f, 1f);

		[SerializeField]
		Color m_RowColor = new Color(0.13f, 0.17f, 0.24f, 1f);

		[SerializeField]
		Color m_AccentColor = new Color(0.15f, 0.39f, 0.92f, 1f);

		[SerializeField]
		Color m_ErrorColor = new Color(0.48f, 0.10f, 0.12f, 1f);

		[SerializeField]
		Color m_PrimaryTextColor = new Color(0.97f, 0.98f, 0.99f, 1f);

		[SerializeField]
		Color m_SecondaryTextColor = new Color(0.69f, 0.74f, 0.82f, 1f);

		[SerializeField]
		Color m_SelectionColor = new Color(0.12f, 0.25f, 0.43f, 1f);

		[SerializeField]
		Color m_FocusColor = new Color(0.38f, 0.65f, 0.98f, 1f);

		[SerializeField]
		Color m_SuccessColor = new Color(0.29f, 0.87f, 0.50f, 1f);

		[SerializeField]
		Color m_WarningColor = new Color(0.98f, 0.75f, 0.14f, 1f);

		[SerializeField]
		Color m_DisabledColor = new Color(0.16f, 0.19f, 0.24f, 1f);

		[SerializeField, Range(0f, 1f)]
		float m_Opacity = 1f;

		[SerializeField]
		Font m_Font;

		[SerializeField]
		Sprite m_PanelSprite;

		[SerializeField]
		Texture2D m_Texture;

		[SerializeField]
		Material m_Material;

		[SerializeField]
		Shader m_WorldVisualShader;

		[SerializeField]
		UnityEngine.Object m_UIToolkitPanelSettingsTemplate;

		[SerializeField, HideInInspector]
		int m_SerializationVersion;

		/// <summary>Gets the Android portrait layout profile.</summary>
		public LayoutProfile AndroidPortrait => m_AndroidPortrait;

		/// <summary>Gets the Android landscape layout profile.</summary>
		public LayoutProfile AndroidLandscape => m_AndroidLandscape;

		/// <summary>Gets the Windows Player layout profile.</summary>
		public LayoutProfile Windows => m_Windows;

		/// <summary>Gets the panel color.</summary>
		public Color PanelColor => m_PanelColor;

		/// <summary>Gets the header and footer color.</summary>
		public Color HeaderColor => m_HeaderColor;

		/// <summary>Gets the normal row color.</summary>
		public Color RowColor => m_RowColor;

		/// <summary>Gets the primary action color.</summary>
		public Color AccentColor => m_AccentColor;

		/// <summary>Gets the validation error color.</summary>
		public Color ErrorColor => m_ErrorColor;

		/// <summary>Gets the primary text color.</summary>
		public Color PrimaryTextColor => m_PrimaryTextColor;

		/// <summary>Gets the secondary text and help color.</summary>
		public Color SecondaryTextColor => m_SecondaryTextColor;

		/// <summary>Gets the current selection background color.</summary>
		public Color SelectionColor => m_SelectionColor;

		/// <summary>Gets the keyboard and gamepad focus color.</summary>
		public Color FocusColor => m_FocusColor;

		/// <summary>Gets the successful operation color.</summary>
		public Color SuccessColor => m_SuccessColor;

		/// <summary>Gets the warning color.</summary>
		public Color WarningColor => m_WarningColor;

		/// <summary>Gets the disabled control color.</summary>
		public Color DisabledColor => m_DisabledColor;

		/// <summary>Gets the global visual opacity multiplier.</summary>
		public float Opacity => m_Opacity;

		/// <summary>Gets the optional legacy Unity font used by renderer implementations.</summary>
		public Font Font => m_Font;

		/// <summary>Gets the optional panel sprite.</summary>
		public Sprite PanelSprite => m_PanelSprite;

		/// <summary>Gets the optional renderer-neutral texture reference.</summary>
		public Texture2D Texture => m_Texture;

		/// <summary>Gets the optional renderer-neutral material reference.</summary>
		public Material Material => m_Material;

		/// <summary>Gets the required Player World Visual shader.</summary>
		public Shader WorldVisualShader => m_WorldVisualShader;

		internal void SetWorldVisualShaderForEditor(Shader shader)
		{
			m_WorldVisualShader = shader;
		}

		internal void SetUIToolkitPanelSettingsTemplateForEditor(UnityEngine.Object panelSettingsTemplate)
		{
			m_UIToolkitPanelSettingsTemplate = panelSettingsTemplate;
		}

		internal UnityEngine.Object SnapshotUIToolkitPanelSettingsTemplate()
		{
			return m_UIToolkitPanelSettingsTemplate;
		}

		void ISerializationCallbackReceiver.OnBeforeSerialize()
		{
			m_SerializationVersion = CurrentSerializationVersion;
		}

		void ISerializationCallbackReceiver.OnAfterDeserialize()
		{
			MigrateSerializedPalette();
		}

		internal void MigrateSerializedPalette()
		{
			if (m_SerializationVersion >= CurrentSerializationVersion)
				return;
			m_PrimaryTextColor = new Color(0.97f, 0.98f, 0.99f, 1f);
			m_SecondaryTextColor = new Color(0.69f, 0.74f, 0.82f, 1f);
			m_SelectionColor = new Color(0.12f, 0.25f, 0.43f, 1f);
			m_FocusColor = new Color(0.38f, 0.65f, 0.98f, 1f);
			m_SuccessColor = new Color(0.29f, 0.87f, 0.50f, 1f);
			m_WarningColor = new Color(0.98f, 0.75f, 0.14f, 1f);
			m_DisabledColor = new Color(0.16f, 0.19f, 0.24f, 1f);
			m_SerializationVersion = CurrentSerializationVersion;
		}
	}

	internal sealed class DexcRuntimeUISkinSnapshot
	{
		internal sealed class Profile
		{
			internal Profile(DexcRuntimeUISkin.LayoutProfile source, string name)
			{
				if (source == null)
				{
					throw new ArgumentException(name + " must be assigned.", "RuntimeUI.Skin");
				}

				ValidateFinitePositive(source.PanelSize.x, name + ".PanelSize.x");
				ValidateFinitePositive(source.PanelSize.y, name + ".PanelSize.y");
				ValidateFiniteNonNegative(source.SafeAreaMargin, name + ".SafeAreaMargin");
				ValidateFinitePositive(source.RowHeight, name + ".RowHeight");
				ValidateFinitePositive(source.MinimumHitHeight, name + ".MinimumHitHeight");
				ValidateFiniteNonNegative(source.SplitPanelSize.x, name + ".SplitPanelSize.x");
				ValidateFiniteNonNegative(source.SplitPanelSize.y, name + ".SplitPanelSize.y");
				if (!Enum.IsDefined(typeof(DexcRuntimeUILayoutMode), source.Layout))
				{
					throw new ArgumentException(name + ".Layout is not defined.", "RuntimeUI.Skin");
				}
				if (!Enum.IsDefined(typeof(DexcRuntimeUIAlignment), source.Alignment))
				{
					throw new ArgumentException(name + ".Alignment is not defined.", "RuntimeUI.Skin");
				}

				PanelSize = source.PanelSize;
				SafeAreaMargin = source.SafeAreaMargin;
				Alignment = source.Alignment;
				RowHeight = source.RowHeight;
				MinimumHitHeight = source.MinimumHitHeight;
				Layout = source.Layout;
				// A skin saved before the setting existed has zero here, which means the default.
				SplitPanelSize = source.SplitPanelSize.x > 0f && source.SplitPanelSize.y > 0f ? source.SplitPanelSize : DefaultSplitPanelSize;
			}

			internal static readonly Vector2 DefaultSplitPanelSize = new Vector2(960f, 560f);

			internal Vector2 PanelSize { get; }
			internal float SafeAreaMargin { get; }
			internal DexcRuntimeUIAlignment Alignment { get; }
			internal float RowHeight { get; }
			internal float MinimumHitHeight { get; }
			internal DexcRuntimeUILayoutMode Layout { get; }
			internal Vector2 SplitPanelSize { get; }
		}

		private DexcRuntimeUISkinSnapshot(DexcRuntimeUISkin skin)
		{
			AndroidPortrait = new Profile(skin.AndroidPortrait, "AndroidPortrait");
			AndroidLandscape = new Profile(skin.AndroidLandscape, "AndroidLandscape");
			Windows = new Profile(skin.Windows, "Windows");
			ValidateColor(skin.PanelColor, "PanelColor");
			ValidateColor(skin.HeaderColor, "HeaderColor");
			ValidateColor(skin.RowColor, "RowColor");
			ValidateColor(skin.AccentColor, "AccentColor");
			ValidateColor(skin.ErrorColor, "ErrorColor");
			ValidateColor(skin.PrimaryTextColor, "PrimaryTextColor");
			ValidateColor(skin.SecondaryTextColor, "SecondaryTextColor");
			ValidateColor(skin.SelectionColor, "SelectionColor");
			ValidateColor(skin.FocusColor, "FocusColor");
			ValidateColor(skin.SuccessColor, "SuccessColor");
			ValidateColor(skin.WarningColor, "WarningColor");
			ValidateColor(skin.DisabledColor, "DisabledColor");
			if (!Finite(skin.Opacity) || skin.Opacity < 0f || skin.Opacity > 1f)
			{
				throw new ArgumentException("RuntimeUI.Skin.Opacity must be finite and between zero and one.", "RuntimeUI.Skin");
			}
			if (skin.WorldVisualShader == null)
			{
				throw new ArgumentException("RuntimeUI.Skin.WorldVisualShader must be assigned.", "RuntimeUI.Skin");
			}

			PanelColor = WithOpacity(skin.PanelColor, skin.Opacity);
			HeaderColor = WithOpacity(skin.HeaderColor, skin.Opacity);
			RowColor = WithOpacity(skin.RowColor, skin.Opacity);
			AccentColor = WithOpacity(skin.AccentColor, skin.Opacity);
			ErrorColor = WithOpacity(skin.ErrorColor, skin.Opacity);
			PrimaryTextColor = WithOpacity(skin.PrimaryTextColor, skin.Opacity);
			SecondaryTextColor = WithOpacity(skin.SecondaryTextColor, skin.Opacity);
			SelectionColor = WithOpacity(skin.SelectionColor, skin.Opacity);
			FocusColor = WithOpacity(skin.FocusColor, skin.Opacity);
			SuccessColor = WithOpacity(skin.SuccessColor, skin.Opacity);
			WarningColor = WithOpacity(skin.WarningColor, skin.Opacity);
			DisabledColor = WithOpacity(skin.DisabledColor, skin.Opacity);
			Font = skin.Font;
			PanelSprite = skin.PanelSprite;
			Texture = skin.Texture;
			Material = skin.Material;
			WorldVisualShader = skin.WorldVisualShader;
			UIToolkitPanelSettingsTemplate = skin.SnapshotUIToolkitPanelSettingsTemplate();
		}

		internal Profile AndroidPortrait { get; }
		internal Profile AndroidLandscape { get; }
		internal Profile Windows { get; }
		internal Color PanelColor { get; }
		internal Color HeaderColor { get; }
		internal Color RowColor { get; }
		internal Color AccentColor { get; }
		internal Color ErrorColor { get; }
		internal Color PrimaryTextColor { get; }
		internal Color SecondaryTextColor { get; }
		internal Color SelectionColor { get; }
		internal Color FocusColor { get; }
		internal Color SuccessColor { get; }
		internal Color WarningColor { get; }
		internal Color DisabledColor { get; }
		internal Font Font { get; }
		internal Sprite PanelSprite { get; }
		internal Texture2D Texture { get; }
		internal Material Material { get; }
		internal Shader WorldVisualShader { get; }
		internal UnityEngine.Object UIToolkitPanelSettingsTemplate { get; }

		internal static DexcRuntimeUISkinSnapshot Create(DexcRuntimeUISkin skin)
		{
			return skin == null ? null : new DexcRuntimeUISkinSnapshot(skin);
		}

		private static Color WithOpacity(Color color, float opacity)
		{
			color.a *= opacity;
			return color;
		}

		private static void ValidateColor(Color color, string name)
		{
			if (!Finite(color.r) || !Finite(color.g) || !Finite(color.b) || !Finite(color.a))
			{
				throw new ArgumentException("RuntimeUI.Skin." + name + " must contain finite values.", "RuntimeUI.Skin");
			}
		}

		private static void ValidateFinitePositive(float value, string name)
		{
			if (!Finite(value) || value <= 0f)
			{
				throw new ArgumentException("RuntimeUI.Skin." + name + " must be finite and greater than zero.", "RuntimeUI.Skin");
			}
		}

		private static void ValidateFiniteNonNegative(float value, string name)
		{
			if (!Finite(value) || value < 0f)
			{
				throw new ArgumentException("RuntimeUI.Skin." + name + " must be finite and zero or greater.", "RuntimeUI.Skin");
			}
		}

		private static bool Finite(float value)
		{
			return !float.IsNaN(value) && !float.IsInfinity(value);
		}
	}

	internal readonly struct DexcRuntimeUISemanticPalette
	{
		internal DexcRuntimeUISemanticPalette(
			Color panel,
			Color header,
			Color row,
			Color accent,
			Color error,
			Color primaryText,
			Color secondaryText,
			Color selection,
			Color focus,
			Color success,
			Color warning,
			Color disabled
		)
		{
			Panel = panel;
			Header = header;
			Row = row;
			Accent = accent;
			Error = error;
			PrimaryText = primaryText;
			SecondaryText = secondaryText;
			Selection = selection;
			Focus = focus;
			Success = success;
			Warning = warning;
			Disabled = disabled;
		}

		internal Color Panel { get; }
		internal Color Header { get; }
		internal Color Row { get; }
		internal Color Accent { get; }
		internal Color Error { get; }
		internal Color PrimaryText { get; }
		internal Color SecondaryText { get; }
		internal Color Selection { get; }
		internal Color Focus { get; }
		internal Color Success { get; }
		internal Color Warning { get; }
		internal Color Disabled { get; }

		/// <summary>The message under a field that is not valid, and the outline of that field: the error colour lifted to read on the band.</summary>
		internal Color FieldErrorText => Color.Lerp(Error, PrimaryText, 0.55f);

		internal static DexcRuntimeUISemanticPalette FromSnapshot(DexcRuntimeUISkinSnapshot skin)
		{
			return skin == null
				? new DexcRuntimeUISemanticPalette(
					new Color(0.04f, 0.06f, 0.10f, 0.98f),
					new Color(0.10f, 0.13f, 0.19f, 1f),
					new Color(0.13f, 0.17f, 0.24f, 1f),
					new Color(0.15f, 0.39f, 0.92f, 1f),
					new Color(0.48f, 0.10f, 0.12f, 1f),
					new Color(0.97f, 0.98f, 0.99f, 1f),
					new Color(0.69f, 0.74f, 0.82f, 1f),
					new Color(0.12f, 0.25f, 0.43f, 1f),
					new Color(0.38f, 0.65f, 0.98f, 1f),
					new Color(0.29f, 0.87f, 0.50f, 1f),
					new Color(0.98f, 0.75f, 0.14f, 1f),
					new Color(0.16f, 0.19f, 0.24f, 1f)
				)
				: new DexcRuntimeUISemanticPalette(
					skin.PanelColor,
					skin.HeaderColor,
					skin.RowColor,
					skin.AccentColor,
					skin.ErrorColor,
					skin.PrimaryTextColor,
					skin.SecondaryTextColor,
					skin.SelectionColor,
					skin.FocusColor,
					skin.SuccessColor,
					skin.WarningColor,
					skin.DisabledColor
				);
		}
	}
}
