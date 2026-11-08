using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dexc.Telemetry.CodeGen;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine.UIElements;

namespace Dexc.Telemetry.Editor
{
	/// <summary>
	/// Project Settings, Dexc Telemetry: chooses the preset of the watch. The choice is the user's, in UserSettings, so
	/// that switching it changes no file of the project. The presets themselves are edited in the files.
	/// </summary>
	internal sealed class TelemetryProfilesSettings : SettingsProvider
	{
		const string None = "(none)";
		const string Template = "{\n  \"presets\": [\n    {\n      \"name\": \"Example\",\n      \"assemblies\": [\"Assembly-CSharp\"],\n      \"namespaces\": [],\n      \"attributes\": []\n    }\n  ]\n}\n";

		VisualElement m_Root;

		TelemetryProfilesSettings()
			: base("Project/Dexc Telemetry", SettingsScope.Project)
		{
			keywords = new HashSet<string> { "telemetry", "watch", "preset", "profile" };
		}

		[SettingsProvider]
		static SettingsProvider Create()
		{
			return new TelemetryProfilesSettings();
		}

		public override void OnActivate(string searchContext, VisualElement rootElement)
		{
			m_Root = rootElement;
			Build();
		}

		void Build()
		{
			m_Root.Clear();
			var page = new VisualElement();
			page.style.paddingLeft = 10;
			page.style.paddingRight = 10;
			page.style.paddingTop = 4;
			m_Root.Add(page);
			var title = new Label("Dexc Telemetry");
			title.style.fontSize = 19;
			title.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
			title.style.marginBottom = 8;
			page.Add(title);

			var folder = Directory.GetCurrentDirectory();
			TelemetryProfiles profiles;
			try
			{
				profiles = TelemetryProfiles.Load(folder);
			}
			catch (Exception exception) when (exception is FormatException || exception is IOException)
			{
				page.Add(new HelpBox("The profile files could not be read: " + exception.Message, HelpBoxMessageType.Error));
				AddFileButtons(page, folder);
				return;
			}

			var choices = new List<string> { None };
			choices.AddRange(profiles.Presets.Select(preset => preset.Name));
			var current = profiles.Active?.Name ?? None;
			var dropdown = new DropdownField("Preset of the watch", choices, current);
			dropdown.RegisterValueChangedCallback(change =>
			{
				SetUserActive(folder, change.newValue == None ? null : change.newValue);
				Build();
			});
			page.Add(dropdown);
			if (profiles.ActiveName != null && profiles.Active == null)
			{
				page.Add(new HelpBox("The active preset " + profiles.ActiveName + " does not exist.", HelpBoxMessageType.Warning));
			}

			var target = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));
			var symbols = PlayerSettings.GetScriptingDefineSymbols(target).Split(';');
			if (!symbols.Contains(TelemetryWeaver.WatchSymbol))
			{
				page.Add(new HelpBox(
					TelemetryWeaver.WatchSymbol + " is not among the scripting define symbols of " + target.TargetName + ", so no build has the measuring of the watch. An assembly may still define it of its own.",
					HelpBoxMessageType.Info
				));
			}
			page.Add(new HelpBox(
				"A change of the preset or of the files compiles the scripts again, without the build cache, when the Editor comes back to the front. A Player build made with other presets stops and asks for a clean build.",
				HelpBoxMessageType.None
			));
			AddFileButtons(page, folder);
		}

		static void AddFileButtons(VisualElement page, string folder)
		{
			var row = new VisualElement();
			row.style.flexDirection = FlexDirection.Row;
			row.style.marginTop = 6;
			row.Add(new Button(() => Open(Path.Combine(folder, TelemetryProfiles.ProjectFile))) { text = "Open the presets of the project" });
			row.Add(new Button(() => Open(Path.Combine(folder, TelemetryProfiles.UserFile))) { text = "Open your presets" });
			row.Add(new Button(TelemetryProfilesFreshness.Check) { text = "Apply the files" });
			page.Add(row);
		}

		/// <summary>Writes the choice into the file of the user and applies it.</summary>
		internal static void SetUserActive(string folder, string active)
		{
			var path = Path.Combine(folder, TelemetryProfiles.UserFile);
			var text = File.Exists(path) ? File.ReadAllText(path) : null;
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, TelemetryProfiles.WithActive(text, active));
			TelemetryProfilesFreshness.Check();
		}

		static void Open(string path)
		{
			if (!File.Exists(path))
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.WriteAllText(path, Template);
			}
			EditorUtility.OpenWithDefaultApp(path);
		}
	}
}
