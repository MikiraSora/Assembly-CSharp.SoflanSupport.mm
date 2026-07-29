# SoflanLogTests Status

## Research

- Complete: bounded inventory, existing conventions, acceptance checklist, and async-writer risk documented.

## Plan

- Complete: every requested behavior maps to a concrete named console test.

## Implement

- Complete: `Program.cs` now exercises enabled DIAG, disabled DIAG, UTF-8 without BOM, and the existing ERROR path with four named test methods.

## Validation

- Clean narrow command: `dotnet run --project tools/SoflanLogTests/SoflanLogTests.csproj -c Release`
- Result: exit code 0, `SoflanLogTests: PASS`.
- Runtime file evidence contained `[DIAG]diagnostic marker enabled` and `[ERROR]mixed modifier marker failure`; the disabled marker was absent.

## Gap and assertion-quality review

- Async gap addressed: `WaitForLog` waits for both queued messages, not merely file existence or non-zero length.
- Level/message coupling strengthened: DIAG and ERROR assertions require the level token immediately followed by the expected message.
- Disabled-state transition covered by toggling the setting false for one unique marker and restoring it before subsequent checks.
- Encoding assertions cover both BOM absence and strict UTF-8 decoding.
- No uncovered requested behavior remains in the bounded target.
