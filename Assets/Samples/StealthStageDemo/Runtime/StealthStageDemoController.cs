using System;
using System.Collections.Generic;
using Dexc;
using UnityEngine;

namespace DexcDebugger.Samples.StealthStageDemo
{
	public enum StealthStageState
	{
		Running,
		Detected,
		StageCleared,
		AllStagesComplete,
	}

	public enum StealthWarpDestination
	{
		Start,
		Exit,
	}

	public sealed class StealthStageDemoController : MonoBehaviour
	{
		/// <summary>Radius of the green Exit pad. Standing anywhere on it clears the stage.</summary>
		public const float ExitRadius = 0.75f;

		/// <summary>Seconds the all-clear screen stays up before the run starts again from stage 1.</summary>
		const float AllClearPause = 3f;

		const string InspectorPath = "Samples/Stealth/Inspector/Stage";

		sealed class StageLayout
		{
			public string Objective;
			public Vector3 PlayerStart;
			public Vector3 Exit;
			public Vector3[] Obstacles;
			public Vector3[] ObstacleSizes;
			public Vector3[][] GuardRoutes;
		}

		static readonly StageLayout[] s_Stages =
		{
			new StageLayout
			{
				Objective = "Cross the training yard",
				PlayerStart = new Vector3(-7f, 1f, -5f),
				Exit = new Vector3(7f, 0.15f, 5f),
				Obstacles = new[] { new Vector3(0f, 1f, 0f), new Vector3(4.5f, 1f, -2f) },
				ObstacleSizes = new[] { new Vector3(1.5f, 2f, 7f), new Vector3(4f, 2f, 1.5f) },
				GuardRoutes = new[]
				{
					new[] { new Vector3(-3.5f, 1f, 4f), new Vector3(3f, 1f, 4f), new Vector3(3f, 1f, 1f), new Vector3(-3.5f, 1f, 1f) },
				},
			},
			new StageLayout
			{
				Objective = "Thread the storage lanes",
				PlayerStart = new Vector3(-7f, 1f, 5f),
				Exit = new Vector3(7f, 0.15f, -5f),
				Obstacles = new[] { new Vector3(-2.5f, 1f, 0.5f), new Vector3(2.5f, 1f, -0.5f), new Vector3(0f, 1f, 4.5f) },
				ObstacleSizes = new[] { new Vector3(2f, 2f, 7f), new Vector3(2f, 2f, 7f), new Vector3(4f, 2f, 1.5f) },
				GuardRoutes = new[]
				{
					new[] { new Vector3(-6f, 1f, -4f), new Vector3(-1f, 1f, -4f), new Vector3(-1f, 1f, -1f), new Vector3(-6f, 1f, -1f) },
					new[] { new Vector3(1f, 1f, 2.5f), new Vector3(6f, 1f, 2.5f), new Vector3(6f, 1f, 5f), new Vector3(1f, 1f, 5f) },
				},
			},
			new StageLayout
			{
				Objective = "Escape the command post",
				PlayerStart = new Vector3(-7f, 1f, 0f),
				Exit = new Vector3(7f, 0.15f, 0f),
				Obstacles = new[]
				{
					new Vector3(-2.5f, 1f, 3.5f),
					new Vector3(-2.5f, 1f, -3.5f),
					new Vector3(2.5f, 1f, 3.5f),
					new Vector3(2.5f, 1f, -3.5f),
					new Vector3(0f, 1f, 0f),
				},
				ObstacleSizes = new[]
				{
					new Vector3(4f, 2f, 1.4f),
					new Vector3(4f, 2f, 1.4f),
					new Vector3(4f, 2f, 1.4f),
					new Vector3(4f, 2f, 1.4f),
					new Vector3(1.5f, 2f, 4f),
				},
				GuardRoutes = new[]
				{
					new[] { new Vector3(-5f, 1f, 5f), new Vector3(0f, 1f, 5f), new Vector3(0f, 1f, 2f), new Vector3(-5f, 1f, 2f) },
					new[] { new Vector3(0f, 1f, -5f), new Vector3(5f, 1f, -5f), new Vector3(5f, 1f, -2f), new Vector3(0f, 1f, -2f) },
					new[] { new Vector3(-1f, 1f, 0f), new Vector3(4f, 1f, 0f) },
				},
			},
		};

