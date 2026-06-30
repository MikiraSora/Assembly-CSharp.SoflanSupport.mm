# Soflan Y Position 模拟工具 — 设计文档

## Summary

新建一个 C# 本地 Web 服务器工具，解析 ma2 谱面文件，引用 `SimpleSoflanFramework.Core.dll` 执行与游戏运行时完全一致的 soflan 时间轴计算，通过 HttpListener 提供 JSON API，前端 HTML/Canvas 页面交互式展示全局 Y(t) 曲线和单 note 逐帧中间值。忠实复现 `GetNoteYPosition_soflan` 当前代码行为（含已知 bug）。

## 架构

```
D:\sdez165_soflan_support\
  SoflanSupport.sln                              新建, 包含下面两个项目
  Assembly-CSharp.SoflanSupport.mm.csproj        已有 patch 项目
  SoflanSimulator/
    SoflanSimulator.csproj                       net472, 引用 SimpleSoflanFramework.Core.dll
    Program.cs                                   入口 + HttpListener 服务器
    Ma2Parser.cs                                 解析 ma2 -> BPM/SFL/MET/NMTAP 数据
    Simulator.cs                                 调用 TGridCalculator/SoflanList 复现计算
    www/
      index.html                                 主页面
      app.js                                     前端交互 + Canvas 绘制
      style.css                                  样式
```

## 后端实现

### Ma2Parser.cs

逐行解析 ma2 文件（tab 分隔），提取以下行类型：

- `RESOLUTION  N` -> 分辨率（默认 384）
- `BPM_DEF  bpm  ...` -> 首 BPM
- `BPM  measure  grid  bpm` -> `BPMChange { TGrid(measure, grid), BPM=bpm }`（measure/grid 直接作为 TGrid unit/grid，与游戏 NotesTime->ToTGrid 转换结果一致）
- `MET  measure  grid  num  den` -> 拍号（仅用于显示）
- `SFL  unit  grid  gridLength  speed  [group]` -> `Soflan { TGrid(unit,grid), EndTGrid=TGrid+GridOffset(0,gridLength), Speed, SoflanGroup }`，解析方式与 `SoflanManager.tryParseSoflan` 完全一致
- 解析所有 note 类型行以保持 `indexNote` 与游戏一致（遍历所有 note 行，按出现顺序递增 indexNote），包括 NMTAP/EXTAP/BRTAP/BXTAP/NMSTR/EXSTR 等 `*TAP` 和 `*STR` 行
- `indexNote` 分配：对**所有** note 类型行（含 EXTAP/BRTAP/BXTAP/NMSTR/EXSTR 等）按出现顺序递增，与游戏 `NotesReader.loadNote` 对每种 note 类型都调用的行为一致
- soflanGroup 解析：将行按 tab 分割成字段数组后，从末尾逆序查找以 `#` 开头的字段，`TrimStart('#')` 后 int.Parse 得到 soflanGroup（无则 group=0），与 `SoflanManager.loadNote` 遍历 `record._str` 逆序查找 `#` 的逻辑一致
- **仅对 `checkSupportSoflan` 返回 true 的类型计算和展示 Y(t)**：当前代码中 `checkSupportSoflan` 仅对 `NotesTypeID.BaseDef.Tap` 返回 true，对应 ma2 行类型 `NMTAP`。其他类型（EXTAP/BRTAP/BXTAP/NMSTR/EXSTR 等）仅参与 indexNeed 计数，不计算 Y 位置

输出结构体：`Ma2Data { Resolution, BpmChanges[], Soflans[], Notes[], DurationMs }`

### Simulator.cs

复用 `SimpleSoflanFramework.Core` 的 `BpmList` / `SoflanListMap` / `TGridCalculator`，构建方式与 `SoflanManager.loadComposition` 一致：

1. `BpmList`：`grid==0` 的设为 `FirstBpm`，其余 `Add(BPMChange)`
2. `SoflanListMap`：逐个 `Add(soflan)`
3. 每个 note 的 `AppearMsec = TGridCalculator.ConvertTGridToAudioTime(noteTGrid, bpmList).TotalMilliseconds`
4. `noteSoflanTime = ConvertAudioTimeToY_PreviewMode(AppearMsec, soflanGroup)`

