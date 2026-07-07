# Soflan Runtime Reference

This reference summarizes the current Soflan support model. Prefer live repository docs and code when available.

## Scope

Soflan is a visual timeline speed system implemented as a MonoMod patch. It reads MA2 `SFL` rows into `SoflanManager`, then uses the Soflan time axis to drive note visibility, Y position, scaling, and selected original animations. It does not change audio playback or judgment timing.

The runtime depends on `SimpleSoflanFramework.Core`:

- `SoflanListMap`: multiple Soflan lists keyed by group.
- `BpmList`: chart BPM changes.
- `TGridCalculator`: conversion between audio time, `TGrid`, and Soflan Y.

## MA2 SFL Rows

Runtime consumes MA2 rows that begin with `SFL`.

```text
SFL    grid    unit    length    speed    group
```

Fields:

| Field | Index | Meaning |
| --- | ---: | --- |
| `SFL` | 0 | Row type, case-insensitive |
| `grid` | 1 | Start `TGrid` grid |
| `unit` | 2 | Start `TGrid` unit |
| `length` | 3 | Duration, applied as `GridOffset(0, length)` |
| `speed` | 4 | Visual timeline multiplier |
| `group` | 5 | Soflan group; empty means `0` |

Runtime object shape:

```csharp
new Soflan
{
    TGrid = new TGrid(grid, unit),
    EndTGrid = TGrid + new GridOffset(0, length),
    Speed = speed,
    SoflanGroup = groupOrZero
}
```

`speed = 1` is normal visual speed, `speed = 0` is stop, and negative speed reverses the visual timeline. Current parsing logs `parse soflan failed` and stops reading later `SFL` rows when a row cannot be parsed.

## Note Markers

Note records can include one Soflan marker beginning with `#`.

```text
#0
#1
#219
```

Meaning:

- `#N` assigns that note to Soflan group `N`.
- Missing marker means group `0`.
- Multiple `#...` markers on one note are invalid.
- The marker is a patch extension scanned by `SoflanManager.loadNote()` from `record._str`; it is not an original game API.

## FixedSoflan Markers

FixedSoflan appends `F` or `f` after the group.

```text
#1F
#1F600
#F
#F750
```

Rules:

- `#1F` is equivalent to `#1F600`.
- `#F` is group `0` with fixed speed `600`.
- Speeds use invariant-culture float parsing and must be positive.
- Marker errors log and throw `FormatException`.
- FixedSoflan is per note, not global, and does not inherit to child notes or other each notes.

FixedSoflan writes these `Manager.NoteData` fields:

```csharp
public bool isFixedSoflanToUnifiedSpeed;
public float fixedSoflanUnifiedSpeed;
```

## MajSimai HS Boundary

Runtime does not parse MajSimai `<HS...>` commands. HS commands must be converted upstream into MA2 `SFL` rows. Conversion and chart-generation work must align:

- Generated `SFL` intervals to the intended `HSpeedInterpolationGrid`.
- Participating notes to the intended group, such as `#219`.
- Tap-family notes to FixedSoflan markers such as `#219F` or `#219F600` when the behavior must be independent of player note speed.

## Load Flow

Important runtime hooks:

1. `NotesReader.loadMa2Main()` calls Soflan clear before BPM calculation.
2. `SoflanManager.clearAll()` resets Soflan lists, visible-range caches, current Soflan time cache, and note group registration.
3. `SoflanManager.loadComposition()` reads the MA2 file, parses `SFL` rows, and builds `BpmList`.
4. `NotesReader.loadNote()` calls `SoflanManager.loadNote()` before returning each note.
5. `loadNote()` registers `noteIndex -> group` and, when the marker has `F`, writes FixedSoflan fields.

If a chart contains no `SFL` rows, `SoflanManager.containsSoflans()` remains false and note visual patches fall back to original behavior.

## Visibility

Original visibility uses audio time and player note speed. Soflan visibility uses the current Soflan time and group-level visible-range lookup:

```csharp
currentMsec = NotesManager.GetCurrentMsec()
group = getNoteSoflanGroup(note)
currentSoflanTime = GetCurrentSoflanTimeCached(currentMsec, group)
visibleMsec = FixedSoflan.IsEnabledForNote(note) ? FixedSoflanVisibleMsec : num
checkNoteVisible(note, currentMsec, visibleMsec, group, currentSoflanTime)
```

Conceptually it maps `[currentSoflanTime, currentSoflanTime + visibleMsec]` back to one or more original audio-time ranges, then checks whether `note.time.msec` falls inside any range. This is required for stop, reverse, and bounce behavior.

The current implementation rebuilds visible ranges lazily per group per frame and caches current Soflan time per group.

## Soflan Y Integration

`SimpleSoflanFramework` combines BPM and Soflan speed changes into timing points. Between adjacent timing events:

```csharp
scaledLen = CalculateBPMLength(prev.TGrid, cur.TGrid, prev.Bpm.BPM) * prev.Speed
currentY += scaledLen
```

Converting audio time to Soflan Y:

1. Convert audio time to `TGrid`.
2. Find the containing Soflan segment.
3. Measure BPM length from the segment start to the target `TGrid`.
4. Multiply by segment speed.
5. Add the segment start Y.

## Visual Support Matrix

| Object or command | Normal Soflan | FixedSoflan | Notes |
| --- | --- | --- | --- |
| MA2 `SFL` row | Supported | Not applicable | Parsed into Soflan intervals |
| BPM changes | Supported | Supported | From composition BPM list |
| `#N` marker | Supported | Not applicable | Assigns group |
| `#NF` / `#NF600` | Assigns group | Tap-family only | Patch extension |
| Tap / Break / ExTap | Supported | Supported | Y and scale are recalculated |
| Star / BreakStar / ExStar | Supported | Supported for position/scale | Rotation is not separately patched |
| Hold / BreakHold | Supported | Not supported | Head and tail use Soflan times |
| TouchNoteB / TouchNoteC | Supported | Not supported | Preserve fixed touch-area animation |
| TouchHoldC | Not covered by TouchTap logic | Not supported | Requires separate design |
| Slide | No dedicated support | Not supported | Treat as unsupported unless code changed |
| MajSimai `<HS...>` text | Not parsed directly | Not parsed directly | Convert upstream to MA2 `SFL` |

Tap-family FixedSoflan supported kinds are `Begin`, `Break`, `ExTap`, `Star`, `BreakStar`, `ExStar`, `ExBreakTap`, and `ExBreakStar`.

## Algorithms By Object Type

Tap / Break / Star:

- `NoteBase.GetNoteYPosition_soflan()` computes `diffTime = noteSoflanTime - currentSoflanTime`.
- Normal Soflan maps `diffTime` from `[moveStartTime, 0, -moveStartTime]` to `[StartPos, EndPos, outsideY]`.
- FixedSoflan uses progress from declared speed and maps `StartPos -> EndPos -> outsideY`.
- `NoteBase.NoteCheck()` and `BreakNote.NoteCheck()` recalculate scale in Soflan.

Hold / BreakHold:

- Use separate head and tail Soflan times.
- Recalculate head Y, tail Y, body length, endpoint position, and scale.
- FixedSoflan currently does not apply.

TouchNoteB / TouchNoteC:

- Do not use Tap Y-axis movement.
- Keep original semantic phases: hidden, color fade-in, gather to center, then Notice at judgment time.
- Replace only the timing axis with Soflan time.
- `TouchNoteC` inherits `TouchNoteB` display logic, so it does not need a separate patch in the current design.

## FixedSoflan Core

Default unified speed:

```csharp
DefaultUnifiedSpeed = 600f
DefaultMsec = 240000 / unifiedSpeed
```

Movement and scale timing:

```csharp
MoveStartTime = DefaultMsec - MaiBugAdjustMSec
ScaleStartTime = 2 * DefaultMsec - MaiBugAdjustMSec
VisibleMsec = DefaultMsec * 2
MotionProgress = Clamp01((MoveStartTime - diffTime) / (2 * MoveStartTime))
ScaleProgress = Clamp01((ScaleStartTime - absDiffTime) / DefaultMsec)
Y = Lerp(StartPos, EndPos + (EndPos - StartPos), MotionProgress)
```

At fixed speed `600`, `DefaultMsec = 400ms`, `MoveStartTime = 410ms`, `ScaleStartTime = 810ms`, and `VisibleMsec = 800ms`.

## Important Files And Symbols

- `SoflanSupport/SoflanManager.mm.cs`: `loadComposition`, `loadNote`, marker parsing, visibility ranges, Soflan Y conversion.
- `SoflanSupport/FixedSoflan.mm.cs`: FixedSoflan constants, supported kind whitelist, progress functions.
- `Monitor.Game.GameCtrl.mm.cs`: `__SoflanNoteDecision`, current Soflan time cache clear.
- `Monitor.NoteBase.mm.cs`: Tap-family Y and scale logic.
- `Monitor.BreakNote.mm.cs`: Break-specific scale patch.
- `Monitor.HoldNote.mm.cs` and `Monitor.BreakHoldNote.mm.cs`: head/tail and body visuals.
- `Monitor.TouchNoteB.mm.cs`: Touch timing-axis substitution.
- `Manager.NoteData.mm.cs`: FixedSoflan injected fields.
- `Manager.NotesReader.mm.cs` and `MonoModRules.cs`: load-time wiring.
- `SoflanSupport/SoflanPanelBehaviour.mm.cs`: DEBUG monitor and selected Tap data.

## Verification Anchors

- No `SFL`: original behavior.
- `speed = 1`: near-original visual behavior.
- `speed = 0`: stop without losing visibility.
- Negative speed: reverse/re-entry remains visible when appropriate.
- Multi-group: marker group affects only that note.
- Touch: fixed touch-area animation remains intact.
- Hold: head/tail and body follow Soflan time.
- FixedSoflan Tap: visual progress stays consistent across player note speeds.
