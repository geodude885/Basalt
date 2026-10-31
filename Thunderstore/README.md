# Basalt

Survive scripted boss battles down a single lane in **RUMBLE**.

Battles are charts: projectiles telegraphed and thrown down a lane at you, and a boss - a real,
game-simulated player - standing in it, moving and using its moves. Survive to the end; hurting the
boss is the bonus. Battles run solo, wherever you stand or in the Gym, Ring or Pit, optionally to
music. Charts are shared as `.bbchart` packages.

**Needs** MelonLoader 0.7.3 and UIFramework. **Optional:** RumbleReShaded 1.2.0-dev.2 or later, for charts' shader
looks - older versions just skip them.

## Playing

**The Basalt menu** stands in front of you between battles: the chosen song - author, difficulty,
arena, length, song and your best - over PREV / START / NEXT, with a slider beside it to pick the
difficulty when a song has more than one. Push its buttons with either hand. It comes back after each
battle with the results.

The settings window (UIFramework) has *Stop battle*, *Menu here* - in the Gym it then stays
where you put it - the chart-making tools, section names on the board, and how strongly a chart's
shader looks apply.

- **Arenas.** Starting a chart takes you to its arena: the Ring and Pit load solo, and you are put
  on the chart's spot facing down the lane - every time, so a restart starts from the same place.
- **Walls.** Invisible walls fence the lane: they stop players, not structures, so shots can cut
  across it. Your **zone** ends at a pale line short of the boss and 3.5 m up.
- **Rules.** A chart can let you spawn only discs, or nothing; your spawn moves come back afterwards.
  Every battle starts on a clear floor.
- **Music** plays through the game's music volume. The chart
  follows the song's clock, so it stays on the beat.
- **Scoring.** Surviving is worth 1000; damage on the boss scores 10 a point, times the section's
  multiplier; each point of damage you take costs 25. Rank: S untouched, A up to
  3 damage, B up to 8, C any survival, D defeated. Best scores are kept per chart.

## Charts

Charts live in `UserData/Basalt/Charts`: a folder holding `chart.json` with its
song, cover and `clips/`, or a `.bbchart` package. Charts reload by
themselves when saved, and a chart that does not load says why on the menu.

The format, field by field, is in
[docs/chart-format.md](https://github.com/geodude885/Basalt/blob/main/docs/chart-format.md).

## Making charts

Turn on **Chart editing** in the settings. The menu then gains:

- **SETUP:** walk a preview of the lane; resize it, move it and the boss, and save it into the chart.
- **RECORD:** perform as the boss, from its end of the lane and to the song. Watch it performed at
  you, then KEEP, AGAIN, REPLAY or DISCARD. **MIME** records movement only, with the chart's
  projectiles flying, for a boss that pretends to throw them. In a chart with more than one actor, a
  button picks whose performance you record; the others perform their kept takes beside you as you go,
  so duets and group dances are recorded one actor at a time.
- **UNDO:** puts back the chart as it was before its last save.

The settings window also has *New chart* (a blank chart with a boss wearing your look and stones).

**The chart editor** is a separate desktop app for Windows, macOS and Linux, Basalt
Editor: the song's waveform under a beat grid, projectiles on a top-down lane, phases, colours, and a
problems list that matches the mod's own checks. Open a chart's folder in it, save, and the game
reloads the chart.

**Sharing.** Drop a `.bbchart` (or `.zip`) into the chart folder to play it. Packages with paths
outside themselves, over 512 files or over 256 MB are refused.
