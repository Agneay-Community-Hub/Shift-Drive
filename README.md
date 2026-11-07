# SHIFT DRIVE

**A racing game where every gear changes the rules.**
Made for [GitHub Game Off 2026](https://itch.io/jam/game-off-2026), whose theme is **SHIFT**.

Shifting gears changes more than your speed:

| Gear | Name | What it does |
|:---:|---|---|
| **1** | GRIP | Normal gravity, strong brakes, sharp steering. |
| **2** | FLIP | **Gravity reverses.** Your car falls up and drives on the ceiling. |
| **3** | SLOW-MO | **Time slows down.** Crushers, blades and music slow to 30% while you keep full control. |
| **4** | OVERDRIVE | Top speed of 330 km/h. **No brakes.** Downshift to slow down. |

The race is a time trial through a neon tunnel in four sectors:

1. **Flip.** The floor drops away, so shift into 2nd and drive on the ceiling. Then flip between the floor and the ceiling to dodge barriers.
2. **Crusher Hall.** Pistons and spinning blades. Shift into 3rd to slow time and slip through.
3. **Overdrive.** A long runway to a ramp over a 90 m chasm. You only clear it in 4th, and 4th has no brakes.
4. **Final sector.** An upside-down slalom over a pit, a second blade gauntlet, then one last jump to the finish.

You earn a Gold, Silver or Bronze medal for your time (45 s, 60 s and 80 s). Your best time is saved.

## Controls

| Action | Keyboard | Gamepad |
|---|---|---|
| Throttle / brake | W / S or ↑ / ↓ | RT / LT (or A / B) |
| Steer | A / D or ← / → | Left stick |
| Shift up | E or Shift | RB / Y |
| Shift down | Q | LB / X |
| Pick a gear directly | 1 – 4 | D-pad |
| Respawn at checkpoint | R | |
| Restart race | Esc | Select |

## Tech

- **Unity 6** (6000.4.3f1) with URP. The project was created and built entirely with the **Unity CLI** (`unity`).
- **No imported art or audio.** The track, car, materials and textures are generated in code. All sound and the synthwave music loop are synthesized at startup (`Synth.cs`).
- The track is a single straight tunnel, so the gear mechanics drive all of the variety.

### Project layout

```
Assets/
  Scripts/
    RaceManager.cs     boot, race state, HUD, audio, post-processing
    CarController.cs   hover-suspension car + the gear rules
    TrackBuilder.cs    builds the whole course from primitives
    ChaseCamera.cs     camera that rolls with the car when gravity flips
    Synth.cs           procedural audio
    Piston/Spinner/Hazard/Debris/WorldClock.cs
    Autopilot.cs       automated test driver (see below)
  Editor/
    ShiftDriveSetup.cs generates materials, scene & settings; build entry points
```

## Building with the Unity CLI

```bash
# Regenerate generated assets + scene (optional, builds do this automatically)
unity run . -- -executeMethod ShiftDriveSetup.Setup

# WebGL (for itch.io)
unity build . --target WebGL --execute-method ShiftDriveSetup.BuildWebGL -o Builds/WebGL

# Windows
unity build . --target StandaloneWindows64 --execute-method ShiftDriveSetup.BuildWindows -o Builds/Windows
```

### Automated playtest

`autotest.sh` builds the Windows player and runs it with `-autopilot <dir>`. A scripted driver picks gears, times the crushers and blades, logs every wreck and jump, takes screenshots and then quits. The current build finishes the course in about 49 s with zero wrecks.

```bash
./autotest.sh Logs/autopilot
```

### Trailer

`Media/ShiftDrive_Trailer.mp4` (30 s, 1080p) is made from real gameplay:

```bash
# 1. Record: the autopilot drives, the game renders at a fixed clock and saves every frame,
#    the per-frame car state, gameplay events and its synthesized audio
./Builds/Windows/ShiftDrive.exe -screen-width 960 -screen-height 540 -screen-fullscreen 0 -trailer <recDir>
# 2. Edit: cut, captions, flash cuts, soundtrack rebuilt from the event log (needs ffmpeg + numpy)
python tools/make_trailer.py <recDir> Media/ShiftDrive_Trailer.mp4
```

## Submitting to Game Off

1. Push this folder to a **public** GitHub repository.
2. Zip the **contents** of `Builds/WebGL` (`index.html` must be at the root of the zip).
3. On itch.io, create a new HTML project, upload the zip, and tick **"This file will be played in the browser"**. Set the viewport to 1280×720 and enable the fullscreen button.
4. Submit it to the jam at <https://itch.io/jam/game-off-2026> before **December 1, 2026 at 13:37 PST**, and include the repository link.
