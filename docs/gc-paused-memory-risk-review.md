# GC 暂停前提下的内存风险复查

范围: 当前 patch 运行时代码与最近新增的 `Monitor.BreakNote.mm.cs`。`SoflanCalculator/` 已在 csproj 中排除, 属于离线工具, 不计入游戏运行期风险。

前提: 游戏播放过程中 GC 暂停。因此即使某些对象不是传统意义上的“泄漏”, 只要在播放热路径持续分配, 也会在一首歌内累积到 GC 恢复后才释放, 表现为播放期间内存持续上升或结算/切场景时 GC 抖动。

## 结论

- 新增的 `BreakNote` soflan 缩放 patch 本身没有发现集合、事件订阅或静态引用泄漏。
- 主要风险集中在调试面板 `OnGUI`、soflan 可见性缓存重建、静态选中 note 引用和日志。
- 如果目标是实机稳定运行, `R-003` 已处理; 建议继续优先处理 `R-001`、`R-002`。

## 风险项

### R-001 播放热路径可能每帧产生可见范围枚举/集合分配

位置:
- `SoflanSupport/SoflanManager.mm.cs:181`
- `SoflanSupport/SoflanManager.mm.cs:212`
- `SoflanSupport/SoflanManager.mm.cs:227`
- `Monitor.Game.GameCtrl.mm.cs:31`

现象:
`GameCtrl.UpdateCtrl` 的 soflan 可见性判定会调用 `SoflanManager.checkNoteVisible()`。当前实现已改为按 group 懒计算: 帧时间变化时只递增 `visibleRangeCacheVersion`, 本帧首次检查某个 group 时才调用:

```csharp
var visibleRanges = soflanList.Value
    .GetVisibleRanges_PreviewMode(soflanTimeMsec, apperMsec, 0, bpmList, 1);
```

当前代码复用了 `visibleRangeListMap` 里的 `VisibleMsecRangeCache.Ranges`, 但 `GetVisibleRanges_PreviewMode()` 返回值是否每次分配由外部库决定。从调用形态看, 它很可能创建临时集合/迭代器。

影响:
播放中 GC 暂停时, 若该方法分配, 临时对象会在整首歌期间累积。P-001 已将成本限制到“本帧实际被检查到的 group”, 但当前帧触发可见性检查越频繁, 累积仍会越明显。

建议:
- 优先用 Unity Profiler / Mono allocation 采样确认 `GetVisibleRanges_PreviewMode()` 是否分配。
- 如确认分配, 考虑在本项目中维护非分配版本, 或扩展 SimpleSoflanFramework 提供写入调用方 `List` 的 API。
- 在确认前, 该项按高风险处理。

### R-002 调试面板 `OnGUI` 可见时会在播放中持续分配字符串和 IMGUI 对象

位置:
- `SoflanSupport/SoflanPanelBehaviour.mm.cs:135`
- `SoflanSupport/SoflanPanelBehaviour.mm.cs:143`
- `SoflanSupport/SoflanPanelBehaviour.mm.cs:148`
- `SoflanSupport/SoflanPanelBehaviour.mm.cs:149`
- `SoflanSupport/SoflanPanelBehaviour.mm.cs:152`
- `SoflanSupport/SoflanPanelBehaviour.mm.cs:155`

现象:
`OnGUI()` 在面板可见时每次 IMGUI 调用都会格式化字符串并调用 `GUILayout`。Unity IMGUI 通常一帧会多次调用, 且 `GUILayout.Label`、插值字符串、`TimeSpan` 格式化都可能产生托管分配。

影响:
播放中 GC 暂停时, 面板显示期间的临时字符串/GUILayout 分配会持续累积。该风险与谱面无关, 只要面板可见就存在。

建议:
- 实机/发布构建默认关闭或移除调试面板。
- 如果需要保留面板, 建议改为 `Update` 中低频刷新缓存文本, `OnGUI` 只绘制缓存; 或使用非 IMGUI UI。
- 当前 `_visible` 默认是 `true`, 若只用于调试, 建议默认改为 `false`。

### R-003 静态 `_selectedNote` 可能滞留 NoteBase/GameObject 引用

位置:
- `SoflanSupport/SoflanPanelBehaviour.mm.cs:49`
- `SoflanSupport/SoflanPanelBehaviour.mm.cs:56`
- `SoflanSupport/SoflanPanelBehaviour.mm.cs:58`
- `SoflanSupport/SoflanPanelBehaviour.mm.cs:131`