计算函数 `ComputeY(noteIndex, currentMsec, params)` 忠实复现 Monitor.NoteBase.mm.cs:102-205 的 `GetNoteYPosition_soflan`：

- `diffTime = GetSoflanTimeDiff()` -- 注意：复现原代码的**两次独立调用** `GetSoflanTimeDiff()`（diffTime 和 absDiffTime 各调一次），不合并
- `scaleStartTime = 2 * DefaultMsec - GetMaiBugAdjustMSec`
- `moveStartTime = DefaultMsec - GetMaiBugAdjustMSec`
- guide alpha/scale 三段逻辑（含末尾无条件 `SetAlpha(1f)` 覆盖）
- `NoteStat` 无条件设为 `Move`（复现原代码覆盖 `Scale` 的行为）
- `speedRatio = noteSpeed / 150f`，`offsetYAdj` 计算但 `sign=0` 导致无效
- `soflanY = MapValue(diffTime, -moveStartTime, moveStartTime, outsideY, insideY)`
- `adjustedSoflanY = soflanY + 0 * offsetYAdj`（即 `= soflanY`）
- `return Clamp(adjustedSoflanY, 120, 680)`

返回所有中间值：`{ currentMsec, diffTime, absDiffTime, noteSoflanTime, currentSoflanTime, scaleStartTime, moveStartTime, soflanY, offsetYAdj, guideScale, finalScale, screenY, noteSpeed, speedRatio }`

### Program.cs -- HttpListener

端口默认 `http://localhost:3721/`，采用 session token 机制避免重复解析 ma2：

- `GET /` -> 返回 `www/index.html`
- `GET /www/{file}` -> 返回静态资源
- `POST /api/load` (body: `{ file }`) -> 解析 ma2，构建 BpmList/SoflanListMap 并缓存，返回 `{ sessionId, resolution, bpmList, sflList, notes[], durationMs }`，其中 notes 包含每个 note 的 indexNote/measure/grid/lane/soflanGroup/appearMsec/type（仅 NMTAP 标记 active=true）。此 API 只解析不计算 Y。
- `GET /api/computeCurve?sessionId=<id>&noteIndex=<i>&defaultMsec=<ms>&maiBugAdjust=<ms>&startPos=<y>&endPos=<y>&noteSpeed=<v>&step=<ms>` -> 对**单个 note** 在其活跃窗口 `[appearMsec - scaleStartTime, appearMsec + scaleStartTime]` 内按 step 采样计算 Y(t)，返回 `{ noteIndex, appearMsec, points: [{ t, diffTime, screenY, ... }] }`。前端选中 note 时按需请求。
- `GET /api/computeAt?sessionId=<id>&time=<ms>&defaultMsec=<ms>&maiBugAdjust=<ms>&startPos=<y>&endPos=<y>&noteSpeed=<v>` -> 给定单个时间点，计算所有**活跃 note**（absDiffTime <= scaleStartTime 的 note）的完整中间值，返回 `{ time, notes: [{ noteIndex, diffTime, absDiffTime, screenY, soflanY, guideScale, ... }] }`。游标拖动时实时调用。

**session 管理**：后端维护 `sessionId -> (Ma2Data + BpmList + SoflanListMap)` 的内存字典。`load` 时生成新 sessionId（GUID），前端保存并在后续请求中携带。后端不限 session 数量（本地工具），可选 LRU 清理（长时间未访问的 session 回收）。

**不做全量预计算**。曲线按 note 按需请求，游标值按时间点实时计算，避免大 JSON 和采样精度问题。

启动时自动打开默认浏览器访问首页。文件路径支持拖拽到输入框或手动输入。

## 前端实现

### 游标实时调用策略

- 前端用 `requestAnimationFrame` 对齐，每帧最多发一次 `/api/computeAt` 请求
- 发新请求前 abort 未完成的旧请求（XMLHttpRequest.abort），避免乱序响应覆盖最新状态
- 后端不节流，单次 `computeAt` 计算量很小（仅遍历活跃 note，每个 note 一次 `ConvertAudioTimeToY_PreviewMode`）

