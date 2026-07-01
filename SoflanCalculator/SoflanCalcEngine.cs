// SoflanCalcEngine — soflan 计算引擎.
// 复用 SimpleSoflanFramework.Core.dll 中的 TGridCalculator / BpmList / SoflanListMap,
// 与游戏运行时 (SoflanManager + patch_NoteBase.GetNoteYPosition_soflan) 完全一致的计算逻辑.
using OngekiFumenEditor.Core.Base;
using OngekiFumenEditor.Core.Base.Collections;
using OngekiFumenEditor.Core.Base.OngekiObjects;
using OngekiFumenEditor.Core.Modules.FumenVisualEditor;
using OngekiFumenEditor.Core.Utils;
using System;

namespace SoflanCalculator
{
    /// <summary>
    /// 与 NoteBase.NoteStatus 一致的枚举.
    /// </summary>
    public enum NoteStat
    {
        Init,
        Scale,
        Move,
        Check,
        End
    }

    /// <summary>
    /// 一次 soflan 计算的全部输出数据.
    /// </summary>
    public class CalcResult
    {
        // --- Parameters ---
        public float NoteSpeedValue;
        public float SpeedRatio;
        public float DefaultMsec;
        public float MaiBugAdjustMSec;
        public float StartPos;
        public float EndPos;

        // --- Note Timing ---
        public float AppearMsec;
        public float NoteSoflanTime;
        public float CurrentMsec;
        public float CurrentSoflanTime;
        public double CurrentSoflanSpeed;

        // --- Computed Values ---
        public float DiffTime;
        public float AbsDiffTime;
        public float ScaleStartTime;
        public float MoveStartTime;
        public NoteStat NoteStat;
        public float MoveProgress;
        public float FinalScale;
        public float InsideY;
        public float OutsideY;
        public float SoflanY;
        public float ClipedSoflanY;

        // --- Note Info ---
        public int LineNumber;
        public string NoteType;
        public int Bar;
        public int Grid;
        public int Pos;
        public int SoflanGroup;
        public bool ContainsSoflans;
    }

