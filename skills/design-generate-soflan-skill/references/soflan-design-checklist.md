# Soflan Design Checklist

Use this checklist when a request asks for a new Soflan behavior, a runtime patch, or chart-command generation.

## 1. Classify The Request

- Runtime code change: new object support, marker syntax, caching, visibility, debug display, or MonoMod injection.
- Chart command design: MA2 `SFL` rows, groups, markers, or examples.
- HS conversion/alignment: MajSimai `<HS...>` conversion to MA2 `SFL`, interpolation sampling, and grid alignment.
- Debugging: mismatch between expected visual behavior and current runtime behavior.

## 2. Lock Requirements

State these before designing:

- Target object type and exact game class if known.
- Desired visual behavior: normal speed, stop, reverse, bounce, fixed-speed motion, or per-note group behavior.
- Whether the change must be independent of player note speed.
- Whether FixedSoflan is involved and whether the target type is supported.
- Target group number and marker syntax.
- Required grid alignment and sampling interval.
- Whether judgment timing must remain unchanged.
- Required verification: build, calculator, DEBUG panel, or in-game test.

## 3. Inspect The Right Files

For runtime work, inspect the files that own the behavior:

- Loading and parsing: `SoflanSupport/SoflanManager.mm.cs`, `Manager.NotesReader.mm.cs`, `MonoModRules.cs`.
- Fixed speed: `SoflanSupport/FixedSoflan.mm.cs`, `Manager.NoteData.mm.cs`.
- Visibility: `Monitor.Game.GameCtrl.mm.cs`, `SoflanManager.checkNoteVisible()`.
- Tap-family visuals: `Monitor.NoteBase.mm.cs`, `Monitor.BreakNote.mm.cs`.
- Hold-family visuals: `Monitor.HoldNote.mm.cs`, `Monitor.BreakHoldNote.mm.cs`.
- Touch visuals: `Monitor.TouchNoteB.mm.cs`.
- Debug and validation data: `SoflanSupport/SoflanPanelBehaviour.mm.cs`.
- Offline math and parser parity: `SoflanCalculator/*`.

For chart-command work, inspect the target chart format and any conversion tools, especially `tools/Convert-Ma2ToMajdata.ps1` or MA2 parser/calculator code if relevant.

## 4. Algorithm Template

For each supported object type, design with this sequence:

1. Identify the note group and convert the relevant audio time(s) to Soflan Y.
2. Compute `currentSoflanTime` once per group/frame when possible.
3. Compute `diffTime = noteSoflanTime - currentSoflanTime`.
4. Map visual progress using the target object's original visual semantics.
5. Preserve original judgment checks on real audio time.
6. Route visibility through Soflan visible-range lookup when stop/reverse/bounce can alter entry timing.
7. Add FixedSoflan only if the object type has a validated progress-to-position mapping independent of player note speed.

Object-specific templates:

- Tap-family: use one note Soflan time; map `diffTime` to Y and scale. FixedSoflan can replace move/scale progress with declared-speed progress.
- Hold-family: use head and tail Soflan times; recalculate head Y, tail Y, body length, endpoints, and scale. Do not apply Tap FixedSoflan by default.
- TouchNoteB/C: keep hidden/fade/gather/Notice phases and substitute Soflan time only. Do not convert to Tap-style Y movement.
- New object type: first document original visual semantics, then choose which time axis inputs can safely be replaced by Soflan time.

## 5. MA2/SFL Command Template

When producing MA2 commands:

```text
SFL    grid    unit    length    speed    group
```

Also specify which notes receive markers:

```text
#N
#NF
#NF600
```

Check:

- `speed` is a visual multiplier, not player note speed.
- `length` is the duration from the start `TGrid`.
- Missing group means `0`.
- Notes using a nonzero group must have matching `#N` markers.
- Tap-family notes that must ignore player note speed need `#NF` or `#NFspeed`.
- A group with markers but no SFL usually behaves like no speed change for that group; avoid unintentional missing groups.

## 6. HS Conversion Template

For MajSimai `<HS...>` work:

- Treat `<HS...>` as source notation only.
- Convert to MA2 `SFL`; do not assume the runtime patch reads HS text.
- Align SFL boundaries to the intended `HSpeedInterpolationGrid`.
- Keep BPM changes and grid/unit calculations consistent with the MA2 resolution.
- Attach participating notes to the corresponding Soflan group.
- Add FixedSoflan markers for Tap-family bounce behavior that must be stable across player speeds.

## 7. Edge Cases

Always consider:

- No `SFL` rows: patched visuals should fall back to original behavior.
- `speed = 0`: visual time stops; visibility must not drop notes.
- `speed < 0`: visual time reverses; visibility may map to multiple audio ranges.
- Many groups: avoid per-frame full-group traversal on the hot path.
- Invalid marker: fail clearly, log enough context, and avoid silent downgrade.
- Multiple markers on one note: invalid.
- FixedSoflan marker on unsupported type: group marker can parse, but FixedSoflan math must not run unless deliberately implemented.
- Touch and Hold semantics: do not reuse Tap math without proving the original visual meaning matches.

## 8. Verification Checklist

Use the narrowest checks that match the risk:

- Build: `dotnet build -c Release Assembly-CSharp.SoflanSupport.mm.csproj` and Debug when debug panel code changed.
- Static structure: confirm the intended MonoMod patch method or injected type exists.
- Parser parity: compare `SoflanManager` behavior with `SoflanCalculator` if command parsing is changed.
- Numeric tests: verify expected Y/progress at `diffTime = moveStartTime`, `0`, and `-moveStartTime`.
- Gameplay smoke: no-SFL chart, normal speed `1.0`, stop, reverse, multi-group, Touch, Hold, and FixedSoflan Tap.
- DEBUG panel: confirm selected Tap shows group, speed, `DiffTime`, FixedSoflan flag, fixed speed, motion progress, and scale progress when relevant.

## 9. Common Traps

- Confusing Soflan speed with player note speed.
- Designing only movement and forgetting Soflan visibility registration.
- Treating FixedSoflan as global or as supported for Hold/Touch.
- Letting visual Soflan alter judgment windows.
- Forgetting that `<HS...>` must become `SFL` before runtime.
- Applying Tap Y-axis math to TouchNoteB/C.
- Breaking existing comments during code migration.
- Adding per-frame allocations or full group scans in hot paths.
