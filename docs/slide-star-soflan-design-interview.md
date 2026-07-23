# Slide 内部星标 Soflan 支持设计访谈

## 文档状态

- 状态：候选最终方案已保存，暂不执行；等待后续总确认或实施指令。
- 创建日期：2026-07-16。
- 最后更新：2026-07-17。
- 当前轮次：访谈暂停，未进入实现。
- 更新规则：每轮只处理一个决策；收到回答后，立即记录用户结论、推荐方案、理由和由此解锁的后续分支。

## 计划目标

为 Slide 的内部星标运动接入现有 MA2 `SFL` 视觉时间轴，使其能够表现加速、减速、停车、反向或弹跳等视觉变速，同时不改变音频时间和判定时间。

## 已由代码确认的事实

1. `SlideRoot.MoveStarLane()` 控制普通 Slide、Break Slide 以及由多个 `ConnectSlide` 拼接而成的单颗内部星标。
2. `SlideFan.MoveStarLane()` 覆盖基类实现，独立控制扇形 Slide 的三颗内部星标。
3. 外圈飞向判定位置的 `StarNote` 是独立音符对象，已经通过 `NoteBase` 的现有逻辑支持 Soflan；它不是本次所说的 Slide 内部星标。
4. 普通 Slide 当前使用真实时间进度：`(currentMsec - StarLaunchMsec) / (StarArriveMsec - StarLaunchMsec)`，再按路径累计几何距离插值。
5. Fan 的三颗星使用同一个真实时间进度，并分别映射到三条路径。
6. 内部星标运动不读取触摸进度；触摸只推进 `HitArea`、箭头擦除和最终判定。
7. 路径箭头的预显示、触摸检查、AutoPlay 进度、`lastWaitTime`、判定及回收目前都使用真实时间或输入状态。
8. 当前 `SoflanManager.IsSupportedVisualSoflanKind()` 未包含 `Slide`、`BreakSlide`、`ExSlide`、`ExBreakSlide` 或 `ConnectSlide`。
9. FixedSoflan 当前明确只支持 Tap 系，不支持 Slide。
10. 现有 Soflan 系统的基本约束是视觉专用：不得改动音频时间和判定时间。

## 暂定设计原则

- 判定、音频和触摸输入继续使用真实时间。
- 无 `SFL` 的谱面必须完整回退到原版行为。
- 普通、Break、EX 变体应尽量共享同一套运动进度算法。
- 在定义进度公式前，先锁定对象边界、端点语义、连续 Slide 语义以及停车/反向行为。
- FixedSoflan for Slide 不在本计划范围内，不设计、不实现，也不纳入后续决策。

## 决策记录

| 编号 | 决策 | 状态 | 推荐答案 |
| --- | --- | --- | --- |
| D1 | 第一版影响哪些 Slide 视觉对象 | 已确认 | 只改变 `SlideRoot` 与 `SlideFan` 的内部星标；不扩大到外部 `StarNote`、路径箭头、触摸、判定和回收；不考虑 FixedSoflan for Slide |
| D2 | 星标路径进度的 Soflan 锚定语义 | 已确认 | 使用发射点锚定的视觉时间增量，保留 Soflan 倍率本身对运动速度的作用；raw progress 持续计算，路径取点使用非粘滞钳制 |
| D3 | `rawProgress` 越界时的可见性 | 已确认 | `rawProgress < 0` 或 `rawProgress > 1` 时 alpha 设为 `0`；回到 `[0, 1]` 时重新显示 |
| D4 | 真实到达时刻与原版生命周期 | 由 D1/D2 派生确认 | `StarArriveMsec` 不强制吸附视觉终点；判定、等待和回收继续使用真实时间，可在星标位于路径中段时回收 |
| D5 | 发射前淡入与缩放使用哪条时间轴 | 已确认 | 淡入和缩放也使用所属 group 的 Soflan 时间；采用发射点锚定公式，保证发射时连续 |
| D6 | 出现与路径运动是否仍由真实 `StarLaunchMsec` 分段 | 已确认 | 保留真实发射门槛；发射前只计算出现动画，发射后只计算路径 progress，不因倒退跨阶段重播 |
| D7 | 连续 `ConnectSlide` 的路径进度模型 | 已确认 | 保留原版整链统一进度：第一段发射、最终段到达、合并总路径，中间段 timing 不单独介入 |
| D8 | 连续 Slide 的 Soflan group 来源 | 已确认 | 整颗内部星标只使用根 Slide 的 `#group`；默认 group `0`，不在连接点切换 group |
| D9 | `ConnectSlide` marker 与根 group 冲突时如何处理 | 已确认 | 加载期 fail-fast：未声明或同 group 可接受，不同 group 写日志并抛 `FormatException` |
| D10 | Slide 何时从对象池注册 | 已确认 | 原版真实时间预注册与 Soflan 出现窗口取 OR；使用 Slide 专用判断，不复用以 `note.time` 为锚的通用判断 |
| D11 | 提前注册导致对象池容量不足时如何处理 | 明确暂不纳入 | 保持原版固定池和分配逻辑；记录极端 Soflan 下的容量与资源复用风险，不在本计划修复 |
| D12 | 路径 progress 减小时星标朝向 | 已确认 | 朝实际运动方向：倒退时在路径切线基础上加 `180°`，停车时保持最后非零方向 |
| D13 | 退化 timing、非有限数和路径查询失败如何处理 | 已确认 | 静态无效谱面加载期拒绝；合法零等待跳过淡入；运行时异常按 note 一次性记录并降级原版运动 |
| D14 | 是否增加 Slide DEBUG 可观测性 | 明确不纳入 | 不扩展现有 Soflan 面板，不新增 Slide 注册表、选择 UI 或专项运行时快照 |
| D15 | 实现完成需要达到哪一级验证 | 已确认 | Release/Debug 构建、补丁后静态检查、离线数值 oracle 与游戏内视觉/判定回归全部通过 |

## 已确认：D1 第一版对象边界

建议第一版覆盖：

- `SlideRoot` 的内部星标，包括普通、Break、EX 以及连续 Slide 共用的星标。
- `SlideFan` 的三颗内部星标。

建议第一版明确排除：

- 外圈独立 `StarNote`，因为它已有 Soflan 支持。
- 路径箭头的出现与消失。
- 触摸 `HitArea` 推进、AutoPlay、判定窗口、音效与对象回收。
- FixedSoflan for Slide，不属于本计划。

推荐理由：这与“Slide 的星标运动支持变速”的目标严格一致，也能避免视觉时间轴意外进入玩法和判定逻辑。普通 Slide 与 Fan 分别覆盖各自的 `MoveStarLane()`，否则 Fan 会成为明显的功能缺口。

用户结论：接受推荐边界，并明确暂时不考虑 FixedSoflan for Slide。

派生约束：

- `SlideRoot` 与 `SlideFan` 都必须覆盖，不能只修改基类实现。
- 外部 `StarNote` 继续使用现有 `NoteBase` Soflan 支持。
- 路径箭头、触摸、AutoPlay、判定、音效和回收继续使用原有真实时间或输入状态。
- 后续设计中不得引入 Slide FixedSoflan marker 或固定速度算法。

