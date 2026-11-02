# Basalt

Survive scripted boss battles down a single lane against game-simulated actors: a
[MelonLoader](https://melonloader.co/) mod for **RUMBLE**. What players see is in
[Thunderstore/README.md](Thunderstore/README.md); the chart format is in
[docs/chart-format.md](docs/chart-format.md), with a [JSON Schema](docs/chart.schema.json).

## Projects

```
Basalt/                 the mod
  BasaltMod.cs          builds the services, hands each what it needs, and passes MelonLoader's calls on
  Mod.cs, Logger.cs     the services for DevTools, and the logger with its log level
  Settings.cs           the MelonPreferences, shown by UIFramework
  Actors/               Actor (simulated player): lifecycle, playback, body; rig; the actor registry
  Battle/               BattleController, its request and run, and what it drives: projectiles, moves, the player
  Arenas/               the maps, and the lane's layout, walls and visuals
  Presentation/         music, the board, the game's assets, RumbleReShaded looks and signals
  Editing/              the studio (recording, lane setup) and the clip recorder
  Patches/              ActorPatches (health, knockback, input, sounds), ArenaPatches
  Menu/                 the Basalt menu: its screens, components (card, buttons, slider) and chart selection
  Services/             game-free and unit tested: charts, clips, audio, scoring, JSON storage
Basalt.Tests/           xUnit: services, patch targets against the interop
tools/                  song analysis, cpp2il-isil.py
```

UIFramework is required. RumbleReShaded is optional: only `Presentation/Shaders/ReShade.cs` touches its API, and
only once it is loaded with pack overrides (`RRS.Override`).

## How the actors work

An actor is a real `PlayerController`, created as a **Local** player (its actor number is yours),
so the game itself simulates it: movement, physics states, knockback, moves and shiftstones. It is
**drawn as a Remote** player, so it has a body and its sounds play in the world. It never reaches
0 HP, where the game's death handling would run: before a lethal hit it is put back on full health. In a battle it is **planted**:
X and Z frozen on its physics bodies, while jumps and gravity still work. `CLAUDE.md`, "Extra
players", has the findings behind this.

Actors must never be taken into a room with other players: a Local actor would report health
changes as you.

## Building

Needs the .NET SDK and RUMBLE with MelonLoader and UIFramework installed.

```sh
dotnet build -c Release
dotnet test
```

Both mods are copied into `RUMBLE\Mods`, and the song decoders into `RUMBLE\UserLibs`. If Steam is
elsewhere: `dotnet build -c Release /p:RumblePath="D:\Games\RUMBLE"`. The target framework must stay
`net6.0` (MelonLoader hosts .NET 6). The build enforces `.editorconfig` with warnings as errors.

The chart editor is a separate desktop app, in its own repository (`BasaltEditor`). It takes
`docs/chart.schema.json` and the `=measure` structure sizes from here with `npm run sync` - change the
format here first.

## Packaging for Thunderstore

```sh
build-thunderstore.bat
```

Edit `Version.props`, `Thunderstore\manifest.json` and `Thunderstore\CHANGELogger.md` first, and add a
256x256 `Thunderstore\icon.png`. The package carries `Basalt.dll`, the song decoders and
`Thunderstore\README.md`.
