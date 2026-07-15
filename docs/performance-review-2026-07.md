# Soflan 支持代码性能复查（2026-07）

## 结论

当前实现存在可确认的 Release 播放期性能问题。最高优先级不是 Note 位置计算本身，而是 `GameCtrl.UpdateCtrl` 在原版时间窗过滤前扫描所有尚未注册 note，并为每个 note 重复执行 TGrid 到音频毫秒的转换。

建议按以下顺序处理：

1. 在谱面 BPM/SFL 加载完成后预计算 note 头尾音频毫秒，消除播放期每 note 重复转换。
2. 把可见范围缓存改为“帧 + `(group, visibleMsec)`”键，避免 Tap、Touch 和不同 FixedSoflan 窗口交替时反复重建。
3. 每帧只做一次与 group 无关的 Audio→TGrid，并在 Core 中用版本/dirty 标记和二分替代 BPM 热路径的全表扫描。
4. 把每帧 group time 字典改为版本戳或 touched-key 方案，避免大谱面后每帧按历史桶容量 `Clear()`。

这四项都能保留现有可见范围语义，不改变音频或判定时间。其中前两项位于补丁自有的 `SoflanManager`，MonoMod 接入风险较低。

不要把 `noteSoflanY` 直接落在 `[currentY, currentY + visibleMsec]` 作为上述优化的等价替换。随机验证表明，当前逆向范围算法在停车、反向和折返时是保守近似；直接 Y 判断会明显改变 note 注册时机，应作为单独的行为修正项目验证。

## 范围与方法

检查范围：

- MonoMod patch 运行时代码：`SoflanSupport/`、`Monitor.*.mm.cs`、`Manager.*.mm.cs`、`Process.*.mm.cs`、`MonoModRules.cs`。
- 随补丁部署的 `Dependencies/SimpleSoflanFramework/SimpleSoflanFramework.Core/`。
- 原版 `Assembly-CSharp.dll` 的 `GameCtrl.UpdateCtrl` / `Initialize` IL，以及当前 Release/Debug `.mm.dll` 产物 IL。
- 现有两份专项评审：`many-soflan-groups-performance-review.md`、`gc-paused-memory-risk-review.md`。

验证方式：

- 主审查和三个独立子审查分别检查播放热路径、底层框架算法、加载/Debug 路径。
- 第四个子审查以反方角色尝试推翻高优先级结论。
- 使用仓库现有 .NET 3.5 形状 benchmark 验证旧枚举 API 与当前填充式 API。
- 使用精确分配探针、随机等价测试和 Cecil IL 检查验证隐藏分配、复杂度、真实调用顺序和条件编译边界。
- 未进行 Unity Profiler 或游戏内实机采样；绝对耗时只用于比较趋势，严重度主要依据确定的调用次数、IL 分配和复杂度。

## 已确认问题总表

| 编号 | 严重度 | 配置/阶段 | 问题 | 真实性 | 改进风险 |
| --- | --- | --- | --- | --- | --- |
| PERF-01 | 高 | Release/Debug 播放期 | 全谱扫描未来 note，并逐 note 重复 TGrid→毫秒 | 四路确认 + IL + 微基准 | 缓存低；改索引高 |
| PERF-02 | 高 | Release/Debug 播放期 | `visibleMsec` 全局单值导致缓存抖动 | 四路确认 + 原版 IL | 低 |
| PERF-03 | 高 | Release/Debug 播放期 | BPM“缓存命中”仍线性遍历并分配 | 源码 + IL + 分配探针 | 中低 |
| PERF-04 | 中高 | Release/Debug 播放期 | 帧级 group 字典每帧按历史容量清空 | 源码 + 游戏 `mscorlib` IL | 低到中 |
| PERF-05 | 中 | Release/Debug 首次播放查询 | interval tree 延迟到首次查询构建 | 源码 + 冷启动探针 | 低到中 |
| PERF-06 | 中 | Release/Debug 加载期 | 全组预热构建双缓存且会立即失效 | 源码 + 反射版本探针 | 中 |
| PERF-07 | 中 | Release/Debug 加载/内存 | 所有 note 无条件保存 TGrid | 源码 + 注入顺序 | 中 |
| PERF-08 | 中 | Release/Debug 加载期 | 已解析 MA2 后再次完整读取文件 | 源码 + 钩子顺序 | 中高 |
| PERF-09 | 中 | 病态谱面播放期 | 大量停车/折返使范围查询退化 | 算法分析 + 压力探针 | 中高 |
| PERF-10 | 中 | 缺失 group 谱面 | 读索引器隐式创建完整中性组 | 源码 + 分配探针 | 低到中 |
| PERF-11 | 中 | Debug 加载/播放期 | 无界日志队列与默认可见 IMGUI | Release/Debug IL + 源码 | 低 |