## 已确认：D2 运动进度的 Soflan 锚定语义

设：

```text
L = StarLaunchMsec
R = StarArriveMsec
D = R - L
S(t) = 所属 group 在真实时间 t 对应的 Soflan 视觉时间
```

### 方案 A：发射点锚定，推荐

```text
progress = (S(currentMsec) - S(L)) / D
```

行为：

- `1x` 与无 Soflan 时完全等价于原版。
- `2x` 时星标按两倍速度前进，可能在真实到达时间前走到终点。
- `0x` 时进度不变，星标停车。
- 负速时进度减小；只要进度仍在路径范围内，星标就沿路径反向运动。
- 发射前发生的 Soflan 累计偏移会被 `S(L)` 抵消，发射瞬间仍严格位于路径起点。
- 星标是否允许在真实到达时间后仍未到终点、以及进度越界后如何显示，留给后续边界决策。

#### 方案 A 的 progress 状态说明

仅有 progress 公式还不足以定义越界显示。以下采用推荐的“两层进度、非粘滞钳制”来说明：

```text
rawProgress = (S(currentMsec) - S(L)) / D
displayProgress = Clamp01(rawProgress)
targetDistance = totalPathLength * displayProgress
```

`rawProgress` 每帧持续按 Soflan 时间重算，不会因为曾经小于 `0` 或大于 `1` 就永久锁死；`displayProgress` 只负责保证路径取点不会越界。

| `rawProgress` | 星标状态 | 后续能否重新进入路径 |
| ---: | --- | --- |
| `< 0` | 逻辑位置按路径起点，即 `displayProgress = 0` 处理；是否隐藏由 D3 决定 | 能；之后增大并越过 `0` 时重新向前运动 |
| `= 0` | 位于路径第一个点；这是发射瞬间的连续状态 | 能向前，也能因负速继续停靠在起点边界 |
| `0 < progress < 1` | 按整条路径累计几何距离插值；例如 `0.25` 位于总长度的 25%，不是第 25% 个采样点或箭头 | 能；progress 增大时向前，减小时沿原路径后退 |
| `= 1` | 位于最终路径点 | 能；之后减到 `< 1` 时从终点反向重新进入路径 |
| `> 1` | 逻辑位置按路径终点，即 `displayProgress = 1` 处理；是否隐藏由 D3 决定 | 能；原始进度继续计算，之后减到 `< 1` 时重新进入路径 |

处于 `[0, 1]` 路径区间时，保持原版的完全可见状态：`alpha = 1`，缩放为 `1.5 * SlideSize`。越界时是否隐藏由 D3 决定，但不重新触发发射前的淡入和缩放动画。

progress 的变化方向直接由当前 Soflan 速度决定：

| 当前 Soflan 速度 | progress 和运动表现 |
| ---: | --- |
| `> 0` | progress 增大，星标向终点前进 |
| `= 0` | progress 不变，星标停在当前位置 |
| `< 0` | progress 减小；在 `(0, 1)` 内沿路径倒退，到达 `0` 后停靠起点 |

举例，若 `D = 1000ms`：

- 从发射开始保持 `1x` 250ms，`progress = 0.25`。
- 从发射开始保持 `2x` 250ms，`progress = 0.5`。
- 已到 `progress = 0.4` 时切到 `0x`，之后持续保持 `0.4`。
- 已到 `progress = 0.4` 时切到 `-1x` 200ms，退回 `progress = 0.2`。
- 到达 `progress = 1.2` 时逻辑位置钳制在终点；如果之后反向降到 `0.9`，星标会重新进入路径。越界期间是否可见由 D3 决定。

普通/连续 Slide 使用 `totalPathLength * displayProgress` 在合并后的总路径上取点。Fan 的三颗星共享同一个 progress，但分别乘以各自路径总长度，因此仍然同时到达各自相同比例的位置。

SFL 速度切换只改变 `S(t)` 的斜率，视觉时间本身连续，所以正常的速度切换不会让星标瞬移。帧间跨过较大距离时仍可能出现普通的低帧率跳步。

尚未由 D2 决定的边界：

- `StarArriveMsec` 到来时，若 `rawProgress != 1`，是否继续按 rawProgress 显示、强制吸附终点，或直接服从原有回收。
- 倒退时星标朝向是否保持路径正向切线，还是翻转 `180` 度朝向实际运动方向。
- `D <= 0` 或计算结果非有限数时的退化策略。

这些边界依赖 D2 是否采用方案 A，将在 D2 确认后逐项询问。

### 方案 B：发射和到达双端归一化

```text
progress = (S(currentMsec) - S(L)) / (S(R) - S(L))
```

行为：发射时恒为 `0`，到达时恒为 `1`。但如果整段恒定为 `2x`，分子和分母会同时加倍，最终运动仍与原版同速，恒定倍率本身被抵消；当 `S(R) == S(L)` 时还会出现不可定义的分母。

### 方案 C：到达点锚定

```text
progress = 1 - (S(R) - S(currentMsec)) / D
```

行为：真实到达时恒为 `1`，但发射时不保证为 `0`。如果发射到到达区间的累计视觉距离不是 `D`，星标会在发射瞬间跳到路径中段或路径外，不符合现有发射语义。

推荐方案 A。它是真正把 Slide 动画的“已流逝时间”替换为 Soflan 视觉时间增量；不会抵消恒定速度倍率，同时自然支持停车、反向和弹跳。其代价是视觉终点不再天然绑定真实到达时间，但这正是后续需要显式定义的视觉边界，而不是在进度公式里偷偷归一化掉变速。

用户结论：同意方案 A，以及 raw progress 持续计算、路径取点非粘滞钳制的状态模型；提出越界时隐藏星标的要求，转入 D3。

## 已确认：D3 progress 越界时的可见性

用户原话为“`> 1` 和 `< 1` 是否考虑隐藏”。若 `< 1` 按字面执行，则正常运动区间 `0 <= progress < 1` 都会隐藏，星标只会在恰好等于 `1` 时出现，这与“显示沿路径变速运动”的目标冲突。后续已确认实际下界是 `< 0`。

### 方案 A：仅在有效路径区间显示，推荐

```text
visible = 0 <= rawProgress && rawProgress <= 1
displayProgress = Clamp01(rawProgress)
```

行为：

- `rawProgress < 0`：逻辑位置仍钳制在起点，但主 Sprite alpha 为 `0`。
- `0 <= rawProgress <= 1`：alpha 为 `1`，正常显示和运动；边界 `0`、`1` 本身可见。
- `rawProgress > 1`：逻辑位置仍钳制在终点，但主 Sprite alpha 为 `0`。
- raw progress 从越界范围返回 `[0, 1]` 时立即重新显示，因此反向和弹跳可以重新进入路径。
- 不使用 `SetActive(false)`，避免破坏再次进入路径所需的对象状态。
- 普通、Break、Fan 都通过主 `SpriteRenderer.color.a` 隐藏；Break 星标闪光本来就乘以主 Sprite alpha，因此也会一并隐藏。

优点：星标只在 Soflan 视觉时间真正落入 Slide 路径定义域时存在，不会长时间钉在起点或终点；反向重新进入时仍可恢复。