		readonly List<StealthGuardController> m_Guards = new List<StealthGuardController>();
		readonly Dictionary<Color, Material> m_Materials = new Dictionary<Color, Material>();
		Transform m_StageRoot;
		Transform m_Player;
		CharacterController m_PlayerCharacter;
		Vector3 m_ExitPosition;
		UnityEngine.UI.Text m_StatusText;
		UnityEngine.UI.Text m_ControlsText;
		GameObject m_Banner;
		UnityEngine.UI.Text m_BannerText;
		float m_StateTimer;
		bool m_GameplayInputEnabled = true;

		[DebugField(Access = DebugFieldAccess.ReadOnly, DisplayName = "Stage Index")]
		int m_CurrentStageIndex;

		public static StealthStageDemoController Instance { get; private set; }
		public int CurrentStageNumber => m_CurrentStageIndex + 1;
		public int StageCount => s_Stages.Length;
		public string Objective => s_Stages[m_CurrentStageIndex].Objective;

		[field: DebugField(Access = DebugFieldAccess.ReadOnly)]
		public StealthStageState State { get; private set; }

		public Transform PlayerTransform => m_Player;

		/// <summary>The player as Dexc draws it, so a guard's line to the player starts and ends at the right objects.</summary>
		public DebugDisplaySubject PlayerSubject { get; private set; }
		public Vector3 PlayerPosition => m_Player.position;
		public Vector3 ExitPosition => m_ExitPosition;
		/// <summary>Distance to the Exit's centre across the floor. The player stands taller than the pad, so height is left out.</summary>
		public float DistanceToExit
		{
			get
			{
				var offset = m_Player.position - m_ExitPosition;
				offset.y = 0f;
				return offset.magnitude;
			}
		}
		public IReadOnlyList<StealthGuardController> Guards => m_Guards;

		[field: DebugField(Access = DebugFieldAccess.ReadOnly)]
		public bool IsAlarmForced { get; private set; }

		[field: DebugField(Access = DebugFieldAccess.ReadOnly)]
		public int DetectionCount { get; private set; }

		public float PlayerMoveSpeed { get; private set; } = 5f;
		public float GuardMoveSpeed { get; private set; } = 2.2f;
		public float FieldOfView { get; private set; } = 70f;
		public float DetectionDistance { get; private set; } = 6.5f;
		public bool ShowDebugWorldVisuals { get; private set; } = true;
		public Material GuardMaterial { get; private set; }
		public Material AlertMaterial { get; private set; }

		void Awake()
		{
			Instance = this;
			Time.timeScale = StealthDemoConfig.TimeScale;
			CreateMaterials();
			CreateCameraAndLight();
			CreateHud();
			BuildStage((int)StealthDemoConfig.StartStage);
			// Without a Dexc session, registering and drawing do nothing.
			DebugInspector.Register(InspectorPath, this, "Stage Session");
		}

		void OnDestroy()
		{
			if (Instance == this)
				Instance = null;
			DebugInspector.Unregister(InspectorPath, this);
			PlayerSubject.Dispose();
			foreach (var material in m_Materials.Values)
				Destroy(material);
			m_Materials.Clear();
		}

		void Update()
		{
			if (StealthStageInput.WasRetryPressed())
				RestartStage();
			if (State == StealthStageState.Running)
			{
				if (m_GameplayInputEnabled)
					MovePlayer(StealthStageInput.ReadMove(), Time.deltaTime);
				if (DistanceToExit <= ExitRadius)
					CompleteCurrentStage();
			}
			else
			{
				// Every other state is a pause that ends in the next step of the run.
				m_StateTimer -= Time.deltaTime;
				if (m_StateTimer <= 0f)
					EndPause();
			}

			UpdateHud();
		}