## 详细问题

### PERF-01 全谱扫描未来 note，并逐 note 重复 TGrid→毫秒

证据链：

- `MonoModRules.cs:186` 把 Soflan 可见性派发插在原版毫秒时间窗判断之前。
- `Monitor.Game.GameCtrl.mm.cs:25` 对每个受支持且尚未注册的 note 调用 `checkNoteVisible()`。
- `SoflanSupport/SoflanManager.mm.cs:360` 对每个 note 调用 `getNoteAudioMsecForSoflan()`。
- `SoflanSupport/SoflanManager.mm.cs:384` 每次从 `noteIndex -> TGrid` 字典取 TGrid，再调用 `TGridCalculator.ConvertTGridToAudioTime()`。
- 原版 `GameCtrl.UpdateCtrl` IL 证实每帧遍历整张 NoteData list；只对 `isUsed` note 跳过，没有在遇到未来 note 后提前退出。

底层转换不是常量时间：

- `Dependencies/SimpleSoflanFramework/SimpleSoflanFramework.Core/TGridCalculator.cs:47` 调 BPM 缓存 getter，然后通过捕获 `tGrid` 的 LINQ `LastOrDefault` 线性查找。
- Release IL 每次都会创建 display class 和 `Func<BpmTimingPoint, bool>`。
- 精确探针测得 TGrid→音频约 `176 B/次`；其 CPU 成本随 BPM 点数量线性增长。

在 60 FPS 下，这条路径的分配下限约为：

```text
176 × 尚未注册的受支持 note 数 / 帧
```

例如 1000 个未来 note 约为 `176 KB/帧`、`10.56 MB/s`；10000 个未来 note 约为 `1.76 MB/帧`。如果播放时暂停 GC，这些临时对象会持续累积。

独立反方探针使用 3000 note × 600 帧：1/16/64 个 BPM 点分别约 214/902/3146 ms，并各触发 50 次 Gen0；缓存后的版本约 18–20 ms 且没有 Gen0。

建议：

- 保留 TGrid 作为语义源。
- 在 `loadComposition()` 完成 BPM/SFL 构建后，一次性生成 `noteIndex -> float audioMsec` 和长物件尾部毫秒缓存。
- 播放期只做字典或紧凑数组读取。
- 当前钩子顺序允许这样做：`loadNote` 先登记 TGrid，`loadComposition` 在 `calcTotal` 后执行。

不要简单退回 `note.time.msec`，因为最近提交正是为统一校正后的视觉时间轴而保存原始 TGrid。

上述缓存只消除重复转换，不消除全谱扫描。剩余未注册 note 数为 `U` 且每个 note 独占 group 时，本帧仍会触及约 `U` 个 group，并为它们分别计算 current Y 和逆向可见范围；“按 group 懒查询”在这种谱面上仍近似每帧全组查询。每个首次触及的 group 还会创建 range cache、List、scratch 和 full-check 数组；独立探针测得 10000 个空形 cache 约分配 6.0 MB，尚未包含字典桶和 interval tree。

