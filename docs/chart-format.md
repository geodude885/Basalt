# Basalt chart format (format 2)

A chart is one JSON object. Comments (`//`, `/* */`) and trailing commas are allowed; field names
are case-insensitive; fields the mod does not know are ignored, so a chart written for a newer
version still loads in an older one as long as its `format` is one the older version reads.
Saving from the game (lane setup's SAVE, a recording's KEEP) rewrites the chart as plain indented
JSON, so comments and hand layout are not kept.

Point an editor at [chart.schema.json](chart.schema.json) for completion and checking:

```jsonc
{ "$schema": "https://raw.githubusercontent.com/geodude885/Basalt/main/docs/chart.schema.json", ... }
```

## Making one

In game, *New chart* in the settings window makes `Charts/my-chart/chart.json`
from a template that plays as it is, with a comment on every part, and picks it. Edit it and save:
charts reload by themselves between battles, and one that does not load shows its problems on the
picker. Put a `song.wav` and `cover.png` beside it and uncomment their lines. *SETUP* on the picker
walks the lane in place and writes the `"arena"` line. *Export chart* packs the finished chart to share.

## Where a chart lives

| Form | Path | Its song and clips |
|---|---|---|
| File | `Charts/<name>.json` | beside it in `Charts/` |
| Folder | `Charts/<folder>/chart.json` | in the folder; clips in `clips/` |
| Package | `Charts/<name>.bbchart` | inside the package |

A **package** (`.bbchart`) is a zip of a chart folder: `chart.json` at the top (or inside one
top-level folder), the song and cover, and `clips/<clip>.json` for each clip the chart plays. *Export chart* in game
makes one. A package may not contain paths reaching outside itself, more than 512 entries, or more
than 256 MB unpacked.

Recorded clips are kept in the chart's own `clips/`.

## Coordinates and time

- **Lane:** `x` across (0 = the centre line, positive to your right looking down the lane), `y` up
  from the floor, `z` from your end (0) to the far end (`lane.length`). Metres.
- **Arena:** `x`/`z` metres from the arena's centre - the Gym: world coordinates. `facing` is a
  compass bearing in degrees: 0 = world +Z, 90 = world +X. Lane setup's *LANE HERE* writes your own
  spot in exactly this form.
- **Time:** seconds from GO (`t`, phase `start`), or beats (`beat`, `startBeat`) when the chart
  has a `bpm`. With a song, time follows the song: chart time 0 is `music.start` seconds into it.

## Top level