代价：越过边界时是直接显隐。由于显隐发生在路径端点，通常不会表现为空间跳跃，但高速跨界时可能只显示很短时间。

### 方案 B：越界后继续显示在端点

保持此前的钳制显示：`rawProgress < 0` 时显示在起点，`rawProgress > 1` 时显示在终点。优点是视觉稳定；缺点是 Soflan 时间已经离开 Slide 区间后，星标仍会钉在端点。

### 方案 C：非对称处理

例如 `< 0` 隐藏、`> 1` 保留终点，接近原版“到达后留在终点”的表现。此方案会让正向和反向越界语义不对称，不推荐作为变速系统的默认规则。

推荐方案 A，并确认用户原意中的下界是 `< 0` 而不是 `< 1`。

用户结论：确认采用方案 A；实际下界为 `< 0`。仅当 `rawProgress` 位于闭区间 `[0, 1]` 时显示星标，越界时使用 alpha 隐藏，并允许返回区间后重新显示。

## 派生确认：D4 真实到达时刻与原版生命周期

该项可以由已确认的 D1 和 D2 直接推出，无需另改玩法规则：

- `StarArriveMsec` 继续参与原版判定、`lastWaitTime` 和 TooLate 逻辑。
- Soflan 运动分支不会在真实时间到达 `StarArriveMsec` 时把 `rawProgress` 强制改成 `1`。
- Slide 对象仍然每帧先更新星标视觉，再执行使用真实时间和触摸状态的 `NoteCheck()`。
- 判定成立后，原版会递减 `lastWaitTime`；等待耗尽或 TooLate 时关闭内部星标和整个 Slide 对象。
- 因此 `StarArriveMsec` 到来时若 `rawProgress < 1`，星标可以暂时仍显示在路径中段，随后被原版生命周期回收。
- 若此时 `rawProgress > 1`，星标已经按 D3 隐藏；回收逻辑仍照常执行。
- 不为视觉变速延长对象池占用，也不允许星标 progress 反向影响成绩或输入。

这保留了“视觉时间轴可以偏离、玩法时间轴不变”的系统边界。

## 已确认：D5 发射前淡入与缩放使用 Soflan 时间

原版内部星标在路径运动前有单独阶段：

```text
currentMsec <= AppearMsec
    alpha = 0
    scale = 0.75 * SlideSize

AppearMsec < currentMsec <= StarLaunchMsec
    appearProgress = (currentMsec - AppearMsec)
                   / (StarLaunchMsec - AppearMsec)
    alpha = appearProgress
    scale = (0.5 + appearProgress) * SlideSize
    position = 路径起点
```

用户结论：出现阶段也按 Soflan 时间计算，并要求先根据全部既定需求预计算状态和实现依赖。

### 预计算结论：统一使用发射点视觉偏移

不能直接使用此前举例的出现点锚定公式：

```text
(S(currentMsec) - S(AppearMsec))
/ (StarLaunchMsec - AppearMsec)
```

因为它不能保证真实发射时刻等于 `1`，会导致发射瞬间透明度或尺寸跳变。推荐让出现和路径运动共享同一个发射点视觉偏移：

```text
A = Appear 对应的原始音频时间
L = Shoot/StarLaunch 对应的原始音频时间
R = 最终 Arrive 对应的原始音频时间

appearDuration = L - A
motionDuration = R - L
launchSoflanTime = S(L)
visualOffset = S(currentMsec) - launchSoflanTime

appearRawProgress = 1 + visualOffset / appearDuration
pathRawProgress = visualOffset / motionDuration
```

在无 Soflan 或恒定 `1x` 下：

```text
appearRawProgress = (currentMsec - A) / (L - A)
pathRawProgress = (currentMsec - L) / (R - L)
```

因此精确退化为原版进度。

### 推荐的阶段内状态

在暂时保留真实发射时刻作为阶段门槛的前提下：

```text
currentMsec <= L:
    若 0 <= appearRawProgress <= 1:
        position = 路径起点
        alpha = appearRawProgress
        scale = (0.5 + appearRawProgress) * SlideSize
    否则:
        alpha = 0

currentMsec > L:
    若 0 <= pathRawProgress <= 1:
        alpha = 1
        scale = 1.5 * SlideSize
        position = 按累计几何距离插值
    否则:
        alpha = 0
```

在 `currentMsec == L` 时必有：

```text
visualOffset = 0
appearRawProgress = 1
pathRawProgress = 0
```

所以发射前最后一帧与发射后第一帧都位于路径起点，且均为 `alpha = 1`、`scale = 1.5 * SlideSize`，不存在状态跳变。

### 恒定正速度的预计算

若发射附近速度恒为正数 `speed`：

```text
出现动画进入点 = L - appearDuration / speed
路径视觉终点 = L + motionDuration / speed
```

示例设 `A = 1000ms`、`L = 1500ms`、`R = 2500ms`：

| 恒定速度 | 出现动画开始 | 发射 | progress 到 `1` | 真实 `A` 时出现进度 | 真实 `R` 时路径进度 |
| ---: | ---: | ---: | ---: | ---: | ---: |
| `0.5x` | `500ms` | `1500ms` | `3500ms` | `0.5` | `0.5` |
| `1x` | `1000ms` | `1500ms` | `2500ms` | `0` | `1` |
| `2x` | `1250ms` | `1500ms` | `2000ms` | `-1`，隐藏 | `2`，隐藏 |

由此可见：

- 加速会推迟出现动画的真实开始时刻，同时加快淡入、缩放和路径运动。
- 减速会把出现动画的真实开始时刻提前，同时减慢整套动画。
- 停车使 `visualOffset` 不变，因此无论处于淡入还是路径运动，当前 alpha、scale 和位置都会冻结。
- 负速使对应阶段的 progress 下降；在发射前可以倒放淡入，在发射后可以沿路径倒退。
- 真实 `R` 仍不强制 progress 为 `1`，对象生命周期继续遵循 D4。

### 初始化时需要预计算和缓存的数据

`SlideRoot.Initialize()` 与 `SlideFan.Initialize()` 需要缓存：

```text
SoflanManager 引用
是否存在 SFL
根 Slide 的 Soflan group
launchSoflanTime
appearDuration
motionDuration
```

时间锚建议来自 `NotesTime.grid`，经 `TGrid -> audio time / Soflan Y` 转换，而不是只使用可能经过运行时修正的裸 `msec`：

- `A`：根 Slide 的 `note.time`。
- `L`：根 Slide 的 `note.slideData.shoot.time`。
- `R`：普通/Fan 使用根 Slide 的 `arrive.time`；连续 Slide 使用最后一个 `ConnectSlide` 的 `arrive.time`。

项目现有 `SoflanManager` 只缓存 note 头尾 TGrid，没有缓存 Slide shoot TGrid。实施时需要增加一个接受 `NotesTime` 的 grid-native 转换入口，或为 Slide 单独登记 shoot/最终 arrive 锚点，避免重新退回不一致的裸 msec 轴。

每帧只需要调用一次该 group 的 `GetCurrentSoflanTimeCached()`，随后普通 Slide 的一颗星或 Fan 的三颗星共享同一个 `visualOffset` 和 progress。

