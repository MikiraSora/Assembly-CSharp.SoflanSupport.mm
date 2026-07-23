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
using System.Globalization;
using System.IO;

namespace SoflanSupport
{
    public class SoflanManager
    {
        private SoflanListMap soflanListMap = new();
        private BpmList bpmList = new BpmList();
        private bool containSoflans = false;
        private Dictionary<int, int> registerNoteIndexToSoflanGroupMap = new();
        private Dictionary<int, TGrid> registerNoteIndexToSoflanTGridMap = new();
        private Dictionary<int, TGrid> registerNoteIndexToSoflanEndTGridMap = new();

        private float cachedCalculatedCurrentMsec = float.MinValue;
        private float cachedCalculatedApperMsec = float.MinValue;
        private int visibleRangeCacheVersion = 0;
        private Dictionary<int, VisibleMsecRangeCache> visibleRangeListMap = new();
        private float cachedCurrentSoflanTimeMsec = float.MinValue;
        private Dictionary<int, float> cachedCurrentSoflanTimeMap = new();

        /// <summary>
        /// clear all
        /// </summary>
        public void clearAll()
        {
            soflanListMap = new();
            bpmList = new();
            containSoflans = false;

            cachedCalculatedCurrentMsec = float.MinValue;
            cachedCalculatedApperMsec = float.MinValue;
            visibleRangeCacheVersion = 0;
            visibleRangeListMap.Clear();
            clearCurrentSoflanTimeCache();

            registerNoteIndexToSoflanGroupMap.Clear();
            registerNoteIndexToSoflanTGridMap.Clear();
            registerNoteIndexToSoflanEndTGridMap.Clear();

            PatchLog.WriteLine("SoflanManager cleared");
        }

        public void loadNote(NoteData noteData, MA2Record record, NotesReader sr)
        {
            if (noteData == null)
                return;

            var fixedNoteData = (patch_NoteData)noteData;
            fixedNoteData.isFixedSoflanToUnifiedSpeed = false;
            fixedNoteData.fixedSoflanUnifiedSpeed = FixedSoflan.DefaultUnifiedSpeed;

            if (TryReadRecordTGrid(record, out var noteTGrid) || TryReadNotesTimeTGrid(noteData.time, sr, out noteTGrid))
                registerNoteIndexToSoflanTGridMap[noteData.indexNote] = noteTGrid;
            if (HasMeaningfulEndTime(noteData) && TryReadNotesTimeTGrid(noteData.end, sr, out var noteEndTGrid))
                registerNoteIndexToSoflanEndTGridMap[noteData.indexNote] = noteEndTGrid;

            SoflanMarkerParseResult marker;
            string markerReason;
            if (!SoflanMarkerParser.TryParse(record?._str, out marker, out markerReason))
                FailSoflanMarker(noteData, marker.Marker, markerReason);

            if (!marker.HasMarker)
                return;

            var soflanGroup = marker.Group;
            var isFixedSoflan = marker.IsFixedSoflan;
            var fixedSoflanUnifiedSpeed = marker.HasFixedSpeed
                ? marker.FixedSpeed
                : FixedSoflan.DefaultUnifiedSpeed;

            registerNoteIndexToSoflanGroupMap[noteData.indexNote] = soflanGroup;
            fixedNoteData.isFixedSoflanToUnifiedSpeed = isFixedSoflan;
            fixedNoteData.fixedSoflanUnifiedSpeed = fixedSoflanUnifiedSpeed;

            PatchLog.WriteLine(
                $"register noteIndex:{noteData.indexNote}, marker:{marker.Marker}, soflanGroup:{soflanGroup}, fixedSoflan:{isFixedSoflan}, fixedSoflanSpeed:{fixedSoflanUnifiedSpeed.ToString(CultureInfo.InvariantCulture)}");
        }

