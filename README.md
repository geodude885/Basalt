# BossBattles

Survive scripted boss battles down a single lane against game-simulated actors: a
[MelonLoader](https://melonloader.co/) mod for **RUMBLE**. What players see is in
[Thunderstore/README.md](Thunderstore/README.md); the chart format is in
[docs/chart-format.md](docs/chart-format.md), with a [JSON Schema](docs/chart.schema.json).

## Projects

```
BossBattles/            the mod
  ModEntry*.cs          entry point, chart actions, the picker's panel
  Core/                 shared state (Mod: actors, battle, studio) and Log, with its log level
  Actors/               Actor (simulated player): lifecycle, playback, body; rig, recorders
  Battle/               BattleController, projectiles, lane, arenas, music, studio, RumbleReShaded
  Patches/              ActorPatches (health, knockback, input, sounds), ArenaPatches
  UI/                   settings, the chart picker, covers, the game's buttons
  Services/             game-free and unit tested: charts, clips, audio, scoring, logging
BossBattles.DevTools/   a second mod for development: self-tests, test actor, flatscreen play
BossBattles.Tests/      xUnit: services, patch targets against the interop
shaders/                the BossBattlesNight RumbleReShaded pack (a Unity project)
tools/                  song analysis, cpp2il-isil.py
```

UIFramework is required. RumbleReShaded is optional: `Battle/ReShade.cs` finds it by reflection,
and nothing else references it.

## How the actors work

An actor is a real `PlayerController`, created as a **Local** player (its actor number is yours),
so the game itself simulates it: movement, physics states, knockback, moves and shiftstones. It is
**drawn as a Remote** player, so it has a body and its sounds play in the world. It never reaches
0 HP, where the game's death handling would run: before a lethal hit it is put back on full health. In a battle it is **planted**:
X and Z frozen on its physics bodies, while jumps and gravity still work. `CLAUDE.md`, "Extra
players", has the findings behind this.

Actors must never be taken into a room with other players: a Local actor would report health
changes as you.

## Dev tools

`BossBattles.DevTools.dll` is built beside the mod and copied into `RUMBLE\Mods`; it is never
packaged. It adds:

| Keys | Does |
|---|---|
| Ctrl+F | Desktop camera: right drag orbits, scroll zooms, middle drag pans |
| Ctrl+T | Spawn / despawn a test actor 3 m in front of you |
| Ctrl+9 | Test actor: mirror (copies you) / bot (keys drive it) |
| Ctrl+P / Ctrl+K | Plant / unplant it; cycle the knockback it takes |
| Ctrl+Shift+P | The test actor plays the newest clip |
| (bot) WASD, Q/E, F | Walk, turn, face you on/off |
| (bot) Space, Shift, 1-4, Z/X/C/V, T/G | Jump, dash, spawn cube/pillar/wall/ball, straight/uppercut/kick/stomp, disc/explode |
| Numpad 8/2/4/6, 7/9 | No headset: walk and turn your own player |

**Self-tests** run with Steam open (launching `RUMBLE.exe` directly gets no sign-in), write
`SELFTEST PASS` or the problems to `UserData/BossBattles/selftest.txt`, and quit:

```sh
steam.exe -applaunch 890550 --bossbattles-selftest=ring --bossbattles-shots
```

| Argument | Runs |
|---|---|
| `--bossbattles-selftest` (`=ring`, `=pit`) | The self-test chart, in place, in the Ring from a chart spot, or the Pit laid out automatically |
| `=record` | Records, reviews and keeps a clip |
| `=look` | A new chart's boss wears your look and stones, and an edited look |
| `=measure` | Writes structure sizes to `SelfTest/structures.json` for the editor |
| `=chart:<name>` | Plays one of your charts and measures its clips |
| `--bossbattles-shots` | Adds screenshots to `UserData/BossBattles/SelfTestShots` |

The game sometimes closes at the loading screen before the Gym is up; run it again.

## Building

Needs the .NET SDK and RUMBLE with MelonLoader and UIFramework installed.

```sh
dotnet build -c Release
dotnet test
```

Both mods are copied into `RUMBLE\Mods`, and the song decoders into `RUMBLE\UserLibs`. If Steam is
elsewhere: `dotnet build -c Release /p:RumblePath="D:\Games\RUMBLE"`. The target framework must stay
`net6.0` (MelonLoader hosts .NET 6). The build enforces `.editorconfig` with warnings as errors.

The chart editor is a separate desktop app, in its own repository (`RumbleBossBattlesEditor`). It takes
`docs/chart.schema.json` and the `=measure` structure sizes from here with `npm run sync` - change the
format here first.

## Packaging for Thunderstore

```sh
build-thunderstore.bat
```

Edit `Version.props`, `Thunderstore\manifest.json` and `Thunderstore\CHANGELogger.md` first, and add a
256x256 `Thunderstore\icon.png`. The package carries `BossBattles.dll`, the song decoders and
`Thunderstore\README.md`.
