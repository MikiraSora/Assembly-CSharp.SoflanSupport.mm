# Soflan 运行时时间轴偏移调查与修复设计

> 本文保留问题证据、修复推导和实施记录。当前日常规范以 [Soflan 变速系统](soflan-system.md)、[FixedSoflan](fixed-soflan.md) 和 [配置、日志与调试](configuration-and-debugging.md) 为准。

## 文档状态

- 调查日期：2026-07-24
- 调查结论：根因已确认
- 修复状态：阶段 1～4 已实施并通过数值、构建、MonoMod 应用和静态 IL 验证
- 未实施增强：identity group 原版路径 bypass；当前仍保留最多约 1px 的往返量化残差
- 目标边界：只修正 Soflan 视觉时间轴，不修改歌曲播放、判定时间或 MA2 谱面时间
- 主要复现谱面：`example_01.ma2` 与 `example_04.ma2`（文件名已脱敏）

本文记录以下问题的完整证据、数学证明、修复建议和测试方法：

> 当 `EnableSoflanMaiBugAdjust=0` 时，变速谱面中的物件击打音效和歌曲节拍不一致；相同物件配置的无变速谱面没有该问题。即使选择一个在两份谱面中完全相同、所属 Soflan group 全程恒定 `1.0x` 的物件，其注册、位移、缩放和 Guide 渐显是否真的与原版一致？

## 结论摘要

修复前的答案是否定的：两份谱面中数据完全相同、全程恒定 `1.0x` 的物件，运行时视觉结果仍不相同。阶段 1～4 实施后，约 `60/70ms` 的系统性错误已消除；恒定 1x 对照只剩毫秒量化造成的极小残差。

修复前的根因不是 SFL 倍率计算，而是 Soflan 两端使用了不同的毫秒时间轴：

- 原版 `NotesReader` 给所有 `NotesTime.msec` 加入 `UserOption.GetAdjustMSec()`。
- 默认 `AdjustTiming=Normal` 时，该值约为 `+60 ms`，并不是 `0 ms`。
- Soflan 代码把物件的 MA2 `bar/grid` 重新换算为从 `0 ms` 开始的原始谱面时间。
- 修复前的歌曲当前时间仍直接使用 `NotesManager.GetCurrentMsec()`，没有先减去上述基础偏移。

所以，修复前实现的视觉时间轴相对原版系统性提前：

| 设置 | 相对原版的主要时间差 |
| --- | ---: |
| `EnableSoflanMaiBugAdjust=1` | 约 `60 ms` |
| `EnableSoflanMaiBugAdjust=0` | 约 `70 ms`，即缺失约 `60 ms`，再加取消原版 `-10 ms` MaiBug |

修复分为两层：

1. **已实施：统一运行时与 MA2 原始时间轴。** 在进入 Soflan 积分前，把游戏运行时钟还原为 MA2 原始时钟。
2. **严格等价增强：identity group 走原版路径。** 对整条 group 都没有有效非 `1.0x` 变化的物件，在开关启用时直接使用原版渲染和可见性逻辑，从而消除音频时间到 TGrid 往返换算的残余量化误差。

基础偏移 `J` 必须按玩家/Monitor 取得并规范化为该玩家真实的 `UserOption.GetAdjustMSec()`。不能把每条 note 的 `note.time.msec - rawTGridMsec` 原样作为主偏移或缓存键；该差值会受 .NET Framework 毫秒量化影响，在同一谱面中产生大量不同浮点值。

## 调查样本与版本身份

### 仓库版本

```text
Git commit: a9b547ac0cdc115242b092d79de7400356758710
```

### 游戏二进制

```text
Path:
F:\yourGame\Package\Sinmai_Data\Managed\Assembly-CSharp.dll

SHA-256:
257CC5F7733E6F48D75627DB32DC182F7C00D267CEA065F6AA6DDB6E2A2A0985
```

### 谱面文件

```text
Path:
F:\yourGame\Package\option\yourOption\music\yourMusic\example_01.ma2

SHA-256:
AC070C1DA756363C6DD939AEA0A27F41584F2EFDFB97FFCE50F13B82970F857E
```

```text
Path:
F:\yourGame\Package\option\yourOption\music\yourMusic\example_04.ma2

SHA-256:
56B2405D920A8B83975C903E346F5A466505CEAFCC855A4BBC13BA09A21F212A
```

这些哈希用于确认本文结论对应的具体二进制和谱面版本。文件变化后应重新执行验证，不能直接假定数值仍完全相同。

## 术语和符号

后文使用以下符号：

| 符号 | 含义 |
| --- | --- |
| `C` | 从 MA2 `bar/grid` 和 BPM 换算出的原始谱面毫秒时间，时间轴从 `0 ms` 开始 |
| `J` | 原版 `GetAdjustMSec()` 加入运行时 `NotesTime.msec` 的基础偏移 |
| `A` | 原版运行时物件时间，即 `A = C + J` |
| `t` | `NotesManager.GetCurrentMsec()` 返回的当前游戏时间 |
| `a` | `GetMaiBugAdjustMSec()` 对视觉使用的偏移；600 速时约为 `-10 ms` |
| `F_g(x)` | group `g` 把原始音频时间 `x` 积分为 Soflan Y 时间轴位置的函数 |
| `d` | note Soflan 位置减去当前 Soflan 位置，用于位移、缩放和 Guide alpha |

需要特别区分：

- `GetAdjustMSec()` 是原版谱面运行时基础时间偏移。
- `GetMaiBugAdjustMSec()` 是原版高速物件视觉运动中的小偏移。
- `EnableSoflanMaiBugAdjust` 只应控制第二项，不应让第一项从 Soflan 时间轴中消失。

## 谱面数据证据

### 逐行对照结果

对两份谱面执行只读逐行比较，并将差异按 `_04` 行内容分类：

| 项目 | 数量 |
| --- | ---: |
| `_01` 总行数 | 3169 |
| `_04` 总行数 | 3169 |
| 原始内容不同的行 | 1734 |
| `_04` 中的 SFL 行差异 | 1622 |
| `_04` 中的 `#group` 标记差异 | 112 |
| 除 SFL 和 group marker 外的差异 | **0** |

因此，排除变速命令和物件分组扩展以后，两份文件不存在其他物件配置差异。

### 完全相同的对照物件

第 1653 行在两份文件中完全相同：

```text
NMHLD	7	0	1	96
```

第 1654 行在两份文件中也完全相同：

```text
NMTAP	7	0	3
```

两条记录都没有 `#group` marker，因此按当前解析规则属于默认 group `0`。

### group 0 确实全程为 1.0x

`example_04.ma2` 的 1622 条 SFL 分布如下：

| Group | SFL 行数 |
| ---: | ---: |
| 1 | 159 |
| 2 | 337 |
| 3 | 393 |
| 4 | 260 |
| 5 | 134 |
| 6 | 71 |
| 7 | 67 |
| 8 | 67 |
| 9 | 67 |
| 10 | 67 |