长期根治方案是按 group 建立以 canonical audio msec 排序的未注册 note 索引，再对每个逆向 visible range 做二分并派发候选 note。这样可以保留当前范围算法，不需要改成不等价的直接 Y 判断。但这会改变 `GameCtrl.UpdateCtrl` 的遍历架构，必须保留：

- 原 note 注册顺序和一次性 `isUsed` 语义。
- 一个 note 因折返被多个 range 命中时只尝试一次。
- `RegistNote()` 失败后的原控制流和中断行为。
- Tap/Touch/Hold/BreakHold 的对象池容量与创建时机。

因此该架构方案收益高但行为/MonoMod 风险也高，应排在前三项低风险优化之后。

### PERF-02 可见窗口切换导致缓存抖动

`SoflanSupport/SoflanManager.mm.cs:419` 同时把 `currentMsec` 和当前 note 的 `apperMsec` 当作一个全局“最近键”。任一值变化都会递增 `visibleRangeCacheVersion`；每个 group 只保存一份 `Version/Ranges`。

同一帧内存在多种窗口：

- 原版 `GameCtrl.Initialize` IL 计算 `apperMsecTap = guideSpeed4BeatTap * 8`。
- 原版 IL 计算 `apperMsecTouch = guideSpeed4BeatTouch * 5`。
- `Monitor.Game.GameCtrl.mm.cs:36` 对 FixedSoflan 使用声明速度计算任意 `visibleMsec`。

因此 note 顺序为 Tap→Touch→Tap，或同一 group 中 Fixed 速度 A→B→A 时：

1. 第一个 A 计算范围。
2. B 推进全局 version。
3. 第二个 A 虽然与第一个 A 同帧、同 group、同窗口，仍因 version 不同而重建。

最坏情况下范围重建次数可接近本帧检查的 note 数，而不是每个 `(group, visibleMsec)` 一次。

建议：

- 帧 version 只绑定 `currentMsec`，或由 `GameCtrl.__SoflanClearCache()` 显式推进。
- 范围缓存键至少包含 `(group, visibleMsec)`。
- `visibleMsec` 来自稳定字段或确定公式，可按 float 精确位/默认相等语义作为键；如担心大量自定义 Fixed 速度，使用每 group 本帧小列表而不是永久嵌套字典。
- 切谱时释放这些 entry，避免高水位常驻。

### PERF-03 BPM 缓存命中仍线性遍历并分配

`BpmList.GetCachedAllBpmUniformPositionList()` 的命名容易让调用方误以为命中是 O(1)，但 `Dependencies/SimpleSoflanFramework/SimpleSoflanFramework.Core/BpmList.cs:162` 每次都会：

1. 通过接口枚举器遍历所有 BPM 点并重新计算内容 hash。
2. 比较 hash 后才决定是否重建缓存。

Release IL 证实接口枚举器会装箱。随后：

- `TGridCalculator.ConvertAudioTimeToTGrid()` 用捕获 `audioTime` 的 LINQ `LastOrDefault` 线性扫描缓存表。
- `TGridCalculator.ConvertTGridToAudioTime()` 用捕获 `tGrid` 的 LINQ `LastOrDefault` 线性扫描缓存表。
- Audio→Y 会先走 Audio→TGrid，再对 Soflan position list 做二分；瓶颈主要在 BPM 路径。

精确探针结果：

| 操作 | 稳态分配 |
| --- | ---: |
| BPM 缓存 getter 命中 | 40 B/次 |
| Audio→TGrid / Audio→Y | 232 B/次 |
| TGrid→Audio | 176 B/次 |
| 已预热的 TGrid→Y | 0 B/次 |

10 万次 Audio→Y 的趋势样本：

| BPM 点数 | 耗时 |
| ---: | ---: |
| 1 | 约 32–36 ms |
| 128 | 约 344 ms |
| 512 | 约 1.19–1.37 s |

Soflan 段从 8 增至 2048 时，同类探针只从约 47 ms 增至约 59 ms，进一步说明主因是 BPM 线性扫描。

建议：

