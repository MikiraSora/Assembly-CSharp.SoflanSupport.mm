// SoflanSupport.SoflanManager — 新增类型, verbatim 自 head commit 2a7a4a4.
// 依赖外部 SimpleSoflanFramework.Core.dll (OngekiFumenEditor.Core.* 命名空间).
// 运行时需将该 DLL 部署到 Sinmai_Data/Managed/.
using Manager;
using OngekiFumenEditor.Core.Base;
using OngekiFumenEditor.Core.Base.Collections;
using OngekiFumenEditor.Core.Base.OngekiObjects;
using OngekiFumenEditor.Core.Modules.FumenVisualEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SoflanSupport
{
    public class SoflanManager
    {
        private SoflanListMap soflanListMap = new();
        private BpmList bpmList = new BpmList();
        private bool containSoflans = false;
        private Dictionary<int, int> registerNoteIndexToSoflanGroupMap = new();

        private float cachedCalculatedCurrentMsec = float.MinValue;
        private Dictionary<int, List<VisibleMsecRange>> visibleRangeListMap = new();

        /// <summary>
        /// clear all
        /// </summary>
        public void clearAll()
        {
            soflanListMap = new();
            bpmList = new();
            containSoflans = false;

            cachedCalculatedCurrentMsec = float.MinValue;
            visibleRangeListMap.Clear();

            registerNoteIndexToSoflanGroupMap.Clear();

            PatchLog.WriteLine("SoflanManager cleared");
        }

        public void loadNote(NoteData noteData, MA2Record record, NotesReader sr)
        {
            if (noteData == null)
                return;
            foreach (var str in record._str.AsEnumerable().Reverse())
            {
                if (str?.StartsWith("#") ?? false)
                {
                    if (!int.TryParse(str.TrimStart('#').Trim(), out var soflanGroup))
                    {
                        PatchLog.WriteLine($"register noteIndex:{noteData.indexNote} failed, str:{str}");
                        return;
                    }

                    registerNoteIndexToSoflanGroupMap[noteData.indexNote] = soflanGroup;
                    PatchLog.WriteLine($"register noteIndex:{noteData.indexNote}, soflanGroup:{soflanGroup}");
                }
            }
        }

        public void loadComposition(MA2RecordList records, NotesReader sr)
        {
            var filePath = sr.GetHeader()._notesName;
            if (!File.Exists(filePath))
            {
                //log error
                return;
            }

            foreach (var line in File.ReadAllLines(filePath))
            {
                if (line.StartsWith("SFL", StringComparison.InvariantCultureIgnoreCase))
                {
                    var split = line.Split('\t').Select(x => x.Trim()).ToArray();

                    if (!tryParseSoflan(split, out var soflan))
                    {
                        PatchLog.WriteLine($"parse soflan failed, line content:{line}");
                        break;
                    }
                    soflanListMap.Add(soflan);
                    containSoflans = true;
                    PatchLog.WriteLine($"parse soflan: {soflan}");
                }
            }


            foreach (var item in sr.GetCompositioin()._bpmList)
            {
                if (item.time.grid == 0)
                {
                    bpmList.FirstBpm = item.bpm;
                }
                else
                {
                    var bpmChange = new BPMChange
                    {
                        BPM = item.bpm,
                        TGrid = item.time.ToTGrid(sr)
                    };

                    bpmList.Add(bpmChange);
                }
            }

            PatchLog.WriteLine($"-------DUMP SOFLAN TIMING POINTS-------");
            PatchLog.WriteLine($"FilePath: {sr.GetHeader()._notesName}");
            foreach (KeyValuePair<int, SoflanList> pair in soflanListMap)
            {
                var soflanGroup = pair.Key;
                var soflanList = pair.Value;

                PatchLog.WriteLine($"");
                PatchLog.WriteLine($"SoflanGroup: {soflanGroup}");
                foreach (var timingPoint in soflanList.GetCachedSoflanPositionList_PreviewMode(bpmList))
                    PatchLog.WriteLine($"\t\t * AudioTime:{TGridCalculator.ConvertTGridToAudioTime(timingPoint.TGrid, bpmList).TotalMilliseconds}ms {timingPoint}");
            }

            PatchLog.WriteLine($"---------------------------------------");
        }

        private bool tryParseSoflan(string[] paramList, out ISoflan soflan)
        {
            try
            {
                soflan = new Soflan()
                {
                    TGrid = new TGrid(int.Parse(paramList.ElementAtOrDefault(1)), int.Parse(paramList.ElementAtOrDefault(2))),
                    Speed = float.Parse(paramList.ElementAtOrDefault(4)),
                    SoflanGroup = 0
                };
                soflan.EndTGrid = soflan.TGrid + new GridOffset(0, int.Parse(paramList.ElementAtOrDefault(3)));
                var soflanGroup = paramList.ElementAtOrDefault(5);
                if (!string.IsNullOrWhiteSpace(soflanGroup))
                    soflan.SoflanGroup = int.Parse(soflanGroup);
                return true;
            }
            catch (Exception e)
            {
                //todo log ex
                soflan = default;
                return false;
            }
        }

        public bool containsSoflans()
        {
            return containSoflans;
        }

        public SoflanList getSoflanList(int soflanGroup)
        {
            return soflanListMap[soflanGroup];
        }

        //-------------------------------------------

        private struct VisibleMsecRange
        {
            public VisibleMsecRange(float minMSec, TGrid minTGrid, float maxMSec, TGrid maxTGrid)
            {
                MinMSec = minMSec;
                MinTGrid = minTGrid;
                MaxMSec = maxMSec;
                MaxTGrid = maxTGrid;
            }

            public float MinMSec { get; }
            public TGrid MinTGrid { get; }
            public float MaxMSec { get; }
            public TGrid MaxTGrid { get; }

            public bool Contain(float msec)
            {
                return MinMSec <= msec && msec <= MaxMSec;
            }
        }

        public bool checkNoteVisible(NoteData noteData, float currentMsec, float apperMsec)
        {
            if (cachedCalculatedCurrentMsec != currentMsec)
            {
                rebuildCacheVisibleNoteMap(currentMsec, apperMsec);
                cachedCalculatedCurrentMsec = currentMsec;
            }

            if (!visibleRangeListMap.TryGetValue(getNoteSoflanGroup(noteData), out var visibleRangeList))
                return false;

            // foreach 替代 LINQ Any, 避免每帧闭包/委托/迭代器分配 (热路径零分配).
            var msec = noteData.time.msec;
            foreach (var range in visibleRangeList)
            {
                if (range.Contain(msec))
                    return true;
            }
            return false;
        }

        public int getNoteSoflanGroup(int noteIndex)
        {
            return registerNoteIndexToSoflanGroupMap.TryGetValue(noteIndex, out var soflanGroup) ? soflanGroup : 0;
        }

        public int getNoteSoflanGroup(NoteData noteData)
        {
            return getNoteSoflanGroup(noteData.indexNote);
        }

        private void rebuildCacheVisibleNoteMap(float currentMsec, float apperMsec)
        {
            // 不 Clear 整个 map (会丢弃已分配的 List 容量); 改为逐 key 复用已有 List (Clear + 重填),
            // 仅对新出现的 soflan group 才 new List. 避免每帧 LINQ Select/ToList 的闭包/委托/迭代器/List 分配.
            foreach (KeyValuePair<int, SoflanList> soflanList in soflanListMap)
            {
                var soflanTimeMsec = ConvertAudioTimeToY_PreviewMode(currentMsec, soflanList.Key);

                if (!visibleRangeListMap.TryGetValue(soflanList.Key, out var list))
                {
                    list = new List<VisibleMsecRange>();
                    visibleRangeListMap[soflanList.Key] = list;
                }
                list.Clear();

                var visibleRanges = soflanList.Value
                    .GetVisibleRanges_PreviewMode(soflanTimeMsec, apperMsec, 0, bpmList, 1);
                foreach (var x in visibleRanges)
                {
                    var minMsec = (float)TGridCalculator.ConvertTGridToAudioTime(x.minTGrid, bpmList).TotalMilliseconds;
                    var maxMsec = (float)TGridCalculator.ConvertTGridToAudioTime(x.maxTGrid, bpmList).TotalMilliseconds;
                    list.Add(new VisibleMsecRange(minMsec, x.minTGrid, maxMsec, x.maxTGrid));
                }
            }

            // 移除已不存在的 soflan group (谱面重新加载后 soflanListMap 可能变化), 复用而非整 map Clear.
            if (visibleRangeListMap.Count != soflanListMap.Count)
            {
                var stale = new List<int>();
                foreach (var key in visibleRangeListMap.Keys)
                    if (!soflanListMap.ContainsKey(key))
                        stale.Add(key);
                foreach (var key in stale)
                    visibleRangeListMap.Remove(key);
            }
        }

        public float ConvertAudioTimeToY_PreviewMode(float msec, int soflanGroup)
        {
            return (float)TGridCalculator.ConvertAudioTimeToY_PreviewMode(TimeSpan.FromMilliseconds(msec), getSoflanList(soflanGroup), bpmList, 1);
        }

        // 调试面板用: soflan 组号 + 当前变速倍率 (值类型, 零堆分配).
        public struct GroupSpeed
        {
            public readonly int Group;
            public readonly double Speed;
            public GroupSpeed(int group, double speed) { Group = group; Speed = speed; }
        }

        // 返回指定 soflan 组在指定音频时间(msec)的当前变速倍率。无该组或无 soflan 时返回 1.0。
        // 面板每帧调用; 仅 TimeSpan 栈分配 + 同源计算, 无堆分配。
        public double GetCurrentSpeed(int soflanGroup, float audioMsec)
        {
            if (!containSoflans) return 1.0;
            if (!soflanListMap.ContainsKey(soflanGroup)) return 1.0;
            var tGrid = TGridCalculator.ConvertAudioTimeToTGrid(
                TimeSpan.FromMilliseconds(audioMsec), bpmList);
            return getSoflanList(soflanGroup).CalculateSpeed(bpmList, tGrid);
        }

        // 把所有 soflan 组的 (group, currentSpeed) 写入调用方复用的 outList (Clear 后追加), 零 List 分配。
        public void FillCurrentSpeeds(float audioMsec, List<GroupSpeed> outList)
        {
            outList.Clear();
            if (!containSoflans) return;
            var tGrid = TGridCalculator.ConvertAudioTimeToTGrid(
                TimeSpan.FromMilliseconds(audioMsec), bpmList);
            foreach (KeyValuePair<int, SoflanList> pair in soflanListMap)
                outList.Add(new GroupSpeed(pair.Key, pair.Value.CalculateSpeed(bpmList, tGrid)));
        }

        public void DumpCurrent(int currentTime = -1)
        {
            PatchLog.WriteLine($"-------DUMP SOFLAN TIMING POINTS-------");
            foreach (KeyValuePair<int, SoflanList> pair in soflanListMap)
            {
                var soflanGroup = pair.Key;
                var soflanList = pair.Value;

                PatchLog.WriteLine($"");
                PatchLog.WriteLine($"SoflanGroup: {soflanGroup}");
                foreach (var timingPoint in soflanList.GetCachedSoflanPositionList_PreviewMode(bpmList))
                    PatchLog.WriteLine($"\t\t * AudioTime:{TGridCalculator.ConvertTGridToAudioTime(timingPoint.TGrid, bpmList).TotalMilliseconds}ms {timingPoint}");
            }
            PatchLog.WriteLine($"---------------------------------------");

            PatchLog.WriteLine($"containSoflans: {containSoflans}");
            PatchLog.WriteLine($"cachedCalculatedCurrentMsec: {cachedCalculatedCurrentMsec}");
            PatchLog.WriteLine($"cachedVisibleRangeListMap:");
            foreach (KeyValuePair<int, List<VisibleMsecRange>> pair in visibleRangeListMap)
            {
                PatchLog.WriteLine($"[{pair.Key}]:");
                foreach (var visibleRange in pair.Value)
                    PatchLog.WriteLine($"\t\t{visibleRange.MinTGrid}({visibleRange.MinMSec}ms) ~ {visibleRange.MaxTGrid}({visibleRange.MaxMSec}ms), current:{ConvertAudioTimeToY_PreviewMode(cachedCalculatedCurrentMsec, pair.Key)}");
            }
        }
    }
}