不存在 group `0` 的 SFL 行。`SoflanListMap` 为缺失 group 建立默认 `SoflanList`，其初始 keyframe 是：

```csharp
new KeyframeSoflan
{
    TGrid = new TGrid(0, 0),
    Speed = 1f,
}
```

所以第 1654 行是一个严格的“谱面数据相同，且所属 group 从谱面开始到结束恒为 `1.0x`”的 Tap 对照物件。

### 受影响的默认 group 物件数量

只统计当前运行时已支持 Soflan 视觉的 Tap、Star 和 Hold 家族，`_04` 中没有 group marker、因而进入 group `0` 的物件共有 1186 个：

| 家族 | 数量 | 组成 |
| --- | ---: | --- |
| Tap | 940 | `BRTAP=60`、`BXTAP=5`、`EXTAP=6`、`NMTAP=869` |
| Star | 166 | `BRSTR=33`、`BXSTR=1`、`EXSTR=12`、`NMSTR=120` |
| Hold | 80 | `BRHLD=1`、`NMHLD=79` |

这不是单一物件或单一行的偶发问题。

## 原版 GetAdjustMSec 证据

### NotesReader 的起始毫秒

对上述 `Assembly-CSharp.dll` 反编译后，`Manager.NotesReader.calcBPMList()` 包含：

```csharp
NotesTime notesTime = default(NotesTime);
float bpm = _composition._bpmList[0].bpm;
notesTime.setMsec(
    Singleton<GamePlayManager>.Instance
        .GetGameScore(_playerID)
        .UserOption.GetAdjustMSec());
```

其后所有 BPM 点、note 起点和 note 终点都基于这个起始毫秒继续计算。

当计算发生在首个 BPM 点以前时，`calcMsec()` 同样显式加入该值：

```csharp
return timing.getFourBeat(getResolution()) * 60000f / num
    + Singleton<GamePlayManager>.Instance
        .GetGameScore(_playerID)
        .UserOption.GetAdjustMSec();
```

### GetAdjustMSec 公式

`Manager.UserDatas.UserOption.GetAdjustMSec()` 的反编译结果：

```csharp
public float GetAdjustMSec()
{
    return (float)(AdjustTiming - 20 + 36) / 10f * 16.666666f;
}
```

默认记录是：

```text
OptionJudgetimingTableRecord(20, "Normal", "0.0", "0.0", ..., isDefault: 1)
```

所以默认值为：

```text
J = (20 - 20 + 36) / 10 × 16.666666
  = 59.999996 ms
```

界面显示的 `Normal / 0.0` 并不表示内部 `GetAdjustMSec()` 为零。该公式还意味着不同 `AdjustTiming` 下 `J` 会变化，不能把修复写死为 `60 ms`：

| `AdjustTiming` 枚举值 | 内部 `J` 约值 |
| ---: | ---: |
| 0 | 26.666666 ms |
| 20（默认） | 59.999996 ms |
| 40 | 93.333328 ms |

## 修复前 Soflan 代码路径

### note 目标使用 MA2 原始时间

`SoflanManager.loadNote()` 会为每个 note 保存原始 TGrid。这一步发生在是否存在 `#group` marker 的判断以前，因此默认 group `0` 的 note 也会保存。

`getNoteAudioMsecForSoflan()` 随后执行：

```csharp
return (float)TGridCalculator
    .ConvertTGridToAudioTime(tGrid, bpmList)
    .TotalMilliseconds;
```

这得到的是 `C`，不包含原版 `J`。

### 修复前当前时间没有还原到 MA2 原始时间轴

修复前 `GetCurrentSoflanTimeWithAudioOffsetCached()` 的核心逻辑是：

```csharp
var adjustedAudioMsec =
    MaiBugAdjust.ApplyToAudioMsec(currentMsec, audioOffsetMsec);

soflanTime = ConvertAudioTimeToY_PreviewMode(
    adjustedAudioMsec,
    soflanGroup);
```

即当前端使用 `t + a`，没有先执行 `t - J`。

### 为什么 group 0 也受影响

只要谱面中存在任意一条 SFL，`containSoflans` 就会变成 `true`。`NoteBase.Initialize()` 随后对所有受支持 Tap base type 使用 Soflan 分支，并不是只对带 `#group` 的 note 使用。

因此：

- `_01` 没有 SFL，物件走原版路径。
- `_04` 只要其他 group 存在 SFL，第 1654 行的 group `0` Tap 也走 Soflan 路径。
- group `0` 虽然恒为 `1.0x`，两者仍因运行代码路径和时间原点不同而产生差异。

## 数学证明

### 当前公式

原版无 SFL 物件的视觉剩余时间可以写成：

```text
d01 = (C + J) - (t + a)
```

当前 Soflan 路径在开关启用时使用：

```text
d04_on = C - (t + a)
        = d01 - J
```

当前 Soflan 路径在开关关闭时使用：

```text
d04_off = C - t
         = d01 - (J - a)
```

600 速时 `a=-10 ms`，默认 `J≈60 ms`，因此：

```text
d04_on  = d01 - 60 ms
d04_off = d01 - 70 ms
```

这证明了偏差与当前 SFL 倍率是否恰好为 `1.0x` 无关。即使 `F_g(x)=x`，输入 `F_g` 的两个时间原点也已经不同。

### 正确公式

保持 note 目标在 MA2 原始时间轴 `C`，把当前游戏时间还原为原始时间轴：

```text
rawCurrent = t - J + a
```

然后：

```text
dFixed = F_g(C) - F_g(t - J + a)
```

在恒定 `1.0x` 下：

```text
dFixed = C - (t - J + a)
       = C + J - t - a
       = d01
```

因此，缺失的 `-J` 正好就是当前系统性偏差的根因。

## Tap 位移、缩放与 Guide 渐显

以玩家物件速度 `600` 为例：

```text
DefaultMsec D = 400 ms
StartPos S = 120
EndPos E = 400
OutsidePos = 680
MaiBug a = -10 ms
```

在该物件正常出现到结束的区间里，原版和当前 Soflan 渲染的主要量都可以表示为 `d` 的函数。

主物件缩放：

```text
bodyScale = Clamp01((2D - |d|) / D)
```

Y 位置：

```text
Y = Clamp(Map(d, -D, D, 680, 120), 120, 680)
```

代入数值：

```text
Y = Clamp(400 - 0.7d, 120, 680)
```

Guide 缩放：

```text
moveProgress = (Y - 120) / 280
guideScale = 0.25 + 0.75 × moveProgress
```

Guide alpha：

```text
|d| > 800:        alpha = 0
400 < |d| <= 800: alpha = (800 - |d|) / 400
|d| <= 400:       alpha = 1
```