- `currentMsec -> TGrid` 与 group 无关。`SoflanManager` 应每帧只转换一次当前 TGrid，各 group 改走已存在的 TGrid→Y 二分路径；17 BPM 的 10 万次探针从 Audio→TGrid 的约 55 ms/23.2 MB 降为共享 TGrid 后各 group TGrid→Y 的约 9.2 ms/近零分配。
- `BpmList` 已有 BPM/TGrid 属性变更通知链，使用单调 version 或 dirty flag 替代每次全量内容 hash。
- 在 `List<BpmTimingPoint>` 上按 `AudioTime` 和 `TGrid.TotalGrid` 手写二分。
- 保留现有公开方法签名，可把 MonoMod 风险限制在随补丁部署的 Core DLL 内。
- 为 `IsNotifying = false` 或批量编辑场景提供显式 `Invalidate/Prepare`；游戏加载完成后把 BPM snapshot 冻结是风险更低的运行时方案。
- 如果目标是完全零分配，进一步提供基于 total-grid/scalar 的 API，避免 `GridOffset/TGrid` 对象构造。

### PERF-04 帧级 group time 字典按历史容量清空

`SoflanSupport/SoflanManager.mm.cs:464` 每帧调用 `cachedCurrentSoflanTimeMap.Clear()`。大谱面访问几千个独立 group 后，字典桶扩容且不会收缩。

游戏自带 `mscorlib` 的 `Dictionary<TKey,TValue>.Clear()` IL 证实：只要当前 `count > 0`，它会逐个重置完整 `buckets.Length`，并 `Array.Clear(entries, 0, count)`。因此：

- 成本不仅与当前帧访问 group 数有关，还受历史最大桶容量影响。
- 一次几千 group 的高水位后，后续每帧即使只访问少数组，也可能持续扫描大桶。

建议：

- 将 value 改为 `(frameVersion, soflanTime)`，查找时只接受当前 version，不再每帧清表。
- 或维护本帧 touched-key 列表，只移除/覆盖实际写入的键。
- version 溢出时再一次性清表。

### PERF-05 interval tree 首次播放查询才构建

`Dependencies/SimpleSoflanFramework/SimpleSoflanFramework.Core/SoflanList_CachedPositionList.cs:317` 的 `RebuildIntervalTreePositionList()` 只逐项 `tree.Add()`；`IntervalTree.Add()` 仅标记 dirty。真正的递归建树发生在首次 `FillQuery()`。

当前加载期 `SoflanSupport/SoflanManager.mm.cs:245` 所谓预热只读取 position list，没有强制 interval tree 同步。于是首个播放帧扫描所有未来 note 时，可能集中构建大量 group 的树。

冷查询探针：

| 形状 | 耗时 | 分配 |
| --- | ---: | ---: |
| 1000 group × 3 segment | 约 6.8 ms | 约 5.4 MB |
| 5000 group × 3 segment | 约 60 ms | 约 27 MB |
| 单 group × 8192 segment | 约 6.9 ms | 约 9.2 MB |

建议：

- 在加载阶段增加显式 `PreparePreviewCache()`，同时构建 position list 并强制 tree sync。
- 更进一步提供 interval tree bulk build，避免逐项 Add 后再递归重建。
- 这会把尖峰从播放首帧移动到加载阶段；需要一起显示加载进度或设置预算，不能仅删除现有循环后把成本静默推回首帧。

### PERF-06 全组双缓存预热无效且会重复构建

`SoflanSupport/SoflanManager.mm.cs:243` 的日志 dump 外层 `foreach` 在 Release 中仍存在。虽然 `[Conditional("DEBUG")]` 会移除所有 `PatchLog.WriteLine()` 调用和插值参数，循环本身仍对每个 group 调用 `GetCachedSoflanPositionList_PreviewMode()`。

这个调用存在三层问题：

1. `CheckAndUpdateSoflanPositionList()` 无论请求哪种模式，都同时构建 Design 和 Preview 两份 position list。
2. 每个 group 都独立合并完整 BPM 事件。
3. 预热发生时只读取 `bpmList.cachedBpmContentHash`；首次时间转换调用 BPM getter 后会把随机 dirty hash 改成内容 hash，使刚构建的 group 缓存立即失效。