### 可见性注册的预计算影响

只修改 `MoveStarLane()` 不足以完整支持 D5。例如上表的 `0.5x` 会要求星标从 `500ms` 开始淡入，早于原版 `AppearMsec = 1000ms`；如果 Slide 尚未从对象池注册，前半段动画无法显示。

因此实施必须加入 Slide 专用可见性判断：

```text
visualAppearanceStart = launchSoflanTime - appearDuration
visualPathEnd = launchSoflanTime + motionDuration
```

并保证 Slide 在当前 group 的 Soflan 时间可能进入这段视觉区间前完成注册。不能只把 Slide 类型加入现有通用白名单，因为通用检查以 `note.time` 为可见性锚，而本方案以 `S(L) - appearDuration` 为出现起点。

同时仍需保留原版箭头的真实时间预注册需求；最终注册条件应覆盖“原版路径箭头需要注册”或“内部星标的 Soflan 视觉区间需要注册”中的任一情况。

低速、停车、反向和弹跳可能让 Slide 比原版更早注册并占用对象池更久。当前池只有 24 个 `SlideRoot` 和 4 个 `SlideFan`，后续必须把池耗尽场景纳入验证和保护策略。

## 已确认：D6 保留真实发射阶段门槛

上述预计算暂按以下语义进行：

- `currentMsec <= StarLaunchMsec` 时，只允许显示/倒放出现动画；即使 `visualOffset > 0`，也不提前进入路径。
- `currentMsec > StarLaunchMsec` 时，只允许路径运动；若倒退到 `pathRawProgress < 0`，按 D3 隐藏，不重播出现动画。

推荐保留这个门槛。它让 Soflan 改变两个阶段各自的速度，但不会让负速造成“真实发射前就沿路径运动”或“发射后退回淡入阶段”，并与已经确认的 D3 保持一致。

另一种做法是完全按 `visualOffset` 选择状态：负速可以在真实发射前进入路径，也可以在发射后倒退重播缩小/淡出。这会修改 D3 的 `< 0` 隐藏规则，属于更激进的时间倒放语义。

用户结论：同意保留真实发射阶段门槛。Soflan 只改变发射前出现阶段和发射后路径阶段各自的进度，不允许视觉时间跨越真实发射点切换到另一阶段。

## 已确认：D7 连续 ConnectSlide 的进度模型

### 已由代码确认的原版行为

连续 Slide 只创建一颗内部星标。`SlideRoot.Initialize()` 会：

- 以根 Slide 的 `shoot.time` 作为唯一 `StarLaunchMsec`。
- 沿 `child` 链查找每个 `ConnectSlide`，依次把路径加入 `_slideVecListList`。
- 每找到一段就用该段 `arrive.time` 覆盖 `StarArriveMsec`，最后得到最终段到达时间。
- 把各段路径的几何长度相加。
- 运行时用一个总 progress 乘合并总长度，再从前往后定位所属子路径。

中间 `ConnectSlide` 自己的 `shoot/arrive` 不参与分段速度计算；连接点经过时刻只由累计路径长度比例决定。`ConnectSlide` 本身被标记为已使用，不会注册另一个可见 Slide 对象。

### 方案 A：保留整链统一 progress，推荐

设：

```text
L0 = 根 Slide 的 shoot 时间
Rn = 最后一个 ConnectSlide 的 arrive 时间
D = Rn - L0
totalLength = 所有子路径几何长度之和

visualOffset = S(currentMsec) - S(L0)
pathRawProgress = visualOffset / D
targetDistance = totalLength * Clamp01(pathRawProgress)
```

行为：

- 一颗星连续穿过整个组合路径。
- Soflan 停车、前进和倒退作用于整条合并路径。
- 倒退可以跨越连接点返回上一段。
- 中间连接点没有暂停、重置、显隐或 progress 跳变。
- 某连接点的视觉经过比例为“此前累计几何长度 / totalLength”。
- 继续保持当前谱面语义，只替换 progress 的时间轴。

### 方案 B：每个 ConnectSlide 单独计算 progress

星标进入某个子路径后，改用该段自己的 `shoot/arrive` 和可能的 Soflan 时间重新计算局部 progress。

代价：

- 改变当前连续 Slide 的长度比例匀速语义。
- 需要定义段间 timing 重叠、空隙以及中间时刻不连续时怎么办。
- 倒退跨连接点时必须反向切换段和时间锚。
- 后续若允许不同 segment 使用不同 group，还要定义 group 切换是否造成位置跳变。
- 实施和验证复杂度显著上升，并可能让原本连续的一颗星在连接点发生速度突变或瞬移。

推荐方案 A。当前目标是给既有星标运动换上 Soflan 时间轴，而不是重写 `ConnectSlide` 的分段时序语义。

本问题只决定时间和路径进度模型；整链采用根 Slide group，还是允许 `ConnectSlide` 各自 marker 参与，由后续 D8 单独决定。

用户结论：同意方案 A。连续 `ConnectSlide` 保持一颗星、第一段发射、最终段到达、合并总几何路径和单一 Soflan progress；中间 timing 不参与分段进度。

## 已确认：D8 连续 Slide 的 Soflan group 来源

### 已由代码确认的现状

- `SoflanManager.loadNote()` 会逐条扫描 note record，因此根 Slide 和每条 `ConnectSlide` 理论上都可以各自登记 `#group`。
- marker 当前按 note 独立保存，不会自动继承给 child 或 slide child。
- 但 `ConnectSlide` 会被标记为已使用，不创建独立 `SlideRoot`。
- 实际运行的内部星标属于根 Slide 创建的唯一对象。

### 方案 A：根 Slide group 控制整颗星，推荐

```text
slideSoflanGroup = getNoteSoflanGroup(rootSlide)
launchSoflanTime = S_rootGroup(L0)
visualOffset = S_rootGroup(currentMsec) - launchSoflanTime
```

行为：

- 根 Slide 未写 marker 时，整链使用默认 group `0`。
- 根 Slide 写 `#N` 时，出现动画和全部连接路径都使用 group `N`。
- 星标跨越连接点时不切换 group，progress 连续。
- 多条彼此独立的 Slide 即使共享同一个外部 Star，仍各自读取自己的根 Slide group。
- Fan 没有逐分支 group；三颗内部星共享 Fan 根 Slide 的 group。
- 该规则只定义“由根 Slide 创建的内部星标采用哪个 group”，不改变其它 note marker 的通用非继承规则。

### 方案 B：进入每个 ConnectSlide 时切换到该段 group

这与 D7 的整链单一 progress 存在结构冲突：当前所在段由总 progress 决定，但切换 group 后 progress 又会改变，容易形成循环依赖；不同 group 的累计 Soflan Y 也可能让星标在连接点瞬移。

要可靠实现方案 B，需要重新引入逐段时间锚、局部 progress 和跨段连续性补偿，实质上回到 D7 已拒绝的分段模型。

推荐方案 A。它与“一颗星、一个整链 progress”的已确认语义完全一致，也让谱面作者只需在可见根 Slide 上声明一次 group。