        private static bool TryReadRecordTGrid(MA2Record record, out TGrid tGrid)
        {
            tGrid = default;
            if (record?._str == null || record._str.Count < 3)
                return false;

            if (!int.TryParse(record._str[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var grid))
                return false;

            if (!int.TryParse(record._str[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var unit))
                return false;

            tGrid = new TGrid(grid, unit);
            return true;
        }

        private static bool TryReadNotesTimeTGrid(NotesTime notesTime, NotesReader sr, out TGrid tGrid)
        {
            tGrid = default;
            if (sr == null)
                return false;

            try
            {
                tGrid = notesTime.ToTGrid(sr);
                return tGrid != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasMeaningfulEndTime(NoteData noteData)
        {
            return noteData.end.grid != 0 || noteData.end.msec != 0f;
        }

        private static void FailSoflanMarker(NoteData noteData, string marker, string reason)
        {
            var message = $"register noteIndex:{noteData.indexNote} failed, marker:{marker}, reason:{reason}";
            PatchLog.Error(message);
            throw new FormatException(message);
        }

        public void loadComposition(MA2RecordList records, NotesReader sr)
        {
            var filePath = sr.GetHeader()._notesName;
            if (!File.Exists(filePath))
            {
                //log error
                return;
            }

            foreach (var line in File.ReadLines(filePath))
            {
                if (line.StartsWith("SFL", StringComparison.InvariantCultureIgnoreCase))
                {
                    if (!tryParseSoflan(line, out var soflan))
                    {
                        PatchLog.Error($"parse soflan failed, line content:{line}");
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

        private bool tryParseSoflan(string line, out ISoflan soflan)
        {
            try
            {
                soflan = new Soflan()
                {
                    TGrid = new TGrid(int.Parse(GetTabField(line, 1)), int.Parse(GetTabField(line, 2))),
                    Speed = float.Parse(GetTabField(line, 4)),
                    SoflanGroup = 0
                };
                soflan.EndTGrid = soflan.TGrid + new GridOffset(0, int.Parse(GetTabField(line, 3)));
                var soflanGroup = GetTabField(line, 5);
                if (!string.IsNullOrWhiteSpace(soflanGroup))
                    soflan.SoflanGroup = int.Parse(soflanGroup);
                return true;
            }
            catch
            {
                //todo log ex
                soflan = default;
                return false;
            }
        }

        private static string GetTabField(string line, int fieldIndex)
        {
            var start = 0;
            var currentIndex = 0;
            for (var i = 0; i <= line.Length; i++)
            {
                if (i < line.Length && line[i] != '\t')
                    continue;

                if (currentIndex == fieldIndex)
                    return line.Substring(start, i - start).Trim();

                start = i + 1;
                currentIndex++;
            }

            return null;
        }

        public bool containsSoflans()
        {
            return containSoflans;
        }

        public static bool IsSupportedVisualSoflanKind(NotesTypeID.Def noteKind)
        {
            switch (noteKind)
            {
                case NotesTypeID.Def.Begin:
                case NotesTypeID.Def.Break:
                case NotesTypeID.Def.ExTap:
                case NotesTypeID.Def.Star:
                case NotesTypeID.Def.BreakStar:
                case NotesTypeID.Def.ExStar:
                case NotesTypeID.Def.TouchTap:
                case NotesTypeID.Def.ExBreakTap:
                case NotesTypeID.Def.ExBreakStar:
                case NotesTypeID.Def.Hold:
                case NotesTypeID.Def.ExHold:
                case NotesTypeID.Def.BreakHold:
                case NotesTypeID.Def.ExBreakHold:
                    return true;
                default:
                    return false;
            }
        }

        public SoflanList getSoflanList(int soflanGroup)
        {
            return soflanListMap[soflanGroup];
        }

        //-------------------------------------------

        private sealed class VisibleMsecRangeCache
        {
            public int Version;
            public readonly List<SoflanList.VisibleMsecRange> Ranges = new List<SoflanList.VisibleMsecRange>();
            public readonly SoflanList.VisibleRangeQueryScratch VisibleRangeScratch = new SoflanList.VisibleRangeQueryScratch();
        }

        public bool checkNoteVisible(NoteData noteData, float currentMsec, float apperMsec)
        {
            var soflanGroup = getNoteSoflanGroup(noteData);
            var currentSoflanTime = GetCurrentSoflanTimeCached(currentMsec, soflanGroup);
            return checkNoteVisible(noteData, currentMsec, apperMsec, soflanGroup, currentSoflanTime);
        }

        public bool checkNoteVisible(NoteData noteData, float currentMsec, float apperMsec, int soflanGroup, float currentSoflanTime)
        {
            BeginVisibleRangeFrame(currentMsec, apperMsec);

            var visibleRangeList = GetVisibleRangeList(soflanGroup, currentSoflanTime, apperMsec);
            if (visibleRangeList == null)
                return false;

            // foreach 替代 LINQ Any, 避免每帧闭包/委托/迭代器分配 (热路径零分配).
            var msec = getNoteAudioMsecForSoflan(noteData);
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

        public float getNoteAudioMsecForSoflan(NoteData noteData)
        {
            return noteData == null ? 0f : getNoteAudioMsecForSoflan(noteData.indexNote, noteData.time.msec);
        }

        public float getNoteAudioMsecForSoflan(int noteIndex, float fallbackMsec)
        {
            if (!registerNoteIndexToSoflanTGridMap.TryGetValue(noteIndex, out var tGrid))
                return fallbackMsec;

            try
            {
                return (float)TGridCalculator.ConvertTGridToAudioTime(tGrid, bpmList).TotalMilliseconds;
            }
            catch
            {
                return fallbackMsec;
            }
        }

        public float getNoteEndAudioMsecForSoflan(NoteData noteData)
        {
            return noteData == null ? 0f : getNoteEndAudioMsecForSoflan(noteData.indexNote, noteData.end.msec);
        }

        public float getNoteEndAudioMsecForSoflan(int noteIndex, float fallbackMsec)
        {
            if (!registerNoteIndexToSoflanEndTGridMap.TryGetValue(noteIndex, out var tGrid))
                return fallbackMsec;

            try
            {
                return (float)TGridCalculator.ConvertTGridToAudioTime(tGrid, bpmList).TotalMilliseconds;
            }
            catch
            {
                return fallbackMsec;
            }
        }

        private void BeginVisibleRangeFrame(float currentMsec, float apperMsec)
        {
            if (cachedCalculatedCurrentMsec == currentMsec && cachedCalculatedApperMsec == apperMsec)
                return;

            cachedCalculatedCurrentMsec = currentMsec;
            cachedCalculatedApperMsec = apperMsec;

            if (visibleRangeCacheVersion == int.MaxValue)
            {
                visibleRangeListMap.Clear();
                visibleRangeCacheVersion = 1;
            }
            else
            {
                visibleRangeCacheVersion++;
            }
        }

        private List<SoflanList.VisibleMsecRange> GetVisibleRangeList(int soflanGroup, float currentSoflanTime, float apperMsec)
        {
            if (!visibleRangeListMap.TryGetValue(soflanGroup, out var cache))
            {
                cache = new VisibleMsecRangeCache();
                visibleRangeListMap[soflanGroup] = cache;
            }

            if (cache.Version == visibleRangeCacheVersion)
                return cache.Ranges;

            cache.Ranges.Clear();

            // Lazy per-group rebuild: only groups touched by notes in this frame are recalculated.
            var soflanList = getSoflanList(soflanGroup);
            soflanList.FillVisibleMsecRangesForGamePreview(currentSoflanTime, apperMsec, bpmList, cache.Ranges, cache.VisibleRangeScratch);

            cache.Version = visibleRangeCacheVersion;
            return cache.Ranges;
        }

        public float ConvertAudioTimeToY_PreviewMode(float msec, int soflanGroup)
        {
            return (float)TGridCalculator.ConvertAudioTimeToY_PreviewMode(TimeSpan.FromMilliseconds(msec), getSoflanList(soflanGroup), bpmList, 1);
        }

        public void clearCurrentSoflanTimeCache()
        {
            cachedCurrentSoflanTimeMsec = float.MinValue;
            cachedCurrentSoflanTimeMap.Clear();
        }

        public float GetCurrentSoflanTimeCached(float currentMsec, int soflanGroup)
        {
            if (cachedCurrentSoflanTimeMsec != currentMsec)
            {
                cachedCurrentSoflanTimeMsec = currentMsec;
                cachedCurrentSoflanTimeMap.Clear();
            }

            if (!cachedCurrentSoflanTimeMap.TryGetValue(soflanGroup, out var soflanTime))
            {
                soflanTime = ConvertAudioTimeToY_PreviewMode(currentMsec, soflanGroup);
                cachedCurrentSoflanTimeMap[soflanGroup] = soflanTime;
            }

            return soflanTime;
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
        public void FillCurrentSpeeds(float audioMsec, List<GroupSpeed> outList, int maxCount = int.MaxValue)
        {
            outList.Clear();
            if (!containSoflans) return;
            var tGrid = TGridCalculator.ConvertAudioTimeToTGrid(
                TimeSpan.FromMilliseconds(audioMsec), bpmList);
            foreach (KeyValuePair<int, SoflanList> pair in soflanListMap)
            {
                if (outList.Count >= maxCount)
                    break;
                outList.Add(new GroupSpeed(pair.Key, pair.Value.CalculateSpeed(bpmList, tGrid)));
            }
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
            foreach (KeyValuePair<int, VisibleMsecRangeCache> pair in visibleRangeListMap)
            {
                PatchLog.WriteLine($"[{pair.Key}]:");
                foreach (var visibleRange in pair.Value.Ranges)
                    PatchLog.WriteLine($"\t\t{visibleRange.MinMsec}ms ~ {visibleRange.MaxMsec}ms, current:{ConvertAudioTimeToY_PreviewMode(cachedCalculatedCurrentMsec, pair.Key)}");
            }
        }
    }
}
