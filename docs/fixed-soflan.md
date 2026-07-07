# FixedSoflan 设计与使用说明

## 目标

FixedSoflan 是 Soflan 支持里的一个按物件声明的视觉固定速度模式。

普通 Soflan 下，Tap 系物件的可见窗口、缩放和移动进度仍会使用当前玩家设置的物件速度参与计算。因此同一条弹跳/停车变速命令，在低速和高速玩家设置下可能出现不同视觉表现，例如高速下物件还没有靠近判定线就开始弹跳。

FixedSoflan 的目标是把这类物件的 Soflan 视觉进度固定到一个声明速度上计算，再把进度映射到游戏原本的显示坐标。默认声明速度是 `600`，也就是 `#1F` 等价于 `#1F600`。

FixedSoflan 只影响 Soflan 视觉显示逻辑，不改变判定时间。`NoteCheck()` 的判定窗口仍按真实音频时间执行。

## 语法

Soflan 组声明仍使用 note record 中以 `#` 开头的 marker。FixedSoflan 在原有组号后追加 `F` 或 `f`。

| Marker | 含义 |
| --- | --- |
| `#1` | 使用 Soflan group `1`，不启用 FixedSoflan |
| `#1F` | 使用 Soflan group `1`，启用 FixedSoflan，固定速度 `600` |
| `#1F600` | 使用 Soflan group `1`，启用 FixedSoflan，固定速度 `600` |
| `#1F750.5` | 使用 Soflan group `1`，启用 FixedSoflan，固定速度 `750.5` |
| `#F` | 使用 Soflan group `0`，启用 FixedSoflan，固定速度 `600` |
| `#F600` | 使用 Soflan group `0`，启用 FixedSoflan，固定速度 `600` |
| `#0F600` | 使用 Soflan group `0`，启用 FixedSoflan，固定速度 `600` |

说明：

- `F` 和 `f` 都支持。
- `F` 前面的组号为空时，组号按 `0` 处理。
- `F` 后面的速度为空时，速度按 `FixedSoflan.DefaultUnifiedSpeed`，也就是 `600` 处理。
- 速度使用 invariant culture 浮点解析，必须是正数。
- marker 是按物件声明的，不是全局配置。
- marker 不会自动继承或扩散给其它物件、child note、slide child 或 each 中的其它 note。

示例：

```text
#219
#219F
#219F600
#219F750.5
```

对于弹跳 Soflan 命令，实际使用时需要让参与弹跳显示的 Tap 系物件挂到对应 group，并在 marker 上加 `F`，例如 `#219F` 或 `#219F600`。否则物件仍会按玩家当前物件速度计算显示窗口和移动进度。

## 语法错误策略

解析发生在 `SoflanManager.loadNote()`。每个 note 加载时会先重置 FixedSoflan 字段，再扫描该 note record 中以 `#` 开头的 marker。

以下情况会写入 `PatchLog` 并抛出 `FormatException`：

- 同一个 note record 中出现多个 Soflan marker。
- marker 为空，例如 `#`。
- marker 内部存在空白，例如 `# 1`、`#1 F`、`#1F 600`。
- 组号不是整数，例如 `#A`、`#AF600`。
- 固定速度不是正数，例如 `#1F0`、`#1F-600`、`#1FNaN`、`#1FInfinity`。

当前实现选择“写日志并抛异常”，而不是静默降级。这样谱面语法错误会在加载阶段暴露，避免运行时表现变成难以定位的普通 Soflan。

## 数据字段

FixedSoflan 状态直接注入到 `Manager.NoteData`：

```csharp
public bool isFixedSoflanToUnifiedSpeed;
public float fixedSoflanUnifiedSpeed;
```

`patch_NoteData.clear()` 会把它们重置为：

```csharp
isFixedSoflanToUnifiedSpeed = false;
fixedSoflanUnifiedSpeed = FixedSoflan.DefaultUnifiedSpeed;
```

`SoflanManager.loadNote()` 解析 marker 后会写入：

- `registerNoteIndexToSoflanGroupMap[noteData.indexNote] = soflanGroup`
- `noteData.isFixedSoflanToUnifiedSpeed = isFixedSoflan`
- `noteData.fixedSoflanUnifiedSpeed = fixedSoflanUnifiedSpeed`

## 支持范围

第一版 FixedSoflan 只支持 Tap 系物件：

```csharp
NotesTypeID.Def.Begin
NotesTypeID.Def.Break
NotesTypeID.Def.ExTap
NotesTypeID.Def.Star
NotesTypeID.Def.BreakStar
NotesTypeID.Def.ExStar
NotesTypeID.Def.ExBreakTap
NotesTypeID.Def.ExBreakStar
```