本问题确认后，还需单独决定 ConnectSlide record 若写了不同 `#group`，应被静默忽略、记录警告还是作为谱面错误拒绝。

用户结论：同意方案 A。普通、连续和 Fan Slide 的内部星标都只读取创建该对象的根 Slide group；连接点不切换 group。

## 已确认：D9 ConnectSlide marker 冲突策略

在 D8 规则下，根 Slide group 是整链唯一有效 group。仍需定义以下谱面如何处理：

```text
根 Slide：      #1
ConnectSlide：  #2
```

### 方案 A：加载期拒绝不同 group，推荐

规则：

- child 未声明 marker：接受，运行时整链使用根 group。
- child 声明与根相同的数值 group：接受，视为冗余声明。
- child 声明与根不同的数值 group：写入包含根/child note index 和两个 group 的日志，然后抛出 `FormatException`，停止加载该谱面。
- 比较只针对 Soflan group 数值；本计划不扩展 Slide FixedSoflan。

推荐理由：

- 谱面作者不会误以为连接点真的切换了 group。
- 与现有非法 marker “记录日志并抛异常”的 fail-fast 策略一致。
- 校验可以在所有 marker 已登记且 `calcSlide()` 已建立 child 链之后执行一次，不进入每帧热路径。
- 允许同 group 的冗余 marker，可兼容可能按每段重复输出 marker 的上游工具。

### 方案 B：记录警告，根 group 胜出

不同 group 时继续播放，但只用根 group，并写一次加载期警告。兼容性较宽松，但谱面仍能以与作者声明不同的视觉效果运行，警告也可能被忽略。

### 方案 C：静默忽略 child marker

运行时代码最少，但最容易产生难以定位的谱面行为，不推荐。

推荐方案 A。

用户结论：同意方案 A。不同 group 的 ConnectSlide marker 在谱面加载阶段记录上下文并抛 `FormatException`；未声明或与根 group 相同则接受。

## 已确认：D10 Slide 专用 Soflan 注册条件

### 已由代码确认的注册流程

`GameCtrl.UpdateCtrl()` 会遍历尚未使用的 note，原版注册条件为：

```text
currentMsec >= note.time.msec - apperMsecTap
apperMsecTap = noteSpeedForBeat * 8
```

满足后才从固定对象池借出 `SlideRoot` 或 `SlideFan`、调用 `Initialize()` 并加入活动列表。同一帧末尾才执行 `MoveStarLane()`。

当前通用 Soflan 可见性检查以 `note.time` 为目标，适用于 Tap/Hold/Touch 等已有对象。D5 的 Slide 出现窗口却是：

```text
visualAppearanceStart = S(shoot) - (shootAudioMsec - appearAudioMsec)
```

二者锚点不同，因此不能只把 Slide 枚举加入 `IsSupportedVisualSoflanKind()`。

### 方案 A：原版条件与 Slide 星标条件取 OR，推荐

对尚未使用的根 Slide 计算：

```text
originalDue = currentMsec >= AppearMsec - apperMsecTap

visualOffset = currentSoflanTime - launchSoflanTime
appearRawProgress = 1 + visualOffset / appearDuration

soflanStarDue = currentMsec <= StarLaunchMsec
             && 0 <= appearRawProgress
             && appearRawProgress <= 1

register = originalDue || soflanStarDue
```

行为：

- 原版条件先满足时，照常提前创建路径箭头和星标对象。
- Soflan 减速使淡入早于原版窗口时，在星标第一次进入可见出现区间的当帧提前注册。
- 注册发生后，同帧的 `MoveStarLane()` 会直接计算正确 alpha、scale 和位置。
- 提前注册不会提前显示路径箭头；`UpdateAlpha()` 在真实 `StartMsec` 前仍将箭头 alpha 保持为 `0`。
- 触摸检查、AutoPlay、判定和回收仍使用真实时间，不会随提前注册而提前。
- `ConnectSlide` 已被标记为 used，不单独注册。
- 一旦根 Slide 注册，它会一直存在到原版真实时间生命周期结束；Soflan 反复进出出现窗口只改星标 alpha，不重复借还对象。
- 无任何 `SFL` 的谱面完整走原版条件。

为使 `GameCtrl` 能在对象创建前计算该条件，加载阶段需要按根 note index 预存至少以下数据：

```text
group
appearAudioMsec
launchAudioMsec
launchSoflanTime
appearDuration
```

每帧 `currentSoflanTime` 继续复用现有 group 缓存；不应在 GameCtrl 热路径重复进行 TGrid、BPM 或最终 ConnectSlide 遍历。

### 方案 B：只保留原版注册窗口

实现最少，但低速下会丢失发生在原版注册时间之前的 Soflan 淡入，与 D5 已确认语义不完整。

### 方案 C：完全改用 Soflan 注册窗口

会丢掉原版路径箭头按玩家 `SlideSpeed` 提前显示所需的真实时间预注册，也会扩大本次改动范围，不推荐。

推荐方案 A。

本问题只决定“何时尝试注册”。若同时需要显示的 Soflan Slide 超过现有 24 个普通池或 4 个 Fan 池，应扩池、降级还是保持原版失败重试，将在下一项单独决定。

用户结论：同意方案 A。原版真实时间窗口与 Soflan 星标出现窗口任一满足即可注册根 Slide；通用 note 可见性逻辑保持不变。

## 已排除：D11 对象池容量策略

### 已由代码确认的耦合资源

当前固定预创建：

```text
SlideRoot：24
SlideFan：4
SlideJudge：16
普通箭头：640
Break 箭头：640
```

每个根 Slide 注册时不仅占用一个 Slide 本体，还会：

- 按环形 `_slideJudgeIndex` 取得一个 `SlideJudge`，注册时不会检查该 Judge 是否已经被另一个尚未判定的 Slide 预留。
- 普通非 Fan Slide 从对应箭头池借出整条合并路径所需的所有箭头。
- 一直持有这些资源到原版真实时间判定与回收阶段。

D10 允许减速、停车或折返让未来 Slide 提前很久注册，因此可能出现：

- 24 个普通或 4 个 Fan 本体全部占用，`RegistNote()` 返回失败并阻塞当前遍历位置之后的 note 注册。
- 超过 16 个已注册 Slide 时，环形索引把同一个 `SlideJudge` 分配给多个 Slide，后续判定位置和动画互相覆盖。
- 箭头池不足时，借到的箭头数少于 `GetSlideArrowNum()`；现有初始化代码仍假定资源完整，存在显示缺失或索引风险。

因此只扩 Slide 本体、或只依赖下一帧重试，都不是完整方案。

### 方案 A：成套弹性容量，推荐

容量单元同时覆盖：

```text
普通 SlideRoot
SlideFan
被唯一预留的 SlideJudge
普通箭头
Break 箭头
```

建议分两层：

1. 谱面加载完成后，根据 Slide 的最早注册时刻、最晚真实回收时刻、类型和箭头需求，预计算保守的最大并发量。
2. 在游戏预加载阶段把各池扩到该容量，避免正常播放中 `Instantiate()`。
3. 运行时获取资源时仍做完整性检查；若预估不足或调试跳转改变时序，允许按需扩容作为兜底。
4. 一个已注册 Slide 必须独占一个已预留的 `SlideJudge`，直到该 Slide 触发 Judge；Judge 动画结束后才完全回池。
5. 借箭头必须是原子操作：数量不足时先扩容，不能拿到半条路径后继续 `Initialize()`。
6. 播放中不缩池；曲目结束统一复用或释放，避免频繁创建销毁和 GC 抖动。