		void LateUpdate()
		{
			var position = PlayerPosition;
			DebugDisplay.DrawLabel(
				DebugDrawOptions.Frame("Samples/Stealth/HUD/Player", Color.cyan),
				"Player  " + position.x.ToString("0.0") + ", " + position.z.ToString("0.0")
			);
			if (ShowDebugWorldVisuals)
				DebugDisplay.DrawWireCube(
					DebugDrawOptions.Frame("Samples/Stealth/World/Exit", Color.green),
					m_ExitPosition + Vector3.up * 0.5f,
					new Vector3(ExitRadius * 2f, 1f, ExitRadius * 2f)
				);
			PlayerSubject.SetMetadata("Position", position);
			PlayerSubject.SetMetadata("State", State.ToString());
			PlayerSubject.SetMetadata("DistanceToExit", DistanceToExit);
		}

		public void ApplyTuning(float playerSpeed, float guardSpeed, float fieldOfView, float detectionDistance, bool showWorldVisuals)
		{
			PlayerMoveSpeed = playerSpeed;
			GuardMoveSpeed = guardSpeed;
			FieldOfView = fieldOfView;
			DetectionDistance = detectionDistance;
			ShowDebugWorldVisuals = showWorldVisuals;
		}

		public void SetGameplayInputEnabled(bool value)
		{
			m_GameplayInputEnabled = value;
		}

		public string RestartStage()
		{
			BuildStage(m_CurrentStageIndex);
			return "Restarted stage " + CurrentStageNumber + ".";
		}

		public string NextStage()
		{
			if (m_CurrentStageIndex + 1 >= s_Stages.Length)
			{
				CompleteAllStages();
				return "All stealth stages complete.";
			}

			BuildStage(m_CurrentStageIndex + 1);
			return "Loaded stage " + CurrentStageNumber + ".";
		}

		public string LoadStageNumber(int stageNumber)
		{
			if (stageNumber < 1 || stageNumber > s_Stages.Length)
				throw new ArgumentOutOfRangeException(nameof(stageNumber));
			BuildStage(stageNumber - 1);
			return "Loaded stage " + CurrentStageNumber + ".";
		}

		public string WarpPlayer(StealthWarpDestination destination, float offset)
		{
			m_PlayerCharacter.enabled = false;
			var position =
				destination == StealthWarpDestination.Start
					? s_Stages[m_CurrentStageIndex].PlayerStart + Vector3.right * offset
					: m_ExitPosition - Vector3.right * Mathf.Max(0.25f, offset);
			position.y = s_Stages[m_CurrentStageIndex].PlayerStart.y;
			m_Player.position = position;
			m_PlayerCharacter.enabled = true;
			return "Warped player to " + destination + " with offset " + offset.ToString("0.0") + ".";
		}

		public string SetAlarm(bool enabled)
		{
			IsAlarmForced = enabled;
			foreach (var guard in m_Guards)
				guard.SetAlert(enabled);
			return "Alarm " + (enabled ? "enabled." : "disabled.");
		}

		public void NotifyDetected(StealthGuardController guard)
		{
			if (State != StealthStageState.Running)
				return;
			guard.SetAlert(true);
			if (StealthDemoConfig.Invincible)
				return;
			DetectionCount++;
			State = StealthStageState.Detected;
			m_StateTimer = 1.4f;
		}

		public void CompleteCurrentStage()
		{
			if (State != StealthStageState.Running)
				return;
			if (m_CurrentStageIndex + 1 >= s_Stages.Length)
			{
				CompleteAllStages();
				return;
			}

			State = StealthStageState.StageCleared;
			m_StateTimer = 1.1f;
		}

		void CompleteAllStages()
		{
			State = StealthStageState.AllStagesComplete;
			m_StateTimer = AllClearPause;
		}

		/// <summary>Seen: the same stage again. Cleared: the next one. All cleared: the run starts over from stage 1.</summary>
		void EndPause()
		{
			switch (State)
			{
				case StealthStageState.Detected:
					RestartStage();
					break;
				case StealthStageState.StageCleared:
					NextStage();
					break;
				case StealthStageState.AllStagesComplete:
					BuildStage(0);
					break;
			}
		}

