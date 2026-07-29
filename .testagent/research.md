# SoflanLogTests Research

## Bounded target inventory

- Production target: `SoflanSupport/PatchLog.mm.cs`, specifically `PatchLog.Diagnostic` and the shared asynchronous file writer.
- Test target: `tools/SoflanLogTests/Program.cs` and its existing `net8.0` console assertion project.
- Scope constraint: only `.testagent/**` and `tools/SoflanLogTests/**` may be changed.

## Existing conventions

- Tests are a self-contained console program, not MSTest/xUnit/NUnit.
- Assertions use the local `Require(bool, string)` helper and failure is reported through process exit code 1.
- The production logger is linked directly into the test project.
- Unity and `Setting` dependencies are represented by narrow local stubs.
- Log output is asynchronous, so assertions must wait for specific expected content rather than only file creation.

## Acceptance checklist

- [x] "Release 下 PatchLog.Diagnostic 会写 `[DIAG]` 和消息"
- [x] "Setting.EnableSoflanDiagnosticLog=false 时不写"
- [x] "日志仍是 UTF-8 without BOM"
- [x] "既有 ERROR 行继续通过"
- [x] Validation uses the narrow Release command for `tools/SoflanLogTests/SoflanLogTests.csproj`.

## Risks and boundaries

- A file-length-only wait can observe an incomplete async batch; wait for both enabled DIAG and ERROR messages.
- Disabled diagnostics must use a unique marker and be asserted absent after the enabled/error batch is observed.
- Release behavior must be demonstrated by a clean `-c Release` run because `PatchLog.WriteLine` is conditional in Debug while `Diagnostic` is intentionally not.