优点：完整兑现 D5/D10 的提前可见性，不因极端 Soflan 悄悄漏星标、复用错误 Judge 或缺箭头。

代价：需要扩展当前 GameCtrl 的池管理，并为异常谱面设置合理的安全上限；本计划已经明确暂不进入该分支。

### 方案 B：保持原版固定池，资源不足时等待重试

改动最小，但并发需求持续超过容量时，靠后的 Slide 会一直无法注册；Judge 环形复用和箭头半借出问题仍需额外修复。视觉正确性无法保证。

### 方案 C：固定池超限即拒绝谱面

可避免运行时错误，但必须精确或保守计算所有并发资源需求；长停车谱面可能仅因视觉设计超过原版容量而无法加载，扩展能力受限。

推荐方案 A。

用户结论：暂时不考虑对象池容量调整。本计划不实施加载期容量预计算、弹性扩容、Judge 独占改造或安全上限。

保留的已知限制：

- 继续使用 24 个 `SlideRoot`、4 个 `SlideFan`、16 个 `SlideJudge`、640 个普通箭头和 640 个 Break 箭头。
- 极端长减速、停车或折返使大量 Slide 提前注册时，可能出现本体池不足、Judge 环形重复分配或箭头不足。
- 不通过截断 Soflan 出现窗口来规避容量问题，因为那会直接违背 D5/D10；本轮只记录风险，不解决。

## 已确认：D12 星标倒退时的朝向

### 已由代码确认的原版行为

普通 Slide 当前使用：

```text
rotation = interpolatedPathTangent + 90°
```

Fan 三颗星也使用各自路径当前位置的正向切线 `+90°`。原版 progress 只增不减，所以没有运动方向状态。

接入负速后，如果保持该逻辑，星标虽然沿路径倒退，图案仍朝路径的名义正方向。

### 方案 A：朝实际运动方向，推荐

```text
delta = currentPathRawProgress - previousPathRawProgress

delta > epsilon：forward
delta < -epsilon：reverse
其余：保持最后一次非零方向

rotation = interpolatedPathTangent
         + 90°
         + (reverse ? 180° : 0°)
```

行为：

- 正向运动保持原版朝向。
- progress 开始减小时，在反向拐点翻转 `180°`，朝向实际倒退方向。
- `0x` 停车或极小浮点抖动时保持最后方向，不逐帧来回翻转。
- 从 `progress > 1` 的隐藏终点反向重新进入时，以反向朝向出现。
- 从 `progress < 0` 的隐藏起点正向重新进入时，以正向朝向出现。
- Fan 三颗星共享同一 progress 方向，但各自使用本分支的路径切线。
- 普通、Break、EX 星标使用相同方向规则；Break 闪光逻辑不变。
- 发射前出现阶段固定采用路径正向起点朝向，不把淡入 progress 的倒放视为沿路径倒退。

### 方案 B：始终保持路径正向朝向

完全保留原版旋转公式。实现更简单、不会在速度符号变化时翻转，但负速时图案朝向与实际位移相反。

推荐方案 A。

用户结论：同意方案 A。路径 progress 减小时翻转 `180°`；停车保持最后一次非零运动方向；出现阶段始终使用路径正向起点朝向。

## 已确认：D13 退化与异常数据处理

### 方案 A：静态错误 fail-fast，运行时异常安全降级，推荐

#### 1. 无 SFL 谱面

不进入任何新公式，直接执行原版 `MoveStarLane()`，确保完全回归。

#### 2. 加载期可验证的 timing

优先按 `NotesTime.grid` 验证顺序，再计算 grid-native 音频时长：

```text
appearGrid <= shootGrid < finalArriveGrid
```

具体规则：

- `shootGrid > appearGrid`：正常计算 `appearDuration`。
- `shootGrid == appearGrid`：作为合法零等待；没有淡入阶段，真实发射时刻及以前保持隐藏，之后直接进入路径阶段。
- `shootGrid < appearGrid`：记录 note index 和三个 timing，抛 `FormatException`。
- `finalArriveGrid <= shootGrid`：无法定义正的 `motionDuration`，记录上下文并抛 `FormatException`。
- grid 转音频时间后得到 NaN、Infinity 或非正 `motionDuration`：同样加载失败。

这些错误属于谱面或 timing 数据本身无效，静默回退会掩盖问题。

#### 3. 初始化期元数据或路径异常

如果某个 Slide 在 Soflan 谱面中缺失预计算元数据，记录一次 note 级错误，并对该对象关闭 Slide Soflan，整个生命周期调用原版运动。

路径数据若为空、总长度非正或任一必要子路径不足两个有效点，则不把 NaN/越界索引写入 Transform。推荐隐藏内部星标、保留触摸与判定，并记录一次错误；因为原版路径查询同样可能不安全，不能盲目回调原版路径插值。

#### 4. 每帧运行时异常

以下任一情况发生时，不继续计算本帧 Transform：

- `currentSoflanTime`、`visualOffset` 或 progress 为 NaN/Infinity。
- alpha、scale 或目标距离为非有限数。
- `0 < displayProgress < 1` 时找不到可插值的有效路径段。

处理方式：

1. 以 note index 为键只写一次错误日志，避免每帧刷屏。
2. 若路径本身有效，但 Soflan 数值异常：该 Slide 本次生命周期永久关闭 Soflan 并从下一次更新起使用原版真实时间运动，避免原版/Soflan 每帧来回切换。
3. 若路径本身无效：隐藏内部星标，不影响真实时间触摸、判定与回收。

#### 5. 路径边界硬化

- `displayProgress == 0` 直接使用第一点。
- `displayProgress == 1` 直接使用最终点，不进入 segment 搜索。
- 中间段循环上限固定为 `Count - 1`，禁止访问 `i + 1` 越界。
- 零长度 segment 跳过；找到下一条有效 segment 后再插值。
- 这些检查普通、连续和 Fan 共用。

### 方案 B：所有异常都加载失败

最严格，但路径资源或运行时浮点异常可能在游戏启动后才可见，全部提升为谱面错误会降低容错，并且不一定能在加载期拿到完整 Unity 路径数据。

### 方案 C：所有异常都静默回退原版

实现看似简单，但会掩盖无效 timing；路径本身损坏时原版也可能除零或越界，不能保证安全。

推荐方案 A。

用户结论：同意方案 A。合法零等待跳过淡入；静态 timing 错误加载失败；运行时 Soflan 数值异常对该对象永久降级原版；路径损坏时隐藏内部星标并保留玩法生命周期。

## 已排除：D14 DEBUG 面板支持 Slide 检视

### 已由代码确认的现状

现有 `SoflanPanelBehaviour`：

