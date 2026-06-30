using System;
using System.Collections.Generic;
using OngekiFumenEditor.Core.Base;
using OngekiFumenEditor.Core.Base.Collections;
using OngekiFumenEditor.Core.Base.OngekiObjects;
using OngekiFumenEditor.Core.Modules.FumenVisualEditor;
using OngekiFumenEditor.Core.Utils;

namespace SoflanSimulator
{
    public class SimParams
    {
        public float DefaultMsec = 2000f;
        public float MaiBugAdjustMSec = 0f;
        public float StartPos = 120f;
        public float EndPos = 400f;
        public float NoteSpeed = 150f;
    }

    public class ComputeResult
    {
        public float CurrentMsec;
        public float DiffTime;
        public float AbsDiffTime;
        public float NoteSoflanTime;
        public float CurrentSoflanTime;
        public float ScaleStartTime;
        public float MoveStartTime;
        public float SoflanY;
        public float OffsetYAdj;
        public float GuideScale;
        public float FinalScale;
        public float ScreenY;
        public float SpeedRatio;
        public int NoteStat; // 0=hidden, 1=scale, 2=move (faithful: always 2)
    }

    // 预计算的 note 条目 (仅 active=true 的 note)
    public class NoteEntry
    {
        public int IndexNote;
        public string Type;
        public int Measure;
        public int Grid;
        public int Lane;
        public int SoflanGroup;
        public double AppearMsec;
        public float NoteSoflanTime;
    }

    public class Session
    {
        public string Id;
        public Ma2Data Data;
        public BpmList BpmList;
        public SoflanListMap SoflanListMap;
        public List<NoteEntry> ActiveNotes = new List<NoteEntry>();
        public double DurationMs;
    }

    public class Simulator
    {
        public static Session BuildSession(string id, Ma2Data data)
        {
            var session = new Session { Id = id, Data = data };

            // 构建 BpmList (与 SoflanManager.loadComposition 一致)
            session.BpmList = new BpmList();
            bool firstBpmSet = false;
            foreach (var rec in data.BpmRecords)
            {
                if (rec.Measure == 0 && rec.Grid == 0)
                {
                    session.BpmList.FirstBpm = rec.Bpm;
                    firstBpmSet = true;
                }
                else
                {
                    var bpmChange = new BPMChange
                    {
                        BPM = rec.Bpm,
                        TGrid = new TGrid(rec.Measure, rec.Grid)
                    };
                    session.BpmList.Add(bpmChange);
                }
            }
            if (!firstBpmSet)
                session.BpmList.FirstBpm = data.FirstBpm;

            // 构建 SoflanListMap (与 SoflanManager.loadComposition 一致)
            session.SoflanListMap = new SoflanListMap();
            foreach (var sfl in data.SflRecords)
            {
                var soflan = new Soflan
                {
                    TGrid = new TGrid(sfl.Unit, sfl.Grid),
                    Speed = sfl.Speed,
                    SoflanGroup = sfl.SoflanGroup
                };
                soflan.EndTGrid = soflan.TGrid + new GridOffset(0, sfl.GridLength);
                session.SoflanListMap.Add(soflan);
            }

            // 预计算每个 active note 的 AppearMsec 和 noteSoflanTime
            double maxAppearMsec = 0;
            foreach (var note in data.Notes)
            {
                if (!note.Active) continue;

                var tGrid = new TGrid(note.Measure, note.Grid);
                double appearMsec = TGridCalculator.ConvertTGridToAudioTime(tGrid, session.BpmList).TotalMilliseconds;
                float noteSoflanTime = (float)ConvertAudioTimeToY_PreviewMode(
                    session, (float)appearMsec, note.SoflanGroup);

                var entry = new NoteEntry
                {
                    IndexNote = note.IndexNote,
                    Type = note.Type,
                    Measure = note.Measure,
                    Grid = note.Grid,
                    Lane = note.Lane,
                    SoflanGroup = note.SoflanGroup,
                    AppearMsec = appearMsec,
                    NoteSoflanTime = noteSoflanTime
                };
                session.ActiveNotes.Add(entry);

                if (appearMsec > maxAppearMsec)
                    maxAppearMsec = appearMsec;
            }

            session.DurationMs = maxAppearMsec + 10000;
            return session;
        }