    public static class SoflanCalcEngine
    {
        /// <summary>
        /// 执行完整的 soflan 计算.
        /// </summary>
        /// <param name="data">解析后的 ma2 数据</param>
        /// <param name="note">目标 note 记录</param>
        /// <param name="currentMsec">当前播放时间 (msec)</param>
        /// <param name="noteSpeedValue">物件速度值 (对应 OptionNotespeedID.GetValue)</param>
        /// <param name="startPos">NoteStart Y 坐标 (从 Unity prefab 读取)</param>
        /// <param name="endPos">NoteEnd Y 坐标 (从 Unity prefab 读取)</param>
        public static CalcResult Calculate(
            Ma2Data data,
            NoteRecord note,
            float currentMsec,
            float noteSpeedValue,
            float startPos,
            float endPos)
        {
            // --- 构建 BpmList ---
            // 与 SoflanManager.loadComposition 一致:
            //   首个 BPM (grid==0) → FirstBpm; 其余 → Add(BPMChange)
            var bpmList = new BpmList();
            bpmList.FirstBpm = (double)data.FirstBpm;
            foreach (var bpm in data.BpmChanges)
            {
                // BPM 行的 bar/grid → TGrid (Unit=bar, Grid=within-bar grid)
                // 与 TGridHelper.ToTGrid 等价: absolute_grid = bar*res + grid; Unit = abs/res; Grid = abs%res
                var tGrid = new TGrid(bpm.Bar, bpm.Grid);
                bpmList.Add(new BPMChange
                {
                    BPM = (double)bpm.Bpm,
                    TGrid = tGrid
                });
            }

            // --- 构建 SoflanListMap ---
            // 与 SoflanManager.loadComposition 一致
            var soflanMap = new SoflanListMap();
            bool containSoflans = data.Soflans.Count > 0;
            foreach (var sfl in data.Soflans)
            {
                var soflan = new Soflan
                {
                    TGrid = new TGrid(sfl.Unit, sfl.Grid),
                    Speed = sfl.Speed,
                    SoflanGroup = sfl.SoflanGroup
                };
                soflan.EndTGrid = soflan.TGrid + new GridOffset(0, sfl.Length);
                soflanMap.Add(soflan);
            }

            // --- 速度参数推导 ---
            // 与 NoteBase.Initialize + GetMaiBugAdjustMSec 一致
            float speedRatio = noteSpeedValue / 150f;
            // DefaultMsec = GetNoteSpeedForBeat * 4 = (60000 / NoteSpeedValue) * 4 = 240000 / NoteSpeedValue
            float defaultMsec = 240000f / noteSpeedValue;
            // GetMaiBugAdjustMSec = (speedRatio - 1) * (-0.5f / speedRatio) * 1.6f * 1000f / 60f
            float maiBugAdjustMSec = (speedRatio - 1f) * (-0.5f / speedRatio) * 1.6f * 1000f / 60f;

            // --- AppearMsec 计算 ---
            // bar/grid → TGrid → TGridCalculator.ConvertTGridToAudioTime → msec
            var noteTGrid = new TGrid(note.Bar, note.Grid);
            var appearMsec = (float)TGridCalculator.ConvertTGridToAudioTime(noteTGrid, bpmList).TotalMilliseconds;

            // --- SoflanTime 计算 ---
            // ConvertAudioTimeToY_PreviewMode 对 AppearMsec 和 currentTime 分别求值.
            // 与游戏一致: 始终通过 SoflanListMap 索引器获取 SoflanList (缺失组自动创建空列表,
            // 空 SoflanList → speed=1.0 → soflanTime == msec, 与游戏行为完全一致).
            int soflanGroup = note.SoflanGroup;
            var soflanList = soflanMap[soflanGroup];
            float noteSoflanTime = (float)TGridCalculator.ConvertAudioTimeToY_PreviewMode(
                TimeSpan.FromMilliseconds(appearMsec), soflanList, bpmList, 1);
            float currentSoflanTime = (float)TGridCalculator.ConvertAudioTimeToY_PreviewMode(
                TimeSpan.FromMilliseconds(currentMsec), soflanList, bpmList, 1);

            // --- 当前变速速度 ---
            // 与 SoflanManager.GetCurrentSpeed 一致: currentTime → TGrid → SoflanList.CalculateSpeed.
            // 无 SFL 或无该组时, SoflanList 为空 → CalculateSpeed 返回 1.0.
            var currentTGrid = TGridCalculator.ConvertAudioTimeToTGrid(
                TimeSpan.FromMilliseconds(currentMsec), bpmList);
            double currentSoflanSpeed = soflanList.CalculateSpeed(bpmList, currentTGrid);

            // --- GetNoteYPosition_soflan 逻辑 ---
            // 与 patch_NoteBase.GetNoteYPosition_soflan 完全一致
            float diffTime = noteSoflanTime - currentSoflanTime;
            float absDiffTime = Math.Abs(diffTime);

            float scaleStartTime = 2f * defaultMsec - maiBugAdjustMSec;
            float moveStartTime = defaultMsec - maiBugAdjustMSec;

            NoteStat noteStat = NoteStat.Init;

            if (absDiffTime > scaleStartTime)
            {
                // 不修改 NoteStat (保持 Init, Guide 隐藏)
            }
            else if (absDiffTime > moveStartTime)
            {
                noteStat = NoteStat.Scale;
            }
            else
            {
                noteStat = NoteStat.Move;
            }

            // offsetYAdj (与游戏一致, 但 sign=0 故 adjustedSoflanY == soflanY)
            float offsetYAdj = (endPos - startPos) * (-1f / 120f) * (speedRatio - 1f);
            float guideScaleAdj = 0f;

            float moveProgress = MathUtils.MapValue(diffTime, 0, moveStartTime, 1, 0, false);
            moveProgress = Math.Max(0, moveProgress); // always >= 0

            float guideScale = 0.75f * moveProgress;
            float adjustedGuideScale = guideScale + guideScaleAdj;
            float finalScale = 0.25f + adjustedGuideScale;

            float insideY = startPos;
            float outsideY = endPos + (endPos - startPos);

            float soflanY = MathUtils.MapValue(diffTime, -moveStartTime, moveStartTime, outsideY, insideY);
            // sign = 0 (与游戏当前代码一致)
            float adjustedSoflanY = soflanY; // + sign * offsetYAdj  (sign=0)

            float clipedSoflanY = Math.Max(120f, Math.Min(680f, adjustedSoflanY));

            return new CalcResult
            {
                NoteSpeedValue = noteSpeedValue,
                SpeedRatio = speedRatio,
                DefaultMsec = defaultMsec,
                MaiBugAdjustMSec = maiBugAdjustMSec,
                StartPos = startPos,
                EndPos = endPos,
                AppearMsec = appearMsec,
                NoteSoflanTime = noteSoflanTime,
                CurrentMsec = currentMsec,
                CurrentSoflanTime = currentSoflanTime,
                CurrentSoflanSpeed = currentSoflanSpeed,
                DiffTime = diffTime,
                AbsDiffTime = absDiffTime,
                ScaleStartTime = scaleStartTime,
                MoveStartTime = moveStartTime,
                NoteStat = noteStat,
                MoveProgress = moveProgress,
                FinalScale = finalScale,
                InsideY = insideY,
                OutsideY = outsideY,
                SoflanY = soflanY,
                ClipedSoflanY = clipedSoflanY,
                LineNumber = note.LineNumber,
                NoteType = note.Type,
                Bar = note.Bar,
                Grid = note.Grid,
                Pos = note.Pos,
                SoflanGroup = soflanGroup,
                ContainsSoflans = containSoflans,
            };
        }
    }
}