所以 `d` 整体少 `60/70 ms` 时，位移、主物件缩放、Guide 缩放和 Guide alpha 必然同时提前。

主 note sprite 本身没有按上述公式做 alpha 淡入或淡出。渐变的是 `NoteGuide.SetAlpha()`；物件结束时由 `EndNote()` 立即停用或回收，而不是逐渐淡出。

## 第 1654 行的数值反例

### 时间参数

该谱面的 BPM 为 `217`，分辨率为 `384`。第 1654 行位于 bar `7`、grid `0`。

实际 net472 Soflan 框架换算得到：

```text
C = 7741.93555 ms
J = 59.999996 ms
A = C + J = 7801.93555 ms
a = -10 ms（600 速）
```

下表每格依次表示：

```text
状态 / Y位置 / 主物件scale / Guide scale / Guide alpha
```

| 当前时间 | `_01` 原版 | `_04` 开关开启 | `_04` 开关关闭 |
| --- | --- | --- | --- |
| `A-800` | `Init / 120 / 0 / 0 / 不可见` | `Scale / 120 / .12788 / .25 / .12788` | `Scale / 120 / .14948 / .25 / .14948` |
| `A-600` | `Scale / 120 / .47500 / .25 / .47500` | `Scale / 120 / .62471 / .25 / .62471` | `Scale / 120 / .65351 / .25 / .65351` |
| `A-400` | `Scale / 120 / .97500 / .25 / .97500` | `Move / 156.048 / 1 / .34656 / 1` | `Move / 162.097 / 1 / .36276 / 1` |
| `A-200` | `Move / 253.000 / 1 / .60625 / 1` | `Move / 295.161 / 1 / .71918 / 1` | `Move / 301.210 / 1 / .73538 / 1` |
| `A-70` | `Move / 344.000 / 1 / .85000 / 1` | `Move / 385.887 / 1 / .96220 / 1` | `Move / 393.952 / 1 / .98380 / 1` |
| `A-5` | `Move / 389.500 / 1 / .97188 / 1` | `Move / 432.258 / 1 / 1.08641 / 1` | `Move / 438.307 / 1 / 1.10261 / 1` |
| `A`，调用 `EndNote` 前 | `Move / 393.000 / 1 / .98125 / 1` | `Move / 434.274 / 1 / 1.09181 / 1` | `Move / 442.339 / 1 / 1.11341 / 1` |

在原版判定时间 `A`，Soflan 物件已经越过判定环。之后判定和 `EndNote()` 仍使用原版 `AppearMsec=A`，因此视觉提前但判定、歌曲基准没有一起提前。

### 密集采样结果

使用实际 net472 框架，从 `A-790 ms` 到 `A` 以 `0.001 ms` 步长采样 790001 个时间点：

| 开关 | 最大 Y 差 | 最大主 scale 差 | 最大 Guide scale 差 | 最大 Guide alpha 差 | 状态阶段不同的累计时间 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 开启 | 43.356260 px | 0.15482060 | 0.11613290 | 0.15482060 | 约 58.436 ms |
| 关闭 | 50.356260 px | 0.17982050 | 0.13488280 | 0.17982050 | 约 68.436 ms |

理想连续模型中的系统性差异是：

| 开关 | 时间差 | 运动区 Y 差 | 主 scale/alpha 差 | Guide scale 差 |
| --- | ---: | ---: | ---: | ---: |
| 开启 | 60 ms | 42 px | 0.15 | 0.1125 |
| 关闭 | 70 ms | 49 px | 0.175 | 0.13125 |

实测比理想值多出的约 `1.356 px` 和约 `0.00483`，来自当前 `TimeSpan.FromMilliseconds`、音频时间到最近 TGrid、再到 Soflan Y 的量化过程。

### 可见性注册

问题也发生在物件实例注册阶段，不仅发生在实例创建后的移动公式中。

该 Tap 的原版连续注册阈值：

```text
A - 800 = 7001.93555 ms
```

实际 Soflan 可见区查询首次包含该 note 的时间：

| 路径 | 首次包含时间 | 相对原版阈值 |
| --- | ---: | ---: |
| `_04` 开关开启 | 6949.501 ms | 提前约 52.435 ms |
| `_04` 开关关闭 | 6939.501 ms | 提前约 62.435 ms |

按从 `0 ms` 开始、帧长 `16.666666 ms` 的 60 FPS 序列取样：

| 路径 | 首次注册帧时间 |
| --- | ---: |
| `_01` 原版 | 7016.666 ms |
| `_04` 开关开启 | 6950.000 ms |
| `_04` 开关关闭 | 6950.000 ms |

该例中 Soflan 路径会早 4 帧注册。

## Hold、Break、Star、Touch 与 Slide 的影响

### Hold 和 BreakHold

Hold 必须把头和尾作为两个独立 TGrid 目标：

```text
headDiff = F_g(headC) - F_g(rawCurrent)
tailDiff = F_g(tailC) - F_g(rawCurrent)
```

当前代码对头和尾都使用原始 TGrid 目标，但当前端缺少 `-J`，所以两者都会提前。

Hold body 几何还由头尾继续派生：

```text
bodyLength = headY - tailY
bodyCenter = headY - bodyLength / 2
bodyHeight = 140 + bodyLength
```

因此不能只修 Hold 头部位置；头、尾、body 中心、body 长度、端点缩放和可见性必须共同使用同一个还原后的当前时间轴。

第 1653 行 `NMHLD 7 0 1 96` 是可用于实际游戏对照的完全相同 group `0` Hold。

### Break 和 Star

Break、Tap、Star 共用或复用 `NoteBase` 的 Y 和缩放时间轴，因此都受同一个基础偏移影响。

- Break 特有效果仍走原版效果逻辑，但其父物件位置和缩放提前。
- Star 旋转仍走原版旋转逻辑，但其父物件位置和缩放提前。

### TouchNoteB 和 TouchNoteC

Touch 不使用普通 Tap 的 Y 轴移动，也不使用 MaiBug；但它仍把 `NotesManager.GetCurrentMsec()` 直接送入 Soflan 转换，所以同样缺少 `-J`。

正确做法是保持 Touch 的固定触摸区淡入、收束和 Notice 语义，只把其当前时间替换为：

```text
rawCurrent = t - J
```

不能把 Touch 改成普通 Tap 运动。

### Slide

当前 Slide 视觉没有接入这套 Soflan 时间轴，因此不应在本修复中顺带改变 Slide 判定或视觉。混合谱面中可能出现 Tap/Hold/Touch 被错轴而 Slide 仍走原版的情况，这会进一步放大玩家感知到的不一致。

## 为什么会表现为击打音效和歌曲不合拍

Soflan patch 只改变视觉，不改变原版 `AppearMsec`、判定窗口或歌曲播放时间。

当前错误让物件视觉提前到达或越过判定环：