| Field | Type | Default | Meaning |
|---|---|---|---|
| `format` | int | 1 | The format the chart was written for. A newer one than the mod reads is refused with a message to update. |
| `name` | string | "Untitled" | Shown in the picker and on the board; with `author`, the key for best scores. |
| `author` | string | - | Shown in the picker. |
| `difficulty` | string | - | Free text, e.g. Easy / Normal / Hard / Expert. |
| `description` | string | - | One or two lines for the picker. |
| `arena` | object | here | Where it is played - below. |
| `music` | object | - | A song - below. |
| `cover` | path | - | A PNG or JPG beside the chart, shown on the picker (square looks best; up to 16 MB, though 512 px is plenty). |
| `bpm` | number or list | - | Beats per minute, for `beat` / `startBeat` times - or tempo changes: `[ { "beat": 0, "bpm": 120 }, { "beat": 64, "bpm": 140 } ]` (the first at beat 0; each holds until the next). |
| `lane` | object | 12 x 3 | `length` (over 2) and `width` in metres. |
| `zone` | object | 2 m short of the far end, 3.5 m high | Where you may go: `length` along the lane from your end, `height` above the floor. Invisible walls stop you - not structures, not actors. |
| `rules` | object | everything allowed | `spawn`: `all`, `discs` (discs only - for moving) or `none` (moves only). `cleanup`: seconds a structure may sit still in a battle before it breaks (default 3; 0 never) - a wall grounded in front of you does not soak up a whole section. |
| `actors` | array | one at the far end | Below. |
| `phases` | array | one, "Battle" | Below. |
| `events` | array | none | Below. |
| `tracks` | array | none | Recorded performances - below. Needs `"format": 2`. |
| `colours` | object | the mod's own | The lane's colours (`colors` works too) - below. |
| `visuals` | array | none | Shader looks over time, with [RumbleReShaded](https://github.com/Pepuldope/RumbleReShaded) - below. |
| `end` | number | last event + 3 s | When you have survived. |

### `arena`

| Field | Type | Default | Meaning |
|---|---|---|---|
| `map` | `here` / `gym` / `ring` / `pit` | `here` | Starting the chart takes you there. |
| `at` | `{ x, z }` | - | Where you stand. The lane runs `lane.length` from here along `facing`. |
| `facing` | degrees | your facing, or north with `at` | The compass direction the lane runs from you. |

Without `at`, the lane starts wherever you stand - in the Ring and Pit, where the map puts you - and
runs the way you face, or along `facing`. Lane setup in game saves an `at` and `facing` for you. `here`
takes no `at` or `facing`. The player's arena override (setting) replaces the
map; the spot is kept only when the override is the chart's own map.

### `music`

| Field | Type | Default | Meaning |
|---|---|---|---|
| `file` | path | required | A song in the chart's folder or package - a path inside it, no `..`. |
| `start` | seconds | 0 | How far into the song chart time 0 is: skip an intro, or put beat 0 on the first downbeat. |
| `volume` | 0-1 | 1 | The chart's own level, times the player's music volume. |

Songs can be **MP3, OGG Vorbis or WAV** (PCM 8/16/24/32-bit or 32-bit float). The game ships
without Unity's audio decoding, so the mod decodes them itself (NLayer and NVorbis, in `UserLibs`)
while the battle loads; the countdown waits and shows how far it has got. Songs over about six
minutes are kept in mono to save memory. Prefer MP3 or OGG for sharing - a WAV is ten times bigger.

### `actors[]`

| Field | Type | Default | Meaning |
|---|---|---|---|
| `id` | string | `actor`, `actor2`... | What events, phases and `by` refer to. The first is the default. |
| `name` | string | the id | Shown over its head. |
| `at` | `{ x, z, facing }` | far end, centre | Its lane spot; `facing` in degrees, 0 = looking down the lane at you. |
| `look` | string | `you` | `you` (whoever plays), `default`, `defaultMale`, `defaultFemale`, or a character string as the game stores it. A chart made in game stores its maker's, so everyone fights the boss as it was dressed. |
| `shiftstones` | `"you"` or list | `you` | The stones it wears: `"you"` for whoever plays's, or up to two names - `Adamant`, `Charge`, `Flow`, `Guard`, `Stubborn`, `Surge`, `Vigor`, `Volatile` (`"VigorStone"` works too); `[]` for none. A chart made in game stores its maker's. |
| `measurement` | `{ height, armSpan }` | yours | The body's size in metres. KEEPing a clip stores the recorder's, so recorded poses fit the body whoever plays. |

### `phases[]`

| Field | Type | Default | Meaning |
|---|---|---|---|
| `name` | string | "Phase n" | Shown as it starts. |
| `start` / `startBeat` | time | required (first must be 0) | Strictly increasing. |
| `scoreMultiplier` | number | 1 | Multiplies the points for damage on actors. |
| `colours` | object | the chart's | The lane's colours from this phase on, over the chart's own (fields as in [`colours`](#colours); any left out stay as the chart has them). The lane fades to them over a second. `"walls": "none"` hides walls the chart shows. |
| `actor`, `at` | id, spot | - | Moves one actor as the phase starts. |

### `events[]`

Each has a time (`t` or `beat`), optionally `actor` (default: the first), and exactly one of:

| Field | Value | Does |
|---|---|---|
| `projectile` | object, below | A structure telegraphs, then flies at you. |
| `move` | name | The actor does one of its moves: `Straight`, `Uppercut`, `Kick`, `Stomp`, `Explode`, `Flick`, `Parry`, `Jump`, `RockSlide`, `Disc`, `SpawnBall`, `SpawnCube`, `SpawnPillar`, `SpawnWall`, `HoldLeft`, `HoldRight`. |
| `at` | `{ x, z, facing }` | The actor moves to a new spot, planted. |

`projectile`:

| Field | Type | Default | Meaning |
|---|---|---|---|
| `structure` | pool name | required | `Disc`, `Ball`, `Pillar`, `Wall`, `RockCube`, `BoulderBall`, `SmallRock`, `LargeRock`. |
| `from` | `{ x, y, z }` | required | Where it appears: `z` over 0, in the lane or up to 6 m past its far end; `x` up to 6 m outside its sides - shots can come from beyond the boss or across from the side. A grounded one (Disc, cube, Pillar, Wall) needs floor under it there. |
| `to` | `"player"` or `{ x, y }` | `"player"` | Where it arrives, on your end of the lane. |
| `speed` | m/s | required | Sets the flight time; the arc is chosen so it still arrives where aimed. |
| `telegraph` | seconds | 0.5 | How long before the throw it is spawned: it comes up out of the floor at `from` like a player's spawn (with the spawn move's sound and dust), waits there, and at `t` flies with the punch's sound. |
| `grounded` | bool | Pillar, RockCube, Wall: true | Thrown grounded: it rests on the floor in the game's grounded state - more damage - and is driven along the floor at its target (only the target's `x` matters), as a player's straight sends a grounded structure. `false` throws a Pillar, cube or Wall through the air instead; `true` grounds any other structure. A grounded one ignores `from.y`. |
| `by` | actor id | - | Credits it to an actor. |

### `tracks[]`

Written by RECORD / KEEP; one per actor. Each clip plays until the next clip starts, or its own end.

| Field | Type | Default | Meaning |
|---|---|---|---|
| `actor` | id | the first actor | Whose performance. |
| `clips[].t` / `clips[].beat` | time | required | When the clip starts, in chart time; strictly increasing. |
| `clips[].clip` | name | required | The recording, in the chart's `clips/`. |
| `clips[].from` | seconds | 0 | Where in the recording to start - clips keep chart time, so a whole clip is `from` = `t`. |
| `clips[].to` | seconds | its end | Where in the recording to stop. |
| `clips[].mime` | bool | false | Mimed: the actor moves as recorded but runs none of its moves, so nothing spawns - a performance over the chart's own projectiles. Recorded with MIME on the picker: the chart's projectiles fly while you perform at the boss's end, and your own moves stay off. |