反射版本探针确认：

```text
PrewarmSoflanHash == 加载期随机 BpmHash
首次 GetCachedAllBpmUniformPositionList 后 BpmHash 改变
PrewarmSoflanHash != 新 BpmHash
```

即当前 Release 预热付出 CPU/内存后，下一次运行时访问仍会重建；同时 interval tree 依然没有真正构建。

压力探针显示 1000 group、8 BPM、3 SFL 约 11.5 ms/12.5 MB；5000 group 约 80.9 ms/62.2 MB。

建议：

- 先正规化/冻结 BPM snapshot，再准备 group 缓存。
- 分离 Design 与 Preview 的 valid/version，只构建请求的模式。
- 把调试 dump 与显式运行时预热拆开；Release 只调用明确的 Preview prepare API。
- 显式 prepare 应同时完成 interval tree，而不是只构建 position list。

### PERF-07 所有 note 无条件保存 TGrid

`SoflanSupport/SoflanManager.mm.cs:54` 在每次 `loadNote()` 时保存 note 起点 TGrid；只要 end time 有意义，还保存终点 TGrid。这个动作发生在检查 `#group` marker 之前，也不检查 note 类型或谱面是否最终含 SFL。

影响：

- 无 SFL 的普通谱面也为所有 note 建立至少一个字典项和引用类型 `TGrid`。
- 长物件增加第二份字典项。
- `clearAll()` 只 Clear 三个 note 字典，桶容量跨谱保留。

当前 `loadComposition()` 晚于 `loadNote()`，不能直接用 `containSoflans` 在加载 note 时早退。

建议：

- 与 PERF-01 一并设计：加载期可以先暂存紧凑的 total-grid，确认含 SFL 后再生成必要缓存；无 SFL 时立即释放。
- 至少限制到 `IsSupportedVisualSoflanKind()`，终点只存 Hold/BreakHold 等实际消费者。
- 若 note index 稠密，数组/结构体表通常比三个字典更紧凑。
- 必须保留原注释和最近修复的 TGrid 视觉时间轴语义。

### PERF-08 已解析 MA2 后再次完整读取文件

`Manager.NotesReader.mm.cs:20` 在原版 MA2 解析完成后调用 `loadComposition()`，但 `SoflanSupport/SoflanManager.mm.cs:200` 忽略传入的 `records`，通过 `File.ReadLines()` 再扫描整份谱面。

这是确定的加载期额外 IO/字符串成本；无 SFL 谱面也会扫描到 EOF。当前代码已比旧评审中的 `ReadAllLines + Split/LINQ` 更好：它使用流式读取和手写 tab 字段解析，因此旧描述不可原样沿用。

建议：

- 最佳方案是在原解析流程读取每行时旁路收集 SFL，或让 `records` 保留 SFL record。
- 次优方案保留二次扫描，但用 `OrdinalIgnoreCase`、减少重复字段扫描，并在 header/索引能证明无 SFL 时跳过。
- 直接改原版 record 识别表或更深的 IL 插入会增加版本兼容风险，优先级低于播放期前三项。

### PERF-09 大量停车/折返使范围查询退化

稳态正速时，interval tree 查询接近 `O(log S + K)`。大量停车或反向折返会让许多 segment 覆盖同一 Y 区间，`K` 接近总段数；随后还有 segment 排序、双向递归、range 排序和合并，最坏接近 `O(S log S)`。

1000 次稳态查询探针：

| 单 group 段数/形状 | 每次约耗时 |
| --- | ---: |
| 8192 全正速 | 1.3 µs |
| 512 全停车 | 261 µs |
| 2048 全停车 | 1.38 ms |
| 8192 全停车 | 6.34 ms |
| 8192 正负交替 | 5.72 ms |

建议：