- 玩家跟随视觉击打时，会比歌曲节拍更早输入，击打音效也随输入提前。
- AutoPlay 或原版结束逻辑仍在 `A=C+J` 附近结束物件，因此会出现物件已经越过判定环、稍后才触发原版判定或回收的现象。
- 关闭 MaiBug 开关会在缺失约 `60 ms` 的基础上再提前约 `10 ms`，所以反馈在 `EnableSoflanMaiBugAdjust=0` 时更明显。

这不表示歌曲文件本身被移动，也不表示音效时钟被 Soflan 代码直接修改；它是视觉提示和原版音频/判定基准分离造成的结果。

## 现有测试为什么没有发现

`SoflanCalculator/SoflanCalcEngine.cs` 当前把 note 时间直接计算为：

```csharp
var appearMsec = (float)TGridCalculator
    .ConvertTGridToAudioTime(noteTGrid, bpmList)
    .TotalMilliseconds;
```

随后测试也使用同一条从 `0 ms` 开始的 raw 时间轴：

```csharp
float noteMsec = 2f * BarMsec;
var judgment = Calculate(data, note, noteMsec);
```

测试没有构造：

```text
runtimeAppearMsec = rawNoteMsec + GetAdjustMSec
runtimeCurrentMsec = rawCurrentMsec + GetAdjustMSec
```

所以 `SoflanMaiBugTests: PASS` 只证明了假设 `J=0` 时内部公式自洽，不能证明与实际 `NotesReader` 运行结果一致。

## 推荐修复：统一时间轴

### 设计原则

1. MA2 TGrid、BPM 点和 SFL 边界继续使用从 `0 ms` 开始的原始谱面时间轴。
2. note 目标继续使用 `F_g(C)`，不要给目标时间硬加 `60 ms`。
3. 每次把游戏当前时间送入 Soflan 积分以前，先减去该玩家/Monitor 在谱面加载和游戏运行时使用的规范化基础偏移。
4. MaiBug 在完成基础时间轴还原后再加入，而且仍由 `EnableSoflanMaiBugAdjust` 控制。
5. 歌曲播放时间、原版 `NoteData.time.msec`、判定和音效代码保持不变。

### J 不能全局写死，也不能逐 note 原样使用

不建议在 `SoflanManager` 中简单保存全局常量 `60f`，原因包括：

- 玩家 `AdjustTiming` 可改变 `J`。
- 双玩家可能具有不同设置。
- `NotesReader` 本身按 `_playerID` 读取对应玩家的 `UserOption.GetAdjustMSec()`。
- 单个全局 `J` 会让 P1/P2 在不同 `AdjustTiming` 下共用错误时间原点。

同样不应把逐 note 差值直接当成规范化 `J`：

```text
observedOffset = note.time.msec - rawTGridMsec
```

原因是目标运行环境使用 .NET Framework `TimeSpan.FromMilliseconds()`。MA2 TGrid 转毫秒时会发生毫秒量化，而原版 `NotesReader` 的 `NotesTime.msec` 仍保留 float 计算结果。因此 `observedOffset` 会围绕真实 `J` 小幅变化。

### 逐 note 偏移碎片的实测证据

对 `example_04.ma2` 的 1488 条物件记录，使用 BPM `217`、分辨率 `384`、默认 `AdjustTiming=20`，按当前 net472 框架执行 TGrid 到毫秒换算，再计算：

```csharp
observedOffset = originalRuntimeNoteMsec - frameworkRawNoteMsec;
```

结果：

```text
真实 GetAdjustMSec：59.9999962 ms
物件样本数：1488
不同 observedOffset 浮点值：296
最小 observedOffset：59.5 ms
最大 observedOffset：60.5 ms
```

如果把这 296 种 float 原样加入 `CurrentSoflanTimeCacheKey`，同一玩家、同一 group、同一帧中的物件也可能无法共享 current Soflan time，缓存会从“接近每 group 计算一次”退化为“接近每 note 计算一次”。

### 推荐的规范化 J 来源

运行时应按 `MonitorId` 或 `MonitorIndex` 使用与原版相同的公开方法：

```csharp
float runtimeChartOffsetMsec =
    Singleton<GamePlayManager>.Instance
        .GetGameScore(monitorId)
        .UserOption
        .GetAdjustMSec();
```

该值建议在该玩家谱面加载完成或物件初始化时快照一次，并在本局内保持稳定。另一种等价来源是该玩家 `NotesReader` 已完成 `calcBPMList()` 后、TGrid `0` 的首个 BPM 运行时毫秒，但仍必须按玩家保存。

逐 note 差值只用于：

- DEBUG 验证 `observedOffset` 是否位于规范化 `J` 的预期量化邻域。
- 无法取得玩家 `UserOption` 时的诊断回退。
- 检测 raw TGrid 映射或玩家归属是否异常。

需要对非有限值和异常数据做安全回退：

```text
NaN / Infinity / 无法取得玩家 J -> 记录诊断日志，并使用经过明确定义的安全回退
```

不建议在 Release 中静默把异常玩家 J 当成逐 note 任意 float 长期参与缓存；否则会重新引入缓存碎片和难以复现的双玩家差异。

### 还原当前时间后再积分

建议新增语义明确的统一入口，例如：

```csharp
public float GetCurrentSoflanTimeWithOffsetsCached(
    int monitorId,
    float runtimeCurrentMsec,
    float runtimeChartOffsetMsec,
    float visualAudioOffsetMsec,
    int soflanGroup)
{
    float rawCurrentMsec =
        runtimeCurrentMsec
        - runtimeChartOffsetMsec
        + visualAudioOffsetMsec;

    if (rawCurrentMsec < 0f)
        rawCurrentMsec = 0f;

    return ConvertAudioTimeToY_PreviewMode(
        rawCurrentMsec,
        soflanGroup);
}
```

参数语义：

- `monitorId`：玩家/Monitor 身份，用于隔离 current-time 缓存和诊断数据。
- `runtimeCurrentMsec`：游戏当前时间 `t`。
- `runtimeChartOffsetMsec`：原版运行时轴相对 MA2 原始轴的偏移 `J`。
- `visualAudioOffsetMsec`：可开关的 MaiBug `a`；Touch 为 `0`。
- `soflanGroup`：物件所属 group。

必须在减去基础偏移并加入视觉偏移以后再执行负时间钳制：

```text
ClampToZero(t - J + a)
```

不能先钳制 `t`，再减 `J`。

### 可见性阶段

`GameCtrl.__SoflanNoteDecision()` 发生在物件实例 `Initialize()` 以前，因此应按当前 `GameCtrl.MonitorIndex` 取得该玩家规范化的 `J`：

