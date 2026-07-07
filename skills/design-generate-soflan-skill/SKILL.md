---
name: design-generate-soflan-skill
description: Design, modify, generate, or debug Soflan speed-change features for this MA2/MonoMod project. Use when the task mentions Soflan, SFL, HS, FixedSoflan, 变速, 弹跳, 停车, 反向, MA2, note group markers, runtime visual timing, or converting/aligning MajSimai HS commands to SFL behavior.
---

# Soflan Design Workflow

Use this skill to reason about Soflan changes before editing runtime code or chart commands. Soflan here means visual timeline speed changes; it must not alter audio timing or judgment timing unless the user explicitly asks for a separate non-Soflan change.

## Required Context

1. If the current workspace is this Soflan support repository, read `docs/soflan-system.md`, `docs/fixed-soflan.md`, and the relevant code before designing or editing.
2. If those repository docs are unavailable, read [references/soflan-runtime.md](references/soflan-runtime.md) for the runtime facts and [references/soflan-design-checklist.md](references/soflan-design-checklist.md) for the reusable design flow.
3. For implementation tasks, inspect the exact target files and symbols instead of relying only on the bundled references; the repository may have evolved.

## Task Split

Classify the request before proposing a solution:

- Runtime code modification: patching `SoflanManager`, `GameCtrl`, `NoteBase`, Hold/Touch classes, `FixedSoflan`, debug panel, or MonoMod wiring.
- MA2/SFL command design: producing or adjusting `SFL grid unit length speed group` rows and note `#group` / `#groupFspeed` markers.
- MajSimai HS conversion/alignment: converting or reasoning about `<HS...>` commands, interpolation sampling, `HSpeedInterpolationGrid`, and group/marker alignment before they become MA2 `SFL`.

## Design Gate

Before design or code edits, lock these facts explicitly:

- Target note/object type: Tap, Break, Star, Hold, BreakHold, TouchNoteB/C, TouchHold, Slide, or another type.
- Whether FixedSoflan is required, and whether the type is actually supported by FixedSoflan.
- Target visual speed behavior: normal speed, stop, reverse, bounce, fixed-speed Tap, or multi-group behavior.
- Soflan group assignment and marker syntax, including default group `0`.
- Grid/unit/length alignment and whether HS conversion must sample to `SFL`.
- Whether the behavior should be independent of player note speed.
- Verification level: static build only, numeric calculator check, DEBUG panel check, or in-game visual validation.

## Core Rules

- Keep Soflan visual-only: do not move judgment timing or audio timing.
- Treat MA2 `SFL` as the runtime input. MajSimai `<HS...>` is an upstream notation that must be converted to `SFL`; the runtime patch does not parse `<HS...>` text directly.
- Keep FixedSoflan scoped to Tap-family objects unless the implementation is explicitly being expanded and validated for another type.
- Do not turn TouchNoteB/TouchNoteC into ordinary Tap Y-axis movement; preserve their fixed touch-area animation and substitute only the Soflan time axis.
- For Hold/BreakHold, reason about head and tail Soflan times separately.
- For reverse, stop, and bounce behavior, verify visibility registration as well as final movement math; visible-range lookup is part of the feature, not an optimization detail.

## Output Expectations

For design-only tasks, produce a concrete algorithm or chart-command plan, list the files/symbols affected, and state the validation cases. For implementation tasks, make the change, keep existing comments unless there is a clear reason to edit them, and verify with the narrowest meaningful build or static checks available.