### 全局视图（Canvas）

- X 轴 = 真实音频时间 (ms)，Y 轴 = 屏幕纵向位置 (120~680，Y 轴反转）
- 每个 note 用判定时间点标记（AppearMsec 位置的竖线）做总览，不画完整曲线；选中 note 后才用 `computeCurve` 请求并绘制该 note 的高精度 Y(t) 曲线
- note 标记视觉规则：所有 note 统一灰色竖线；游标时刻活跃的 note（absDiffTime <= scaleStartTime）高亮为亮色；选中的 note 用独立颜色标注
- soflan 速度变化区间用半透明色带叠加（不同速度用不同颜色，负速度标红）
- BPM 变化点用竖虚线标注
- 可拖拽时间游标，实时显示该时刻所有 note 的屏幕位置
- 顶部参数面板：ma2 路径输入、DefaultMsec、GetMaiBugAdjustMSec、StartPos、EndPos、NoteSpeed、采样步长

### 单 note 详情视图（点击 note 曲线选中）

- 展示选中 note 的 Y(t) 曲线，标注三段区间（hidden / scale / move）
- 表格显示游标位置的中间值：diffTime、absDiffTime、noteSoflanTime、currentSoflanTime、soflanY、guideScale、finalScale、screenY
- 附带 soflan 速度曲线（该 note 所属 group 的 speed vs time），便于理解 Y(t) 曲线形状成因

### 参数默认值

| 参数 | 默认值 | 说明 |
|------|--------|------|
| DefaultMsec | 2000 | note 接近时间，可在 UI 调整 |
| GetMaiBugAdjustMSec | 0 | bug 修正偏移 |
| StartPos | 120 | 出现位置 |
| EndPos | 400 | 判定线位置 |
| NoteSpeed | 150 | 对应 ratio=1.0；因 sign=0 实际不影响 Y |
| step | 16 | 采样步长 (ms)，约 60fps |

## 测试计划

1. 加载 `003999_03.ma2`（含 27 条 SFL + 多 BPM 变化），确认 Y(t) 曲线在 soflan 变速区段形状正确（加速段变陡、减速段变缓）
2. 加载无 SFL 的 ma2（如 `010622_00.ma2`），确认 Y(t) 为线性（speed=1）
3. 点击单个 note，验证 `diffTime=0` 时 `screenY~EndPos`、`diffTime=+/-moveStartTime` 时 `screenY~120/680`
4. 调整 DefaultMsec 参数，确认曲线水平拉伸/压缩
5. 验证 soflan group 非零的 note（若有 `#N` 后缀）使用对应 group 的速度曲线
6. 对比 `ConvertAudioTimeToY_PreviewMode` 输出与面板的 soflan 虚拟 Y 值一致性

## 假设

- `DefaultMsec / StartPos / EndPos / GetMaiBugAdjustMSec` 的精确游戏内值无法从 DLL 静态提取，设为 UI 可调参数，默认值基于代码注释推断（120/400/680 对应 StartPos/EndPos/outsideY）
- 仅解析 `NMTAP` 行（`checkSupportSoflan` 仅对 Tap 返回 true）；其他 note 类型（HOLD/SLIDE/EXTAP）不参与 Y 计算
- BPM 行的 `measure` 和 `grid` 直接作为 TGrid(unit, grid)，与游戏 `NotesTime.grid / resolution` 转换结果等价（已验证：`measure=9, grid=0, resolution=384` -> `totalGrid=3456` -> `TGrid(9, 0)`）
- 忠实复现当前代码所有行为，包括：`sign=0` 导致 NoteSpeed 修正无效、`SetAlpha(1f)` 无条件覆盖、`NoteStat` 无条件覆盖为 `Move`、`diffTime`/`absDiffTime` 两次独立调用 `GetSoflanTimeDiff()`
- 工具项目独立于 patch 项目，不引入 `Assembly-CSharp` 依赖；ma2 解析逻辑在工具内自行实现（不依赖游戏 `NotesReader`）

---

## Grill 记录

（以下为 grill 过程中逐步补充的内容）
