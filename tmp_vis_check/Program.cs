// 模拟 SoflanManager.checkNoteVisible 的 group 级可见范围逻辑:
//   currentSoflanTime = ConvertAudioTimeToY_PreviewMode(rawChartCurrent, list[group], bpm)
//   FillVisibleMsecRangesForGamePreview(currentSoflanTime, apperMsec, bpm, ranges)
//   visible = ranges.Any(r => r.Contain(noteAudioMsec))
using System;
using System.Collections.Generic;
using OngekiFumenEditor.Core.Base;
using OngekiFumenEditor.Core.Base.Collections;
using OngekiFumenEditor.Core.Base.OngekiObjects;
using OngekiFumenEditor.Core.Modules.FumenVisualEditor;
using OngekiFumenEditor.Core.Utils;

internal static class Program
{
    private static int Main(string[] args)
    {
        // args: speed length group apperMsec noteBarNoteGrid...
        float speed = float.Parse(args[0]);
        int sflLength = int.Parse(args[1]);
        int group = int.Parse(args[2]);
        float apperMsec = float.Parse(args[3]);

        var bpmList = new BpmList { FirstBpm = 120.0 };

        var map = new SoflanListMap();
        var sfl = new Soflan
        {
            TGrid = new TGrid(1, 0), // bar 1 grid 0
            Speed = speed,
            SoflanGroup = group
        };
        sfl.EndTGrid = sfl.TGrid + new GridOffset(0, sflLength);
        map.Add(sfl);

        // 6 个 tap: bar1 grid 0/96/192/288, bar2 grid 0/96
        var noteGrids = new[] { new TGrid(1, 0), new TGrid(1, 96), new TGrid(1, 192), new TGrid(1, 288), new TGrid(2, 0), new TGrid(2, 96) };
        var noteMsec = new double[noteGrids.Length];
        for (int i = 0; i < noteGrids.Length; i++)
            noteMsec[i] = TGridCalculator.ConvertTGridToAudioTime(noteGrids[i], bpmList).TotalMilliseconds;

        var list = map[group];
        var ranges = new List<SoflanList.VisibleMsecRange>();
        var scratch = new SoflanList.VisibleRangeQueryScratch();

        // 记录每个 note 首次/最后可见时间
        var firstVisible = new double[noteGrids.Length];
        var lastVisible = new double[noteGrids.Length];
        var everVisible = new bool[noteGrids.Length];
        for (int i = 0; i < noteGrids.Length; i++) { firstVisible[i] = -1; lastVisible[i] = -1; }

        for (double cur = 0; cur <= 6000; cur += 8)
        {
            float currentSoflanTime = (float)TGridCalculator.ConvertAudioTimeToY_PreviewMode(
                TimeSpan.FromMilliseconds(cur), list, bpmList, 1);
            ranges.Clear();
            list.FillVisibleMsecRangesForGamePreview(currentSoflanTime, apperMsec, bpmList, ranges, scratch);
            for (int i = 0; i < noteMsec.Length; i++)
            {
                bool vis = false;
                foreach (var r in ranges)
                    if (r.Contain(noteMsec[i])) { vis = true; break; }
                if (vis)
                {
                    if (!everVisible[i]) { everVisible[i] = true; firstVisible[i] = cur; }
                    lastVisible[i] = cur;
                }
            }
        }

        Console.WriteLine($"speed={speed} sflLen={sflLength} group={group} apperMsec={apperMsec}");
        for (int i = 0; i < noteMsec.Length; i++)
        {
            float noteSoflanY = (float)TGridCalculator.ConvertAudioTimeToY_PreviewMode(
                TimeSpan.FromMilliseconds(noteMsec[i]), list, bpmList, 1);
            Console.WriteLine($"note{i} grid={noteGrids[i]} audioMsec={noteMsec[i]:F0} soflanY={noteSoflanY:F1} " +
                (everVisible[i]
                    ? $"visible: first={firstVisible[i]:F0} last={lastVisible[i]:F0} lead={noteMsec[i] - firstVisible[i]:F0}ms 判定前后可见={(lastVisible[i] >= noteMsec[i] ? "是" : "否")}"
                    : "NEVER VISIBLE -> 无法注册 -> Miss"));
        }
        return 0;
    }
}