		void BuildStage(int stageIndex)
		{
			m_CurrentStageIndex = Mathf.Clamp(stageIndex, 0, s_Stages.Length - 1);
			State = StealthStageState.Running;
			m_StateTimer = 0f;
			IsAlarmForced = false;
			m_Guards.Clear();
			if (m_StageRoot != null)
				Destroy(m_StageRoot.gameObject);

			var root = new GameObject("Stage " + CurrentStageNumber);
			root.transform.SetParent(transform, false);
			m_StageRoot = root.transform;
			var layout = s_Stages[m_CurrentStageIndex];
			CreatePrimitive(
				"Floor",
				PrimitiveType.Cube,
				new Vector3(0f, -0.5f, 0f),
				new Vector3(18f, 1f, 14f),
				MaterialFor(new Color(0.10f, 0.13f, 0.18f))
			);
			CreateBoundaryWalls();
			for (var index = 0; index < layout.Obstacles.Length; index++)
			{
				CreatePrimitive(
					"Obstacle " + (index + 1),
					PrimitiveType.Cube,
					layout.Obstacles[index],
					layout.ObstacleSizes[index],
					MaterialFor(new Color(0.28f, 0.34f, 0.43f))
				);
			}

			var exit = CreatePrimitive(
				"Exit",
				PrimitiveType.Cylinder,
				layout.Exit,
				new Vector3(ExitRadius * 2f, 0.15f, ExitRadius * 2f),
				MaterialFor(new Color(0.15f, 0.95f, 0.42f))
			);
			exit.GetComponent<Collider>().isTrigger = true;
			m_ExitPosition = layout.Exit;
			CreatePlayer(layout.PlayerStart);
			var guardCount = Mathf.Min(layout.GuardRoutes.Length, StealthDemoConfig.GuardCount);
			for (var index = 0; index < guardCount; index++)
				CreateGuard(index, layout.GuardRoutes[index]);
		}

		void CreatePlayer(Vector3 position)
		{
			var player = CreatePrimitive(
				"Player",
				PrimitiveType.Capsule,
				position,
				new Vector3(0.8f, 1f, 0.8f),
				MaterialFor(new Color(0.12f, 0.62f, 1f))
			);
			var primitiveCollider = player.GetComponent<Collider>();
			primitiveCollider.enabled = false;
			m_PlayerCharacter = player.AddComponent<CharacterController>();
			m_PlayerCharacter.height = 2f;
			m_PlayerCharacter.radius = 0.42f;
			m_Player = player.transform;
			PlayerSubject?.Dispose();
			PlayerSubject = DebugDisplay.CreateSubject("Samples/Stealth/Player", m_Player);
		}

		void CreateGuard(int index, Vector3[] route)
		{
			var guardObject = CreatePrimitive(
				"Guard " + (index + 1),
				PrimitiveType.Capsule,
				route[0],
				new Vector3(0.9f, 1f, 0.9f),
				GuardMaterial
			);
			var guard = guardObject.AddComponent<StealthGuardController>();
			guard.Initialize(this, route);
			m_Guards.Add(guard);
		}

		void MovePlayer(Vector2 input, float deltaTime)
		{
			if (input.sqrMagnitude < 0.001f)
				return;
			var direction = new Vector3(input.x, 0f, input.y);
			m_PlayerCharacter.Move(direction * PlayerMoveSpeed * deltaTime);
			m_Player.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
		}

		void CreateBoundaryWalls()
		{
			var material = MaterialFor(new Color(0.18f, 0.22f, 0.29f));
			CreatePrimitive("North Wall", PrimitiveType.Cube, new Vector3(0f, 1f, 7.25f), new Vector3(18.5f, 2f, 0.5f), material);
			CreatePrimitive("South Wall", PrimitiveType.Cube, new Vector3(0f, 1f, -7.25f), new Vector3(18.5f, 2f, 0.5f), material);
			CreatePrimitive("East Wall", PrimitiveType.Cube, new Vector3(9.25f, 1f, 0f), new Vector3(0.5f, 2f, 14f), material);
			CreatePrimitive("West Wall", PrimitiveType.Cube, new Vector3(-9.25f, 1f, 0f), new Vector3(0.5f, 2f, 14f), material);
		}

