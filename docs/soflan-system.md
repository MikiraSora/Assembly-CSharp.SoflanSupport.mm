# Soflan 变速系统说明

本文是当前运行时行为的规范说明。构建部署见 [构建与部署](build-and-deployment.md)，配置、日志、暂停快捷键和 DEBUG 面板见 [配置、日志与调试](configuration-and-debugging.md)，仓库功能入口见 [文档索引](README.md)。

## 定位

本项目的 Soflan 支持是一个 MonoMod patch。它把 MA2 谱面里的变速区间读入 `SoflanManager`，再在游戏播放时用 Soflan 时间轴驱动物件的可见性和视觉位置。

这里的 Soflan 指“视觉时间轴变速”，不是改变音频播放速度，也不是改变判定时间。判定仍按原始音频时间执行；Soflan 只改变物件何时出现、如何移动、如何缩放，以及某些类型的原版动画时间轴。

当前实现使用 `SimpleSoflanFramework.Core` 的源码；它通过 Shared Project 直接编入 patch `.mm.dll`，运行时不需要额外部署 Core DLL：

- `SoflanListMap` 按 group 管理多条 Soflan 区间。
- `BpmList` 管理谱面 BPM 变化。
- `TGridCalculator` 在音频时间、TGrid 和 Soflan Y 坐标之间转换。

## 功能

当前变速系统提供以下能力：

- 从 MA2 文件读取 `SFL` 行，建立 Soflan 变速区间。
- 从 note record 读取 `#group` marker，把单个物件挂到 Soflan group；group 支持有符号 `int`。
- 支持每个物件独立选择 group，未声明时默认 group `0`。
- 播放时按当前音频时间计算当前 Soflan 时间。
- 在 `GameCtrl.UpdateCtrl` 的物件注册前替换可见性判断，让反向、停车、弹跳等变速下的物件可以正常被创建。
- 对 Tap / Break / Star 系物件重算 Soflan 下的 Y 位置、guide 缩放和本体缩放。
- 对 Hold / BreakHold 重算头尾位置、body 长度、端点位置和缩放。
- 对 TouchNoteB / TouchNoteC 使用 Soflan 时间轴驱动原版 Touch 显示动画。
- 提供 DEBUG 调试面板，显示当前 group 速度、选中 Tap 的 Soflan 计算数据。
- 支持 FixedSoflan 扩展，使指定 Tap 系物件按固定视觉速度计算进度，避免玩家物件速度影响弹跳表现。
- 提供 Release/Debug 都可用的 `P` 键暂停/恢复，并在 NoteGuide 回池时清除 each guide 残留。这两项运维行为不依赖谱面是否含 SFL。

## mai2.ini 开关

`GetMaiBugAdjustMSec()` 是否参与 Soflan 视觉时间轴由游戏目录下的 `mai2.ini` 控制：

```ini
[Patches]
EnablePatchLog=1
EnableSoflanMaiBugAdjust=1
```

- `EnablePatchLog` 只控制 Debug INFO 日志；ERROR 在 Release/Debug 中都不受它控制。
- 配置缺失时默认启用。
- `1` 表示启用，`0` 表示关闭；游戏的 `IniFile` 按整数读取该布尔项。
- 配置在游戏启动后首次使用 Soflan 视觉时读取一次；修改后需要重启游戏。
- 启用时，Tap / Break / Star / Hold / BreakHold 的 MaiBug 音频毫秒偏移会经过 Soflan 积分。
- 关闭时，上述物件和 `GameCtrl` 可见性注册统一使用 `0ms` 偏移；Touch 原本就不使用该偏移，因此不受开关影响。
- 无论开关状态如何，运行时当前时间都会先减去该玩家快照的 `GetAdjustMSec()`；该基础时间轴转换不属于 MaiBug 开关。
- 该开关只改变 Soflan 视觉显示，不改变音频时间、判定时间或原版无 SFL 谱面的逻辑。