A clip holds the performer's head, hands and fingers, path (`"p": [x, z]` per frame, lane
coordinates), facing and the moves made (`"moves": [ { "t", "move" } ]`). While a clip plays the
actor's gesture recognition is off, each move runs at its time, and the actor is held to the path.
Charted `move` events still run on top of a clip; an `at` for an actor in the middle of a clip is
skipped (the path would only drag it back).

### `colours`

Each is `"#RRGGBB"`, or `"#RRGGBBAA"` with opacity. Leave one out for the mod's own colour. Phases can
change them as the battle goes - see `phases[].colours`.

| Field | Default | Meaning |
|---|---|---|
| `edges` | orange | The lines down both sides of the lane. |
| `ends` | yellow | The lines across both ends. |
| `zone` | off-white | The line across the lane where your zone ends. |
| `backstop` | as `edges` | The frame drawn where the wall behind you stands. |
| `walls` | unseen | `"none"` hides them (for a phase). Otherwise shows the invisible walls: a see-through pane on each one's inner face - down both sides, across both ends and over your zone (not across the lane at your zone's end - the floor line marks that) - as high as your zone. Give it an opacity, e.g. `"#66ccff33"` (20%). |

### `visuals[]`

Shader looks, for players with [RumbleReShaded](https://github.com/Pepuldope/RumbleReShaded) (RRS)
installed - everyone else plays the chart as it is. Each cue names an installed RRS **pack** and sets
its sliders; the look fades in over `fade` seconds and holds until the next cue for that pack. Looks
are temporary overrides: the player's own pack settings are never changed, and every pack is theirs
again when the battle ends or stops.

```json
"visuals": [
  { "t": 0,     "pack": "UltraShade", "set": { "Saturation": 0.6, "Vignette": 0.5 }, "fade": 1 },
  { "beat": 64, "pack": "Grayscale",  "set": { "Strength": 1 }, "fade": 2 },
  { "beat": 96, "pack": "Grayscale",  "off": true, "fade": 2 }
]
```

| Field | Type | Default | Meaning |
|---|---|---|---|
| `t` / `beat` | time | required | When the cue starts. |
| `pack` | name | required | An RRS pack's name, as in its menu (`UltraShade`, `Grayscale`, ...). Not installed: skipped. |
| `set` | `{ name: value }` | - | Slider values. A name is the slider's label (`"Chromatic Aberration"`) or property (`"_ChromAb"`, `"ChromAb"`); values are kept within the slider's range. Sliders not named stay as the player has them. |
| `on` | bool | - | `true`: just switch the pack on, with the player's own values. |
| `off` | bool | - | `true`: fade the pack's charted sliders back to the player's, then put it back on or off as they had it. |
| `fade` | seconds | 0 | How long the change takes. |

Players choose how much of it they get with **Chart visuals** in the Basalt settings (0 off, 1
full): it scales how far each slider moves from their own value. RRS's own master switch is
respected - with it off, charts change nothing. RRS draws only in VR.

**Signals for pack authors.** A Basalt battle also tells RRS packs what is happening, whatever
the chart's `visuals`:

| Signal | Kind | When |
|---|---|---|
| `basalt.beat` | event | Every beat of the chart's `bpm`, at your head; strength 1 on the first of each four, 0.6 otherwise; lasts one beat. |
| `basalt.spawn` | event | A projectile comes up, at its spot; lasts its telegraph. |
| `basalt.launch` | event | A projectile is thrown, from its spot. |
| `basalt.hit` | event | You take damage, at your head; strength = damage / 5 (up to 1). |
| `basalt.phase` | event | A new phase starts, at your end of the lane. |
| `basalt.survived` / `basalt.defeated` | event | The battle ends. |
| `_RRS_basalt_running` | global float | 1 during a battle, else 0. When a battle ends, time and beat go back to 0 and health to 1. |
| `_RRS_basalt_time` | global float | Chart time in seconds. |
| `_RRS_basalt_beat` | global float | 0 to 1 through the current beat (charts with a `bpm`). |
| `_RRS_basalt_health` | global float | Your health, 0 to 1. |

A pack reacts to an event by listing its channel in its manifest's `listen`. The floats are shader
globals: declare `float _RRS_basalt_beat;` and read it. (RRS's own `SetGlobal` needs a dotted
key and would publish `_RRS_basalt.beat`, which no shader can declare - so Basalt sets
these itself, with an underscore.) Event and global strengths scale with the player's Chart visuals
setting; at 0 no events are sent.

## Versioning

- **Format 2** added `tracks`. A chart with tracks says `"format": 2`, so an older mod refuses it
  rather than playing it without its recorded actor. Keeping a clip raises the format for you.


`format` changes only when an existing chart would be read differently. New optional fields do not
change it. Planned, not yet read: per-chart visuals (sky, lane and projectile looks, changeable
during a chart).
