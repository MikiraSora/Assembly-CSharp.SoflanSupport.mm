# Patch Diff Report

> 文档状态：本文起源于早期移植验证，保留目标程序集与 IL 选择记录。当前版本已经把 `SimpleSoflanFramework.Core` 通过 Shared Project 内嵌进 `.mm.dll`，并删除 `DependencyAssemblyResolver`；构建、部署和完整接入点以 [`docs/build-and-deployment.md`](docs/build-and-deployment.md) 为准。

- repo: `D:\yourGameSource`（目标 Unity 工程，路径已脱敏）
- diff: `<base-commit>..<soflan-source-commit>`（私有源仓库提交标识已脱敏）
- target assembly: `F:\yourGame\Package\Sinmai_Data\Managed\Assembly-CSharp.dll` (net462)
- model: **A (target ≡ base build)** — 验证: 目标 DLL 中不含任何 SoflanSupport 字符串 (`SoflanManager` / `GamePlayFumenController` / `checkNoteVisible` / `GetNoteYPosition_soflan` 均为 0 命中), 即目标程序集就是 `<base-commit>` 对应的编译产物。

## 外部依赖

当前 `SoflanSupport/*` 使用的 `OngekiFumenEditor.Core.*` 类型来自 `Dependencies/SimpleSoflanFramework` 子模块。主项目导入 `SimpleSoflanFramework.Core.projitems`，把这些源码直接编入 `Assembly-CSharp.SoflanSupport.mm.dll`。

因此当前运行时没有 `SimpleSoflanFramework.Core.dll` AssemblyRef，也不再注册 `AppDomain.AssemblyResolve`。部署只需要 `.mm.dll`，不需要向 `BepInEx/monomod/` 或 `Sinmai_Data/Managed/` 复制独立 Core DLL。

## 生成产物

- patch 项目: `D:\yourSoflanSupport\Assembly-CSharp.SoflanSupport.mm.csproj` (TFM **net472**)
- patch 程序集: `bin\Release\Assembly-CSharp.SoflanSupport.mm.dll` (输出整洁: 仅 .mm.dll + .pdb)
- 修补产物 (staging 验证用): `staging\Assembly-CSharp_modded.dll`
- IL 检视/应用工具: `D:\yourSoflanTools\{IlInspect,Patcher}` (与 patch 项目分离, 避免其 bin 产物干扰 patch 引用解析)

### MonoMod 引用源

patch 项目引用 **BepInEx 随附的经典 MonoMod** (`F:\yourGame\Package\BepInEx\core\MonoMod.dll`, v20.5.21.5), 而非 NuGet `MonoMod.Patcher 25.0.1`。理由:
- 与游戏运行时一致: BepInEx 环境下 patcher 用的是这个旧版 MonoMod。若 .mm.dll 编译时引用 NuGet 25.0.1 (`MonoMod v25.0.1`), 运行时 patcher 是 `v20.5.21.5`, 会产生 MonoMod AssemblyRef 版本错配。
- 避免 NuGet 25.0.1 引入的 `MonoMod.Backports` / `MonoMod.ILHelpers` 等新架构传递依赖 (用户明确不想要)。

`.mm.dll` 仍以游戏随附的 `MonoMod v20.5.21.5` 和 `Mono.Cecil v0.10.4.0` 为编译目标；当前 Core 源码内嵌，不应再出现 `SimpleSoflanFramework.Core` 外部引用。

> TFM 选 net472 而非 net462: BepInEx 的 `Mono.Cecil.dll` 0.10.4 是 netstandard2.0 编译, 其类型签名引用 `netstandard.dll`; net462 不自带 netstandard facade (net472 才自带)。net472 编译会把 Cecil 的 `netstandard::Object` unify 到 `mscorlib`, 使 .mm.dll 不引入 netstandard 引用 (游戏 Managed 目录无 netstandard.dll, 不能引入)。.mm.dll 仍引用 mscorlib v4.0.0.0, 与 net462 Unity 运行时兼容。

## 方法选择记录

按用户选择采用 **IL 精确插入 + 放弃 DumpCurrent** 策略, SFL 静态数组条目 **跳过**:
- 4 个含方法体中间改动的成员 (`loadMa2Main` / `loadNote` / `UpdateCtrl` / `OnUpdate`) 因 `orig_` 无法表达中间插入, 改用 `MonoModRules` + `PostProcessor` 做 Cecil IL 精确插入。
- `GameCtrl.DumpCurrent` / `NoteBase.DumpCurrent` 访问 `GameCtrl` 的 **private** 字段 (`_xxxObjectList` / `apperMsecTap` / `NoteMng`), 派生 patch 类编译期无法访问, 且用户选择不公开化, 故 **放弃** (仅调试日志功能, 不影响 soflan 核心)。
- `Ma2fileRecordID` 的 SFL 数组条目 (143 号) **跳过**: 经分析 `SoflanManager.loadComposition` 直接从谱面文件读取 `SFL` 行, 该数组条目对功能无影响; base 的 `loadMa2Main` 对未知记录仅置 `flag2=false` 不中断加载。复现需脆弱的 144 项静态构造重写, 收益为零。