- 加载期对单 group 段数、同 Y 重叠度设置诊断告警。
- 可研究合并连续零速 run 或建立同 Y run 索引。
- 算法重写必须以停车、反向、弹跳和 BPM 变化 fuzz 等价测试为门槛；这是正确性路径，不建议作为第一批优化。

### PERF-10 缺失 group 会被读取索引器创建

`Dependencies/SimpleSoflanFramework/SimpleSoflanFramework.Core/SoflanListMap.cs:26` 的索引器在 miss 时创建带默认 `1x` keyframe 的 `SoflanList`。运行时 `SoflanSupport/SoflanManager.mm.cs:330` 的读路径直接使用该索引器。

如果 note marker 指向没有 SFL 的 group，首帧全谱扫描会按 group 创建中性 list。探针显示约 `1.55 KB/缺失 group`；5000 个缺失 group 约 7.7 MB/7.1 ms，之后还可能叠加可见范围缓存和树成本。

建议：

- 加载完成后校验 marker 使用的 group 与 SFL group。
- 运行时读路径用 `TryGetValue()`；缺失时返回不写入 map 的中性行为。
- 不能直接共享一个可变 `SoflanList` 给可能修改它的通用 API；应把只读运行时路径与编辑器索引器语义分开。

### PERF-11 Debug 日志和面板会污染性能测试

Release 产物 IL 已确认：

- `loadComposition()` 中没有 `PatchLog.WriteLine` 外部调用。
- `NoteBase.Initialize()` 中没有 `BoxCollider2D` 引用。
- `SoflanPanelBehaviour` 只保留空的兼容方法，不含 `Update/OnGUI`。

因此以下风险只属于 Debug，不应泛化到 Release：

- `SoflanSupport/SoflanPanelBehaviour.mm.cs:23` 默认显示面板，`OnGUI()` 每次调用都会格式化字符串并使用 GUILayout。0.2 秒节流只限制数据刷新，不限制 OnGUI 字符串/IMGUI 分配。
- Debug note 初始化为每个视觉对象添加 `BoxCollider2D`，即使用户从未右键选择；这些 collider 会进入 Physics2D 世界。
- `PatchLog` 使用无界 `ConcurrentQueue`；SFL、Fixed marker 和 timing point dump 可能生产日志快于后台线程写盘与 `Debug.Log`。
- `EnablePatchLog=false` 只让 `WriteLine()` return；调用方的插值、`ToString()`、dump 遍历和时间转换已经发生，不能完全规避成本。

当前 Debug 面板已有有效改进：最多显示 50 group、0.2 秒刷新、`OverlapPointNonAlloc`、静态命中数组和复用 List。旧评审中“每帧显示所有 group”和“右键每次分配数组/List”已不成立。

建议：

- Debug 面板默认隐藏或按明确配置挂载。
- 在节流的 `Update` 中缓存完整显示文本/GUIContent，`OnGUI` 只绘制缓存。
- 只有启用右键选择时才给 note 添加 collider。
- 日志调用点先判断开关；成功日志改为摘要计数；队列增加上限和 dropped count。
- Unity API 是否允许后台线程调用还需实机验证；更稳妥的是后台只写文件，`Debug.Log` 在主线程限量输出。

## 可见范围算法不是直接 Y 判断

当前 `FillVisibleMsecRangesForGamePreview()` 是逆向范围算法，不是简单的数学精确逆像。随机测试比较：

```text
现算法：note 原始音频时间落在任一逆向 msec range
直接法：currentY <= noteY <= currentY + visibleMsec
```

结果：

| 场景 | 运行时形状样本 | 现算法可见/直接法不可见 | 现算法不可见/直接法可见 | 最大窗口外提前量 |
| --- | ---: | ---: | ---: | ---: |
| duration 正速 | 717364 | 8614 | 0 | 约 1.74 ms |
| duration 停车 | 717364 | 22133 | 21140（平台边界） | 约 3958 ms |
| duration 负速 | 714148 | 40557 | 4（边界） | 约 5150 ms |
| 交替折返 | 1009438 | 52530 | 2776（边界） | 约 5129 ms |
| 多 BPM 折返 | 1370906 | 58305 | 4（边界） | 约 4245 ms |