```csharp
float runtimeChartOffsetMsec =
    SoflanVisualTiming.GetRuntimeChartOffsetMsec(MonitorIndex);

float currentSoflanTime =
    soflanManager.GetCurrentSoflanTimeWithOffsetsCached(
        MonitorIndex,
        NotesManager.GetCurrentMsec(),
        runtimeChartOffsetMsec,
        maiBugAdjustMsec,
        noteSoflanGroup);
```

`checkNoteVisible()` 继续使用 raw note msec 与 raw visible ranges 比较，不应改回 `note.time.msec`。

### Tap、Break 和 Star

在 `Initialize()` 中缓存：

```text
noteSoflanGroup
rawNoteMsec
runtimeChartOffsetMsec = 当前 Monitor 的规范化 GetAdjustMSec
noteSoflanTime = F_g(rawNoteMsec)
maiBugAdjustMsec
```

每帧统一计算：

```text
currentSoflanTime = F_g(t - runtimeChartOffsetMsec + a)
diffTime = noteSoflanTime - currentSoflanTime
```

Y、主物件缩放、Guide scale 和 Guide alpha 继续使用现有 `diffTime` 公式，不再增加第二套坐标补偿。

### Hold 和 BreakHold

初始化时分别保留 raw 头尾目标：

```text
headSoflanTime = F_g(headC)
tailSoflanTime = F_g(tailC)
runtimeChartOffsetMsec = 当前 Monitor 的规范化 GetAdjustMSec
```

每帧只计算一次还原后的 `currentSoflanTime`，再分别得到：

```text
headDiff = headSoflanTime - currentSoflanTime
tailDiff = tailSoflanTime - currentSoflanTime
```

建议在 DEBUG 或测试中同时计算：

```text
observedHeadOffset = AppearMsec - headC
observedTailOffset = TailMsec - tailC
```

两者都应位于规范化玩家 `J` 的预期量化邻域，但不要求逐浮点完全相等。头尾必须使用同一个玩家级 `J`，不能分别把两个 observed offset 当成两个当前时间原点，否则 Hold body 会产生额外抖动或长度差。

### TouchNoteB 和 TouchNoteC

Touch 保持 `visualAudioOffsetMsec=0`：

```text
currentSoflanTime = F_g(t - runtimeChartOffsetMsec)
```

仅替换原 Touch 动画使用的时间轴，不改变固定触摸区域、淡入、收束和 Notice 行为。

### 缓存键

当前缓存键只有：

```text
(group, audioOffsetMsec)
```

修复后必须至少包含：

```text
(monitor/player, group, runtimeChartOffsetMsec, visualAudioOffsetMsec)
```

其中 `runtimeChartOffsetMsec` 必须是玩家级规范化 `J`，不能是逐 note observed offset。否则同一帧中双玩家可能错误复用另一个时间轴结果，同一玩家又会因量化碎片生成大量键。

可以继续用 `currentMsec` 作为帧级外层失效条件，但 `CurrentSoflanTimeCacheKey.Equals()` 和 `GetHashCode()` 必须同时加入玩家身份和基础偏移。可见区缓存也要确认不会让 P1/P2 的 `currentSoflanTime`、`visibleMsec` 或 group 结果互相覆盖；若 Soflan 数据本身允许双玩家不同谱面，还需要把 BPM、SFL 和 note-index map 提升为玩家级状态。

## 副作用与风险审计

### 风险总表

| 风险 | 级别 | 表现 | 主要缓解方法 |
| --- | --- | --- | --- |
| 旧变速谱面时序改变 | 高 | 所有依赖旧错误时间轴运行的视觉会相对当前版本后移约 `J`，默认约 60 ms | 审计人工补偿谱面；分阶段部署；必要时提供临时回退开关 |
| 逐 note 浮点缓存碎片 | 高 | 1488 条记录产生 296 种 offset，current-time 缓存可能退化为接近逐 note 计算 | 使用每玩家规范化 `GetAdjustMSec()`，逐 note 差值只做诊断 |
| 双玩家设置不同 | 高 | P1/P2 的 `AdjustTiming`、谱面或难度可能不同，而当前 manager 和 noteIndex map 是全局的 | 缓存和谱面状态按 Monitor/player 隔离，至少保证 J 和 current-time key 隔离 |
| 只修部分入口 | 高 | Tap 对齐但注册、Hold、Break 或 Touch 仍错轴，产生新的局部不同步 | GameCtrl、所有支持类型和 DEBUG 数据在同一版本原子修复 |
| identity bypass 误判 | 中高 | 把曾经或将要变速的 group 当成 1x，会破坏累计 Y、停车、反向和重新注册 | 第一版只 bypass 完全没有显式 SFL 的 group；不要只看当前速度 |
| MaiBug 开关语义冲突 | 中 | 开关关闭时若直接回原版路径，会重新带回原版 MaiBug，使开关失效 | 对使用 MaiBug 的类型，关闭时不使用保留 MaiBug 的原版 bypass |
| 残余时间/TGrid 量化 | 低 | 核心修复后普通 Soflan 路径仍可能有约 1.356 px Y 差 | 真正无 SFL 的 group 可使用保守 identity bypass |
| 开场负时间钳制 | 低 | `t-J+a` 在歌曲最初可能小于 0，开场视觉会停在原始时间 0 | 在所有偏移合并后统一钳到 0；专项测试 bar 0 物件 |
| 额外 CPU/内存 | 低或高 | 规范化 J 仅增加少量字段和键；逐 note float 则可能显著增加转换和哈希次数 | 每玩家快照 J；避免每帧重复 TGrid 转换；复用 group 级缓存 |

### 旧谱面兼容性

时间轴修复会让当前错误版本中的 Soflan 视觉相对现在后移约 `J`。这是恢复原版坐标系的必然结果，但以下内容可能发生可见变化：

- 曾经按当前错误表现人工提前 SFL 边界的谱面。
- 曾经把 note 或 SFL 整体平移约 60 ms 进行补偿的谱面。
- 玩家已经按提前视觉形成肌肉记忆的谱面。

判定逻辑不变，但玩家会因为视觉提示恢复正确而改变实际输入时间，成绩和手感可能发生变化。部署前应检查是否存在已知人工补偿谱面；必要时可增加一个临时的兼容/回退开关用于 A/B 验证，但正确模式应是默认方向，不能长期把错误时间轴作为正常谱面语义。

### 双玩家和全局单例

原版 `NotesReader` 按 `_playerID` 加载各自谱面和 `GetAdjustMSec()`，`GameCtrl` 也持有各自的 `MonitorIndex`。当前 `SoflanManager` 却是全局单例，并使用以下全局状态：

```text
BpmList
SoflanListMap
noteIndex -> group
noteIndex -> head TGrid
noteIndex -> tail TGrid
visible-range cache
current-time cache
```