处理状态: 已处理。

现象:
调试面板用静态字段 `_selectedNote` 保存被右键选中的 `NoteBase`。目前清理路径有:
- note 池化复用时 `OnNoteReinitialized()`
- 被选中 note 结束时 `OnSelectedNoteEnded()`
- 谱面 `__SoflanClearAll()` 时 `ClearSelectedNote()`
- 面板 `OnDestroy()` 时 `ClearSelectedNote()`
- 面板 `Update()` 开始处发现选中 Unity 对象已销毁或 GameObject 失活时 `ClearStaleSelectedNote()`

如果切歌、退场、异常路径或对象销毁没有经过原有两个路径, 现在会通过谱面清理、面板销毁或下一帧 stale 检查释放静态引用。

影响:
原风险是实际引用滞留。处理后, 常规切谱、note 结束/复用、对象销毁后的下一帧都会释放 `_selectedNote`。

建议:
- 保持当前清理入口。
- 如果后续新增其它 note 销毁/回收路径, 继续复用 `SoflanPanelBehaviour.ClearSelectedNote()` 或 `OnNoteReinitialized()`。

### R-004 右键选中路径有点击时分配

位置:
- `SoflanSupport/SoflanPanelBehaviour.mm.cs:104`
- `SoflanSupport/SoflanPanelBehaviour.mm.cs:115`
- `SoflanSupport/SoflanPanelBehaviour.mm.cs:116`
- `SoflanSupport/SoflanPanelBehaviour.mm.cs:123`

现象:
`HandleSelectClick()` 使用 `Physics2D.OverlapPointAll()` 和 `new List<NoteBase>()`。`OverlapPointAll()` 返回数组, `List` 与排序也会产生临时对象或扩容。

影响:
只在右键点击时发生, 不是每帧热路径。播放中 GC 暂停时, 多次右键调试会让这些临时对象累积到播放结束。

建议:
- 如果保留调试功能, 可改用 `OverlapPointNonAlloc()` 和复用的静态/实例缓冲。
- 发布环境建议禁用右键选择。

### R-005 日志在运行期可能制造托管分配和磁盘增长

位置:
- `SoflanSupport/PatchLog.mm.cs:22`
- `SoflanSupport/PatchLog.mm.cs:29`
- `SoflanSupport/PatchLog.mm.cs:31`
- `Monitor.Game.GameCtrl.mm.cs:45`

现象:
`PatchLog.WriteLine()` 使用字符串插值、`File.AppendAllText()` 和 `UnityEngine.Debug.Log()`。多数日志发生在加载阶段, 但 `__SoflanLogRegistNoteFailed()` 位于 `GameCtrl.UpdateCtrl` 的运行路径, RegistNote 失败时可在播放中触发。

影响:
播放中 GC 暂停时, 如果频繁触发失败日志, 托管字符串会累积, 文件也会增长, IO 还可能卡顿。

建议:
- 实机测试/发布时关闭 `EnablePatchLog`。
- 对播放中可能触发的日志加限流或只记录计数。

### R-006 SoflanManager 字典容量会按历史最大谱面保留

位置:
- `SoflanSupport/SoflanManager.mm.cs:21`
- `SoflanSupport/SoflanManager.mm.cs:24`
- `SoflanSupport/SoflanManager.mm.cs:36`
- `SoflanSupport/SoflanManager.mm.cs:38`

现象:
`clearAll()` 对 `visibleRangeListMap` 和 `registerNoteIndexToSoflanGroupMap` 使用 `Clear()`。这会清空元素, 但字典内部桶容量会保留。大谱面之后切小谱面, 容量不会自动缩回。

影响:
这不是持续泄漏, 是高水位内存保留。GC 恢复后也不会释放字典内部数组, 因为字典对象仍存活。

建议:
- 如果希望切歌后释放高水位容量, 在 `clearAll()` 中重新赋值:
  - `registerNoteIndexToSoflanGroupMap = new Dictionary<int, int>();`
  - `visibleRangeListMap = new Dictionary<int, VisibleMsecRangeCache>();`
- 若更重视避免加载期重新分配, 保持现状也可以接受。

### R-007 `visibleRangeListMap` 内部 List 容量会按历史最大可见范围保留

