# 项目文档索引

本目录以当前工作树中的实现为准，覆盖 MonoMod 运行时补丁、谱面扩展语法、构建部署、配置与调试，以及仓库内的离线工具。若专题文档与历史调查记录冲突，以本页列出的“当前规范文档”和实际源码为准。

## 当前规范文档

| 文档 | 适用场景 |
| --- | --- |
| [Soflan 变速系统](soflan-system.md) | 了解 MA2 `SFL`、note group、支持物件、时间轴、可见性和运行时算法 |
| [FixedSoflan](fixed-soflan.md) | 使用 `#groupFspeed` 固定 Tap 系物件的视觉速度 |
| [构建与部署](build-and-deployment.md) | 初始化依赖、编译 `.mm.dll`、部署到 BepInEx，以及理解 MonoMod 接入点 |
| [配置、日志与调试](configuration-and-debugging.md) | 配置 `mai2.ini`、查看日志、使用暂停快捷键和 DEBUG 面板 |
| [离线工具与验证](tools.md) | 使用计算器、MA2 转 Majdata 脚本、Majdata 验证器和自动测试 |

## 功能总览

| 功能 | 当前状态 | 规范文档 |
| --- | --- | --- |
| 读取 MA2 duration `SFL`，支持正常、加速、减速、停车和负速反向 | 支持 | [Soflan 变速系统](soflan-system.md#ma2-命令支持) |
| note `#group` 分组；未声明时使用 group `0` | 支持，group 为有符号 `int` | [Soflan 变速系统](soflan-system.md#note-group-marker) |
| note `#groupFspeed` 固定视觉速度 | Tap、Break、Star 族支持 | [FixedSoflan](fixed-soflan.md) |
| Tap、Break、Star 的 Y、缩放和 Guide | 支持 | [Soflan 变速系统](soflan-system.md#tap--break--star-视觉算法) |
| Hold、ExHold、BreakHold、ExBreakHold 的头尾、body 和缩放 | 支持 | [Soflan 变速系统](soflan-system.md#hold--breakhold-视觉算法) |
| TouchNoteB / TouchNoteC 的触摸区动画时间轴 | `TouchTap` 支持 | [Soflan 变速系统](soflan-system.md#touchnoteb--touchnotec-视觉算法) |
| TouchHold、Slide 路径和 Slide 内部星标 | 未实现 | [Soflan 变速系统](soflan-system.md#支持矩阵) |
| 玩家 `GetAdjustMSec()` 与可选 MaiBug 视觉偏移 | 支持，按玩家隔离 | [Soflan 变速系统](soflan-system.md#时间轴原理) |
| 反向、停车、折返下的可见性注册 | 支持，按 group 懒构建可见范围 | [Soflan 变速系统](soflan-system.md#可见性算法) |
| P1/P2 独立 BPM、SFL、note map、偏移和缓存 | 支持 | [Soflan 变速系统](soflan-system.md#加载流程) |
| `mai2.ini` 开关与 UTF-8 无 BOM 异步日志 | 支持 | [配置、日志与调试](configuration-and-debugging.md) |
| `P` 键暂停/恢复 | Release 和 Debug 均支持 | [配置、日志与调试](configuration-and-debugging.md#运行时快捷键) |
| `F8` 面板、右键选 Tap、复制诊断数据 | 仅 Debug | [配置、日志与调试](configuration-and-debugging.md#debug-soflan-monitor) |
| NoteGuide 回池时隐藏 each guide，避免残留 | 支持 | [配置、日志与调试](configuration-and-debugging.md#noteguide-池化清理) |
| 多个 `.mm.dll` 包装同一方法时选择正确 IL 链 | 支持已知 MonoMod patch-chain 形状 | [构建与部署](build-and-deployment.md#monomod-接入点) |
| SimpleSoflanFramework 运行时依赖 | 源码内嵌到 `.mm.dll`，无需额外部署 DLL | [构建与部署](build-and-deployment.md#产物与依赖模型) |
| Soflan 数值计算、MA2→Majdata、转换结果核验 | 提供离线工具，适用边界不同 | [离线工具与验证](tools.md) |

## 源码与文档对应关系

| 源码范围 | 职责 | 对应文档 |
| --- | --- | --- |
| `SoflanSupport/SoflanManager.mm.cs`、`SoflanRuntimeTime.mm.cs`、`SoflanVisualTiming.mm.cs`、`TGridHelper.mm.cs` | 按玩家状态、SFL/BPM、时间轴、可见性与缓存 | [Soflan 变速系统](soflan-system.md) |
| `SoflanSupport/SoflanMarkerParser.mm.cs`、`Manager.NoteData.mm.cs`、`Manager.NotesReader.mm.cs` | marker 解析、Fixed 字段、谱面和 note 加载 | [Soflan 变速系统](soflan-system.md)、[FixedSoflan](fixed-soflan.md) |
| `Monitor.NoteBase.mm.cs`、`Monitor.BreakNote.mm.cs` | Tap/Break/Star 视觉与 FixedSoflan | [Soflan 变速系统](soflan-system.md)、[FixedSoflan](fixed-soflan.md) |
| `Monitor.HoldNote.mm.cs`、`Monitor.BreakHoldNote.mm.cs` | Hold 族 Soflan 视觉 | [Soflan 变速系统](soflan-system.md#hold--breakhold-视觉算法) |
| `Monitor.TouchNoteB.mm.cs` | TouchTap Soflan 动画 | [Soflan 变速系统](soflan-system.md#touchnoteb--touchnotec-视觉算法) |
| `Monitor.Game.GameCtrl.mm.cs` | 可见性派发和帧缓存清理 | [Soflan 变速系统](soflan-system.md#播放流程) |
| `SoflanSupport/FixedSoflan.mm.cs`、`MaiBugAdjust.mm.cs` | 固定速度进度与 MaiBug 纯计算 | [FixedSoflan](fixed-soflan.md) |
| `SoflanSupport/Setting.mm.cs`、`PatchLog.mm.cs` | 配置和后台日志 | [配置、日志与调试](configuration-and-debugging.md) |
| `SoflanSupport/GamePlayFumenController.mm.cs`、`SoflanPanelBehaviour.mm.cs`、`Process.GameProcess.mm.cs`、`Monitor.NoteGuide.mm.cs` | 暂停、面板和 Guide 生命周期 | [配置、日志与调试](configuration-and-debugging.md) |
| `MonoModRules.cs`、`Assembly-CSharp.SoflanSupport.mm.csproj` | IL 注入、兼容处理、构建接线 | [构建与部署](build-and-deployment.md) |
| `SoflanCalculator/`、`tools/` | 离线诊断、转换和自动验证 | [离线工具与验证](tools.md) |
| `Dependencies/SimpleSoflanFramework/` | 上游框架子模块；选定源码通过 Shared Project 编入 patch | [构建与部署](build-and-deployment.md#产物与依赖模型) |
| `skills/design-generate-soflan-skill/` | 开发协作流程备份，不进入构建或游戏运行时 | 本页及该目录内 `SKILL.md` |

## 调查、评审与设计记录

以下文档用于保留证据、性能审查或尚未实施的设计，不替代当前规范：

| 文档 | 状态 |
| --- | --- |
| [运行时时间轴偏移调查](soflan-runtime-time-axis-offset-investigation.md) | 已实施修复的调查与验证记录；identity-group bypass 仍未实施 |
| [2026-07 性能复查](performance-review-2026-07.md) | 指定版本快照；问题状态需结合当前源码判断 |
| [大量 group 性能风险](many-soflan-groups-performance-review.md) | 专项快照，P-001/P-002 已处理 |
| [GC 暂停内存风险](gc-paused-memory-risk-review.md) | 专项快照；外部 Core resolver 风险已因源码内嵌而消除 |
| [Slide 内部星标设计访谈](slide-star-soflan-design-interview.md) | 仅设计，明确未进入实现；不能据此认定 Slide 已受支持 |

仓库根目录的 `patch-diff-report.md` 是早期移植记录，其中外置 `SimpleSoflanFramework.Core.dll` 和 `DependencyAssemblyResolver` 部署方案已经失效；当前部署要求以 [构建与部署](build-and-deployment.md) 为准。