相同谱面、不同 `AdjustTiming` 的双玩家至少要求 current-time 和 visible-range 查询按玩家隔离。若允许双玩家选择不同难度或不同谱面，当前全局 BPM/SFL/noteIndex 映射本身已有覆盖风险；这是现有架构限制，不完全由本修复新引入，但本修复不能假装单一全局 `J` 可以解决它。

### 部分修复的副作用

时间轴修复必须作为一组原子变更完成。如果只修改 `NoteBase.GetNoteYPosition()`：

- `GameCtrl` 仍可能提前注册或漏注册物件。
- Break 的独立 scale 仍使用旧轴。
- Hold/BreakHold 头、尾和 body 几何仍使用旧轴。
- Touch 的淡入、收束和 Notice 仍使用旧轴。
- DEBUG 面板可能显示与实际视觉不同的 current speed 或 Soflan time。

### 明确不会改变的内容

正确实施时不应改变：

- 歌曲文件、播放速度或播放起点。
- MA2 的 BPM、TGrid、SFL 行和 note 行。
- music XML 启用记录。
- 原版 `NoteData.time.msec`、判定窗口和音效调度。
- FixedSoflan 当前支持类型范围。

修复只改变“运行时当前时间进入 Soflan 视觉积分以前使用哪个坐标原点”。

## 严格 1.0x 等价增强

### 为什么基础修复后仍可能不是逐浮点完全相同

完成 `t - J + a` 修复后，时间轴的系统性 `60/70 ms` 错误会消失。但当前 Soflan 路径仍执行：

```text
audio milliseconds
-> TimeSpan
-> nearest TGrid
-> Soflan Y
```

原版 1x 路径则直接使用连续毫秒差计算位移和缩放。

在把 `J` 正确消除、并启用 MaiBug 的零基础偏移对照中，已观察到的残余最大差异约为：

| 项目 | 最大残余差异 |
| --- | ---: |
| Y | 1.356262 px |
| 主物件 scale / Guide alpha | 0.00483154 |

这不是 60 ms 根因，但意味着“数学上同一时间轴”和“逐浮点、逐帧完全走同一公式”仍是两个不同等级的目标。

### identity group bypass

如果要求像 `_04` group `0` 这样的整条恒定 `1.0x` group 与 `_01` 从实例注册、位移、缩放到 Guide 渐显完全走原版路径，建议增加按 group 的 identity 判定：

```text
GroupRequiresSoflanMapping(group) == false
```

只有在整条 group 的有效 Soflan 轨道从头到尾都等价于 `1.0x` 时才允许 bypass。第一版实现建议更加保守：只 bypass **没有任何显式 SFL 记录** 的 group，例如本例 `_04` 的 group `0`。不能只检查“当前时刻速度等于 1”，因为：

- 之前的停车、反向或加减速可能已经改变累计 Soflan Y。
- 未来的回拉或反向可能要求物件重新进入可见区。
- 可见性是 Soflan 功能的一部分，不只是性能优化。

对于真正 identity 的 group：

- `GameCtrl.__SoflanNoteDecision()` 返回 `0`，使用原版可见性。
- Tap、Break、Star、Hold、BreakHold、Touch 使用原版渲染路径。
- 不经过音频毫秒到 TGrid 的往返，因此可以获得与无 SFL 谱面相同的原版公式和量化行为。

以下情况不能直接使用第一版原版 bypass：

- group 存在任何显式 SFL，即使这些行当前看起来都是 `1.0`。
- Tap-family note 启用了 FixedSoflan，例如 `#F600`；FixedSoflan 仍要改变玩家物件速度语义。
- `EnableSoflanMaiBugAdjust=0` 且该 note 类型原版会使用 MaiBug；直接回到原版会让开关失效。
- group 曾经停车、反向或加减速，只是当前瞬时速度回到 `1.0`。

### 与 MaiBug 开关的关系

`EnableSoflanMaiBugAdjust=1` 时，identity bypass 可以实现与 `_01` 的严格原版一致。

`EnableSoflanMaiBugAdjust=0` 时，用户明确要求取消 Soflan 视觉中的 MaiBug。此时与仍保留原版 MaiBug 的 `_01` 不可能逐值完全相同，这是开关定义产生的预期差异，而不是 bug。

修复后 600 速、1x、判定时应满足：

| 设置 | 期望 `diffTime` | 期望 Y | 含义 |
| --- | ---: | ---: | --- |
| 开关开启 | 约 10 ms | 约 393 | 与原版 MaiBug 一致 |
| 开关关闭 | 约 0 ms | 400 | 主动取消 MaiBug，但与歌曲/判定对齐 |

如果产品目标是“不论开关都与 `_01` 完全相同”，则该开关本身没有实际意义，应另行废弃；不能同时要求“关闭原版 MaiBug”和“与保留 MaiBug 的原版逐值相同”。

## 不推荐或错误的修复方法

### 给 note 目标直接加 60 ms

错误示例：

```text
noteSoflanTime = F_g(C + 60)
```

在恒定 1x 下看似能抵消偏差，但跨越 SFL 边界时会把 note 送进错误的停车、反向或加减速区段。SFL 命令绑定的是 MA2 TGrid，目标必须保持 `F_g(C)`。

### 写死减去 60 ms

`J` 会随 `AdjustTiming` 改变，双玩家也可能不同。硬编码只能修默认设置，并会在其他设置下重新产生系统误差。

### 移动歌曲、音效或整体平移 MA2

这会破坏无 SFL 谱面、判定窗口、XML 数据和其他物件。当前问题只存在于 Soflan 视觉时间轴，不应扩展成音频或谱面变更。

### 只把 EnableSoflanMaiBugAdjust 设为 1

这只能把默认 600 速下的误差从约 `70 ms` 降到约 `60 ms`，不能补回基础 `J`。

### 只对 group 0 特判减 60 ms

带实际 SFL 的其他 group 同样使用错误时间原点。group 0 只是最容易证明问题的反例，不是唯一受影响对象。

### 只修 NoteBase 的 Y

这样会留下以下不同步：

- GameCtrl 仍提前注册或漏注册物件。
- Hold 头尾和 body 几何仍错误。
- Break 独立 scale 仍错误。
- Touch 淡入、收束和 Notice 仍提前。
- DEBUG 面板与实际视觉时间可能使用不同坐标。

## 建议修改位置

