# SoflanLogTests Plan

## Phase 1: Test structure

- Add `DiagnosticWritesDiagLevelAndMessageInRelease` to assert the enabled diagnostic marker has `[DIAG]` and its message.
- Add `DisabledDiagnosticDoesNotWrite` to set `EnableSoflanDiagnosticLog=false`, emit a unique marker, restore the setting, and assert that marker is absent.
- Add `LogUsesUtf8WithoutBom` to decode with strict UTF-8 and reject the UTF-8 BOM prefix.
- Add `ErrorStillWritesErrorLevelAndMessage` to preserve the existing ERROR-level and message assertions.

## Phase 2: Deterministic async observation

- Emit enabled DIAG, disabled DIAG, and ERROR events into one isolated temporary directory.
- Wait until the enabled DIAG and ERROR messages are both present before reading the final byte snapshot.
- Keep existing console-test exit semantics and local dependency stubs.

## Phase 3: Validation and review

- Run `dotnet run --project tools/SoflanLogTests/SoflanLogTests.csproj -c Release`.
- Re-open the final test source and map every acceptance item to its exact named test and assertion.
- Perform inline test-gap and assertion-quality review; record evidence and any fixes in `.testagent/status.md`.