## Generated patches

| source file | target type | change kind | pattern used | patch file |
|---|---|---|---|---|
| Manager/NoteData.cs | Manager.NoteData | add field + clear 末尾插入 + add method | orig_ clear + patch_ 新成员 | Manager.NoteData.mm.cs |
| Monitor/NoteBase.cs | Monitor.NoteBase | add fields + Initialize/NoteCheck/EndNote/GetNoteYPosition 末尾/开头插入 + add methods | orig_ 包装 + patch_ 新成员 | Monitor.NoteBase.mm.cs |
| Manager/NotesReader.cs | Manager.NotesReader | loadMa2Main 中间插入(clearAll/loadComposition) + loadNote 末尾插入 | MonoModRules IL 插入 | Manager.NotesReader.mm.cs + MonoModRules.cs |
| Monitor/Game/GameCtrl.cs | Monitor.Game.GameCtrl | add field + UpdateCtrl 中间插入(可见性派发/缓存清空/失败日志) | MonoModRules IL 插入 | Monitor.Game.GameCtrl.mm.cs + MonoModRules.cs |
| Process/GameProcess.cs | Process.GameProcess | OnUpdate 起始插入 GamePlayFumenController.Update | MonoModRules IL 插入 | Process.GameProcess.mm.cs + MonoModRules.cs |
| SoflanSupport/Setting.cs | (新增类型) | new type | patch assembly 新类型, 整体复制 | SoflanSupport/Setting.mm.cs |
| SoflanSupport/PatchLog.cs | (新增类型) | new type | 同上 | SoflanSupport/PatchLog.mm.cs |
| SoflanSupport/SoflanManager.cs | (新增类型) | new type | 同上 | SoflanSupport/SoflanManager.mm.cs |
| SoflanSupport/GamePlayFumenController.cs | (新增类型) | new type (有偏差, 见下) | 同上 | SoflanSupport/GamePlayFumenController.mm.cs |
| SoflanSupport/TGridHelper.cs | (新增类型) | new type | 同上 | SoflanSupport/TGridHelper.mm.cs |

## MonoModRules IL 插入锚点 (基于真实 base IL 检视)

| 方法 | 插入点 | 锚点 | 插入序列 |
|---|---|---|---|
| `NotesReader.loadMa2Main` | `calcBPMList` 调用前 | `call calcBPMList` (唯一) | `ldarg.0; ldfld _playerID; call __SoflanClearPlayer(playerId)` |
| `NotesReader.loadMa2Main` | `calcTotal` 调用后 | `callvirt calcTotal` (唯一) | `ldarg.2; ldarg.0; ldarg.0; ldfld _playerID; call __SoflanLoadComposition(records, this, playerId)` |
| `NotesReader.loadNote` | 末尾 `ret` 前 | 末尾 `ret` (单 ret) | `ldloc.0(result 保留); ldloc V_2(noteData); ldarg.1(rec); ldarg.0(this); ldarg.0; ldfld _playerID; call __SoflanLoadNote; ret` |
| `GameCtrl.UpdateCtrl` | `ldfld UserOption` 后 | `ldfld GameScoreList::UserOption` (唯一) | `ldarg.0; ldarg.0; ldfld monitorIndex; callvirt __SoflanClearCache(monitorIndex)` |
| `GameCtrl.UpdateCtrl` | 原 msec 可见性检查前 | `call GetCurrentMsec`(匹配 `GetCurrentMsec;ldloc;ldflda time;get_msec;ldloc;sub;blt.un` 模式) | `ldarg.0; ldloc note; ldloc num; ldarg.0; ldfld monitorIndex; callvirt __SoflanNoteDecision(note,num,monitorIndex); ldc.i4.1 beq AFTER; ldc.i4.2 beq CONTINUE` |
| `GameProcess.OnUpdate` | 方法起始 | 首条指令 | `call __SoflanUpdateGamePlayFumenController()` |

