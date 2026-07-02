# 大量 Soflan 变速组性能风险检查

## 前提

本检查基于当前实现, 假设谱面存在几千个 Soflan 变速组。这个场景会出现在“每个物件独立变速组”的谱面设计里, 例如每个 Tap 独占一组停车、卡顿或弹跳曲线。

播放过程中 GC 可能处于暂停状态, 所以下面的风险同时关注 CPU 掉帧和托管分配。

## 结论

P-001 已处理: 当前实现已把 `SoflanManager.checkNoteVisible()` 的可见范围缓存改成按当前 note 的 group 懒计算, 不再在播放热路径中每帧遍历全谱所有变速组。

P-002 已处理: `GameCtrl` 中的每帧 SoflanTime 缓存已经传入 `SoflanManager.checkNoteVisible()`, 避免同一帧同一 group 在可见性判断里重复计算当前 SoflanTime。

剩余主要风险集中在 P-003/P-004: DEBUG 面板显示全部 group 时的全组计算与绘制, 以及加载期大量 SFL 日志/解析成本。

## 风险项

### P-001 每帧按所有 Soflan 组重建可见范围缓存（已处理）

位置:

- `SoflanSupport/SoflanManager.mm.cs:185`
- `SoflanSupport/SoflanManager.mm.cs:215`
- `SoflanSupport/SoflanManager.mm.cs:219`
- `SoflanSupport/SoflanManager.mm.cs:230`

旧流程:

```csharp
public bool checkNoteVisible(NoteData noteData, float currentMsec, float apperMsec)
{
    if (cachedCalculatedCurrentMsec != currentMsec || cachedCalculatedApperMsec != apperMsec)
    {
        rebuildCacheVisibleNoteMap(currentMsec, apperMsec);
        cachedCalculatedCurrentMsec = currentMsec;
        cachedCalculatedApperMsec = apperMsec;
    }
    ...
}
```

`rebuildCacheVisibleNoteMap()` 内部:

```csharp
foreach (KeyValuePair<int, SoflanList> soflanList in soflanListMap)
{
    var soflanTimeMsec = ConvertAudioTimeToY_PreviewMode(currentMsec, soflanList.Key);
    ...
    var visibleRanges = soflanList.Value.GetVisibleRanges_PreviewMode(...);
}
```

旧影响:

- 每帧遍历所有变速组。
- 每组调用一次 `ConvertAudioTimeToY_PreviewMode()`。
- 每组调用一次 `GetVisibleRanges_PreviewMode()`。
- 每个返回 range 还会做 TGrid 到 AudioTime 的转换。
- 如果外部库 `GetVisibleRanges_PreviewMode()` 内部有集合/迭代器分配, GC 暂停时风险更高。

几千组时, 这是播放期间最可能造成掉帧的点。

处理情况:

- 已删除热路径中的 `rebuildCacheVisibleNoteMap()` 全组重建。
- 当前代码使用 `visibleRangeCacheVersion` 标记当前帧缓存版本。
- `checkNoteVisible()` 先调用 `BeginVisibleRangeFrame()`, 然后只对当前 note 的 `soflanGroup` 调用 `GetVisibleRangeList()`。
- 同一帧同一 group 后续复用 `VisibleMsecRangeCache.Ranges`。
- 成本从“每帧所有 group”降低为“本帧实际被检查到的 group”。

### P-002 `GameCtrl` 的每帧 SoflanTime 缓存计算结果未实际使用（已处理）

位置:

- `Monitor.Game.GameCtrl.mm.cs:35`
- `Monitor.Game.GameCtrl.mm.cs:36`
- `Monitor.Game.GameCtrl.mm.cs:37`

旧代码:

```csharp
var noteSoflanGroup = soflanManager.getNoteSoflanGroup(note);
if (!cachedSoflanTimeMap.TryGetValue(noteSoflanGroup, out var soflanTime))
    cachedSoflanTimeMap[noteSoflanGroup] = soflanTime = soflanManager.ConvertAudioTimeToY_PreviewMode(...);
if (!soflanManager.checkNoteVisible(note, NotesManager.GetCurrentMsec(), num))
    return 2;
```

旧问题:

- `soflanTime` 变量计算后没有传入 `checkNoteVisible()`。
- `checkNoteVisible()` 内部又会计算 Soflan 时间。
- 当每个物件独立组时, 本帧扫描到多少不同组, 这里就可能多算多少次。

处理情况:

- `GameCtrl.__SoflanNoteDecision()` 现在先保存一次 `currentMsec`。
- `cachedSoflanTimeMap` 仍然作为“本帧 group -> currentSoflanTime”的缓存。
- `cachedSoflanTimeMsec` 绑定缓存对应的 `currentMsec`; 如果同一次 `UpdateCtrl` 中时间变化, 会清空并按新时间重算, 避免同组复用旧 SoflanTime。
- 新增的 `checkNoteVisible(note, currentMsec, apperMsec, soflanGroup, currentSoflanTime)` 直接使用传入的 `currentSoflanTime`。
- `SoflanManager.GetVisibleRangeList()` 不再自己调用 `ConvertAudioTimeToY_PreviewMode()`。

### P-003 DEBUG 面板显示全部 group 时按所有组计算和绘制

位置:

- `SoflanSupport/SoflanPanelBehaviour.mm.cs:111`
- `SoflanSupport/SoflanManager.mm.cs:274`
- `SoflanSupport/SoflanManager.mm.cs:280`

当前代码:

```csharp
if (_showAllGroups) sm.FillCurrentSpeeds(_msec, _allSpeeds);
```

`FillCurrentSpeeds()` 内部:

```csharp
foreach (KeyValuePair<int, SoflanList> pair in soflanListMap)
    outList.Add(new GroupSpeed(pair.Key, pair.Value.CalculateSpeed(bpmList, tGrid)));
```

影响:

- `_showAllGroups` 打开时, DEBUG 每帧遍历所有组。
- OnGUI 还会把所有组逐行绘制出来。
- 几千组时即使没有 GC, CPU 和 IMGUI 绘制也会很重。

建议:

- 默认禁止显示全部组。
- 添加分页、过滤、上限数量, 例如最多显示 50 组。
- 只显示选中物件所在组、0 组和附近几个组。

### P-004 加载期 SFL 解析和日志会被大量组/大量 SFL 行放大

位置:

- `SoflanSupport/SoflanManager.mm.cs:74`
- `SoflanSupport/SoflanManager.mm.cs:78`
- `SoflanSupport/SoflanManager.mm.cs:87`
- `SoflanSupport/SoflanManager.mm.cs:110`
- `SoflanSupport/PatchLog.mm.cs:29`
- `SoflanSupport/Setting.mm.cs:15`

当前流程:

- `File.ReadAllLines(filePath)` 一次性读取整个谱面。
- 每条 SFL 使用 `Split('\t').Select(...).ToArray()`。
- 每条 SFL 解析后写 `PatchLog`。
- 加载结束后还 dump 所有组的 timing point。
- `EnablePatchLog` 默认是 `true`。

影响:

- 这是加载期问题, 不直接发生在播放热路径。
- 几万条 SFL 时, 文件读取、字符串切分、LINQ、数组分配、日志写盘都会变重。
- `PatchLog.WriteLine()` 内部使用 `File.AppendAllText()` 和 `UnityEngine.Debug.Log()`, 大量日志会显著拖慢加载。

建议:

- 发布/实机测试默认关闭 `EnablePatchLog`。
- 不要逐条记录成功解析的 SFL。
- dump timing point 应改成手动触发的 DEBUG 功能。
- 解析可改为流式 `File.ReadLines()` 和非 LINQ 字段解析, 但优先级低于 P-001。

### P-005 活跃物件每帧 SoflanTime 计算成本随可见物件数增加

位置:

- `Monitor.NoteBase.mm.cs:108`
- `Monitor.BreakNote.mm.cs:47`
- `Monitor.HoldNote.mm.cs:47`
- `Monitor.BreakHoldNote.mm.cs:47`

当前行为:

- Tap/Break 每帧用当前 group 调 `ConvertAudioTimeToY_PreviewMode()`。
- Hold/BreakHold 每帧也会调一次当前 group 的 SoflanTime, 再分别算头尾 diff。

影响:

- 这个成本主要跟“当前活跃物件数量”相关, 不直接跟全谱组数相关。
- 如果独立组导致可见性判断放宽, 活跃物件数变多, 这里的成本也会跟着上升。

建议:

- 对同一帧同一 group 的 `currentSoflanTime` 做缓存。
- Tap/Break/Hold 共用同一个本帧 group time cache。
- 优先级低于 P-001, 因为 P-001 是全谱组数级别的放大。

### P-006 缺失组可能通过索引器隐式扩展 `soflanListMap`

位置:

- `SoflanSupport/SoflanManager.mm.cs:155`
- `SoflanSupport/SoflanManager.mm.cs:157`

当前代码:

```csharp
public SoflanList getSoflanList(int soflanGroup)
{
    return soflanListMap[soflanGroup];
}
```

根据 `SoflanCalculator/SoflanCalcEngine.cs` 中与运行时同步的注释, `SoflanListMap` 索引器在缺失组时可能会自动创建空列表。若谱面 note 标了 `#N`, 但没有对应 SFL, 运行时访问该组可能扩大 `soflanListMap`。

影响:

- 缺失组会变成空 SoflanList, 速度等价于 `1.0x`。
- 后续 P-001 的全组遍历可能把这些空组也纳入每帧处理。

建议:

- 加载后校验 note 使用的 group 是否都有 SFL。
- 对缺失组不要写入 `soflanListMap`, 或在可见性 rebuild 中跳过空组。
- 生成谱面时避免 note group 与 SFL group 不匹配。

## 优先级

1. P-001 已完成: 可见性判断已改为按需计算当前 group, 不再每帧遍历所有组。
2. P-002 已完成: `cachedSoflanTimeMap` 已接入可见性判断, 避免重复计算。
3. 限制 P-003: DEBUG 面板禁止直接显示几千组。
4. 降低 P-004: 关闭默认日志, 移除加载期逐条 SFL 日志。
5. 优化 P-005: 建立播放帧级 group time cache。
6. 防守 P-006: 校验缺失 group, 避免索引器隐式扩大组表。

## 对当前测试谱面的影响

当前测试谱面把 5/7/8 轨道改成每个物件独立组后, 已经从 8 个组增长到数百个组。这个规模可以用于验证 P-001 的趋势, 但还没达到几千组的最坏情况。

如果继续把更多物件改成独立组, 或把物件间隔改得更密, P-001 已不再按全谱组数重建可见范围, P-002 也已避免可见性路径重复计算当前 SoflanTime。后续应优先处理 P-003/P-004, 避免 DEBUG 面板全组遍历和加载期日志成为新的瓶颈。