| 文件或模块 | 建议修改 |
| --- | --- |
| `SoflanSupport/SoflanManager.mm.cs` | 增加运行时到 MA2 原始时间轴的统一转换；缓存键和可见区状态按玩家隔离；记录具有显式 SFL 的 group |
| `SoflanSupport/SoflanVisualTiming.mm.cs` | 按 Monitor/player 返回本局规范化 `GetAdjustMSec()`；统一异常回退和诊断语义 |
| `Monitor.Game.GameCtrl.mm.cs` | 可见性查询前按 `MonitorIndex` 取得玩家 J，使用修正后的 current Soflan time；可选保守 identity group bypass |
| `Monitor.NoteBase.mm.cs` | 按 `MonitorId` 缓存玩家级基础偏移；Tap/Star 共用修正后的 current time；更新 DEBUG 数据 |
| `Monitor.BreakNote.mm.cs` | Break 独立缩放使用同一个基础偏移 |
| `Monitor.HoldNote.mm.cs` | 头尾目标分开，当前端统一减基础偏移 |
| `Monitor.BreakHoldNote.mm.cs` | 与 Hold 相同，并保留 BreakHold 特效 |
| `Monitor.TouchNoteB.mm.cs` | Touch 使用 `t-J`，MaiBug 仍为零，保留原动画语义 |
| `SoflanCalculator/SoflanCalcEngine.cs` | 增加 runtime offset 输入和输出，模拟真实 NotesReader 时间轴 |
| `tools/SoflanMaiBugTests/Program.cs` | 测试非零 `J`、不同 AdjustTiming、可见性、Hold 头尾、Touch 和 identity group |
| `docs/soflan-system.md` | 实现完成后更新正式运行时公式和验证要求 |
| `docs/fixed-soflan.md` | 实现完成后更新 FixedSoflan 当前时间公式和缓存语义 |

本修复不需要修改：

- `example_01.ma2` 或 `example_04.ma2`
- music XML 启用记录
- 歌曲文件
- 原版判定时间和音效调度
- FixedSoflan 支持类型范围

## 阶段 1～4 实施结果

### 运行时代码

- 新增 `SoflanRuntimeTime.ToRawChartAudioMsec()`，唯一公式为
  `ClampToZero(runtimeCurrentMsec - runtimeChartOffsetMsec + visualAudioOffsetMsec)`。
- `NotesReader._playerID` 在 IL 中显式传给清理、composition 加载和 note 加载辅助方法。
- `GameCtrl.monitorIndex` 在 IL 中显式传给逐玩家缓存清理和可见性判断。
- `SoflanManager` 以 `PlayerSoflanState` 隔离每名玩家的 BPM、SFL、note group/TGrid、运行时基础偏移、当前时间缓存和可见范围缓存。
- `runtimeChartOffsetMsec` 在 composition 加载时从同一玩家的
  `UserOption.GetAdjustMSec()` 快照；未写死 `60ms`，也未采用逐 note 的量化差值。
- 可见性、Tap/Break/Star、Hold/BreakHold 和 TouchNoteB/C 全部接入同一入口。
- 歌曲播放、`NoteData.time.msec`、判定窗口和击打音效调度未修改。

### 自动对比结果

对实际 `example_04.ma2` 的复杂组执行了原始轴参考实现与修正运行时轴实现的密集对比：

```text
groups=11
samples=9566
timelineComparisons=315678
visibilityComparisons=825
maxRawInputDelta=0.001953125ms
maxSoflanYDelta=0.000000000
```

测试包含 `0x` 停车、负速反向、加减速、多 group、BPM 边界和可见范围查询。
`maxSoflanYDelta=0` 证明修复只平移输入时间原点，没有改变复杂 Soflan 的积分轨迹。

对 `example_01.ma2` 与 `example_04.ma2` 的恒定 1x、同物件 Tap/Break/Star 全阶段对比：

```text
notes=1106
frames=8848
maxDiffDelta=1.875000000ms
maxYDelta=1.071868896px
maxObjectScaleDelta=0.004687548
maxGuideScaleDelta=0.002871096
maxGuideAlphaDelta=0.004687548
stateMismatches=0
```

上述残差来自 .NET Framework 音频毫秒到 TGrid 的量化往返，不是原先约
`60/70ms` 的时间轴错误；整个采样中没有 `Init/Scale/Move` 状态分歧。若产品要求逐浮点完全相同，仍需实施后文的 identity group bypass。

### 构建与补丁验证

- Release 构建：0 warning / 0 error。
- Debug 构建：0 error；保留目标 AMD64 与补丁 MSIL 的既有架构提示。
- `SoflanMarkerTests`、`SoflanLogTests`、`SoflanMaiBugTests` 全部通过。
- 使用项目对应的经典 MonoMod `v20.5.21.5`，对当前游戏实际目标
  `Assembly-CSharp.dll`（SHA-256
  `257CC5F7733E6F48D75627DB32DC182F7C00D267CEA065F6AA6DDB6E2A2A0985`）应用补丁成功。
- 补丁后 IL 已确认 `_playerID`、`monitorIndex` 参数顺序正确，且所有受支持视觉对象都调用带 player 参数的统一时间轴 API。
- 与当前 `AssetsPatch`、`DpPatches`、`MineSupport`、`SoflanSupport` 四个 `.mm.dll`
  按实际顺序联合应用成功；结构检查确认 3 个 NotesReader hook、2 个 GameCtrl hook、
  6 类统一时间轴调用方和 Mine relink 同时保留。
- Release/Debug 输出目录均只含 `.mm.dll` 和 `.pdb`。

## 建议实现顺序（实施记录）

1. 在计算器和测试中引入玩家级 `runtimeChartOffsetMsec`，先建立能复现当前 60/70 ms 错误的失败测试。
2. 增加逐 note offset 碎片回归测试，确认旧算法在 `_04` 上产生 296 种值，而新算法每玩家只有一个规范化 J。
3. 在 `SoflanVisualTiming` 或等价入口按 Monitor/player 快照真实 `GetAdjustMSec()`。
4. 在 `SoflanManager` 增加统一的 `t - J + a` 转换入口，并把玩家身份和规范化 J 纳入 current-time/visible-range 缓存设计。
5. 修正 `GameCtrl` 可见性，使物件注册与渲染使用相同玩家时间轴。
6. 在同一版本修正 Tap/Star/Break、Hold/BreakHold 和 TouchNoteB/C，避免部分类型错轴。
7. 完成核心修复和双玩家验证后，再增加保守 identity group bypass；第一版只处理没有显式 SFL 的 group。
8. 更新 DEBUG 面板，显示 monitor/player、runtime time、规范化 J、逐 note observed offset、raw current、MaiBug 和最终 current Soflan time。
9. 完成 Release/Debug 构建、单元测试、二进制静态检查、旧谱面兼容性检查和游戏内对照。
10. 实现完成后更新 `soflan-system.md`、`fixed-soflan.md` 和部署说明。

## 测试设计

### 基础时间偏移测试

至少覆盖：

| AdjustTiming | 期望 `J` 约值 |
| ---: | ---: |
| 0 | 26.666666 ms |
| 20 | 59.999996 ms |
| 40 | 93.333328 ms |

对每个值验证：

```text
playerRuntimeChartOffset == UserOption.GetAdjustMSec()
rawCurrent == runtimeCurrent - J + a
```

逐 note 诊断值不应要求严格等于 J，而应验证：