`FixedSoflan.IsEnabledForNote(note)` 同时检查三件事：

- note 不为空。
- `isFixedSoflanToUnifiedSpeed == true` 且 `fixedSoflanUnifiedSpeed > 0`。
- note kind 在 Tap 系白名单中。

不支持范围：

- Hold / BreakHold
- Touch / TouchHold
- Slide 相关派生视觉
- 没有 SFL 数据的谱面

如果非支持类型写了 `#1F`，marker 仍会被解析并登记 Soflan group，但 FixedSoflan 算法不会介入该物件的显示计算。它会继续按当前类型已有的 Soflan 逻辑或原版逻辑运行。

## 核心算法

FixedSoflan 的统一速度不是玩家设置速度，而是 note 自己声明的固定视觉速度。

默认值：

```csharp
DefaultUnifiedSpeed = 600f
```

基础时间窗：

```csharp
DefaultMsec = 240000 / unifiedSpeed
```

与原版 Tap 移动一致的 Mai bug 修正：

```csharp
speedRatio = unifiedSpeed / 150
MaiBugAdjustMSec =
    (speedRatio - 1) * (-0.5 / speedRatio) * 1.6 * 1000 / 60
```

移动和缩放时间点：

```csharp
MoveStartTime = DefaultMsec - MaiBugAdjustMSec
ScaleStartTime = 2 * DefaultMsec - MaiBugAdjustMSec
VisibleMsec = DefaultMsec * 2
```

Soflan 时间差：

```csharp
diffTime = noteSoflanTime - currentSoflanTime
absDiffTime = Abs(diffTime)
```

其中：

- `diffTime > 0` 表示还没到该 note 的 Soflan 判定时间。
- `diffTime == 0` 表示当前 Soflan 时间到达该 note。
- `diffTime < 0` 表示已经越过该 note 的 Soflan 时间。

移动进度：

```csharp
MotionProgress = Clamp01((MoveStartTime - diffTime) / (2 * MoveStartTime))
```

坐标映射：

```csharp
outsideY = EndPos + (EndPos - StartPos)
Y = Lerp(StartPos, outsideY, MotionProgress)
```

因此进度和位置关系为：

| Soflan 时间差 | 进度 | Y 位置 |
| --- | ---: | --- |
| `diffTime == MoveStartTime` | `0` | `StartPos`，通常是 `120` |
| `diffTime == 0` | `0.5` | `EndPos`，通常是 `400` 判定线 |
| `diffTime == -MoveStartTime` | `1` | `outsideY`，通常是 `680` |

缩放进度：

```csharp
ScaleProgress = Clamp01((ScaleStartTime - absDiffTime) / DefaultMsec)
```

对于默认固定速度 `600`：

```text
DefaultMsec = 400ms
MaiBugAdjustMSec = -10ms
MoveStartTime = 410ms
ScaleStartTime = 810ms
VisibleMsec = 800ms
```

也就是说，固定 `600` 速度时，Tap 系物件会用 `600` 的时间窗计算视觉进度；到达 `diffTime == 0` 时映射到判定线位置。

## 运行时接入点

### 可见性

`Monitor.Game.GameCtrl.__SoflanNoteDecision()` 在 Soflan 谱面里决定物件是否可见。

普通 Soflan 使用玩家速度传入的 `num` 作为可见时间窗。FixedSoflan 物件改用：

```csharp
FixedSoflan.GetVisibleMsec(FixedSoflan.GetUnifiedSpeed(note))
```

这样可见性窗口不会因为玩家物件速度不同而改变。

### Tap 系移动和缩放

`Monitor.NoteBase.Initialize()` 缓存：

- `SoflanManager`
- 是否存在 Soflan
- note 所属 Soflan group
- note 的 Soflan 时间
- FixedSoflan flag 和固定速度

`Monitor.NoteBase.GetNoteYPosition()` 只在 `isInSoflan && checkSupportSoflan()` 时进入 Soflan 分支。当前 `checkSupportSoflan()` 只允许 base type 为 `Tap`。

`Monitor.NoteBase.GetNoteYPosition_soflan()` 中：

- 普通 Soflan 继续使用 `DefaultMsec`、`GetMaiBugAdjustMSec()` 和原有 `MathUtils.MapValue()`。
- FixedSoflan 使用 `FixedSoflan.GetMoveStartTime()`、`GetScaleStartTime()`、`GetMotionProgress()`、`GetScaleProgress()` 和 `GetYFromMotionProgress()`。

当前实现保留 `sign = 0`，不恢复玩家速度 offset。也就是说 FixedSoflan 的最终 Y 位置由固定速度进度映射到 `StartPos -> EndPos -> outsideY`，不会再被玩家物件速度二次偏移。

### BreakNote 缩放