- 只在 DEBUG 构建包含完整面板逻辑。
- 右键通过 `Physics2D.OverlapPointNonAlloc()` 命中 `Collider2D`，再筛选 `NoteBase`。
- 只显示选中 Tap 的 diffTime、progress、scale 和 Y 等数据。
- `SlideRoot` 不继承 `NoteBase`，现有选择与数据结构不能直接复用。
- 给内部星标临时增加 Collider 可能扩大物理查询结果并干扰既有调试选择，不推荐。

### 方案 A：增加 DEBUG-only Slide 注册表和面板页，推荐

活动 Slide 在 `Initialize()` 时登记、结束或池化复用时注销；面板通过前一个/后一个控件按稳定的 note index 顺序选择，不依赖 Collider。

建议显示：

```text
NoteIndex / SlideIndex / 普通或 Fan / Break 标记
Soflan group / 当前 group speed
真实 current / appear / launch / final arrive 时间
currentSoflanTime / launchSoflanTime / visualOffset
当前阶段：Hidden / Appearance / Path / OutOfRange / OriginalFallback
appearRawProgress / pathRawProgress / displayProgress
Visible / Forward-Reverse-Stopped
总路径长度 / 当前目标距离 / 当前 Connect 子路径索引
Fan 三分支各自路径长度和目标距离
是否发生 runtime fallback / fallback reason
```

行为约束：

- 选中 Slide 的快照每帧更新，面板其它汇总数据仍可维持现有 `0.2s` 节流。
- 复制面板内容时包含完整 Slide 快照，方便对照数值计算器和日志。
- Slide 结束、对象重新初始化、谱面清理或面板销毁时清除静态引用。
- 不通过改星标 alpha 或颜色做选中高亮，避免干扰 D3 可见性和 Break 闪光。
- 面板使用独立 Tap/Slide 区域或滚动区域，避免新增字段超出当前固定高度。
- 所有注册、快照和 UI 调用放在 `#if DEBUG` 中；Release 不增加每帧分配或面板提交开销。

### 方案 B：只写日志，不扩展面板

实现较少，但停车和反向问题需要逐帧数据；日志会快速膨胀，也难以把某颗活动 Slide 与画面状态同步。

### 方案 C：不增加专项调试数据

改动最少，但只能依赖肉眼和外部计算，定位 group、锚点、阶段门槛或路径 segment 错误的成本较高。

推荐方案 A。

用户结论：否。本计划不扩展 `SoflanPanelBehaviour`，不增加 Slide 选择、快照、复制文本或高亮逻辑；现有 Tap 调试面板保持不变。

## 已确认：D15 验证与验收级别

### 已由代码确认的工具现状

现有 `SoflanCalculator` 的 parser 已识别普通、Break、EX、Connect 和 Fan Slide tag，但 `NoteRecord` 只保存 note 起点、位置与 group：

- 未解析 slide wait length、shoot length 和终点位置。
- 未建立根 Slide 与 `ConnectSlide` 的 child 链。
- `SoflanCalcEngine` 当前只复现 Tap 的 Y、scale 和 NoteStatus 算法。

因此它不能原样验证本方案；若需要离线数值 oracle，必须增加独立的 Slide timing/progress 计算结果，而不是把 Slide 套进 Tap 算法。

### 方案 A：完整四层验证，推荐

#### 1. 构建

```powershell
dotnet build -c Release Assembly-CSharp.SoflanSupport.mm.csproj
dotnet build -c Debug Assembly-CSharp.SoflanSupport.mm.csproj
```

要求无新增编译错误，输出仍只包含预期 `.mm.dll` 和 `.pdb`。

#### 2. 补丁后静态检查

- 对 staging 目标应用 MonoMod patch。
- 反编译确认普通 `SlideRoot` 与覆盖方法 `SlideFan.MoveStarLane()` 都进入 Soflan 分支。
- 确认无 SFL 分支仍调用原版。
- 确认 GameCtrl 使用 D10 的 OR 注册条件，而不是通用 `note.time` 可见性。
- 确认 `ConnectSlide` group 冲突校验位于加载路径，不进入每帧热路径。
- 确认 Release 不包含 D14 已拒绝的 Slide DEBUG 注册和面板数据。

#### 3. 离线数值 oracle

扩展 `SoflanCalculator` 或新增共享纯计算 helper，输入 `A/L/R/group/currentMsec`，输出：

```text
currentSoflanTime
launchSoflanTime
visualOffset
phase
appearRawProgress
pathRawProgress
displayProgress
visible
direction
```

至少覆盖：

- 无 SFL 与恒定 `1x` 等价原版。
- `2x`、`0.5x`、`0x`、负速和正负速弹跳。
- `[0,1]` 边界、越界隐藏和重新进入。
- 零等待、无效 timing、NaN/Infinity 降级。
- 连续 Slide 总长度比例和根 group。
- Fan 三分支共享 progress、各自距离。

#### 4. 游戏内验收

- 普通、Break、EX、Fan、两段及三段 ConnectSlide。
- 出现阶段停车、路径中停车、路径倒退、越过起终点后隐藏、反向重新进入。
- 倒退朝向翻转，停车不抖动。
- 同一外部 Star 分出的多条独立 Slide 使用不同 group。
- 根/child group 冲突在加载期失败。
- 无 SFL 谱面外观、触摸、AutoPlay、判定、音效和回收与原版一致。
- 不同玩家 NoteSpeed、SlideSpeed 下，内部星标 progress 保持由谱面时间决定；`SlideSize` 仍只改变尺寸。
- 极端并发仅记录 D11 已知固定池限制，不把池扩容作为本次验收要求。

### 方案 B：构建、静态检查和离线数值验证，不要求游戏内验收

自动化程度较高，但 Unity 对象池、Sprite alpha、Break 特效、Fan prefab 和实际朝向只能在游戏中确认，仍有明显残余风险。

### 方案 C：只要求构建和静态检查

无法证明停车、反向、显隐和阶段边界符合设计，不足以验收该功能。

推荐方案 A。D14 不做 DEBUG 面板不等于省略游戏内验证；可以用专用测试谱面、录像和离线 oracle 对照验收。

用户结论：同意方案 A。构建、补丁后静态检查、离线数值 oracle 和游戏内验收都是完成条件。

## 候选最终方案

### 1. 最终行为契约

- 目标对象仅为 `SlideRoot` 与 `SlideFan` 的内部星标。
- 普通、Break、EX、Fan 与连续 `ConnectSlide` 全部覆盖。
- 外部 `StarNote`、路径箭头、触摸、AutoPlay、音效、判定和回收保持原逻辑。
- FixedSoflan for Slide、对象池扩容和 Slide DEBUG 面板不在本计划范围内。
- 无 SFL 谱面完整调用原版运动。
- 内部星标 progress 与玩家 NoteSpeed/SlideSpeed 无关；`SlideSize` 仍只改变星标尺寸。

### 2. 最终时间与阶段算法

对根 Slide 定义：

```text
A = 根 Slide appear 的 grid-native 音频时间
L = 根 Slide shoot 的 grid-native 音频时间
R = 普通/Fan 的 arrive，或连续 Slide 最终 child arrive 的 grid-native 音频时间
Da = L - A
Dm = R - L
g = 根 Slide 的 Soflan group，未声明时为 0

launchSoflanTime = S_g(L)
visualOffset = S_g(currentMsec) - launchSoflanTime
```

