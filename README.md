# BossBattles

Survive scripted boss battles down a single lane against game-simulated actors.

A [MelonLoader](https://melonloader.co/) mod for **RUMBLE**. Battles are JSON charts: projectiles
telegraphed and launched down a lane at you, and actors - real, game-simulated players that are
not you - standing in it, moving, and using their moves. Survive to the end; hurting the actors is
the bonus. Battles run solo, wherever you stand or in the arena the chart names - the Gym, the
Ring or the Pit - optionally to music. Charts are shared as `.bbchart` packages.

## Playing

| Keys | Does |
|---|---|
| Ctrl+B | Start the chosen chart / stop the battle |
| Ctrl+N / Ctrl+Shift+N | Next / previous chart |
| Ctrl+M | Bring the chart picker to you / put it away |
| Ctrl+A | Arena override: Chart (each chart's own), Here, Gym, Ring, Pit |
| Ctrl+R | Reload charts from `UserData/BossBattles/Charts` (edit them while the game runs) |
| Ctrl+E | Export the chosen chart as a `.bbchart` package to `UserData/BossBattles/Exports` |
| Ctrl+W | Where am I: prints your spot as a chart's `"arena"` line, ready to paste |
| Ctrl+G | Back to the Gym from the Ring or Pit |
| Ctrl+F | Desktop camera: right drag orbits the first actor (or you), scroll zooms, middle drag pans. Renders to the game window only |
| Numpad 8/2/4/6, 7/9 | No headset only: walk and turn your own player |

**The chart picker** stands in front of you between battles: a card with the chosen chart - its
author, difficulty, arena, length, song and your best - over PREV / START / NEXT buttons (and GYM
when away from it). The buttons are the game's own push buttons, copied from the Gym tutorial (as
RumbleModdingAPI and RockUI do, without needing either). Push one with either hand, or click it in
It comes back after each battle, on the results.

Where it appears is up to you: the *Picker distance / height / sideways* settings place it relative
to your eyes, and in the Gym **Ctrl+M** (or *Chart picker here*) fixes it where it stands in front
of you - it then always appears there (saved in `UserData/BossBattles/places.json`, editable by
hand; turn *Fixed picker spot in the Gym* off to have it always come to you).

With [UIFramework](https://thunderstore.io/c/rumble/p/Reverb_and___and_Spice/UIFramework/)
installed, the settings window has the chart dropdown, the arena override, music volume, and
Start / Stop / Reload / Picker / Export / Back to the Gym buttons. Without it, everything works from
the picker, the keys and `UserData/BossBattles/settings.cfg`.

**Arenas.** A chart names where it is played (`"arena"`), and starting it takes you there: the
Ring and Pit are loaded solo (as MultiplayerMapsInSinglePlayer does, and alongside it if
installed), and you are moved onto the chart's spot, facing down the lane. A chart without a spot
in the Ring or Pit has its lane laid across the floor through the centre, shrunk to fit if it must.
*Here* is wherever you stand, the way you face. The arena override plays every chart in one place.

**Walls.** The lane is fenced with invisible walls: the backstop behind you, both sides 0.3 m
outside the edge lines, and across the far end behind the actors. They stop players - you and the
actors - but not structures, so shots can start outside the lane and cut across it, and missed
shots fly on past you. Should a dash still carry you past one, you are put back inside. *Show backstop* draws the backstop's frame; the other walls are never drawn.

**Your zone.** You cannot go past the zone's line (a pale line across the lane, by default 2 m short
of the far end) or above its height (3.5 m) - so you cannot walk up beside the actor or fly over the
lane. Structures and actors pass through. A chart sets it with `"zone": { "length", "height" }`.

**Rules.** `"rules": { "spawn": "discs" }` lets you spawn discs only (to move with); `"none"` leaves
you your moves alone. Your spawn moves come back when the battle ends. Every battle starts on a
clear floor: structures left from before are broken first.

**Music.** A chart can name a song beside it. It starts at GO, and the chart's clock follows the
song's own audio clock, so a beat-timed chart stays on the beat. It plays through the game's Music
mixer, so the game's music volume applies, times the Music volume setting. Songs can be MP3, OGG or
WAV: the game ships without Unity's audio decoding, so the mod decodes them itself while the battle
loads (a seven-minute MP3 takes about two seconds), with NLayer and NVorbis in `UserLibs`.

**Best scores** are kept per chart in `UserData/BossBattles/scores.json`; the results board says
when you beat yours.

**Scoring.** Surviving is worth 1000. Each point of damage on an actor scores 10, each knock-out
200, both times the phase's `scoreMultiplier`; each point of damage you take costs 25. Rank: S
untouched, A up to 3 damage, B up to 8, C any survival, D defeated.

## Charts

The full format, every field and its default, is in [docs/chart-format.md](docs/chart-format.md),
with a [JSON Schema](docs/chart.schema.json) for editor completion. In short:

A chart is a JSON file in `UserData/BossBattles/Charts` (an empty folder gets the starter charts) -
or a folder there holding `chart.json` with its song and `clips/`, or a `.bbchart` package.
Lane coordinates: `x` across (0 is the centre line), `y` up, `z` from your end (0) to the far end
(`lane.length`). Times are seconds (`t`, phase `start`), or beats (`beat`, `startBeat`) when the
chart has a `bpm`. Comments and trailing commas are allowed.

```jsonc
{
  "format": 1,
  "name": "Crossfire",
  "author": "you", "difficulty": "Normal", "description": "Shots cutting across the lane.",
  "arena": { "map": "ring", "at": { "x": 0, "z": -6 }, "facing": 0 },   // where you stand, from the centre
  "music": { "file": "song.mp3", "start": 0, "volume": 1 },            // beside the chart: mp3, ogg or wav
  "bpm": 120,                                                           // for "beat" times
  "lane": { "length": 12, "width": 3 },                 // defaults 12 and 3
  "actors": [                                           // default: one, at the far end
    { "id": "gunner", "name": "The Gunner", "at": { "x": 0, "z": 12 }, "look": "you" }
  ],
  "phases": [
    { "name": "Angles", "start": 0 },
    { "name": "Open", "start": 18, "scoreMultiplier": 2, "at": { "x": 1.2, "z": 10 } }
  ],
  "events": [
    // A projectile is spawned at "from" "telegraph" seconds before "t" (default 0.5) - up out of
    // the floor, with the spawn move's own sound and dust - then thrown with the punch's sound to
    // "to" on your end of the lane - { x, y }, or "player" - arriving after distance / speed seconds.
    { "t": 2, "projectile": { "structure": "Disc", "from": { "x": -1.5, "y": 1.4, "z": 10 },
                              "to": { "x": 1.2, "y": 1.3 }, "speed": 11, "telegraph": 0.8, "by": "gunner" } },
    { "t": 5,  "move": "Straight" },                       // an actor does one of its moves by name
    { "t": 7,  "clip": "uppercut-combo" },                 // an actor plays a recorded clip
    { "t": 10, "actor": "gunner", "at": { "x": -1.2, "z": 11 } }   // an actor moves (and is planted)
  ],
  "end": 33                                             // default: 3 s after the last event
}
```

- **Actors.** `id` (events refer to it; default the first actor), `name`, `at` (a spot: `x`, `z`,
  optional `facing` in degrees, 0 = looking down the lane at you), `look`: `you` (default),
  `default`, `defaultMale`, `defaultFemale`, or a character string as the game stores it.
- **Projectiles.** `structure` is a pool name: `Disc`, `Ball`, `Pillar`, `Wall`, `RockCube`,
  `BoulderBall`, `SmallRock`, `LargeRock`. Launches are ballistic: the speed sets how long you have,
  and the arc is chosen so it still arrives where aimed. `by` credits it to an actor.
- **Moves.** Any of the actor's moves by name: `Straight`, `Uppercut`, `Kick`, `Stomp`, `Explode`,
  `Flick`, `Parry`, `Jump`, `RockSlide` (dash), `Disc`, `SpawnBall`, `SpawnCube`, `SpawnPillar`,
  `SpawnWall`, `HoldLeft`, `HoldRight`.
- **Clips** are recorded performances (see below), by name - the clip's or its file's in
  `UserData/BossBattles/Clips`.
- **Phases** are timed sections: a name shown as it starts, a score multiplier, and optionally a
  new spot for one actor (`actor`, `at`).

Loading reports every problem in a chart at once, each with its path
(`events[3].projectile.speed must be over 0 m/s`) - in the MelonLoader console and
`UserData/BossBattles/battles.log`.

## Making charts

Chart-making tools are behind the **Chart editing** setting (off by default), so normal play stays
simple. With it on, the picker gains **RECORD**, and Ctrl+Shift+B (record), Ctrl+Shift+C (new
chart), Ctrl+E (export) and Ctrl+W (where am I) work.

**Recording an actor.** Choose a chart and press RECORD. You are put in the actor's place, facing
back down the lane; after the countdown (and with the chart's song) perform - walk, punch, spawn,
jump. Press STOP beside you (or Ctrl+B). You are put back at your own end and the actor performs
the take at you; then KEEP it, record AGAIN, REPLAY it, or DISCARD it. A kept take is saved into the
chart folder's `clips/` and the chart's `tracks`; the rest of `chart.json` - comments included - is
left as written. Playback is exact: the actor's own gesture recognition is off during a take and
every move you made runs at the moment you made it, while the actor is held to the path you walked.
A package (`.bbchart`) cannot be recorded into - unzip it into a folder first.

**Re-recording a section.** RE-RECORD (on a chart with a recorded actor) chooses a section: FROM and
TO move by a beat (half a second without a `bpm`), and a bar on the card shows it against the
chart. PREVIEW plays it as it is; RECORD starts two seconds before it, stands you where the actor
was at its start, counts you in, and stops by itself at its end. KEEP splices it in - the old take
plays up to it and picks up again after it; the takes themselves are never rewritten.

**Lane setup.** SETUP shows the chart's lane as a preview you can walk around (no walls), with a post
where the actor stands. LONGER / SHORTER / WIDER / NARROWER resize it; LANE HERE starts it where you
stand, facing the way you face, on whichever map you are; ACTOR HERE moves the actor to where you
stand; MAP cycles here / Gym / Ring / Pit; SAVE writes it into the chart.

**UNDO** puts back the chart as it was before its last save (kept as `chart.json.bak`).

**Ctrl+Shift+C** (or *New chart*) makes a chart folder from a commented template that plays as it
is, and picks it - so a first chart is editing one that works. Save the file and it reloads by
itself (between battles); a chart that does not load is still listed on the picker, with its
problems on the card. A `cover` image (PNG/JPG) beside the chart shows on the picker. The format,
field by field: [docs/chart-format.md](docs/chart-format.md).

## The chart editor

A chart editor in the browser: the song's waveform under a beat grid (tempo changes included),
projectiles placed on a top-down lane and dragged in time, moves, phases and actors, a live
preview of what flies when, half-speed playback with a click, tap tempo, a problems list that
matches the mod's own checks, and a rough difficulty (projectiles a second). Edits are written
into the chart's text in place, so comments and recorded takes survive.

- **From the game:** with Chart editing on, **Ctrl+Shift+E** (or *Open chart editor*) opens it in
  your browser and opens the chosen chart's folder beside it. In Chrome or Edge, *Open folder* edits
  the chart folder in place and *Save to folder* writes it - the game reloads it within a second.
- **Online:** the same editor is published as a page anyone with the link can use with nothing
  installed. Open the chart's files, edit, then *Copy chart.json* (paste it over the file) or save a
  package (`.zip` there - the game takes `.zip` packages as well as `.bbchart`).

The source is `editor/index.html`; `python tools/build-editor.py` builds the copy the mod carries
(`BossBattles/Editor/BossBattlesEditor.html` - a test fails if it is stale).

## Sharing charts

A `.bbchart` is a zip of a chart folder: `chart.json` at the top, the song and cover it names, and `clips/`
with every clip it plays. **Ctrl+E** (or *Export chart*) packs the chosen chart - its JSON as
written, comments and all, its song, and its clips, taken from your own recordings where the chart
folder has none - into `UserData/BossBattles/Exports`. Drop a `.bbchart` into the chart folder to
play it; it is unpacked into `UserData/BossBattles/ChartCache` once per version. Packages are other
people's files: one with paths reaching outside itself, over 512 files or over 256 MB is refused.

To write a chart with a song, make a folder: `Charts/my-chart/chart.json`, `Charts/my-chart/song.wav`,
`Charts/my-chart/clips/*.json`.

## Clips

**Ctrl+Shift+R** records you - your head and hand poses inside your rig and your two sticks, at
30 Hz - until you press it again, and saves the clip to `UserData/BossBattles/Clips`. A `clip`
event plays it on an actor through the same input the game reads from a headset and controllers,
so the game itself recognises your gestures and the actor's moves are real ones. Clips are
rig-local, so they play anywhere and facing any way. A planted actor stays put however a clip
walks. **Ctrl+Shift+P** plays the newest clip on the test actor (Ctrl+T) to try one out.

## How the actors work

An actor is a real `PlayerController`, created as a **Local** player (its actor number is yours),
so the game itself simulates it: movement, jumps, physics states, knockback, moves, shiftstones
(it copies yours). It is **drawn as a Remote** player, so it has a body, a floating health bar and
sounds in the world rather than in your head. Lethal damage knocks it out for 3 s instead of
running the game's death handling. In a battle it is **planted**: X/Z frozen on its physics
bodies, so nothing slides it, while jumps and gravity still work. `CLAUDE.md`, "Extra players",
has every finding behind this.

Actors must never be taken into a room with other players: a Local actor would report health
changes as you.

## Testing

| Keys | Does |
|---|---|
| Ctrl+T | Spawn / despawn a test actor 3 m in front of you, copying you (Ctrl+Shift+T: a puppet) |
| Ctrl+9 | Test actor: mirror (copies you) / bot (holds your pose, keys drive it) |
| Ctrl+P / Ctrl+K | Plant / unplant it; knockback it takes 1 / 0.5 / 0.2 / 0 |
| Ctrl+Shift+R / Ctrl+Shift+P | Record a clip / the test actor plays the newest clip |
| (bot) WASD, Q/E, F | Walk, turn, face-you on/off |
| (bot) Space, Shift, 1-4, Z/X/C/V, T/G | Jump, dash, spawn cube/pillar/wall/ball, straight/uppercut/kick/stomp, disc/explode |
| Ctrl+0 | Print the census - every counted observation |

**Self-test.** Launch the game with `--bossbattles-selftest` (or `=ring` / `=pit`): once the Gym is
up it plays the "Self Test" chart built into the mod, and checks that actors came up simulated
with a body and stood where they were sent, that every projectile spawned, launched and flew,
that a clip played, that the lane had a real length, that the chart picker appeared and answered a
press, that you were moved onto the lane, that a song loaded and played (a metronome it writes;
drop a `selftest-song.ogg` into `UserData/BossBattles/SelfTest` to try a format instead), that the
battle finished, and that nothing failed - then writes `SELFTEST PASS` or the problems to
`UserData/BossBattles/selftest.txt` and quits. The Ring run uses a chart spot, the Pit run the
automatic layout. `--bossbattles-shots` adds screenshots to `UserData/BossBattles/SelfTestShots`.
It needs Steam running (launching `RUMBLE.exe` directly gets no sign-in):

```sh
steam.exe -applaunch 890550 --bossbattles-selftest=ring --bossbattles-shots
```

**Puppet mode** (Ctrl+Shift+T) is the ReplayMod way of making a clone - a stripped remote player
posed by the mod - kept for comparison. It lives in `Actors/Actor.Puppet.cs`; to remove it, delete
that file, `ActorMode.Puppet`, and the lines marked `// PUPPET`.

## Layout

```
BossBattles/
  ModEntry.cs      entry point: settings, keys, wiring
  Core/            shared state (Mod: log, census, actors, battle), log formatting
  Actors/          Actor (simulated player), rig and pose, input, registry; Actor.Puppet.cs
  Battle/          BattleController, projectiles, lane and backstop, arena loading, board
  Patches/         ActorPatches (health, knockback, input, sounds), ArenaPatches
  UI/              settings; UiBridge, the only code that touches the optional UIFramework
  Testing/         TestBench (keyboard test actor), SelfTest
  Flatscreen/      desktop camera and numpad movement - self-contained, can become its own mod
  Services/        game-free and unit tested: Charts (format, loader, runner, lane, ballistics,
                   library, starter charts), Clips (format, sampling, library), Scoring, Logging
BossBattles.Tests/ xUnit: services, patch targets against the interop, Services/ and Flatscreen/ isolation
```

## Building

> Requires the .NET SDK and a RUMBLE install with MelonLoader. UIFramework is optional at runtime
> but referenced at build time (`RUMBLE\Mods\UIFramework.dll`).

```sh
dotnet build -c Release
dotnet test
```

The built DLL is copied into `RUMBLE\Mods`. If Steam is installed elsewhere:
`dotnet build -c Release /p:RumblePath="D:\Games\RUMBLE"`.

> **Target framework is `net6.0` and must stay that way** - MelonLoader hosts .NET 6. The C#
> language version is `latest`.

The build enforces `.editorconfig` and treats warnings as errors.

## Packaging for Thunderstore

```sh
build-thunderstore.bat
```

Edit `Version.props` and `Thunderstore\manifest.json` first, and add a 256x256
`Thunderstore\icon.png`. Dependency strings:

```
LavaGang-MelonLoader-0.7.3
Reverb_and___and_Spice-UIFramework-0.10.2   (optional - list it only if the mod should pull it in)
```