        // 忠实复现 GetNoteYPosition_soflan (Monitor.NoteBase.mm.cs:102-205)
        public static ComputeResult ComputeY(Session session, NoteEntry note, float currentMsec, SimParams p)
        {
            // BUG 复现: diffTime 和 absDiffTime 各自独立调用 GetSoflanTimeDiff()
            float diffTime = GetSoflanTimeDiff(session, note, currentMsec);
            float absDiffTime = Math.Abs(GetSoflanTimeDiff(session, note, currentMsec));

            float scaleStartTime = 2f * p.DefaultMsec - p.MaiBugAdjustMSec;
            float moveStartTime = p.DefaultMsec - p.MaiBugAdjustMSec;

            // guide alpha 三段逻辑 (复现, 不影响返回值但计算用于展示)
            int noteStat = 0;
            if (absDiffTime > scaleStartTime)
            {
                // hidden: alpha=0 (后被覆盖为 1)
                noteStat = 0;
            }
            else if (absDiffTime > moveStartTime)
            {
                // scale: alpha=scaleProgress (后被覆盖为 1)
                noteStat = 1;
            }
            else
            {
                // move: alpha=1
                noteStat = 2;
            }
            // BUG 复现: NoteStat 无条件覆盖为 Move
            noteStat = 2;

            // speedRatio (计算但 sign=0 导致 offsetYAdj 无效)
            float speedRatio = p.NoteSpeed / 150f;

            // offsetYAdj
            float offsetYAdj = (p.EndPos - p.StartPos) * (-1f / 120f) * (speedRatio - 1f);
            float guideScaleAdj = 0f; // (-1f/120f)*(speedRatio-1f)*0.75f; // 原代码注释掉, =0

            // guideScale
            float moveProgress = MapValue(diffTime, 0f, moveStartTime, 1f, 0f, false);
            moveProgress = Math.Max(0f, moveProgress);
            float guideScale = 0.75f * moveProgress;
            float adjustedGuideScale = guideScale + guideScaleAdj;
            float finalScale = 0.25f + adjustedGuideScale;

            // Y 位置
            float insideY = p.StartPos;
            float outsideY = p.EndPos + (p.EndPos - p.StartPos);
            float soflanY = MapValue(diffTime, -moveStartTime, moveStartTime, outsideY, insideY);
            int sign = 0; // 原代码: sign = 0 (todo 注释)
            float adjustedSoflanY = soflanY + sign * offsetYAdj;
            float screenY = Math.Max(120f, Math.Min(680f, adjustedSoflanY));

            // currentSoflanTime (用于展示; 与 GetSoflanTimeDiff 内部计算一致)
            float currentSoflanTime = (float)ConvertAudioTimeToY_PreviewMode(session, currentMsec, note.SoflanGroup);

            return new ComputeResult
            {
                CurrentMsec = currentMsec,
                DiffTime = diffTime,
                AbsDiffTime = absDiffTime,
                NoteSoflanTime = note.NoteSoflanTime,
                CurrentSoflanTime = currentSoflanTime,
                ScaleStartTime = scaleStartTime,
                MoveStartTime = moveStartTime,
                SoflanY = soflanY,
                OffsetYAdj = offsetYAdj,
                GuideScale = guideScale,
                FinalScale = finalScale,
                ScreenY = screenY,
                SpeedRatio = speedRatio,
                NoteStat = noteStat
            };
        }

        public static List<ComputeResult> ComputeCurve(Session session, NoteEntry note, SimParams p, float step)
        {
            var results = new List<ComputeResult>();
            float scaleStartTime = 2f * p.DefaultMsec - p.MaiBugAdjustMSec;
            float startT = Math.Max(0f, (float)note.AppearMsec - scaleStartTime);
            float endT = (float)note.AppearMsec + scaleStartTime;

            for (float t = startT; t <= endT; t += step)
            {
                results.Add(ComputeY(session, note, t, p));
            }
            return results;
        }

        public class ActiveNoteResult
        {
            public int NoteIndex;
            public ComputeResult Result;
        }

        public static List<ActiveNoteResult> ComputeAt(Session session, float time, SimParams p)
        {
            var results = new List<ActiveNoteResult>();
            float scaleStartTime = 2f * p.DefaultMsec - p.MaiBugAdjustMSec;

            foreach (var note in session.ActiveNotes)
            {
                // 快速检查: note 是否可能活跃 (避免对不活跃 note 做完整计算)
                float approxDiff = (float)(note.NoteSoflanTime - ConvertAudioTimeToY_PreviewMode(session, time, note.SoflanGroup));
                if (Math.Abs(approxDiff) > scaleStartTime + 100f)
                    continue;

                var result = ComputeY(session, note, time, p);
                if (result.AbsDiffTime <= scaleStartTime)
                {
                    results.Add(new ActiveNoteResult { NoteIndex = note.IndexNote, Result = result });
                }
            }
            return results;
        }

        // GetSoflanTimeDiff (Monitor.NoteBase.mm.cs:68-72)
        private static float GetSoflanTimeDiff(Session session, NoteEntry note, float currentMsec)
        {
            double currentSoflanTime = ConvertAudioTimeToY_PreviewMode(session, currentMsec, note.SoflanGroup);
            return (float)(note.NoteSoflanTime - currentSoflanTime);
        }

        // SoflanManager.ConvertAudioTimeToY_PreviewMode
        private static double ConvertAudioTimeToY_PreviewMode(Session session, float msec, int soflanGroup)
        {
            if (msec < 0) msec = 0;
            return TGridCalculator.ConvertAudioTimeToY_PreviewMode(
                TimeSpan.FromMilliseconds(msec),
                session.SoflanListMap[soflanGroup],
                session.BpmList,
                1);
        }

        // MathUtils.MapValue (忠实复制)
        private static float MapValue(float srcCurrent, float srcFrom, float srcTo, float distFrom, float distTo, bool limitDistInRange = true)
        {
            if (srcFrom == srcTo)
                return distFrom;
            float t = (srcCurrent - srcFrom) / (srcTo - srcFrom);
            if (limitDistInRange)
            {
                if (t < 0f) t = 0f;
                else if (t > 1f) t = 1f;
            }
            return distFrom + t * (distTo - distFrom);
        }
    }
}