`Monitor.BreakNote` 有自己的 `NoteCheck()` patch。它同样缓存 FixedSoflan flag 和固定速度，并在 Soflan 中按 FixedSoflan 的 `ScaleProgress` 重算 `NoteObj.transform.localScale`。

Break 的移动仍依赖 Tap base 的 Soflan Y 逻辑。Break 特有的效果闪光没有被 FixedSoflan 修改。

### 调试面板

DEBUG 构建下，`SoflanPanelBehaviour` 的右键选中 Tap 数据中增加了：

- `IsFixedSoflanToUnifiedSpeed`
- `FixedSoflanUnifiedSpeed`
- `FixedMotionProgress`
- `FixedScaleProgress`

面板和复制文本中会显示：

```text
Fixed: True/False  FixedSpd: ...
FixedMoveP: ...  FixedScaleP: ...
```

这用于验证某个 Tap 是否真的进入 FixedSoflan，以及当前帧的固定速度进度是否符合预期。

## 与弹跳 Soflan 命令的关系

FixedSoflan 解决的是“同一条 Soflan 视觉命令在不同玩家物件速度下表现不同”的问题。

它不会自动生成或重排 `<HS...>` 弹跳命令。弹跳命令本身仍要按谱面 BPM、Soflan group 和 MajSimai 的 `HSpeedInterpolationGrid` 对齐。

实际使用时需要同时满足：

1. 谱面里有对应的 SFL/HS 变速组。
2. 参与弹跳的 Tap 系物件使用对应 group，例如 `#219`。
3. 如果希望不受玩家物件速度影响，需要写成 `#219F` 或 `#219F600`。
4. 弹跳命令的采样对齐要按目标 grid 生成。当前讨论里的默认要求是按 `32` 对齐。

如果只写弹跳命令但不写 `F` marker，玩家物件速度仍会影响可见窗口、缩放和移动进度，高速玩家仍可能看到物件提前弹跳。

## 行为边界

- FixedSoflan 只在 `SoflanManager.containsSoflans()` 为 true 的谱面里生效。
- 无 SFL 谱面里，即使 note record 写了 `#F`，显示逻辑仍回到原版。
- 判定窗口不变，仍按真实音频时间。
- Star 旋转没有被修改。
- Break 特效闪光没有被修改。
- Guide 的显示仍走当前 Soflan Tap 视觉流程，只是 FixedSoflan 物件的 scale/move 进度来源变为固定速度。
- TouchNoteB / TouchNoteC 的 Soflan 支持是独立逻辑，当前 FixedSoflan 不覆盖 Touch。

## 验证清单

实现后需要至少检查以下内容：

```powershell
dotnet build -c Release Assembly-CSharp.SoflanSupport.mm.csproj
dotnet build -c Debug Assembly-CSharp.SoflanSupport.mm.csproj
```

静态结构检查：

- `Manager.NoteData` 含 `isFixedSoflanToUnifiedSpeed` 和 `fixedSoflanUnifiedSpeed`。
- `SoflanSupport.FixedSoflan` 被注入到目标程序集。
- `SoflanManager.loadNote()` 会调用 FixedSoflan marker parser，并在错误时调用日志和抛异常路径。
- `GameCtrl.__SoflanNoteDecision()` 中 FixedSoflan 物件使用 `FixedSoflan.GetVisibleMsec()`。
- `NoteBase.GetNoteYPosition_soflan()` 调用 `GetMotionProgress()` 和 `GetYFromMotionProgress()`。
- `NoteBase.NoteCheck()` 和 `BreakNote.NoteCheck()` 调用 `GetScaleProgress()`。
- DEBUG 面板含 FixedSoflan 的选中 note 字段和显示文本。

数值验证建议：

- 固定速度 `600` 时，`diffTime = MoveStartTime / 0 / -MoveStartTime` 应分别映射到 `StartPos / EndPos / outsideY`。
- 同一个 FixedSoflan Tap，在不同玩家物件速度下，FixedSoflan 的 `MotionProgress` 和 `ScaleProgress` 应保持一致。
- 普通 Tap/Hold/BreakHold/Touch 谱面不应因为 FixedSoflan 逻辑产生回退。

游戏内验证建议：

- 无 SFL 谱面：Tap 行为与原版一致。
- 有 SFL 且 marker 为 `#N`：Tap 走普通 Soflan。
- 有 SFL 且 marker 为 `#NF`：Tap 使用默认固定速度 `600`。
- 有 SFL 且 marker 为 `#NF750`：Tap 使用固定速度 `750`。
- Hold/Touch/TouchHold 写 `#NF` 时，不进入 FixedSoflan 算法。
- 语法错误 marker 会写日志并中断加载，而不是静默降级。