**派发控制流说明** (`UpdateCtrl` 可见性): `__SoflanNoteDecision` 返回 `0`=非 soflan(fall-through 走原 msec 检查), `1`=soflan 可见(跳 `AFTER`=原检查通过后的 RegistNote 处理), `2`=soflan 不可见(跳 `CONTINUE`=MoveNext)。`AFTER`=`blt` 后一条, `CONTINUE`=`blt` 跳转目标(MoveNext), 与原 `continue` 目标一致。

## Skipped / 偏差

| source file | member / change | 原因 |
|---|---|---|
| Manager/Ma2fileRecordID.cs | `s_Ma2fileRecord_Data` 数组新增 143 号 SFL 条目 | 功能无效(SoflanManager 直接读谱面文件); 复现需脆弱的 144 项 cctor 重写 |
| Monitor/Game/GameCtrl.cs | `GameCtrl.DumpCurrent` (新增 internal 方法) | 访问 private 字段 `_xxxObjectList`/`apperMsecTap`/`NoteMng`, 不公开化下派生 patch 类无法编译 |
| Monitor/NoteBase.cs | `NoteBase.DumpCurrent` (新增 virtual 方法) | 同上(虽 NoteBase 成员 protected, 但其调用 GameCtrl.DumpCurrent, 随 GameCtrl.DumpCurrent 一并放弃) |
| SoflanSupport/GamePlayFumenController.cs | L 键 DumpCurrent 调试路径 | 依赖被放弃的 GameCtrl.DumpCurrent; 仅保留 P 键暂停。类型由 `internal` 改为 `public` 以满足 `Singleton<T>` 的 `new()` 约束 |
| Process/GameProcess.cs | `OnUpdate` 的 Play case 内插入 | 改为方法起始每帧调用(功能等价: 每帧检查 P 键暂停)。OnUpdate 含 1597 行 IL 的巨型 switch, 在 Play case 内精确锚点脆弱 |

## 其他 (非 IL 可映射)

| source file | change | 原因 |
|---|---|---|
| `Assembly-CSharp.SoflanSupport.mm.csproj` | 导入 SimpleSoflanFramework Shared Project | 构建接线，非 IL；Core 源码直接内嵌进 patch |
| Monitor/Game/GameCtrl.cs, Monitor/NoteBase.cs, Manager/NotesReader.cs 等 | 大量空白/缩进重排 | `git diff -w` 后非实质性变更, 无需 patch |

## 验证结果

修补产物 `Assembly-CSharp_modded.dll` 经结构化验证全部通过:
- `MonoMod.WasHere` 标记存在 ✓
- 5 个新增 SoflanSupport 类型全部注入 ✓
- 7 处 IL 插入调用全部落位且实参/形参数量匹配、声明类型正确 ✓
- `beq` 跳转目标均在方法体内 ✓
- `orig_` 包装结构正确 (Initialize/NoteCheck/EndNote/GetNoteYPosition/clear) ✓
- patch 项目输出整洁 (仅 `Assembly-CSharp.SoflanSupport.mm.dll` + `.pdb`) ✓

## 应用方式

将 `Assembly-CSharp.SoflanSupport.mm.dll` 放入 `BepInEx\monomod\`。BepInEx 的 MonoMod.Loader 在游戏启动时自动应用 `.mm.dll`；不要再部署旧版 `SimpleSoflanFramework.Core.dll`。

离线验证 (本报告所用): 用 BepInEx 随附的经典 MonoMod 应用 (验证脚本 `D:\yourSoflanTools\Patcher`):
```
# 经典 MonoModder API (v20.5.21.5): Read -> ReadMod -> MapDependencies -> AutoPatch -> Write
# 等价 CLI (若环境提供): MonoMod.Patcher.exe Assembly-CSharp.dll Assembly-CSharp.SoflanSupport.mm.dll Assembly-CSharp_modded.dll
# 注意: staging 目录须含 MonoMod.dll/MonoMod.Utils.dll/Mono.Cecil.*.dll (因 .mm.dll 引用 MonoMod),
#       否则 MapDependencies 抛 RelinkTargetNotFoundException: .mm.dll -> MonoMod not found.
```
（若目标目录含 `Assembly-CSharp.pdb`, PDB 写入器对 IL 修改后的局部作用域可能报错; 应用前移除目标 `.pdb` 或用无符号模式。）

离线生成的 `Assembly-CSharp_modded.dll` 只用于结构化验证；直接替换游戏程序集前必须自行备份并确认目标版本一致。

> 注: 本报告及 patch 均为离线结构化验证 (Cecil 静态校验 + IL 语义核对), 未在游戏进程中实跑。运行时行为(尤其 soflan 可见性数值与 NoteBase 缩放/Y 位置计算)需在游戏内进一步验证。
