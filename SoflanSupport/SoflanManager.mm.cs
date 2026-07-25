// SoflanSupport.SoflanManager — 新增类型，管理按 player/monitor 隔离的 Soflan 运行时状态。
// SimpleSoflanFramework.Core 源码由 Shared Project 内嵌进 .mm.dll，运行时无需外部 Core DLL。
// MA2/BPM/SFL 使用原始谱面时间轴；运行时当前时间统一通过 t - GetAdjustMSec + visualOffset 转换。
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
        private readonly Dictionary<int, PlayerSoflanState> playerStateMap = new();

        private sealed class PlayerSoflanState
        {
            public readonly int PlayerId;
            public SoflanListMap SoflanListMap = new();
            public BpmList BpmList = new BpmList();
            public bool ContainSoflans;
            public float RuntimeChartOffsetMsec;
            public readonly Dictionary<int, int> NoteIndexToSoflanGroupMap = new();
            public readonly Dictionary<int, TGrid> NoteIndexToSoflanTGridMap = new();
            public readonly Dictionary<int, TGrid> NoteIndexToSoflanEndTGridMap = new();

            public float CachedCalculatedCurrentMsec = float.MinValue;
            public float CachedCalculatedApperMsec = float.MinValue;
            public int VisibleRangeCacheVersion;
            public readonly Dictionary<int, VisibleMsecRangeCache> VisibleRangeListMap = new();
            public float CachedCurrentSoflanTimeMsec = float.MinValue;
            public readonly Dictionary<CurrentSoflanTimeCacheKey, float> CachedCurrentSoflanTimeMap = new();

            public PlayerSoflanState(int playerId)
            {
                PlayerId = playerId;
            }

            public void ResetComposition(float runtimeChartOffsetMsec)
            {
                SoflanListMap = new SoflanListMap();
                BpmList = new BpmList();
                ContainSoflans = false;
                RuntimeChartOffsetMsec = SoflanRuntimeTime.NormalizeRuntimeChartOffsetMsec(
                    runtimeChartOffsetMsec);
                ResetCaches();
            }

            public void ResetCaches()
            {
                CachedCalculatedCurrentMsec = float.MinValue;
                CachedCalculatedApperMsec = float.MinValue;
                VisibleRangeCacheVersion = 0;
                VisibleRangeListMap.Clear();
                CachedCurrentSoflanTimeMsec = float.MinValue;
                CachedCurrentSoflanTimeMap.Clear();
            }
        }

        private PlayerSoflanState GetOrCreatePlayerState(int playerId)
        {
            if (!playerStateMap.TryGetValue(playerId, out var state))
            {
                state = new PlayerSoflanState(playerId);
                playerStateMap[playerId] = state;
            }

            return state;
        }

        private bool TryGetPlayerState(int playerId, out PlayerSoflanState state)
        {
            return playerStateMap.TryGetValue(playerId, out state);
        }

        /// <summary>
        /// clear all
        /// </summary>
        public void clearAll()
        {
            playerStateMap.Clear();

            PatchLog.WriteLine("SoflanManager cleared");
        }

        public void clearPlayer(int playerId)
        {
            playerStateMap.Remove(playerId);
            PatchLog.WriteLine($"SoflanManager player {playerId} cleared");
        }

        public void loadNote(NoteData noteData, MA2Record record, NotesReader sr, int playerId)
        {
            if (noteData == null)
                return;

            var state = GetOrCreatePlayerState(playerId);

            var fixedNoteData = (patch_NoteData)noteData;
            fixedNoteData.isFixedSoflanToUnifiedSpeed = false;
            fixedNoteData.fixedSoflanUnifiedSpeed = FixedSoflan.DefaultUnifiedSpeed;

            if (TryReadRecordTGrid(record, out var noteTGrid) || TryReadNotesTimeTGrid(noteData.time, sr, out noteTGrid))
                state.NoteIndexToSoflanTGridMap[noteData.indexNote] = noteTGrid;
            if (HasMeaningfulEndTime(noteData) && TryReadNotesTimeTGrid(noteData.end, sr, out var noteEndTGrid))
                state.NoteIndexToSoflanEndTGridMap[noteData.indexNote] = noteEndTGrid;

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

            state.NoteIndexToSoflanGroupMap[noteData.indexNote] = soflanGroup;
            fixedNoteData.isFixedSoflanToUnifiedSpeed = isFixedSoflan;
            fixedNoteData.fixedSoflanUnifiedSpeed = fixedSoflanUnifiedSpeed;

            PatchLog.WriteLine(
                $"register player:{playerId}, noteIndex:{noteData.indexNote}, marker:{marker.Marker}, soflanGroup:{soflanGroup}, fixedSoflan:{isFixedSoflan}, fixedSoflanSpeed:{fixedSoflanUnifiedSpeed.ToString(CultureInfo.InvariantCulture)}");
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

        public void loadComposition(
            MA2RecordList records,
            NotesReader sr,
            int playerId,
            float runtimeChartOffsetMsec)
        {
            var state = GetOrCreatePlayerState(playerId);
            state.ResetComposition(runtimeChartOffsetMsec);

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
                    state.SoflanListMap.Add(soflan);
                    state.ContainSoflans = true;
                    PatchLog.WriteLine($"parse soflan: {soflan}");
                }
            }


            foreach (var item in sr.GetCompositioin()._bpmList)
            {
                if (item.time.grid == 0)
                {
                    state.BpmList.FirstBpm = item.bpm;
                }
                else
                {
                    var bpmChange = new BPMChange
                    {
                        BPM = item.bpm,
                        TGrid = item.time.ToTGrid(sr)
                    };

                    state.BpmList.Add(bpmChange);
                }
            }

            PatchLog.WriteLine($"-------DUMP SOFLAN TIMING POINTS-------");
            PatchLog.WriteLine($"PlayerId: {playerId}");
            PatchLog.WriteLine($"RuntimeChartOffsetMsec: {state.RuntimeChartOffsetMsec.ToString(CultureInfo.InvariantCulture)}");
            PatchLog.WriteLine($"FilePath: {sr.GetHeader()._notesName}");
            foreach (KeyValuePair<int, SoflanList> pair in state.SoflanListMap)
            {
                var soflanGroup = pair.Key;
                var soflanList = pair.Value;

                PatchLog.WriteLine($"");
                PatchLog.WriteLine($"SoflanGroup: {soflanGroup}");
                foreach (var timingPoint in soflanList.GetCachedSoflanPositionList_PreviewMode(state.BpmList))
                    PatchLog.WriteLine($"\t\t * AudioTime:{TGridCalculator.ConvertTGridToAudioTime(timingPoint.TGrid, state.BpmList).TotalMilliseconds}ms {timingPoint}");
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

        public bool containsSoflans(int playerId)
        {
            return TryGetPlayerState(playerId, out var state) && state.ContainSoflans;
        }

        public float getRuntimeChartOffsetMsec(int playerId)
        {
            return TryGetPlayerState(playerId, out var state)
                ? state.RuntimeChartOffsetMsec
                : 0f;
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

        public SoflanList getSoflanList(int playerId, int soflanGroup)
        {
            return GetOrCreatePlayerState(playerId).SoflanListMap[soflanGroup];
        }

        //-------------------------------------------

        private sealed class VisibleMsecRangeCache
        {
            public int Version;
            public float CurrentSoflanTime = float.MinValue;
            public float ApperMsec = float.MinValue;
            public readonly List<SoflanList.VisibleMsecRange> Ranges = new List<SoflanList.VisibleMsecRange>();
            public readonly SoflanList.VisibleRangeQueryScratch VisibleRangeScratch = new SoflanList.VisibleRangeQueryScratch();
        }

        private struct CurrentSoflanTimeCacheKey : IEquatable<CurrentSoflanTimeCacheKey>
        {
            public readonly int PlayerId;
            public readonly int Group;
            public readonly float RuntimeChartOffsetMsec;
            public readonly float VisualAudioOffsetMsec;

            public CurrentSoflanTimeCacheKey(
                int playerId,
                int group,
                float runtimeChartOffsetMsec,
                float visualAudioOffsetMsec)
            {
                PlayerId = playerId;
                Group = group;
                RuntimeChartOffsetMsec = runtimeChartOffsetMsec;
                VisualAudioOffsetMsec = visualAudioOffsetMsec;
            }

            public bool Equals(CurrentSoflanTimeCacheKey other)
            {
                return PlayerId == other.PlayerId
                    && Group == other.Group
                    && RuntimeChartOffsetMsec.Equals(other.RuntimeChartOffsetMsec)
                    && VisualAudioOffsetMsec.Equals(other.VisualAudioOffsetMsec);
            }

            public override bool Equals(object obj)
            {
                return obj is CurrentSoflanTimeCacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = PlayerId;
                    hashCode = (hashCode * 397) ^ Group;
                    hashCode = (hashCode * 397) ^ RuntimeChartOffsetMsec.GetHashCode();
                    hashCode = (hashCode * 397) ^ VisualAudioOffsetMsec.GetHashCode();
                    return hashCode;
                }
            }
        }

        public bool checkNoteVisible(int playerId, NoteData noteData, float currentMsec, float apperMsec)
        {
            if (noteData == null)
                return false;

            var soflanGroup = getNoteSoflanGroup(playerId, noteData);
            var maiBugAdjustMsec = SoflanVisualTiming.GetMaiBugAdjustMsec(
                noteData.type.getEnum(),
                apperMsec);
            var currentSoflanTime = GetCurrentSoflanTimeWithOffsetsCached(
                playerId,
                currentMsec,
                maiBugAdjustMsec,
                soflanGroup);
            return checkNoteVisible(playerId, noteData, currentMsec, apperMsec, soflanGroup, currentSoflanTime);
        }

        public bool checkNoteVisible(
            int playerId,
            NoteData noteData,
            float currentMsec,
            float apperMsec,
            int soflanGroup,
            float currentSoflanTime)
        {
            if (!TryGetPlayerState(playerId, out var state))
                return false;

            BeginVisibleRangeFrame(state, currentMsec, apperMsec);

            var visibleRangeList = GetVisibleRangeList(state, soflanGroup, currentSoflanTime, apperMsec);
            if (visibleRangeList == null)
                return false;

            // foreach 替代 LINQ Any, 避免每帧闭包/委托/迭代器分配 (热路径零分配).
            var msec = getNoteAudioMsecForSoflan(playerId, noteData);
            foreach (var range in visibleRangeList)
            {
                if (range.Contain(msec))
                    return true;
            }
            return false;
        }

        public int getNoteSoflanGroup(int playerId, int noteIndex)
        {
            return TryGetPlayerState(playerId, out var state)
                && state.NoteIndexToSoflanGroupMap.TryGetValue(noteIndex, out var soflanGroup)
                ? soflanGroup
                : 0;
        }

        public int getNoteSoflanGroup(int playerId, NoteData noteData)
        {
            return noteData == null ? 0 : getNoteSoflanGroup(playerId, noteData.indexNote);
        }

        public float getNoteAudioMsecForSoflan(int playerId, NoteData noteData)
        {
            return noteData == null
                ? 0f
                : getNoteAudioMsecForSoflan(playerId, noteData.indexNote, noteData.time.msec);
        }

        public float getNoteAudioMsecForSoflan(int playerId, int noteIndex, float fallbackMsec)
        {
            if (!TryGetPlayerState(playerId, out var state))
                return fallbackMsec;
            if (!state.NoteIndexToSoflanTGridMap.TryGetValue(noteIndex, out var tGrid))
                return SoflanRuntimeTime.ToRawChartAudioMsec(
                    fallbackMsec,
                    state.RuntimeChartOffsetMsec,
                    0f);

            try
            {
                return (float)TGridCalculator.ConvertTGridToAudioTime(tGrid, state.BpmList).TotalMilliseconds;
            }
            catch
            {
                return SoflanRuntimeTime.ToRawChartAudioMsec(
                    fallbackMsec,
                    state.RuntimeChartOffsetMsec,
                    0f);
            }
        }

        public float getNoteEndAudioMsecForSoflan(int playerId, NoteData noteData)
        {
            return noteData == null
                ? 0f
                : getNoteEndAudioMsecForSoflan(playerId, noteData.indexNote, noteData.end.msec);
        }

        public float getNoteEndAudioMsecForSoflan(int playerId, int noteIndex, float fallbackMsec)
        {
            if (!TryGetPlayerState(playerId, out var state))
                return fallbackMsec;
            if (!state.NoteIndexToSoflanEndTGridMap.TryGetValue(noteIndex, out var tGrid))
                return SoflanRuntimeTime.ToRawChartAudioMsec(
                    fallbackMsec,
                    state.RuntimeChartOffsetMsec,
                    0f);

            try
            {
                return (float)TGridCalculator.ConvertTGridToAudioTime(tGrid, state.BpmList).TotalMilliseconds;
            }
            catch
            {
                return SoflanRuntimeTime.ToRawChartAudioMsec(
                    fallbackMsec,
                    state.RuntimeChartOffsetMsec,
                    0f);
            }
        }

        private static void BeginVisibleRangeFrame(
            PlayerSoflanState state,
            float currentMsec,
            float apperMsec)
        {
            if (state.CachedCalculatedCurrentMsec == currentMsec
                && state.CachedCalculatedApperMsec == apperMsec)
                return;

            state.CachedCalculatedCurrentMsec = currentMsec;
            state.CachedCalculatedApperMsec = apperMsec;

            if (state.VisibleRangeCacheVersion == int.MaxValue)
            {
                state.VisibleRangeListMap.Clear();
                state.VisibleRangeCacheVersion = 1;
            }
            else
            {
                state.VisibleRangeCacheVersion++;
            }
        }

        private List<SoflanList.VisibleMsecRange> GetVisibleRangeList(
            PlayerSoflanState state,
            int soflanGroup,
            float currentSoflanTime,
            float apperMsec)
        {
            if (!state.VisibleRangeListMap.TryGetValue(soflanGroup, out var cache))
            {
                cache = new VisibleMsecRangeCache();
                state.VisibleRangeListMap[soflanGroup] = cache;
            }

            if (cache.Version == state.VisibleRangeCacheVersion
                && cache.CurrentSoflanTime == currentSoflanTime
                && cache.ApperMsec == apperMsec)
                return cache.Ranges;

            cache.Ranges.Clear();

            // Lazy per-group rebuild: only groups touched by notes in this frame are recalculated.
            var soflanList = state.SoflanListMap[soflanGroup];
            soflanList.FillVisibleMsecRangesForGamePreview(
                currentSoflanTime,
                apperMsec,
                state.BpmList,
                cache.Ranges,
                cache.VisibleRangeScratch);

            cache.Version = state.VisibleRangeCacheVersion;
            cache.CurrentSoflanTime = currentSoflanTime;
            cache.ApperMsec = apperMsec;
            return cache.Ranges;
        }

        public float ConvertAudioTimeToY_PreviewMode(int playerId, float msec, int soflanGroup)
        {
            var state = GetOrCreatePlayerState(playerId);
            return (float)TGridCalculator.ConvertAudioTimeToY_PreviewMode(
                TimeSpan.FromMilliseconds(msec),
                state.SoflanListMap[soflanGroup],
                state.BpmList,
                1);
        }

        public void clearCurrentSoflanTimeCache()
        {
            foreach (var state in playerStateMap.Values)
            {
                state.CachedCurrentSoflanTimeMsec = float.MinValue;
                state.CachedCurrentSoflanTimeMap.Clear();
            }
        }

        public void clearCurrentSoflanTimeCache(int playerId)
        {
            if (!TryGetPlayerState(playerId, out var state))
                return;

            state.CachedCurrentSoflanTimeMsec = float.MinValue;
            state.CachedCurrentSoflanTimeMap.Clear();
        }

        public float GetCurrentSoflanTimeCached(int playerId, float currentMsec, int soflanGroup)
        {
            return GetCurrentSoflanTimeWithOffsetsCached(playerId, currentMsec, 0f, soflanGroup);
        }

        public float GetCurrentSoflanTimeWithOffsetsCached(
            int playerId,
            float runtimeCurrentMsec,
            float visualAudioOffsetMsec,
            int soflanGroup)
        {
            var state = GetOrCreatePlayerState(playerId);
            if (state.CachedCurrentSoflanTimeMsec != runtimeCurrentMsec)
            {
                state.CachedCurrentSoflanTimeMsec = runtimeCurrentMsec;
                state.CachedCurrentSoflanTimeMap.Clear();
            }

            var key = new CurrentSoflanTimeCacheKey(
                playerId,
                soflanGroup,
                state.RuntimeChartOffsetMsec,
                visualAudioOffsetMsec);
            if (!state.CachedCurrentSoflanTimeMap.TryGetValue(key, out var soflanTime))
            {
                var rawChartAudioMsec = SoflanRuntimeTime.ToRawChartAudioMsec(
                    runtimeCurrentMsec,
                    state.RuntimeChartOffsetMsec,
                    visualAudioOffsetMsec);
                soflanTime = ConvertAudioTimeToY_PreviewMode(
                    playerId,
                    rawChartAudioMsec,
                    soflanGroup);
                state.CachedCurrentSoflanTimeMap[key] = soflanTime;
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
        public double GetCurrentSpeed(int playerId, int soflanGroup, float runtimeAudioMsec)
        {
            if (!TryGetPlayerState(playerId, out var state) || !state.ContainSoflans)
                return 1.0;
            if (!state.SoflanListMap.ContainsKey(soflanGroup))
                return 1.0;
            var rawChartAudioMsec = SoflanRuntimeTime.ToRawChartAudioMsec(
                runtimeAudioMsec,
                state.RuntimeChartOffsetMsec,
                0f);
            var tGrid = TGridCalculator.ConvertAudioTimeToTGrid(
                TimeSpan.FromMilliseconds(rawChartAudioMsec), state.BpmList);
            return state.SoflanListMap[soflanGroup].CalculateSpeed(state.BpmList, tGrid);
        }

        // 把所有 soflan 组的 (group, currentSpeed) 写入调用方复用的 outList (Clear 后追加), 零 List 分配。
        public void FillCurrentSpeeds(
            int playerId,
            float runtimeAudioMsec,
            List<GroupSpeed> outList,
            int maxCount = int.MaxValue)
        {
            outList.Clear();
            if (!TryGetPlayerState(playerId, out var state) || !state.ContainSoflans)
                return;
            var rawChartAudioMsec = SoflanRuntimeTime.ToRawChartAudioMsec(
                runtimeAudioMsec,
                state.RuntimeChartOffsetMsec,
                0f);
            var tGrid = TGridCalculator.ConvertAudioTimeToTGrid(
                TimeSpan.FromMilliseconds(rawChartAudioMsec), state.BpmList);
            foreach (KeyValuePair<int, SoflanList> pair in state.SoflanListMap)
            {
                if (outList.Count >= maxCount)
                    break;
                outList.Add(new GroupSpeed(pair.Key, pair.Value.CalculateSpeed(state.BpmList, tGrid)));
            }
        }

        public void DumpCurrent(int playerId, int currentTime = -1)
        {
            if (!TryGetPlayerState(playerId, out var state))
                return;

            PatchLog.WriteLine($"-------DUMP SOFLAN TIMING POINTS-------");
            PatchLog.WriteLine($"PlayerId: {playerId}");
            PatchLog.WriteLine($"RuntimeChartOffsetMsec: {state.RuntimeChartOffsetMsec}");
            foreach (KeyValuePair<int, SoflanList> pair in state.SoflanListMap)
            {
                var soflanGroup = pair.Key;
                var soflanList = pair.Value;

                PatchLog.WriteLine($"");
                PatchLog.WriteLine($"SoflanGroup: {soflanGroup}");
                foreach (var timingPoint in soflanList.GetCachedSoflanPositionList_PreviewMode(state.BpmList))
                    PatchLog.WriteLine($"\t\t * AudioTime:{TGridCalculator.ConvertTGridToAudioTime(timingPoint.TGrid, state.BpmList).TotalMilliseconds}ms {timingPoint}");
            }
            PatchLog.WriteLine($"---------------------------------------");

            PatchLog.WriteLine($"containSoflans: {state.ContainSoflans}");
            PatchLog.WriteLine($"cachedCalculatedCurrentMsec: {state.CachedCalculatedCurrentMsec}");
            PatchLog.WriteLine($"cachedVisibleRangeListMap:");
            foreach (KeyValuePair<int, VisibleMsecRangeCache> pair in state.VisibleRangeListMap)
            {
                PatchLog.WriteLine($"[{pair.Key}]:");
                foreach (var visibleRange in pair.Value.Ranges)
                {
                    var rawCurrentMsec = SoflanRuntimeTime.ToRawChartAudioMsec(
                        state.CachedCalculatedCurrentMsec,
                        state.RuntimeChartOffsetMsec,
                        0f);
                    PatchLog.WriteLine(
                        $"\t\t{visibleRange.MinMsec}ms ~ {visibleRange.MaxMsec}ms, current:{ConvertAudioTimeToY_PreviewMode(playerId, rawCurrentMsec, pair.Key)}");
                }
            }
        }
    }
}