位置:
- `SoflanSupport/SoflanManager.mm.cs:220`
- `SoflanSupport/SoflanManager.mm.cs:222`
- `SoflanSupport/SoflanManager.mm.cs:225`
- `SoflanSupport/SoflanManager.mm.cs:233`

现象:
每个已访问 soflan group 对应的 `VisibleMsecRangeCache.Ranges` 被复用, 本帧该 group 首次检查时 `Clear()` 后重新填充。`Clear()` 不释放内部数组容量。

影响:
和 R-006 类似, 是高水位容量保留, 不是无限增长。好处是减少播放中分配; 代价是内存按历史最大容量常驻。

建议:
- 播放中建议保留现状, 因为 GC 暂停时避免重复分配更重要。
- 在 `clearAll()` 或切歌时可考虑丢弃整个 `visibleRangeListMap`, 释放高水位容量。

### R-008 `DontDestroyOnLoad` 面板是常驻对象

位置:
- `SoflanSupport/GamePlayFumenController.mm.cs:17`
- `SoflanSupport/GamePlayFumenController.mm.cs:30`
- `SoflanSupport/GamePlayFumenController.mm.cs:36`
- `SoflanSupport/GamePlayFumenController.mm.cs:38`

现象:
`MountPanelIfNeeded()` 创建一个 `SoflanPanel` GameObject 并 `DontDestroyOnLoad()`。`_panelMounted` 保证正常情况下只创建一次。

影响:
这是有意的常驻调试对象, 不是重复泄漏。但只要存在, 其 `Update()` 和 `OnGUI()` 相关分配风险就存在。

建议:
- 发布环境不挂载面板, 或用配置控制是否创建。
- 如果保留, 需要确保 R-002/R-003 被处理。

### R-009 DependencyAssemblyResolver 事件订阅为 AppDomain 生命周期

位置:
- `SoflanSupport/DependencyAssemblyResolver.mm.cs:43`
- `SoflanSupport/DependencyAssemblyResolver.mm.cs:47`
- `SoflanSupport/DependencyAssemblyResolver.mm.cs:64`

现象:
`Register()` 通过 `_registered` 防止重复订阅 `AssemblyResolve`。订阅后 handler 与静态字段存活到 AppDomain 结束。

影响:
正常游戏进程中这是预期生命周期, 不属于泄漏。若存在热重载/多次加载不同补丁程序集的环境, 才可能累积旧程序集引用。

建议:
- 普通 BepInEx/MonoMod 启动模式无需处理。
- 若后续支持热重载, 需要提供反注册路径。

### R-010 `BreakNote` soflan patch 未引入额外泄漏点

位置:
- `Monitor.BreakNote.mm.cs:13`
- `Monitor.BreakNote.mm.cs:14`
- `Monitor.BreakNote.mm.cs:15`
- `Monitor.BreakNote.mm.cs:32`

现象:
新增 patch 只在 `BreakNote` 实例上保存 `SoflanManager` 引用、布尔值和 float。`NoteCheck()` 不创建集合, 不订阅事件, 不写静态字段。

影响:
这些字段随 note 实例生命周期存在。由于 `SoflanManager` 本身是 Singleton, 每个 note 保存该引用不会额外阻止 Singleton 回收。

建议:
- 该项无需处理。

## 非播放热路径分配

以下代码会分配, 但主要发生在谱面加载、补丁应用或用户操作阶段, 不属于播放中每帧风险:

- `SoflanSupport/SoflanManager.mm.cs:72`: `File.ReadAllLines(filePath)` 读取谱面文件。
- `SoflanSupport/SoflanManager.mm.cs:76`: `Split()` / `Select()` / `ToArray()` 解析 SFL 行。
- `SoflanSupport/SoflanManager.mm.cs:47`: `AsEnumerable().Reverse()` 用于读取 note soflan group。
- `SoflanSupport/DependencyAssemblyResolver.mm.cs:152`: resolver 注册时创建搜索目录数组。
- `MonoModRules.cs`: 只在 patch 应用期运行, 不进入游戏运行热路径。

## 优先级建议

1. 先确认并处理 R-001。它最可能在播放热路径持续分配, 且与 GC 暂停直接冲突。
2. 发布/实机环境默认禁用调试面板, 可同时规避 R-002、R-004, 并进一步降低 R-003 相关调试状态残留面。
3. 关闭或限流播放期日志, 规避 R-005。
4. 根据内存预算决定是否处理 R-006/R-007 的高水位容量保留。
