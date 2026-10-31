# Display

画面と空間にデバッグ表示を描きます。表示は3つの層に分かれます。

| 層 | 内容 |
| --- | --- |
| HUD | 画面左上の値とラベル。メニューのパネルと重なるときは右上へ移ります |
| Overlay | Subject（objectごとの表示）のラベルと値。HUDの下に並びます |
| World | 空間の線と図形。URPとSkin（World Visual Shader）が必要です |

既定の描画は1フレームだけ残ります。毎フレーム`Update`や`LateUpdate`から呼びます。sessionが無いときは何も描かないので、`IsInitialized`を確認する必要はありません。

## HUD

```csharp
DebugDisplay.DrawValue("Game/HUD/Score", score);
DebugDisplay.DrawLabel("Game/HUD/State", "State " + state, Color.cyan);
```

`DrawValue`は`int`、`float`、`double`、`string`を受け取ります。

## HUD Widget

毎フレーム同じ文字列を出すだけなら、widgetを一度登録します。

```csharp
var widget = new DebugDisplayWidget("Game/HUD/Detections", () => "Detections " + game.DetectionCount);
DebugDisplay.RegisterWidget(widget);
// 終わるとき
DebugDisplay.UnregisterWidget(widget);
```

`Enabled = false`で登録したまま隠せます。FPSとframe番号はDexcが最初からHUDに出します。

## World図形

```csharp
var options = DebugDrawOptions.Frame("Game/World/Exit", Color.green);
DebugDisplay.DrawWireCube(options, exitPosition, new Vector3(2f, 1f, 2f));
DebugDisplay.DrawWireSphere(options, transform.position, 3f);
DebugDisplay.DrawLine("Game/World/Aim", start, end, Color.red);
DebugDisplay.DrawWireSector(options, transform.position, Vector3.up, transform.forward, 70f, 6f);
```

| method | 図形 |
| --- | --- |
| `DrawLine`、`DrawRay`、`DrawPolyLine` | 線 |
| `DrawWireSphere`、`DrawWireCube`、`DrawWireDisc` | 線で描く球、箱、円 |
| `DrawWireSector` | 扇形（視界など）。`DebugDrawOptions`を取る形だけです |
| `DrawSphere`、`DrawCube`、`DrawDisc`、`DrawCapsule` | `DebugDisplayDrawStyle`で`Wire`／`Solid`／`WireAndSolid`を選べる図形 |

```csharp
DebugDisplay.DrawSphere(
	DebugDrawOptions.Frame("Game/World/Hit", Color.red).WithStyle(DebugDisplayDrawStyle.Solid),
	hitPoint,
	0.3f
);
```

座標は既定でworld座標です。`DebugDisplaySpace.Local`と`basis`のTransformを渡すと、そのTransform基準になります。

## PolyLine

点の列は`IEnumerable<Vector3>`、配列、`ReadOnlySpan<Vector3>`のどれでも渡せます。spanを使うと、毎フレームの配列を作らずに済みます。

```csharp
Span<Vector3> route = stackalloc Vector3[points.Length + 1];
for (var index = 0; index < points.Length; index++)
{
	route[index] = points[index];
}
route[points.Length] = points[0];
DebugDisplay.DrawPolyLine(DebugDrawOptions.Frame("Game/World/Patrol", Color.yellow), route);
```

`Span<Vector3>`には`using System;`が必要です。

## 時間を指定して描く

`DebugDrawOptions`の作り方で残る時間が決まります。

| 作り方 | 残る時間 |
| --- | --- |
| `DebugDrawOptions.Frame(path, color)` | 1フレーム |
| `DebugDrawOptions.Realtime(path, color, seconds)` | 実時間で指定秒 |
| `DebugDrawOptions.GameTime(path, color, seconds)` | ゲーム時間で指定秒 |
| `DebugDrawOptions.Infinity(path, color)` | `Clear`まで |

```csharp
DebugDisplay.DrawLabelFor("Game/HUD/Damage", damage.ToString(), 2f, DebugDisplayTimeScope.Realtime);
DebugDisplay.DrawLine(DebugDrawOptions.Infinity("Game/World/Path", Color.yellow), start, end);
DebugDisplay.Clear("Game/World/Path");
```

## Subject

objectごとに名前、値、図形をまとめるときはSubjectを作ります。Subjectの`DrawLabel`／`DrawValue`は「Subject名 · 値」の形でOverlayに出ます。`SetMetadata`の値はEditorのDexc Debugger（Display）とRemoteで確認できます。

```csharp
using Dexc;
using UnityEngine;

namespace MyGame
{
	public sealed class Guard : MonoBehaviour
	{
		[SerializeField]
		Transform m_Player;

		[SerializeField]
		int m_PatrolIndex;

		DebugDisplaySubject m_Subject;

		void OnEnable()
		{
			m_Subject = DebugDisplay.CreateSubject("Game/Guard", this);
			m_Subject.SetLabel(name);
		}

		void OnDisable()
		{
			m_Subject.Dispose();
		}

		void LateUpdate()
		{
			m_Subject.SetMetadata("PatrolIndex", m_PatrolIndex);
			m_Subject.DrawWireSphere(DebugDrawOptions.Frame("Game/World/Guard/Range", Color.cyan), transform.position, 4f);
			m_Subject.DrawRelation(DebugDrawOptions.Frame("Game/World/Guard/Target", Color.red), m_Player);
		}
	}
}
```

`DrawRelation`はSubjectから対象への線です。対象には`UnityEngine.Object`か別のSubjectを渡せます。

## Releaseで消えるようにする

`Draw…`や`SetMetadata`の呼び出しは`DEXC_DEBUGGER`が無いbuildで消えますが、その前に書いた普通の処理は残ります。描画のための処理は`[Conditional("DEXC_DEBUGGER")]`のmethodにまとめます。

```csharp
[System.Diagnostics.Conditional("DEXC_DEBUGGER")]
void DrawDebug()
{
	Span<Vector3> route = stackalloc Vector3[m_Route.Length];
	for (var index = 0; index < m_Route.Length; index++)
	{
		route[index] = m_Route[index] + Vector3.up * 0.25f;
	}
	DebugDisplay.DrawPolyLine(DebugDrawOptions.Frame("Game/World/Route", Color.yellow), route);
}
```

`using System.Diagnostics;`を書くと`Debug`が`UnityEngine.Debug`と衝突するので、属性名を完全な名前で書くと楽です。

## 表示の切替

メニューのSettingで`Display/HUD`、`Display/Overlay`、`Display/World`を切り替えると、層ごとに隠せます。ゲーム側でbool Settingを`Display/<path>`に作ると、`false`のときそのpath以下を隠せます。

```csharp
[DebugSetting("Display/Game/World/Guard")]
public bool ShowGuard { get; private set; } = true;
```

この例では`Game/World/Guard`以下の表示が切り替わります。