```text
observedOffset = runtimeNoteMsec - frameworkRawNoteMsec
observedOffset 位于 J 的预期量化邻域
observedOffset 不直接进入 current-time 缓存键
```

还应覆盖两个不同玩家偏移在同一帧中的缓存隔离，以及相同玩家大量 note 不会生成数百个 offset cache key。

### 1.0x 原版对照

以第 1654 行为主对照：

- 开关开启：`_04` 的状态、Y、主 scale、Guide scale 和 Guide alpha 应与 `_01` 对齐。
- 开关关闭：判定时应为 `Y=400`，只保留取消 MaiBug 的预期差异，不得再提前约 60 ms。
- 从初始注册前一直采样到 `EndNote()`，不能只检查判定时单点。
- 若启用 identity bypass，开关开启时应验证两者走同一原版分支。

### 非 1.0x 和跨边界

至少测试：

- `2.0x`
- `0.5x`
- `0x` 停车
- 负速反向
- 弹跳或多次回拉
- note 的视觉窗口跨越 SFL 起点或终点
- BPM 变化与 SFL 边界重合或相邻

重点证明偏移是在进入 `F_g` 前应用：

```text
F_g(t - J + a)
```

不能使用：

```text
F_g(t + a) - J
```

因为非 1x、停车和反向时 Soflan Y 距离不是普通毫秒距离。

### 可见性测试

验证：

- 1x 开关开启时注册时机与原版一致。
- 1x 开关关闭时只移除 MaiBug 造成的视觉窗口差，不缺失基础时间偏移。
- 停车期间不会丢 note。
- 反向或弹跳后 note 可以重新进入可见范围。
- identity group 使用原版可见性，非 identity group 使用 Soflan visible ranges。
- P1/P2 不同 `AdjustTiming` 时，各自的 visible range 不互相复用或覆盖。
- 如支持双玩家不同难度/谱面，BPM、SFL、group 和 noteIndex map 也必须按玩家隔离。

### Hold 和 BreakHold

以第 1653 行为 group 0 对照，并增加实际变速 group 用例：

- 头部 Y
- 尾部 Y
- body 长度
- body 中心
- body 高度
- 头尾缩放
- 进入、持续、到尾和回收阶段
- head offset 与 tail offset 在容差内一致

### Touch

验证 TouchNoteB/C：

- 仍固定在触摸区域。
- 淡入时间对齐。
- 收束动画对齐。
- Notice 时间对齐。
- 不受 `EnableSoflanMaiBugAdjust` 影响。
- 会正确减去 runtime chart offset。

### FixedSoflan

验证：

- `#NF600` 在不同玩家物件速度下保持相同进度。
- 开关开启时 600 固定速度在判定时约 `Y=393`。
- 开关关闭时判定时 `Y=400`。
- FixedSoflan 只影响现有 Tap 白名单，不顺带扩展 Hold 或 Touch。

### 无 SFL 回归

无 SFL 谱面必须继续满足：

- `containsSoflans(playerId)==false`
- `GameCtrl` 返回原版路径
- 所有物件行为不变
- 不产生额外 current Soflan time 计算

### 构建和现有测试

```powershell
dotnet build -c Release Assembly-CSharp.SoflanSupport.mm.csproj
dotnet build -c Debug Assembly-CSharp.SoflanSupport.mm.csproj
dotnet run --project tools/SoflanMarkerTests/SoflanMarkerTests.csproj -c Release
dotnet run --project tools/SoflanLogTests/SoflanLogTests.csproj -c Release
dotnet run --project tools/SoflanMaiBugTests/SoflanMaiBugTests.csproj -c Release
```

现有 `SoflanMaiBugTests` 必须先扩展为包含真实非零 `J` 的模型，否则测试通过仍不足以证明游戏内等价。

### 游戏内验收

建议同时录像或逐帧截图对照：

1. 播放 `example_01.ma2`。
2. 播放 `example_04.ma2`。
3. 使用相同玩家物件速度、NoteSize、镜像和 AdjustTiming。
4. 对照第 1654 行 Tap 和第 1653 行 Hold。
5. 分别验证开关开启和关闭。
6. 使用 DEBUG 面板记录以下值：

```text
RuntimeCurrentMsec
RuntimeChartOffsetMsec
RawCurrentMsec
MaiBugAdjustMsec
AdjustedRawCurrentMsec
NoteRawMsec
NoteSoflanTime
CurrentSoflanTime
DiffTime
Y
BodyScale
GuideScale
GuideAlpha
```

### 验收标准

必须同时满足：

- 默认 `J≈60 ms` 不再造成 Soflan 物件整体提前。
- `EnableSoflanMaiBugAdjust=0` 只取消 MaiBug，不再取消基础时间偏移。
- 1x 开关开启时与原版轨迹对齐。
- 1x 开关关闭时在原版判定时间到达 `EndPos`。
- Tap、Break、Star、Hold、BreakHold、Touch 使用一致的基础时间轴。
- 可见性注册与物件实际视觉阶段一致。
- 停车、反向、弹跳和跨 SFL 边界行为不回归。
- 歌曲播放、判定时间和音效调度没有改动。
- 若启用 identity bypass，真正恒定 `1.0x` group 在开关开启时与原版走同一代码路径。

## 临时规避办法

历史上的临时规避办法如下；阶段 1～4 实施后不应再依赖这些办法：

- 使用没有 SFL 的 `_01` 可以完全回到原版路径。
- 把 `EnableSoflanMaiBugAdjust` 设为 `1` 只能减少约 `10 ms` 误差，不能解决缺失的约 `60 ms`。
- 不建议通过移动歌曲、XML、MA2 note 或 SFL 行来补偿运行时代码错误。

## 最终建议

阶段 1～4 已完成以下正式修复，并保留第 7 项作为可选增强：

1. 所有 Soflan 视觉入口统一使用 `F_g(t - J + a)`。
2. `J` 按 Monitor/player 使用本局规范化 `UserOption.GetAdjustMSec()`，不写死、不使用逐 note 量化差值作为主时间原点。
3. 可见性、Tap、Break、Star、Hold 头尾、BreakHold 和 Touch 全部使用同一个基础偏移语义。
4. current-time 和 visible-range 缓存按玩家隔离；缓存键包含规范化基础偏移和 MaiBug 偏移。
5. 测试模型显式包含真实 `GetAdjustMSec()`。
6. 每名玩家只保存一个规范化 J，逐 note observed offset 不进入缓存键，避免 1488 条 note 重新产生 296 种 offset 键。
7. 如需真正恒定 1x group 与无 SFL 谱面逐公式、逐量化完全一致，再增加保守 identity group bypass；第一版只绕过没有显式 SFL 且不违反 FixedSoflan/MaiBug 开关语义的物件。

当前实现只修复 Soflan 视觉坐标系，不触碰歌曲、判定或谱面数据；复杂变速对比结果证明其积分轨迹保持不变。
