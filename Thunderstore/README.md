# BossBattles

Survive scripted boss battles down a single lane in **RUMBLE**.

Battles are charts: projectiles telegraphed and thrown down a lane at you, and a boss - a real,
game-simulated player - standing in it, moving and using its moves. Survive to the end; hurting the
boss is the bonus. Battles run solo, wherever you stand or in the Gym, Ring or Pit, optionally to
music. Charts are shared as `.bbchart` packages.

**Needs** MelonLoader 0.7.3 and UIFramework. **Optional:** RumbleReShaded 1.2.0-dev.2 or later, for charts' shader
looks - older versions just skip them.

## Playing

**The chart picker** stands in front of you between battles: the chosen chart - author,
difficulty, arena, length, song and your best - over PREV / START / NEXT. Push its buttons with
either hand. It comes back after each battle with the results.

The settings window (UIFramework) has the chart, an arena override, music volume, where the picker
appears, and buttons to start and stop, reload charts, bring the picker to you and go back to the
Gym. In the Gym, *Chart picker here* fixes the picker where it stands; turn off *Fixed picker spot
in the Gym* to have it always come to you.

- **Arenas.** Starting a chart takes you to its arena: the Ring and Pit load solo, and you are put
  on the chart's spot facing down the lane. *Here* is wherever you stand.
- **Walls.** Invisible walls fence the lane: they stop players, not structures, so shots can cut
  across it. Your **zone** ends at a pale line short of the boss and 3.5 m up.
- **Rules.** A chart can let you spawn only discs, or nothing; your spawn moves come back afterwards.
  Every battle starts on a clear floor.
- **Music** plays through the game's music volume, times the *Music volume* setting. The chart
  follows the song's clock, so it stays on the beat.
- **Scoring.** Surviving is worth 1000; damage on the boss scores 10 a point, times the section's
  multiplier; each point of damage you take costs 25. Rank: S untouched, A up to
  3 damage, B up to 8, C any survival, D defeated. Best scores are kept per chart.

## Charts

Charts live in `UserData/BossBattles/Charts`: a `.json` file, a folder holding `chart.json` with its
song, cover and `clips/`, or a `.bbchart` package. An empty folder gets two starter charts. Charts
reload by themselves when saved, and a chart that does not load says why on the picker.

The format, field by field, is in
[docs/chart-format.md](https://github.com/geodude885/BossBattles/blob/main/docs/chart-format.md).

## Making charts

Turn on **Chart editing** in the settings. The picker then gains:

- **SETUP:** walk a preview of the lane; resize it, move it and the boss, and save it into the chart.
- **RECORD:** perform as the boss, from its end of the lane and to the song. Watch it performed at
  you, then KEEP, AGAIN, REPLAY or DISCARD. **MIME** records movement only, with the chart's
  projectiles flying, for a boss that pretends to throw them.
- **RE-RECORD:** choose a section by the beat, preview it, and record it again; it is spliced in.
- **UNDO:** puts back the chart as it was before its last save.

The settings window also has *New chart* (a commented template that plays as it is, wearing your
look and stones), *Open chart folder*, *Export chart* (a `.bbchart` in `UserData/BossBattles/Exports`)
and *Where am I* (your spot as a chart's arena line).

**The chart editor** is a separate desktop app for Windows, macOS and Linux, the Boss Battles Chart
Editor: the song's waveform under a beat grid, projectiles on a top-down lane, phases, colours, and a
problems list that matches the mod's own checks. Open a chart's folder in it, save, and the game
reloads the chart.

**Sharing.** Drop a `.bbchart` (or `.zip`) into the chart folder to play it. Packages with paths
outside themselves, over 512 files or over 256 MB are refused.