原因包括：

- 负速/停车递归使用保守扩张。
- 可见边界会量化到整数 TotalGrid，再转回毫秒。
- 框架 terminal 0/负速末段分支不完整；当前 MA2 duration 通常会在 EndTGrid 回到 `1x`，所以后者主要是框架级边界。

因此直接 Y 方案技术上能消除逆向查询、interval tree、窗口 cache 和每 note msec 转换，但它会减少当前数秒级的保守提前注册，也可能修复某些末段漏选。它属于行为改变，必须单独验证：

- 停车、反向、弹跳下的物件创建时机和对象池容量。
- 边界 note 是否在正确帧注册。
- FixedSoflan 不同速度下的窗口行为。
- Touch、Hold/BreakHold 头尾和判定时间保持不变。
- 极端 BPM/长谱的 float msec round-trip 与精确 TGrid→Y 差异。

第一阶段应选择 PERF-01 的预计算 msec 方案，以完全保留当前范围语义。

## 内存高水位与非泄漏项

以下现象真实，但应描述为容量权衡而不是泄漏：

- `registerNoteIndexToSoflanGroupMap`、起止 TGrid map、`visibleRangeListMap` 和 current group time map 的外层 Dictionary 在 `Clear()` 后保留桶容量。
- 切谱时 `visibleRangeListMap.Clear()` 已释放每 group 的 `Ranges` 和 scratch 引用；旧评审所说内部 List 跨谱永久保留不成立。
- 同一谱内保留 List/scratch 容量是为了降低播放期分配，GC 暂停前提下通常是合理选择。

可以在切谱时按容量阈值重建外层 Dictionary，以换取释放高水位内存；不要在播放热路径频繁丢弃内部缓冲。

## 低优先级与待实机采样项

### Tap 热路径中的确定死计算

`Monitor.NoteBase.mm.cs:202` 读取的 `currentTime` 没有被使用；`Monitor.NoteBase.mm.cs:219` 开始计算的玩家速度、`offsetYAdj` 最终又乘以恒为 `0` 的 `sign`。Release IL 中这些 getter 和算术仍然存在，每个活跃 Tap/Break/Star 每帧都会执行。

如果当前设计明确保持 `sign = 0`，可以删除整条死计算，视觉结果不变。若注释表示未来准备恢复 offset，则应把计算一起放回真正启用该功能的分支，而不是常驻热路径。

### 原版 scale 写入后被 Soflan scale 覆盖

Tap、Break、Hold、BreakHold 都先执行 `orig_NoteCheck()`，随后在 Soflan 分支再次写 `transform.localScale`。第二次写入是功能需要，第一次写入可能成为多余的 Unity native 边界调用，但不能据静态代码确定它在目标 Unity/Mono 上的实际占比。

不要跳过整个 `NoteCheck()`，因为它还负责真实音频时间上的判定。只有 Unity Profiler 证明此处显著后，才考虑用精确 IL 分支绕过原版方法中的视觉 scale 尾段，并以判定回归为前提。

## 已修复或已排除的旧结论