完整开关、日志级别和读取时机见 [配置、日志与调试](configuration-and-debugging.md#mai2ini)。

## MA2 命令支持

### SFL 行

运行时直接读取 MA2 文件中以 `SFL` 开头的行。

当前解析格式按 tab 字段读取：

```text
SFL    unit    grid    length    speed    group
```

字段含义：

| 字段 | 位置 | 含义 |
| --- | ---: | --- |
| `SFL` | 0 | 行类型，大小写不敏感 |
| `unit` | 1 | 起点 TGrid 的 unit；MA2 中通常表示 bar |
| `grid` | 2 | 起点 TGrid 的 bar 内 grid |
| `length` | 3 | 持续长度，作为 `GridOffset(0, length)` 加到起点 |
| `speed` | 4 | 变速倍率 |
| `group` | 5 | Soflan group，可空；为空时使用 group `0` |

解析后的运行时对象：

```csharp
new Soflan
{
    TGrid = new TGrid(unit, grid),
    EndTGrid = TGrid + new GridOffset(0, length),
    Speed = speed,
    SoflanGroup = groupOrZero
}
```

说明：

- `speed` 是倍率，不是玩家物件速度。例如 `1.0` 表示正常视觉速度，`0` 表示停车，负数表示视觉时间轴反向。
- `group` 由 `int.Parse()` 读取，可使用有符号 32 位整数；空字段才回退为 `0`。
- 当前代码使用 `float.Parse()` 和 `int.Parse()` 解析字段；字段非法时该行解析失败。
- loader 不额外校验 length、speed 的有限性或区间合理性。`0` 和负 speed 是合法功能，但谱面作者仍应避免 NaN、Infinity、无效负 length 等可被基础 parser 接受却没有可靠视觉语义的数据。
- `SFL` 解析失败时会写日志 `parse soflan failed` 并停止继续读取后续 `SFL` 行。
- 当前 loader 只直接消费 MA2 的 `SFL` 行，不直接解析 MajSimai 的 `<HS...>` 命令。

### Note group marker

note record 中可以加入 `#groupFspeed` marker，把该物件挂到 Soflan group。marker 可位于混合扩展字段中的任意位置，读取器使用正则提取连续的 Soflan token，不要求整个字段以 `#` 开头。

支持语法：

```text
#0
#1
#219
#+12
#-2
!m#1
#1!m!y
!y!m#1
```

含义：

- `#1` 表示该 note 使用 Soflan group `1`。
- group 语法是 invariant-culture 的有符号十进制 `int`，所以 `#+12` 和 `#-2` 也会被运行时接受；谱面中的 `SFL` group 必须使用同一数值才能对应。
- 未写 marker 的 note 使用 group `0`。
- 同一个 note record 只能匹配到一个 Soflan marker。
- `!m`、`!y` 等以 `!` 开头的私有修饰可出现在 Soflan token 前后，不会成为 group 或 fixed speed 的一部分。
- marker 必须是连续 token；空白、第二个 `#` 或粘连的普通字符都会使格式校验失败。

当前 marker 是本 patch 的扩展语法。它由共享 `SoflanMarkerParser` 对 `record._str` 执行 `Regex.Matches()` 得到，运行时、`SoflanCalculator` 和 `MajdataValidation` 使用同一解析器；这不是原版游戏公开 API。

### FixedSoflan marker

FixedSoflan 在 group marker 后追加 `F` 或 `f`：

```text
#1F
#1F600
#F
#F750
#-2F750.5
```

简要含义：

- `#1F` 等价于 `#1F600`。
- `#F` 等价于 group `0` + 固定速度 `600`。
- FixedSoflan 只支持 Tap 系物件。
- 它只改变 Soflan 视觉进度，不改变判定。

完整说明见 [fixed-soflan.md](fixed-soflan.md)。

### MajSimai HS 命令

MajSimai 里的 `<HS...>` 命令不由当前 patch 在游戏运行时直接解析。它需要在谱面转换或编辑器导出阶段落成 MA2 `SFL` 行，运行时才会被本系统消费。

因此支持边界是：

- 运行时支持 MA2 `SFL` 区间。
- 运行时支持本 patch 的 `#group` / `#groupFspeed` note marker。
- 运行时不直接支持 `<HS...>` 文本。
- `<HS...>` 的插值、采样、grid 对齐由上游转换器决定；本 patch 只消费转换后的 `SFL` 结果。

MajdataEdit / MajSimaiX 支持未定义组号语法 `<HS?*speed>(...)`。该语法只存在于上游 MajSimai 文本中：

- 每个 `<HS?*...>(...)` 都定义一个新的独立变速组。
- 解析器内部用负数组号 `-1, -2, ...` 表示这些组，编辑器频谱标签显示为 `[?]`。
- 导出 MA2 时才分配最终正数组号，因此正常导出结果通常是正整数 group；运行时 parser 本身仍接受有符号 group。
- 分配规则为：设显式手动组号最大值为 `M`，自动组从 `10^(M 的十进制位数)` 开始；若没有显式非 0 组号，则从 `10` 开始。
- 多个自动组按首次出现顺序依次分配，例如显式最大组号为 `122` 时，自动组导出为 `1000`、`1001`、`1002` ...
- 自动组内的 FixedSoflan 物件也会随映射导出，例如 `<HS?*1.5>(4@600)` 可能导出为 `#10F600`。

对于弹跳类 HS 命令，需要同时保证：

- 上游生成的 `SFL` 区间与目标 `HSpeedInterpolationGrid` 对齐。
- 参与弹跳的物件挂到正确 group，例如 `#219`。
- 如果要求不同玩家物件速度下表现一致，Tap 系物件需要写 `#219F` 或 `#219F600`。

## 加载流程

MA2 加载阶段通过 `MonoModRules` 对 `NotesReader` 做 IL 插入。

流程：

1. `NotesReader.loadMa2Main()` 调用 `calcBPMList` 前执行 `__SoflanClearPlayer(_playerID)`。
2. `__SoflanClearPlayer()` 只清空当前玩家的 Soflan 数据和缓存，不覆盖另一名玩家。
3. `NotesReader.loadMa2Main()` 调用 `calcTotal` 后执行 `__SoflanLoadComposition(records, sr, _playerID)`。
4. `SoflanManager.loadComposition()` 重新读取 MA2 文件，扫描所有 `SFL` 行，写入该玩家的 `SoflanListMap`。
5. `loadComposition()` 同时建立该玩家的 `BpmList`，并快照同一玩家的 `UserOption.GetAdjustMSec()`。
6. `NotesReader.loadNote()` 返回前执行 `__SoflanLoadNote(noteData, rec, sr, _playerID)`。
7. `SoflanManager.loadNote()` 使用共享正则提取 `#groupFspeed`，按玩家登记 `noteIndex -> soflanGroup/TGrid`。
8. 若 marker 带 `F`，同时写入 `NoteData.isFixedSoflanToUnifiedSpeed` 和 `fixedSoflanUnifiedSpeed`。

如果谱面没有任何 `SFL` 行，`containSoflans` 保持 false。播放时各 note patch 会回退到原版逻辑。

## 播放流程

播放阶段有两个核心入口。

### 每帧缓存清理

`GameCtrl.UpdateCtrl` 在读取玩家 option 后调用：

```csharp
SoflanManager.clearCurrentSoflanTimeCache(monitorIndex)
```

这会让同一帧、同一玩家的不同 note 共享
`(group, runtimeChartOffsetMsec, maiBugAdjustMsec) -> currentSoflanTime`
缓存，同时避免跨帧或跨玩家复用旧时间。

### 可见性派发

原版 `GameCtrl.UpdateCtrl` 会用音频时间和玩家物件速度判断 note 是否进入可见窗口。本 patch 在原可见性检查前插入：

```csharp
__SoflanNoteDecision(note, num, monitorIndex)
```

返回值含义：

| 返回值 | 含义 |
| ---: | --- |
| `0` | 非 Soflan 谱面，回到原版 msec 可见性检查 |
| `1` | Soflan 判断可见，跳过原版检查并继续注册 note |
| `2` | Soflan 判断不可见，跳过该 note |

Soflan 可见性判断使用：

```csharp
currentMsec = NotesManager.GetCurrentMsec()
group = getNoteSoflanGroup(monitorIndex, note)
visibleMsec = FixedSoflan.IsEnabledForNote(note) ? FixedSoflanVisibleMsec : num
maiBugAdjustMsec = SoflanVisualTiming.GetMaiBugAdjustMsec(note.type, visibleMsec)
currentSoflanTime = GetCurrentSoflanTimeWithOffsetsCached(
    monitorIndex,
    currentMsec,
    maiBugAdjustMsec,
    group)
checkNoteVisible(monitorIndex, note, currentMsec, visibleMsec, group, currentSoflanTime)
```

`num` 是原版按玩家物件速度得到的可见时间窗。FixedSoflan 物件会改用固定速度时间窗。
所有类型先从 `currentMsec` 移除玩家级 `GetAdjustMSec()`；Tap/Star/Break/Hold 系
随后再加入原版 `GetMaiBugAdjustMSec()` 并映射进 Soflan 时间轴，Touch 的 MaiBug
保持为 `0ms`。

## 时间轴原理

普通谱面里，note 的视觉进度直接和音频时间相关。

Soflan 谱面里，本系统先把音频时间映射到 Soflan Y：

```csharp
rawCurrentAudioMsec = currentAudioMsec - runtimeChartOffsetMsec
adjustedCurrentAudioMsec = rawCurrentAudioMsec + maiBugAdjustMsec
currentSoflanTime = ConvertAudioTimeToY_PreviewMode(adjustedCurrentAudioMsec, group)
noteSoflanTime = ConvertAudioTimeToY_PreviewMode(noteAudioMsec, group)
diffTime = noteSoflanTime - currentSoflanTime
```

`runtimeChartOffsetMsec` 是该玩家在谱面加载时快照的
`UserOption.GetAdjustMSec()`。原版运行时 note 时间包含该偏移，而 MA2 TGrid、BPM 和
SFL 边界从 `0ms` 开始；所以统一公式为：

```text
diff = F_group(noteRawMsec) - F_group(runtimeCurrentMsec - J + a)
```

其中 `J=GetAdjustMSec()`，`a=GetMaiBugAdjustMSec()`。`currentSoflanTime`
不是实际音频毫秒，而是经过 Soflan 速度积分后的视觉时间轴位置。

### Soflan Y 积分

`SimpleSoflanFramework` 会把 BPM 变化和 Soflan 速度变化合并成一组 `SoflanPoint`。

每个点记录：

- `TGrid`
- 当前累计 `Y`
- 当前 `Speed`
- 当前 `BPM`

相邻事件之间的 Y 增量：

```csharp
len = CalculateBPMLength(prev.TGrid, cur.TGrid, prev.Bpm.BPM)
scaledLen = len * prev.Speed
currentY += scaledLen
```

因此：

- `speed = 1` 时，Soflan Y 和普通音频时间推进一致。
- `speed = 0` 时，Soflan Y 在该区间内不推进，表现为停车。
- `speed < 0` 时，Soflan Y 反向推进，可产生回拉/反向移动。
- `speed > 1` 时，视觉时间轴更快推进。

把任意音频时间转成 Soflan Y 的过程：

1. `ConvertAudioTimeToTGrid(audioTime, bpmList)`。
2. 找到该 TGrid 所在的 Soflan segment。
3. 计算该 segment 内从起点到当前 TGrid 的 BPM 长度。
4. 乘以该 segment 的 speed。
5. 加到 segment 起点累计 Y 上。

公式：

```csharp
y = pos.Y + CalculateBPMLength(pos.TGrid, tGrid, pos.Bpm.BPM) * pos.Speed
```

## 可见性算法

Soflan 下不能再用“当前音频时间到 note 音频时间的距离”判断可见性，因为停车、反向和弹跳会让一个 note 在视觉时间轴上提前或重复进入屏幕。

当前实现使用 group 级可见范围查询：

```csharp
visibleRanges = soflanList.FillVisibleMsecRangesForGamePreview(
    currentSoflanTime,
    visibleMsec,
    bpmList,
    output,
    scratch)
```

概念上，它查询的是：

```text
[currentSoflanTime, currentSoflanTime + visibleMsec]
```

这个视觉窗口对应哪些原始音频时间范围。然后判断：

```csharp
note.time.msec 是否落在任意 visible range 内
```

这样即使 Soflan 时间轴反向或折返，只要某个 note 的原始音频时间在当前视觉窗口对应的任何范围里，它就能被注册出来。

运行时最终使用 Soflan 可见范围与原版注册窗口的并集：

```csharp
shouldRegister = soflanVisible
    || currentMsec >= note.time.msec - normalVisibleMsec;
```

其中 `normalVisibleMsec` 是原版按玩家物件速度计算的注册提前量。第二项是生命周期安全网：像 `100001x` 这样的极高速区间会把 `600ms` 的视觉窗口压缩到约 `0.006ms`，正常帧采样可能直接跨过整个窗口。进入原版注册窗口后仍会创建该物件，使它按真实音频时间判定或进入 TooLate 清理；物件位置、缩放和判定窗口本身仍不受这项安全网修改。

### 缓存策略

为了避免每帧遍历所有 group，当前实现是懒计算：

- 每帧或 `visibleMsec` 改变时递增 `visibleRangeCacheVersion`。
- 只有某一帧实际检查到某个 group 时，才重建该 group 的可见范围。
- 同一帧同一 group 后续 note 复用 `VisibleMsecRangeCache.Ranges`。
- 同一帧相同 `(player, group, runtimeChartOffsetMsec, maiBugAdjustMsec)` 的
  `currentSoflanTime` 通过 `GetCurrentSoflanTimeWithOffsetsCached()` 复用；零偏移调用仍由
  `GetCurrentSoflanTimeCached()` 转发到同一缓存。

## Tap / Break / Star 视觉算法

Tap 系物件通过 `NoteBase.GetNoteYPosition_soflan()` 重算 Y。

基础变量：

```csharp
maiBugAdjustMsec = EnableSoflanMaiBugAdjust
    ? MaiBugAdjust.Calculate(noteSpeed)
    : 0
rawCurrentAudioMsec = currentAudioMsec - runtimeChartOffsetMsec
adjustedCurrentAudioMsec = rawCurrentAudioMsec + maiBugAdjustMsec
currentSoflanTime = ConvertAudioTimeToY_PreviewMode(adjustedCurrentAudioMsec, group)
diffTime = noteSoflanTime - currentSoflanTime
absDiffTime = Abs(diffTime)
moveStartTime = DefaultMsec
scaleStartTime = 2 * DefaultMsec
insideY = StartPos
outsideY = EndPos + (EndPos - StartPos)
```

`GetMaiBugAdjustMSec()` 返回的是音频毫秒，不能直接与 Soflan Y 距离相减。把
`currentAudioMsec + adjustMsec` 整体交给 Soflan 转换后，1x 时可还原原版时序，
加速、减速、停车、反向和跨 SFL 边界时则由积分结果自动决定视觉偏移。
若偏移后的音频时间小于 `0`，会钳到谱面起点，避免 `TGridCalculator` 在首帧得到
无效的负时间 TGrid。

当 `EnableSoflanMaiBugAdjust=0` 时，`adjustMsec` 为 `0`，所以
`adjustedCurrentAudioMsec == currentAudioMsec - runtimeChartOffsetMsec`。
基础时间轴换算始终存在，开关只控制 MaiBug；移动和缩放门槛仍保持纯
`DefaultMsec / 2*DefaultMsec`。

普通 Soflan Y：

```csharp
soflanY = MapValue(diffTime, -moveStartTime, moveStartTime, outsideY, insideY)
clipedSoflanY = Clamp(soflanY, 120, 680)
```

对应关系：

| `diffTime` | Y |
| ---: | --- |
| `moveStartTime` | `StartPos` |
| `0` | `EndPos` |
| `-moveStartTime` | `outsideY` |

guide 状态：

| 条件 | 状态 |
| --- | --- |
| `absDiffTime > scaleStartTime` | guide alpha = 0 |
| `moveStartTime < absDiffTime <= scaleStartTime` | `NoteStatus.Scale`，guide 淡入 |
| `absDiffTime <= moveStartTime` | `NoteStatus.Move`，guide 显示 |

本体缩放在 `NoteCheck()` 后重算：

```csharp
scale = Clamp01((2 * DefaultMsec - absDiffTime) / DefaultMsec)
scale *= UserOption.NoteSize
```

BreakNote 有独立 `NoteCheck()` patch，缩放逻辑同 Tap 系，但保留 Break 自身特效。

Star / BreakStar 属于 Tap base 类型，因此位置和缩放跟随 `NoteBase` 的 Soflan 分支。Star 旋转没有被 Soflan patch 单独修改。

## Hold / BreakHold 视觉算法

Hold 和 BreakHold 使用头尾两个 Soflan 时间：

```csharp
headSoflanTime = ConvertAudioTimeToY_PreviewMode(headRawMsec, group)
tailSoflanTime = ConvertAudioTimeToY_PreviewMode(tailRawMsec, group)
currentSoflanTime = ConvertAudioTimeToY_PreviewMode(
    currentAudioMsec - runtimeChartOffsetMsec + maiBugAdjustMsec,
    group)
headDiffTime = headSoflanTime - currentSoflanTime
tailDiffTime = tailSoflanTime - currentSoflanTime
```

头部和尾部 Y 都使用类似 Tap 的映射：

```csharp
y = MapValue(diffTime, -moveStartTime, moveStartTime, outsideY, insideY)
y = Clamp(y, StartPos, EndPos)
```

主要处理：

- 头部未进入移动窗口时，头尾都放在 headY，body 使用默认长度。
- 头部进入后，若尾部也进入移动窗口，则用 `headY - tailY` 计算 body 长度。
- 若尾部还未进入移动窗口，则尾端固定在 `StartPos`。
- Hold 到尾后，头部固定在 `EndPos`，body 回到默认长度。
- Hold 本体和端点缩放按 headDiffTime 重算。

BreakHold 在 Hold 的基础上额外保留 BreakHold 的特效颜色逻辑。

FixedSoflan 当前不介入 Hold / BreakHold。

## TouchNoteB / TouchNoteC 视觉算法

TouchNoteB 不是普通 Tap 的 Y 轴移动。它的原版视觉语义是：

- 固定在触摸区域。
- 先隐藏。
- 然后颜色片淡入。
- 再从外侧收束到中心。
- 到达判定时显示 Notice。

Soflan patch 保留这个语义，只把时间轴替换为 Soflan 时间：

```csharp
currentSoflanTime = GetCurrentSoflanTimeCached(monitorId, currentMsec, group)
touchDispTime = DefaultMsec * 0.25
soflanStartTime = touchNoteSoflanTime - DefaultMsec - touchDispTime
```

`GetCurrentSoflanTimeCached()` 的零视觉偏移仍会先执行 `currentMsec-J`；Touch
不使用 MaiBug，但不能跳过运行时轴到 MA2 原始轴的转换。

三段逻辑：

| 条件 | 行为 |
| --- | --- |
| `currentSoflanTime <= soflanStartTime` | 隐藏，`NoteStatus.Init` |
| `currentSoflanTime <= soflanStartTime + touchDispTime` | 颜色片淡入，`NoteStatus.Scale` |
| 之后 | 颜色片收束，`NoteStatus.Move` |

Notice 显示条件：

```csharp
currentSoflanTime >= touchNoteSoflanTime
```

TouchNoteC 继承 TouchNoteB 的显示逻辑，因此不需要单独 patch。

TouchHoldC 当前不走这套 TouchTap Soflan 逻辑。

## FixedSoflan 算法位置

FixedSoflan 是 Tap 系 Soflan 的补充模式。普通 Soflan 的移动时间窗仍会受玩家物件速度影响，而 FixedSoflan 把以下内容固定到 note 声明速度：

- 可见窗口。
- scale start。
- move start。
- motion progress。
- scale progress。

默认固定速度 `600`：

```csharp
DefaultMsec = 240000 / 600 = 400ms
MaiBugAdjustMSec = -10ms
AdjustedCurrentAudioMsec = CurrentAudioMsec - 10ms
MoveStartTime = 400 Soflan-Y ms
ScaleStartTime = 800 Soflan-Y ms
VisibleMsec = 800ms
```

FixedSoflan 的 Y 映射用进度而不是玩家速度：

```csharp
motionProgress = Clamp01((moveStartTime - diffTime) / (2 * moveStartTime))
y = Lerp(StartPos, outsideY, motionProgress)
```

完整语法和边界见 [fixed-soflan.md](fixed-soflan.md)。

## 支持矩阵

| 对象/命令 | 普通 Soflan | FixedSoflan | 说明 |
| --- | --- | --- | --- |
| MA2 `SFL` duration 行 | 支持 | 不适用 | 直接解析为 `Soflan` 区间 |
| MA2 BPM 变化 | 支持 | 支持 | 从 composition BPM 列表读入 |
| note `#group` group marker | 支持 | 不适用 | 本 patch 扩展；group 是有符号 `int` |
| note `#groupF` / `#groupF600` | 支持 group | 支持 Tap 系 | 本 patch 扩展 |
| Tap | 支持 | 支持 |
| Break | 支持 | 支持 |
| ExTap / ExBreakTap | 支持 | 支持 |
| Star / BreakStar / ExStar / ExBreakStar | 支持 | 支持位置和缩放，不单独改旋转 |
| Hold / ExHold | 支持 | 不支持 |
| BreakHold / ExBreakHold | 支持 | 不支持 |
| TouchNoteB | 支持 | 不支持 |
| TouchNoteC | 支持 | 不支持 |
| TouchHoldC | 不支持本次 TouchTap 逻辑 | 不支持 |
| Slide 路径及内部星标 | 未专门支持 | 不支持 | 设计记录不代表已经实现 |
| MajSimai `<HS...>` 文本 | 不直接支持 | 不直接支持 | 需要上游转换为 MA2 `SFL` |
| SimpleSoflan `InterpolatableSoflan` | 框架支持 | 不适用 | 当前 MA2 loader 不直接生成 |
| SimpleSoflan `KeyframeSoflan` | 框架支持 | 不适用 | 当前 MA2 loader 不直接读取独立 keyframe 命令 |

## 错误和边界

### SFL 解析

`SFL` 字段解析失败时：

- 写日志：`parse soflan failed, line content:...`
- 停止继续读取后续 `SFL` 行。
- 不会抛出到外层。

如果 MA2 文件不存在，`loadComposition()` 直接返回。

### Marker 解析

note marker 解析更严格：

- 多个 `#...` marker 会写日志并抛 `FormatException`。
- 空 marker、内部空白、非法 group、非法 FixedSoflan speed 都会写日志并抛 `FormatException`。
- group 必须落在有符号 32 位整数范围内；正号和负号本身合法。
- `#1!m!y`、`!y!m#1`、`!m#1F600!y` 等排列会先由正则抽出连续的 `#1` 或 `#1F600`，其他私有修饰保持独立。
- marker 与 SFL 格式错误使用 Release 无条件 `PatchLog.Error`，后台写入 UTF-8 without BOM 的 `dpSoflanSupport.log` 并尝试转发 Unity Error；普通 INFO 日志仍只在 Debug 且受配置开关控制。

### 缺失 group

`SoflanListMap` 的索引器在访问不存在 group 时会创建空 `SoflanList`，该 list 默认含 `1.0x` keyframe。因此 note 挂到没有 SFL 的 group 时，通常表现为该 group 没有变速。

### 无 Soflan 谱面

没有任何 `SFL` 行时：

- `SoflanManager.containsSoflans(playerId)` 为 false。
- `GameCtrl.__SoflanNoteDecision()` 返回 `0`。
- 各 note 显示逻辑回到原版。
- note marker 本身仍可能被解析登记，但不会驱动 Soflan 视觉。

## 调试和验证

DEBUG 构建会挂载 Soflan Monitor 面板。

面板能力：

- `F8` 显示/隐藏。
- 显示当前 monitor、播放时间和该玩家快照的 `GetAdjustMSec()`。
- 显示 group `0` 当前速度。
- 可选显示前 50 个 group 的当前速度。
- 右键选中 Tap，显示 diffTime、scaleStartTime、moveStartTime、moveProgress、finalScale、Y 等数据。
- FixedSoflan Tap 会额外显示 fixed speed、fixed motion progress 和 fixed scale progress。
- 选中 Tap 会显示 monitor、运行时钟、基础偏移、MaiBug、最终 MA2 原始轴时间以及原始/偏移后 Soflan 时间。
- 面板顶部及选中 Tap 数据会显示 `MaiBugAdjust: Enabled/Disabled`，用于确认 `mai2.ini` 开关是否生效。

面板默认位于右上角、约每 0.2 秒刷新；未选择 note 时显示 monitor `0`。`P` 键暂停/恢复在 Release 和 Debug 中都存在。完整交互、日志和限制见 [配置、日志与调试](configuration-and-debugging.md)。

建议验证：

- 无 `SFL` 谱面：所有物件保持原版行为。
- 单 group 正常速度 `1.0`：Soflan 行为应接近原版视觉。
- `speed = 0` 停车：物件应停在视觉时间轴对应位置，可见性不应丢 note。
- 负速回拉：物件可被可见性判断重新注册到屏幕范围。
- 多 group：不同 note 的 `#N` marker 应只影响各自 group。
- 混合修饰：`#1!m!y` 与 `!y!m#1` 都应登记为 group `1`。
- TouchNoteB / TouchNoteC：应保留原版固定触摸区动画，而不是变成普通 Tap Y 轴移动。
- Hold / BreakHold：头尾和 body 长度应跟随 Soflan 时间轴。
- FixedSoflan 弹跳 Tap：不同玩家物件速度下，弹跳开始时机和判定线对齐应保持一致。
- P1/P2 使用不同 AdjustTiming 或不同谱面时，BPM、SFL、note map、当前时间和可见范围缓存不得互相覆盖。
- `EnableSoflanMaiBugAdjust=0`：600 速度、1x SFL 的判定时 `diffTime` 应为 `0`，物件 Y 应回到 `EndPos`。
- `EnableSoflanMaiBugAdjust=1` 或缺失：600 速度、1x SFL 的判定时应保留约 `7px` 的原版 MaiBug 位置滞后。

构建验证：

```powershell
dotnet build -c Release Assembly-CSharp.SoflanSupport.mm.csproj
dotnet build -c Debug Assembly-CSharp.SoflanSupport.mm.csproj
dotnet run --project tools/SoflanMarkerTests/SoflanMarkerTests.csproj -c Release
dotnet run --project tools/SoflanLogTests/SoflanLogTests.csproj -c Release
dotnet run --project tools/SoflanMaiBugTests/SoflanMaiBugTests.csproj -c Release

# 可选真实谱面对比：复杂变速轨迹 + 无 SFL/恒定 1x 全阶段视觉对照
dotnet run --project tools/SoflanMaiBugTests/SoflanMaiBugTests.csproj -c Release -- `
  "F:\yourGame\Package\option\yourOption\music\yourMusic\example_04.ma2" `
  "F:\yourGame\Package\option\yourOption\music\yourMusic\example_01.ma2"
```

静态 patch 验证建议：

- `NotesReader.loadMa2Main` 把 `_playerID` 传给 clear/load composition 插入。
- `NotesReader.loadNote` 把 `_playerID` 传给 load note marker 插入。
- `SoflanMarkerParser.TryParse` 调用 `Regex.Matches`，并被 `SoflanManager.loadNote` 与 `SoflanCalculator` 共用。
- `GameCtrl.UpdateCtrl` 把 `monitorIndex` 传给缓存清理和 Soflan 可见性派发。
- `SoflanManager` 含按玩家隔离的 `PlayerSoflanState`，统一入口为 `GetCurrentSoflanTimeWithOffsetsCached()`。
- `NoteBase.GetNoteYPosition` 存在 Soflan 分支。
- `HoldNote.Execute` / `BreakHoldNote.Execute` 存在 Soflan visual 分支。
- `TouchNoteB.GetNoteYPosition` 存在 Soflan 分支。
- 输出目录只包含 `.mm.dll` 和 `.pdb`。

主项目构建、内嵌依赖和 BepInEx 部署见 [构建与部署](build-and-deployment.md)；计算器、转换器、验证器和各测试的具体边界见 [离线工具与验证](tools.md)。