		GameObject CreatePrimitive(string objectName, PrimitiveType primitiveType, Vector3 position, Vector3 scale, Material material)
		{
			var instance = GameObject.CreatePrimitive(primitiveType);
			instance.name = objectName;
			instance.transform.SetParent(m_StageRoot, false);
			instance.transform.position = position;
			instance.transform.localScale = scale;
			instance.GetComponent<Renderer>().sharedMaterial = material;
			return instance;
		}

		void CreateMaterials()
		{
			GuardMaterial = MaterialFor(new Color(1f, 0.72f, 0.12f));
			AlertMaterial = MaterialFor(new Color(1f, 0.12f, 0.12f));
		}

		Material MaterialFor(Color color)
		{
			if (m_Materials.TryGetValue(color, out var cached))
				return cached;
			var shader = Shader.Find("Universal Render Pipeline/Lit");
			var material = new Material(shader) { color = color, hideFlags = HideFlags.DontSave };
			m_Materials[color] = material;
			return material;
		}

		void CreateCameraAndLight()
		{
			if (Camera.main == null)
			{
				var cameraObject = new GameObject("Top Down Camera");
				cameraObject.tag = "MainCamera";
				cameraObject.transform.SetParent(transform, false);
				// Shifted up and zoomed out a little, so the stage leaves a band above it for the game panel and the Dexc
				// HUD, and a thin one below it for the controls.
				cameraObject.transform.position = new Vector3(0f, 22f, 1f);
				cameraObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
				var camera = cameraObject.AddComponent<Camera>();
				camera.orthographic = true;
				camera.orthographicSize = 9.5f;
				camera.clearFlags = CameraClearFlags.SolidColor;
				camera.backgroundColor = new Color(0.025f, 0.035f, 0.055f);
			}

			if (FindFirstObjectByType<Light>() == null)
			{
				var lightObject = new GameObject("Directional Light");
				lightObject.transform.SetParent(transform, false);
				lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
				var light = lightObject.AddComponent<Light>();
				light.type = LightType.Directional;
				light.intensity = 1.25f;
			}
		}

		void CreateHud()
		{
			var canvasObject = new GameObject(
				"Stealth HUD",
				typeof(RectTransform),
				typeof(Canvas),
				typeof(UnityEngine.UI.CanvasScaler),
				typeof(UnityEngine.UI.GraphicRaycaster)
			);
			canvasObject.transform.SetParent(transform, false);
			var canvas = canvasObject.GetComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
			scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1920f, 1080f);
			scaler.matchWidthOrHeight = 0.5f;

			// Top right: the Dexc HUD draws in the top left corner.
			var panel = CreateHudObject("Status Panel", canvasObject.transform, new Color(0.02f, 0.04f, 0.07f, 0.86f));
			SetRect(
				panel.GetComponent<RectTransform>(),
				new Vector2(1f, 1f),
				new Vector2(1f, 1f),
				new Vector2(1f, 1f),
				new Vector2(-24f, -16f),
				new Vector2(620f, 124f)
			);
			m_StatusText = CreateText("Status", panel.transform, 24, TextAnchor.UpperLeft);
			SetStretch(m_StatusText.rectTransform, 20f, 12f, 20f, 12f);

			var controls = CreateHudObject("Controls Panel", canvasObject.transform, new Color(0.02f, 0.04f, 0.07f, 0.78f));
			SetRect(
				controls.GetComponent<RectTransform>(),
				new Vector2(0f, 0f),
				new Vector2(0f, 0f),
				new Vector2(0f, 0f),
				new Vector2(24f, 10f),
				new Vector2(1000f, 50f)
			);
			m_ControlsText = CreateText("Controls", controls.transform, 18, TextAnchor.MiddleLeft);
			SetStretch(m_ControlsText.rectTransform, 20f, 10f, 20f, 10f);
			m_ControlsText.text = "MOVE  WASD / Arrows / Left Stick     RETRY  R / Select     DEBUG MENU  F1 / Start / DEBUG button";