真实发射时刻继续划分两个阶段：

```text
currentMsec <= L:
    Da == 0:
        hidden
    Da > 0:
        appearProgress = 1 + visualOffset / Da
        visible iff 0 <= appearProgress <= 1
        position = path start
        alpha = appearProgress
        scale = (0.5 + appearProgress) * SlideSize
        rotation = forward start tangent + 90°

currentMsec > L:
    pathProgress = visualOffset / Dm
    visible iff 0 <= pathProgress <= 1
    displayProgress = Clamp01(pathProgress)
    targetDistance = totalPathLength * displayProgress
    alpha = visible ? 1 : 0
    scale = 1.5 * SlideSize
```

raw progress 始终持续计算，越界隐藏不锁死状态；返回 `[0,1]` 后重新显示。

### 3. 路径位置与方向

- 普通 Slide 按累计几何距离在路径点之间插值。
- 连续 Slide 将所有子路径视为一条总路径；使用第一段 `shoot`、最终段 `arrive` 和一个总 progress。
- 中间 `ConnectSlide` timing 不重置速度或 progress；倒退可以跨连接点返回上一段。
- Fan 三颗星共享同一个 progress，各自乘本分支路径长度。
- `pathProgress` 增大时使用正向切线；减小时额外旋转 `180°`。
- progress 不变或变化小于 epsilon 时保持最后非零方向。
- 出现阶段即使 progress 倒放，也始终使用正向起点朝向。

### 4. Group 与 marker

- 普通、Fan 和连续 Slide 都只使用根 Slide group。
- 多条独立 Slide 分别读取自己的根 group。
- child 未声明 marker：接受。
- child 声明与根相同的数值 group：接受。
- child 声明不同 group：加载期记录根/child note index 和 group，抛 `FormatException`。
- 不把根 group 规则扩展成其它 note/child 的通用 marker 继承。

### 5. 加载期预计算

`SoflanManager` 在 SFL、BPM、note marker 和 Slide child 关系均可用后，按根 note index 缓存：

```text
group
appearAudioMsec
launchAudioMsec
finalArriveAudioMsec
launchSoflanTime
appearDuration
motionDuration
```

锚点从 `NotesTime.grid -> TGrid` 转换，不直接把可能经过运行时修正的裸 msec 当作 Soflan 锚。

加载期验证：

```text
appearGrid <= shootGrid < finalArriveGrid
```

`appear == shoot` 是合法零等待；其它顺序错误、非有限转换结果或非正 motion duration 均 fail-fast。

### 6. GameCtrl 注册

保留现有 IL 插入点和返回协议。对根 Slide：

- 若 Soflan 出现 progress 当前位于 `[0,1]`，helper 返回“立即注册”。
- 否则返回“执行原版检查”，继续使用 `current >= note.time - apperMsecTap`。
- 语义等价于原版真实时间条件与 Soflan 出现条件取 OR。
- 不把 Slide 塞进现有以 `note.time` 为锚的通用可见范围查询。
- `ConnectSlide` 不单独注册。

提前创建的对象仍由原版 `UpdateAlpha()` 隐藏箭头；触摸和判定不会提前。

### 7. 运行时防护

- 每个活动 Slide 每帧只取一次缓存的当前 group Soflan 时间；Fan 三分支共享结果。
- 新增状态字段在每次 `Initialize()` 显式重置，不依赖 MonoMod 不会复制的字段初始化器。
- 缺少预计算元数据时，按 note 记录一次并在本生命周期使用原版运动。
- Soflan 数值出现 NaN/Infinity 时永久关闭该对象本轮 Soflan，避免逐帧切换。
- 路径损坏时隐藏内部星标，保留真实时间触摸、判定和回收。
- progress 精确为 `0/1` 时直接取首尾点；中间搜索不访问 `i + 1` 越界，并跳过零长度 segment。

### 8. MonoMod 实施边界

计划新增：

- `Monitor.SlideRoot.mm.cs`
- `Monitor.SlideFan.mm.cs`
- `SoflanSupport/SlideSoflanMath.mm.cs`，放置可复用的纯 progress/phase/direction 计算

计划修改：

- `SoflanSupport/SoflanManager.mm.cs`：Slide timing cache、grid-native 锚点、冲突与 timing 校验、注册查询 API。
- `Monitor.Game.GameCtrl.mm.cs`：在现有 `__SoflanNoteDecision()` 中增加 Slide 专用分派。
- `SoflanCalculator/Ma2Parser.cs`：读取 Slide wait/shoot/end 和 Connect 信息。
- `SoflanCalculator/Program.cs` 及新增 Slide calculator：输出并校验 Slide progress 状态。
- `docs/soflan-system.md`：更新支持矩阵、语法边界和算法说明。

预计无需修改：

- `MonoModRules.cs`：现有 `UpdateCtrl` 插入点已经能调用扩展后的 helper。
- `FixedSoflan`、`NoteData`、Hold/Touch/Tap patch 和 `SoflanPanelBehaviour`。

MonoMod 结构要求：

- `patch_SlideRoot` 与 `patch_SlideFan` 使用 `orig_Initialize()` 和 `orig_MoveStarLane()` 包装。
- 目标类型的私有星标/路径字段使用 `[MonoModIgnore]` 字段桩进行编译期访问和目标字段重链接。
- 两个 patch 类型各自持有并重置新增运行时状态，避免依赖编译期不可见的跨 patch 继承字段。
- 纯 helper 必须作为实际注入目标程序集的新类型，不能标记成被 patched method 调用不到的 `[MonoModIgnore]` helper。

### 9. 分阶段实施顺序

1. 提取纯 `SlideSoflanMath`，先完成恒速、停车、负速、边界和方向数值测试。
2. 扩展 `SoflanManager` 的 Slide timing cache、group 冲突和静态 timing 校验。
3. 接入 GameCtrl 提前注册条件。
4. 实现 `SlideRoot` 普通/连续路径运动。
5. 实现 `SlideFan` 三分支运动。
6. 扩展离线 calculator 与测试谱面。
7. 更新系统文档。
8. 执行 Release/Debug 构建、应用 patch、反编译检查和游戏内验收。

### 10. 完成定义

只有以下条件全部满足才算完成：

- Release 与 Debug 构建成功，patch 输出目录保持整洁。
- 应用后的程序集包含预期 SlideRoot、SlideFan 和 GameCtrl 分支，且有 `MonoMod.WasHere`。
- 离线 oracle 覆盖 D15 数值矩阵并通过。
- 游戏内普通、Break、EX、Fan、Connect、停车、反向、弹跳和无 SFL 回归通过。
- 触摸、AutoPlay、音效、判定和回收时间未改变。
- D11 固定池限制被记录为已知风险，不误报为本次已解决。
- D14 Slide DEBUG 面板没有被实现。

## 最终确认

推荐将以上候选最终方案定稿，结束设计访谈，并作为后续实现与验收的唯一基线。

用户结论：先保存，不执行。当前方案保持候选基线；未修改运行时代码，未构建或应用补丁，也未执行验收。
