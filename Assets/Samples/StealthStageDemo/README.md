# Stealth Stage Demo

This sample is a small three-stage top-down stealth game built only from Unity primitives. It shows game code using Dexc directly: the game's own components put their state in the Inspector and draw it with DebugDisplay, a small system component brings Dexc up, DebugCommand and DebugSetting reach the game from the outside, and a boot menu hands start-up choices to the game.

## Requirements

- Unity 6000.3 or later with URP 17.3.0
- `com.unity.inputsystem` 1.0 or later (the repository uses 1.19.0)
- `com.unity.ugui` 2.0.0 (required by the sample gameplay HUD)
- `DEXC_DEBUGGER` in the active build target's Scripting Define Symbols to turn Dexc on

No external art, Resources asset, or project-specific font is required. Input System and uGUI are sample prerequisites; they are not added as dependencies of the Dexc package itself.

## Run

It lives in the Dexc Debugger repository under `Assets/Samples/StealthStageDemo` and is not shipped with the UPM package.

1. Open the repository project and confirm `DEXC_DEBUGGER` is defined.
2. Open one of the scenes under `Assets/Samples/StealthStageDemo/Scenes` and enter Play Mode:
   - `StealthBoot.unity` - the everyday boot menu: server, Invincible, and Time Scale.
   - `StealthBattleBoot.unity` - the battle check: the same server choice plus Start Stage and Guard Count.
   - `StealthStageDemo.unity` - the game directly, with default values and the debugger off, since no boot screen chose it.
   The **Dexc Boot** Editor window can start either boot set as well.
3. Move with **WASD / arrow keys** or the **gamepad left stick**. Reach the green Exit without entering a Guard's red view cone.
4. Press **R / gamepad Select** to retry. Press **F1 / gamepad Start** to toggle the Dexc runtime menu.

Choosing **Production** as the server starts the game with the debugger off, as a release build would. The choice is remembered; **Reset** in the boot menu returns every item to its default. The boot scenes start the game by scene name, so all three scenes are in Build Settings.

If the scenes need to be regenerated after editing the sample, use **Tools > Dexc Debugger Samples > Rebuild Stealth Stage Demo Scenes**. The command creates them through Unity Editor APIs and adds them to Build Settings; the scene YAML is not maintained by hand.

## Dexc mapping

| Feature | Demo paths | What to try |
| --- | --- | --- |
| Boot menu | `stealth`, `stealth-battle` | Pick a server once and both start configurations share it. **Reset** returns the items to their defaults. The values land in the game's own `StealthDemoConfig`, which the stage reads when it loads. |
| DebugSetting | `Samples/Stealth/Settings/*` | Change Player Speed immediately. Change Guard Speed, Field Of View, or Detection Distance and observe the pending Apply state before applying. Apply the Easy or Challenge preset. The game hears about every change through `OnChanged`. |
| DebugCommand | `Samples/Stealth/Commands/*` | Restart/load/advance stages, warp to Start or Exit, and force Alarm. `Load Stage` and `Warp Player` include argument validation; output and history show the result. |
| DebugInspector | `Samples/Stealth/Inspector/*` | The stage controller and each guard register themselves and mark the fields they show with `[DebugField]`: stage index, state, alarm and detections; patrol index, alert and line of sight. The gameplay components are also discoverable through Scene, and fields can be added to Watch. |
| DebugDisplay | `Samples/Stealth/HUD/*`, `Samples/Stealth/World/*` | The game's own panel (top right) shows the stage, objective and exit distance; the Dexc HUD (top left) adds what it does not: the player's position and a widget counting detections. World visuals show each guard's view as a sector, patrol routes, guard-to-player relation, and the Exit volume. |

## Compile boundary

`DexcDebugger.Samples.StealthStageDemo.Gameplay` references `Dexc.Runtime` and calls Dexc where the state lives. Without a Dexc session those calls do nothing, and without `DEXC_DEBUGGER` they are compiled out, so a release build plays the same game with nothing from Dexc. `DexcDebugger.Samples.StealthStageDemo.Debug` holds what only the debugger needs: the boot sets, the commands, the settings, the menu input, and `StealthDebugSystem`, which sits in the demo scene and starts Dexc when the boot screen asked for it. Removing Dexc from a project with `DISABLE_DEXC_DEBUGGER` is not the recommended way to use it, and this sample goes away with it.

The Input System and uGUI are required by the gameplay assembly, which references them directly. The sample uses its public `Keyboard` and `Gamepad` devices rather than installing or modifying an action asset in the host project.

## Repository validation

From the Dexc Debugger repository root, with Unity Editor closed, run:

```sh
sh tools/unity-tests/run-stealth-sample-validation.sh
```

The runner opens the sample scene in a real Play Mode session, captures gameplay and the Dexc menu, and writes the capture manifest under `Logs/UnityTests`. Compilation with Dexc disabled or assembly-excluded is checked by `run-symbol-matrix.sh`, which compiles the whole project, this sample included.