			// Centre of the screen, shown only while a stage or the whole run is cleared.
			m_Banner = CreateHudObject("Clear Banner", canvasObject.transform, new Color(0.02f, 0.06f, 0.04f, 0.96f));
			SetRect(
				m_Banner.GetComponent<RectTransform>(),
				new Vector2(0.5f, 0.5f),
				new Vector2(0.5f, 0.5f),
				new Vector2(0.5f, 0.5f),
				Vector2.zero,
				new Vector2(760f, 200f)
			);
			m_BannerText = CreateText("Clear", m_Banner.transform, 60, TextAnchor.MiddleCenter);
			m_BannerText.color = new Color(0.45f, 1f, 0.6f);
			SetStretch(m_BannerText.rectTransform, 24f, 16f, 24f, 16f);
			m_Banner.SetActive(false);
		}

		GameObject CreateHudObject(string objectName, Transform parent, Color color)
		{
			var instance = new GameObject(objectName, typeof(RectTransform), typeof(UnityEngine.UI.Image));
			instance.transform.SetParent(parent, false);
			var image = instance.GetComponent<UnityEngine.UI.Image>();
			image.color = color;
			image.raycastTarget = false;
			return instance;
		}

		UnityEngine.UI.Text CreateText(string objectName, Transform parent, int fontSize, TextAnchor alignment)
		{
			var instance = new GameObject(objectName, typeof(RectTransform), typeof(UnityEngine.UI.Text));
			instance.transform.SetParent(parent, false);
			var text = instance.GetComponent<UnityEngine.UI.Text>();
			text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			text.fontSize = fontSize;
			text.alignment = alignment;
			text.color = new Color(0.94f, 0.97f, 1f);
			text.raycastTarget = false;
			return text;
		}

		static void SetRect(
			RectTransform rect,
			Vector2 anchorMin,
			Vector2 anchorMax,
			Vector2 pivot,
			Vector2 anchoredPosition,
			Vector2 sizeDelta
		)
		{
			rect.anchorMin = anchorMin;
			rect.anchorMax = anchorMax;
			rect.pivot = pivot;
			rect.anchoredPosition = anchoredPosition;
			rect.sizeDelta = sizeDelta;
		}

		static void SetStretch(RectTransform rect, float left, float top, float right, float bottom)
		{
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = new Vector2(left, bottom);
			rect.offsetMax = new Vector2(-right, -top);
		}

		void UpdateHud()
		{
			var alert = State == StealthStageState.Detected || IsAlarmForced;
			m_StatusText.color = alert ? new Color(1f, 0.36f, 0.28f) : new Color(0.88f, 0.96f, 1f);
			var banner = BannerText();
			m_Banner.SetActive(banner != null);
			if (banner != null)
				m_BannerText.text = banner;
			m_StatusText.text =
				"STAGE "
				+ CurrentStageNumber
				+ " / "
				+ StageCount
				+ "    "
				+ Headline()
				+ "\n"
				+ Objective
				+ "\nExit  "
				+ DistanceToExit.ToString("0.0")
				+ " m    Server  "
				+ StealthDemoConfig.Environment
				+ " ("
				+ StealthDemoConfig.Host
				+ ")"
				+ (StealthDemoConfig.Invincible ? "    INVINCIBLE" : string.Empty);
		}

		/// <summary>What the status panel adds after the stage number. Clearing is shown in the centre banner instead.</summary>
		string Headline()
		{
			if (State == StealthStageState.Detected)
				return "DETECTED";
			return IsAlarmForced ? "ALARM" : string.Empty;
		}

		/// <summary>The centre banner's text, or null while no stage is cleared.</summary>
		string BannerText()
		{
			switch (State)
			{
				case StealthStageState.StageCleared:
					return "STAGE " + CurrentStageNumber + " CLEAR";
				case StealthStageState.AllStagesComplete:
					return "ALL STAGES CLEAR\n<size=30>Stage 1 in " + Mathf.CeilToInt(m_StateTimer) + "</size>";
				default:
					return null;
			}
		}
	}
}
