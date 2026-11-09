# Basalt

Bullet hell/Rythmn game mode for rumble: a [MelonLoader](https://melonloader.co/) mod for **RUMBLE**. 

The chart format is in [docs/chart-format.md](docs/chart-format.md), with a [JSON Schema](docs/chart.schema.json).

UIFramework is required. RumbleReShaded is optional: only `Presentation/Shaders/ReShade.cs` touches its API, and
only once it is loaded with pack overrides (`RRS.Override`).

## How the actors work

Actors are a PlayerController created as a Local player, so all 'normal' game simulation happens to them. They are drawn as remote players, so that they are rendered and have remote sounds.

## Building

On build, the mod is copied into `RUMBLE\Mods`, and the song decoders into `RUMBLE\UserLibs`.

The chart editor is a separate desktop app, in its own repository (`BasaltEditor`). It takes `docs/chart.schema.json` from here.