| 旧项 | 当前状态 | 依据 |
| --- | --- | --- |
| P-001 每帧主动遍历所有 group | 已修复，但全谱 note 扫描仍会触达大量 group | 当前按 group 懒查询；PERF-01/02 是剩余主因 |
| P-002 current Soflan time 重复计算 | 已修复同帧同 group 重复；独立 group 仍各算一次 | `GetCurrentSoflanTimeCached()` 已被所有支持物件使用 |
| P-003 Debug 绘制全部 group | 已修复 | 0.2 秒刷新且最多 50 group |
| P-004 `ReadAllLines + Split/LINQ` | 已修复这部分 | 当前 `File.ReadLines` + 手写 tab 解析；PERF-08 是剩余二次扫描 |
| P-005 活跃物件缺少 group time cache | 已修复 | Note/Break/Hold/Touch 共用 manager 缓存 |
| R-001 旧范围 API“可能分配” | 描述过时 | 当前是填充式 direct-msec API；剩余 40 B 来自 BPM getter |
| R-004 右键 `OverlapPointAll + new List` | 已修复 | 当前 NonAlloc + 静态数组 + 复用 List |
| R-007 range List 跨谱保留 | 不成立 | 外层 map 在切谱 Clear，内部对象失去引用 |
| R-008 Debug 面板常驻 | 有意生命周期，不是泄漏 | 只在 Debug 挂载；成本归 PERF-11 |
| R-009 resolver 事件订阅 | 非性能缺陷 | `_registered` 防重复，生命周期为 AppDomain |
| MonoModRules LINQ/Cecil 扫描 | 非游戏热路径 | 只在 patch 应用期运行 |

## 落地路线

### 第一阶段：低风险、直接收益

1. 加载后预计算 note 头尾音频毫秒。
2. 修复范围缓存键为 frame + `(group, visibleMsec)`。
3. current group time cache 改为 version entry，不再每帧 Clear。
4. 增加覆盖 Tap/Touch/Fixed 窗口交替的缓存命中计数测试。

验收：

- 无 SFL 谱面不增加播放期分配。
- 同一帧同一 `(group, visibleMsec)` 只构建一次范围。
- 播放期不再调用 `ConvertTGridToAudioTime()` 取得 note 时间。
- 停车、反向、弹跳的可见结果与当前版本逐 note 对比完全一致。

### 第二阶段：框架缓存与加载尖峰

1. BPM dirty/version + 时间表二分查询。
2. 分离 Design/Preview cache。
3. 正规化 BPM snapshot 后显式 Prepare Preview + interval tree。
4. 缺失 group 校验和只读中性 fallback。

验收：

- BPM 缓存命中零分配，Audio→TGrid/TGrid→Audio 不再线性扫描。
- 加载期 prepare 后，首个范围查询不再建树。
- 现有 1920 个 game-shape 等价查询继续通过。
- 增加首 BPM 前、同 grid 重复 BPM、边界四舍五入测试。

### 第三阶段：行为级优化

评估直接 note Y 判断、停车 run 合并或新的可见索引。必须先建立当前范围算法的 golden corpus，再做 Unity 实机视觉、对象池和判定回归；不能作为纯性能重构直接替换。

## 本次验证记录

执行并通过：

```powershell
dotnet build -c Release Assembly-CSharp.SoflanSupport.mm.csproj
dotnet build -c Debug Assembly-CSharp.SoflanSupport.mm.csproj
dotnet build -c Release Dependencies\SimpleSoflanFramework\SimpleSoflanFramework.Benchmarks.Net35\SimpleSoflanFramework.Benchmarks.Net35.csproj
Dependencies\SimpleSoflanFramework\SimpleSoflanFramework.Benchmarks.Net35\bin\Release\SimpleSoflanFramework.Benchmarks.Net35.exe
```

现有 benchmark 结果之一：

```text
Equivalence: OK (1920 game-shape queries)
GetVisibleRanges_PreviewMode:          约 201 ms / 50k，Gen0 32
FillVisibleRangesForGamePreview:       约 63 ms / 50k，Gen0 6
FillVisibleMsecRangesForGamePreview:   约 52 ms / 50k，Gen0 1
```

Release/Debug 都成功构建；一次并行 Debug 构建报告 `MSB3270`（patch MSIL 与目标 `Assembly-CSharp` AMD64 引用架构不一致），Release 无警告。这是现有项目/目标引用形状，不是本次文档改动引入，也不属于本次性能问题。

绝对耗时来自桌面 CLR 4 和游戏程序集引用，不等同于 Unity Mono 实机帧时间；分配 IL、调用次数、线性复杂度和相对趋势仍成立。最终优化应补 Unity Profiler 的主线程耗时与 GC Alloc 采样。
