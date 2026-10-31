using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Dexc;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	[InitializeOnLoad]
	public static class DexcStealthStageSamplePlayDriver
	{
		const int CaptureWidth = 1280;
		const int CaptureHeight = 720;
		const string ActiveKey = "DexcStealthStageSamplePlayDriver.Active";
		const string ErrorKey = "DexcStealthStageSamplePlayDriver.Error";
		const string ManifestKey = "DexcStealthStageSamplePlayDriver.Manifest";
		const string PhaseKey = "DexcStealthStageSamplePlayDriver.Phase";
		const string TargetFrameKey = "DexcStealthStageSamplePlayDriver.TargetFrame";
		const string ScenePath = "Assets/Samples/StealthStageDemo/Scenes/StealthStageDemo.unity";
		const string ControllerTypeName =
			"DexcDebugger.Samples.StealthStageDemo.StealthStageDemoController, DexcDebugger.Samples.StealthStageDemo.Gameplay";
		const string SystemTypeName =
			"DexcDebugger.Samples.StealthStageDemo.StealthDebugSystem, DexcDebugger.Samples.StealthStageDemo.Debug";
		const string ConfigTypeName =
			"DexcDebugger.Samples.StealthStageDemo.StealthDemoConfig, DexcDebugger.Samples.StealthStageDemo.Gameplay";

		static bool s_Subscribed;

		static DexcStealthStageSamplePlayDriver()
		{
			if (SessionState.GetBool(ActiveKey, false))
				Subscribe();
		}

		public static void Run()
		{
			try
			{
				if (EditorApplication.isPlayingOrWillChangePlaymode)
					throw new InvalidOperationException("The sample PlayMode driver must start from Edit Mode.");
				var manifest = Environment.GetEnvironmentVariable("DEXC_STEALTH_SAMPLE_CAPTURE_MANIFEST");
				if (string.IsNullOrWhiteSpace(manifest))
					throw new InvalidOperationException("DEXC_STEALTH_SAMPLE_CAPTURE_MANIFEST is required.");

				SessionState.SetString(ManifestKey, Path.GetFullPath(manifest));
				SessionState.SetString(ErrorKey, string.Empty);
				SessionState.SetString(PhaseKey, "starting");
				SessionState.SetBool(ActiveKey, true);
				EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
				// Playing the scene directly leaves the debugger off; ask for it the way the boot screen does.
				Type.GetType(ConfigTypeName, true).GetField("DebuggerRequested").SetValue(null, true);
				Subscribe();
				Debug.Log("DEXC-SAMPLE01 PlayMode validation: entering Play Mode.");
				EditorApplication.isPlaying = true;
			}
			catch (Exception exception)
			{
				Fail(exception);
			}
		}

		static void Subscribe()
		{
			if (s_Subscribed)
				return;
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
			EditorApplication.update += OnUpdate;
			s_Subscribed = true;
		}

		static void Unsubscribe()
		{
			if (!s_Subscribed)
				return;
			EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
			EditorApplication.update -= OnUpdate;
			s_Subscribed = false;
		}

		static void OnPlayModeStateChanged(PlayModeStateChange state)
		{
			if (!SessionState.GetBool(ActiveKey, false))
				return;
			if (state == PlayModeStateChange.EnteredPlayMode)
			{
				BeginPhase("warmup", 30);
				Debug.Log("DEXC-SAMPLE01 PlayMode validation: runtime warm-up.");
				return;
			}

			if (state != PlayModeStateChange.EnteredEditMode)
				return;
			var phase = SessionState.GetString(PhaseKey, string.Empty);
			var error = SessionState.GetString(ErrorKey, string.Empty);
			SessionState.EraseBool(ActiveKey);
			SessionState.EraseString(PhaseKey);
			SessionState.EraseInt(TargetFrameKey);
			Unsubscribe();
			if (phase == "failed")
			{
				Debug.LogError("DEXC-SAMPLE01 PlayMode validation failed: " + error);
				EditorApplication.Exit(1);
				return;
			}

			Debug.Log("DEXC-SAMPLE01 PlayMode validation completed.");
			EditorApplication.Exit(0);
		}

		static void OnUpdate()
		{
			if (!SessionState.GetBool(ActiveKey, false))
				return;
			try
			{
				var phase = SessionState.GetString(PhaseKey, string.Empty);
				if (phase == "starting" && EditorApplication.isPlaying)
				{
					BeginPhase("warmup", 30);
					return;
				}
				if (!EditorApplication.isPlaying || Time.frameCount < SessionState.GetInt(TargetFrameKey, int.MaxValue))
					return;

				switch (phase)
				{
					case "warmup":
						RunSampleResourceAndWarpAssertions(GetRequiredComponent(ControllerTypeName));
						PrepareWarpCheck("Start");
						BeginPhase("sample-start-warp", 1);
						break;
					case "sample-start-warp":
						CheckWarpAfterFrame();
						PrepareWarpCheck("Exit");
						BeginPhase("sample-exit-warp", 1);
						break;
					case "sample-exit-warp":
						CheckWarpAfterFrame();
						RunInitialAssertions();
						BeginPhase("settings", 10);
						break;
					case "settings":
						RunSettingAndDetectionAssertions();
						BeginPhase("display", 10);
						break;
					case "display":
						RunInspectorAndDisplayAssertions();
						Capture("DEXC-SAMPLE01-StealthStage-Gameplay.png");
						DebugMenu.Open();
						DexcRuntimeUGUIRenderer.RenderProfileForTests(
							CaptureWidth,
							CaptureHeight,
							new Rect(0f, 0f, CaptureWidth, CaptureHeight)
						);
						DexcRuntimeUGUIRenderer.SetCanvasVisibleForCaptureTests(true);
						BeginPhase("menu", 20);
						break;
					case "menu":
						Ensure(DexcRuntimeUIState.IsOpen, "The Dexc runtime menu did not open.");
						Capture("DEXC-SAMPLE01-StealthStage-DebugMenu.png");
						RunRestartAndExitAssertions();
						BeginPhase("stage-cleared", 5);
						break;
					case "stage-cleared":
						RunNextStageAssertions();
						BeginPhase("all-complete", 5);
						break;
					case "all-complete":
						RunAllStagesCompleteAssertion();
						WriteManifest(true, string.Empty);
						SessionState.SetString(PhaseKey, "stopping");
						DebugMenu.Close();
						EditorApplication.isPlaying = false;
						break;
				}
			}
			catch (Exception exception)
			{
				Fail(exception);
			}
		}

		static void RunInitialAssertions()
		{
			var controller = GetRequiredComponent(ControllerTypeName);
			Ensure(((Behaviour)GetRequiredComponent(SystemTypeName)).enabled, "StealthDebugSystem did not start.");
			Ensure(DebuggerRuntime.IsInitialized, "DexcDebugger was not initialized.");
			Ensure(GetProperty<int>(controller, "CurrentStageNumber") == 1, "The sample did not start on stage 1.");

			var command = DebugCommand
				.Registry.GetList(new DebugListQuery("Samples/Stealth/Commands", "Load Stage", new DebugPageRequest(0, 20)))
				.Items.Single();
			Ensure(
				DebugCommand.Registry.TryGetDescriptor(command.Id, out var descriptor),
				"The Load Stage command descriptor is unavailable."
			);
			var create = DebugCommand.Draft.Create(command.Id);
			Ensure(create.TryGetDraftId(out var draft), "A Load Stage command draft could not be created.");
			var stageField = descriptor.Fields.Single(field => field.Name == "StageNumber");
			Ensure(
				DebugCommand.Draft.UpdateInput(draft, stageField.Id, DebugValue.From(99)).Status
					== DebugCommandDraftOperationStatus.Succeeded,
				"The invalid stage input could not be staged."
			);
			Ensure(DebugCommand.Draft.Validate(draft).TryGetValidation(out var invalid), "The invalid stage draft could not be validated.");
			Ensure(
				!invalid.CanExecute && invalid.Issues.Any(issue => issue.Code == "stage-out-of-range"),
				"The stage range validation was not reported."
			);
			Ensure(
				DebugCommand.Draft.UpdateInput(draft, stageField.Id, DebugValue.From(2)).Status
					== DebugCommandDraftOperationStatus.Succeeded,
				"The valid stage input could not be staged."
			);
			var creation = DebugCommand.Draft.CreateRequest(draft);
			Ensure(creation.TryGetRequest(out var request), "A Load Stage request could not be created.");
			Ensure(request.Dispatch().Status == CommandExecutionStatus.Succeeded, "The Load Stage command did not succeed.");
			Ensure(GetProperty<int>(controller, "CurrentStageNumber") == 2, "The Load Stage command did not transition to stage 2.");
			Ensure(
				DebugCommand.Draft.GetHistory(command.Id, new DebugPageRequest(0, 20)).Entries.Count >= 1,
				"The successful command was not recorded in history."
			);

			var setting = DebugSetting
				.GetList(new DebugListQuery("Samples/Stealth/Settings", "Player Speed", new DebugPageRequest(0, 20)))
				.Items.Single();
			Ensure(
				DebugSetting.TryGetDescriptor(setting.Id, out var settingDescriptor),
				"The Player Speed setting descriptor is unavailable."
			);
			Ensure(settingDescriptor.ApplyMode == DebugSettingApplyMode.Immediate, "Player Speed must demonstrate immediate apply.");
			Ensure(
				DebugSetting.Write(setting.Id, DebugValue.From(8f)).Status == DebugOperationStatus.Succeeded,
				"Player Speed could not be written."
			);
			var guardSetting = DebugSetting
				.GetList(new DebugListQuery("Samples/Stealth/Settings", "Guard Speed", new DebugPageRequest(0, 20)))
				.Items.Single();
			Ensure(
				DebugSetting.TryGetDescriptor(guardSetting.Id, out var guardDescriptor),
				"The Guard Speed setting descriptor is unavailable."
			);
			Ensure(guardDescriptor.ApplyMode == DebugSettingApplyMode.Submit, "Guard Speed must demonstrate staged submit.");
		}

		static void PrepareWarpCheck(string destination)
		{
			var controller = GetRequiredComponent(ControllerTypeName);
			var warpType = Type.GetType(
				"DexcDebugger.Samples.StealthStageDemo.StealthWarpDestination, DexcDebugger.Samples.StealthStageDemo.Gameplay",
				true
			);
			controller.GetType().GetMethod("WarpPlayer").Invoke(controller, new[] { Enum.Parse(warpType, destination), (object)2f });
		}

		static void CheckWarpAfterFrame()
		{
			var controller = GetRequiredComponent(ControllerTypeName);
			var player = GetProperty<Transform>(controller, "PlayerTransform");
			var character = player.GetComponent<CharacterController>();
			Physics.SyncTransforms();
			Ensure(character.bounds.min.y >= -0.01f, "The player's feet are below the floor on the frame after warp.");
			character.Move(Vector3.forward * 0.05f);
			Ensure(character.bounds.min.y >= -0.01f, "The next-frame move after warp puts the player's feet below the floor.");
		}

		static void RunSampleResourceAndWarpAssertions(Component controller)
		{
			var type = controller.GetType();
			var materials = (ICollection)type.GetField("m_Materials", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
			var originalCount = materials.Count;
			Ensure(originalCount > 0, "The sample did not create materials.");
			var originalStage = GetProperty<int>(controller, "CurrentStageNumber");
			var stageCount = GetProperty<int>(controller, "StageCount");
			var warpType = Type.GetType(
				"DexcDebugger.Samples.StealthStageDemo.StealthWarpDestination, DexcDebugger.Samples.StealthStageDemo.Gameplay",
				true
			);
			for (var repeat = 0; repeat < 3; repeat++)
			{
				for (var stage = 1; stage <= stageCount; stage++)
				{
					type.GetMethod("LoadStageNumber").Invoke(controller, new object[] { stage });
					Ensure(materials.Count == originalCount, "Stage rebuilds keep allocating same-color materials.");
					var player = GetProperty<Transform>(controller, "PlayerTransform");
					var startHeight = player.position.y;
					foreach (var destination in new[] { "Start", "Exit" })
					{
						type.GetMethod("WarpPlayer").Invoke(controller, new[] { Enum.Parse(warpType, destination), (object)0.25f });
						Physics.SyncTransforms();
						var character = player.GetComponent<CharacterController>();
						Ensure(
							Mathf.Abs(player.position.y - startHeight) < 0.001f && character.bounds.min.y >= -0.01f,
							"Warp places the player's feet below the floor: " + destination
						);
					}
				}
			}
			type.GetMethod("LoadStageNumber").Invoke(controller, new object[] { originalStage });
		}

		static void RunSettingAndDetectionAssertions()
		{
			var controller = GetRequiredComponent(ControllerTypeName);
			Ensure(
				Mathf.Abs(GetProperty<float>(controller, "PlayerMoveSpeed") - 8f) < 0.001f,
				"The immediate Player Speed setting was not applied to gameplay."
			);
			var guards = ((IEnumerable)GetProperty<object>(controller, "Guards")).Cast<Component>().ToArray();
			Ensure(guards.Length > 0, "The active stage has no guards.");
			var player = GetProperty<Transform>(controller, "PlayerTransform");
			var character = player.GetComponent<CharacterController>();
			character.enabled = false;
			player.position = guards[0].transform.position + guards[0].transform.forward * 2f;
			character.enabled = true;
			Physics.SyncTransforms();
			var detected = (bool)guards[0].GetType().GetMethod("EvaluateDetectionNow").Invoke(guards[0], null);
			Ensure(detected, "A guard did not detect the player inside its view cone.");
			controller.GetType().GetMethod("NotifyDetected").Invoke(controller, new object[] { guards[0] });
			Ensure(GetProperty<object>(controller, "State").ToString() == "Detected", "The controller did not enter the detected state.");
		}

		static void RunInspectorAndDisplayAssertions()
		{
			var controller = GetRequiredComponent(ControllerTypeName);
			var guardCount = ((IEnumerable)GetProperty<object>(controller, "Guards")).Cast<object>().Count();
			var registered = DebugInspector.GetRegistered(
				new DebugListQuery("Samples/Stealth/Inspector", string.Empty, new DebugPageRequest(0, 20))
			);
			Ensure(
				registered.Entries.Count == guardCount + 1,
				"The stage and guard inspector entries were not registered exactly once."
			);
			Ensure(DebugDisplay.TryGetCompletedFrame(out var frame), "No completed DebugDisplay frame is available.");
			Ensure(frame.Globals.Any(visual => visual.Path == "Samples/Stealth/HUD/Player"), "The player HUD DebugDisplay entry is missing.");
			Ensure(
				frame.Globals.Any(visual => visual.Path == "Samples/Stealth/World/Exit"),
				"The exit world DebugDisplay entry is missing."
			);
			Ensure(frame.Subjects.Count >= 2, "Player and guard DebugDisplay subjects are missing.");
		}

		static void RunRestartAndExitAssertions()
		{
			var controller = GetRequiredComponent(ControllerTypeName);
			Ensure(
				GetProperty<object>(controller, "State").ToString() == "Detected",
				"The detected state was not retained through visual capture."
			);
			DispatchCommand("Restart Stage");
			Ensure(GetProperty<int>(controller, "CurrentStageNumber") == 2, "Restart Stage changed the active stage.");
			Ensure(GetProperty<object>(controller, "State").ToString() == "Running", "Restart Stage did not restore the running state.");
			var warpType = Type.GetType(
				"DexcDebugger.Samples.StealthStageDemo.StealthWarpDestination, DexcDebugger.Samples.StealthStageDemo.Gameplay",
				true
			);
			var exit = Enum.Parse(warpType, "Exit");
			controller.GetType().GetMethod("WarpPlayer").Invoke(controller, new[] { exit, (object)0.25f });
		}

		static void RunNextStageAssertions()
		{
			var controller = GetRequiredComponent(ControllerTypeName);
			Ensure(GetProperty<object>(controller, "State").ToString() == "StageCleared", "Reaching the Exit did not clear stage 2.");
			DispatchCommand("Next Stage");
			Ensure(GetProperty<int>(controller, "CurrentStageNumber") == 3, "Next Stage did not load stage 3.");
			Ensure(GetProperty<object>(controller, "State").ToString() == "Running", "Stage 3 did not start in the running state.");
			var warpType = Type.GetType(
				"DexcDebugger.Samples.StealthStageDemo.StealthWarpDestination, DexcDebugger.Samples.StealthStageDemo.Gameplay",
				true
			);
			var exit = Enum.Parse(warpType, "Exit");
			controller.GetType().GetMethod("WarpPlayer").Invoke(controller, new[] { exit, (object)0.25f });
		}

		static void RunAllStagesCompleteAssertion()
		{
			var controller = GetRequiredComponent(ControllerTypeName);
			Ensure(
				GetProperty<object>(controller, "State").ToString() == "AllStagesComplete",
				"Reaching the final Exit did not complete all stages."
			);
		}

		static void DispatchCommand(string commandName)
		{
			var item = DebugCommand
				.Registry.GetList(new DebugListQuery("Samples/Stealth/Commands", commandName, new DebugPageRequest(0, 20)))
				.Items.Single();
			var create = DebugCommand.Draft.Create(item.Id);
			Ensure(create.TryGetDraftId(out var draft), commandName + " draft creation failed.");
			var creation = DebugCommand.Draft.CreateRequest(draft);
			Ensure(creation.TryGetRequest(out var request), commandName + " request creation failed.");
			Ensure(request.Dispatch().Status == CommandExecutionStatus.Succeeded, commandName + " did not succeed.");
		}

		static Component GetRequiredComponent(string typeName)
		{
			var type = Type.GetType(typeName, true);
			var objects = UnityEngine.Object.FindObjectsByType(type, FindObjectsInactive.Include, FindObjectsSortMode.None);
			var component = objects.OfType<Component>().FirstOrDefault();
			if (component == null)
				throw new InvalidOperationException("Required runtime component was not found: " + typeName);
			return component;
		}

		static T GetProperty<T>(Component component, string propertyName)
		{
			return (T)component.GetType().GetProperty(propertyName).GetValue(component);
		}

		static void BeginPhase(string phase, int frameDelay)
		{
			SessionState.SetString(PhaseKey, phase);
			SessionState.SetInt(TargetFrameKey, Time.frameCount + frameDelay);
		}

		static void Capture(string fileName)
		{
			var manifest = SessionState.GetString(ManifestKey, string.Empty);
			var outputDirectory = Path.GetDirectoryName(manifest);
			Directory.CreateDirectory(outputDirectory);
			var path = Path.Combine(outputDirectory, fileName);
			var camera = Camera.main;
			Ensure(camera != null, "The sample Main Camera was not found.");
			var target = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32);
			var texture = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGBA32, false);
			var previousActive = RenderTexture.active;
			var previousTarget = camera.targetTexture;
			var canvases = Resources
				.FindObjectsOfTypeAll<Canvas>()
				.Where(canvas => canvas != null && canvas.isActiveAndEnabled)
				.Select(canvas => new CanvasCaptureState(canvas))
				.ToArray();
			try
			{
				target.Create();
				camera.targetTexture = target;
				foreach (var state in canvases)
					state.Configure(camera);
				Canvas.ForceUpdateCanvases();
				var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
				RenderPipeline.SubmitRenderRequest(camera, request);
				RenderTexture.active = target;
				texture.ReadPixels(new Rect(0f, 0f, CaptureWidth, CaptureHeight), 0, 0);
				texture.Apply(false, false);
				File.WriteAllBytes(path, texture.EncodeToPNG());
			}
			finally
			{
				foreach (var state in canvases)
					state.Restore();
				camera.targetTexture = previousTarget;
				RenderTexture.active = previousActive;
				target.Release();
				UnityEngine.Object.DestroyImmediate(target);
				UnityEngine.Object.DestroyImmediate(texture);
			}
			Ensure(new FileInfo(path).Length > 1024, "The capture artifact is unexpectedly small: " + path);
		}

		readonly struct CanvasCaptureState
		{
			readonly Canvas m_Canvas;
			readonly RenderMode m_RenderMode;
			readonly Camera m_WorldCamera;
			readonly float m_PlaneDistance;

			internal CanvasCaptureState(Canvas canvas)
			{
				m_Canvas = canvas;
				m_RenderMode = canvas.renderMode;
				m_WorldCamera = canvas.worldCamera;
				m_PlaneDistance = canvas.planeDistance;
			}

			internal void Configure(Camera camera)
			{
				m_Canvas.renderMode = RenderMode.ScreenSpaceCamera;
				m_Canvas.worldCamera = camera;
				m_Canvas.planeDistance = Mathf.Max(camera.nearClipPlane + 0.1f, 1f);
			}

			internal void Restore()
			{
				if (m_Canvas == null)
					return;
				m_Canvas.renderMode = m_RenderMode;
				m_Canvas.worldCamera = m_WorldCamera;
				m_Canvas.planeDistance = m_PlaneDistance;
			}
		}

		static void WriteManifest(bool success, string error)
		{
			var manifest = SessionState.GetString(ManifestKey, string.Empty);
			if (string.IsNullOrWhiteSpace(manifest))
				return;
			var outputDirectory = Path.GetDirectoryName(manifest);
			Directory.CreateDirectory(outputDirectory);
			var gameplay = Path.Combine(outputDirectory, "DEXC-SAMPLE01-StealthStage-Gameplay.png");
			var menu = Path.Combine(outputDirectory, "DEXC-SAMPLE01-StealthStage-DebugMenu.png");
			var json =
				"{\n"
				+ "  \"task\": \"DEXC-SAMPLE01 Stealth Stage Demo\",\n"
				+ "  \"success\": "
				+ (success ? "true" : "false")
				+ ",\n"
				+ "  \"error\": \""
				+ Escape(error)
				+ "\",\n"
				+ "  \"checks\": [\"stage-transition\", \"retry\", \"stage-clear\", \"next-stage\", \"all-stages-complete\", \"guard-detection\", \"setting-apply\", \"command-validation-result-history\", \"inspector-registered\", \"display-hud-world-subjects\", \"bounded-material-reuse\", \"warp-floor-height-and-next-frame-move\"],\n"
				+ "  \"captures\": [\n"
				+ CaptureJson("GameplayWorldVisuals", gameplay)
				+ ",\n"
				+ CaptureJson("DexcRuntimeMenu", menu)
				+ "\n"
				+ "  ]\n"
				+ "}\n";
			File.WriteAllText(manifest, json);
		}

		static string CaptureJson(string scenario, string path)
		{
			var hash = File.Exists(path) ? Sha256(path) : string.Empty;
			return "    { \"scenario\": \"" + scenario + "\", \"path\": \"" + Escape(path) + "\", \"sha256\": \"" + hash + "\" }";
		}

		static void Fail(Exception exception)
		{
			var error = exception.ToString();
			SessionState.SetString(ErrorKey, error);
			SessionState.SetString(PhaseKey, "failed");
			SessionState.SetBool(ActiveKey, true);
			WriteManifest(false, error);
			Debug.LogException(exception);
			if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
			{
				Subscribe();
				EditorApplication.isPlaying = false;
				return;
			}

			SessionState.EraseBool(ActiveKey);
			Unsubscribe();
			EditorApplication.Exit(1);
		}

		static void Ensure(bool condition, string message)
		{
			if (!condition)
				throw new InvalidOperationException(message);
		}

		static string Sha256(string path)
		{
			using (var algorithm = SHA256.Create())
			using (var stream = File.OpenRead(path))
				return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
		}

		static string Escape(string value)
		{
			return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
		}
	}
}
