using System;
using System.Collections.Generic;
using OngekiFumenEditor.Core.Base;
using OngekiFumenEditor.Core.Base.Collections;
using OngekiFumenEditor.Core.Base.OngekiObjects;
using OngekiFumenEditor.Core.Modules.FumenVisualEditor;
using SoflanCalculator;
using SoflanSupport;

internal static class Program
{
    private const float Bpm = 125f;
    private const float BarMsec = 1920f;
    private const float NoteSpeed = 600f;
    private const float DefaultMsec = 400f;
    private const float StartPos = 120f;
    private const float EndPos = 400f;
    private const float Epsilon = 0.02f;

    private static int Main()
    {
        try
        {
            TestPureMaiBugMath();
            TestOneSpeedOriginalParity();
            TestDisabledAdjustmentUsesRawSoflanTime();
            TestSlowNoteSpeedPositiveAdjustment();
            TestConstantAcceleration();
            TestConstantDeceleration();
            TestStopConsumesNoAdjustmentDistance();
            TestReverseFlipsAdjustmentDirection();
            TestAdjustmentCrossesSoflanBoundaryExactly();
            TestGroupsRemainIndependent();
            TestVisibilityUsesAdjustedSoflanTime();

            Console.WriteLine("SoflanMaiBugTests: PASS");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("SoflanMaiBugTests: FAIL");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void TestPureMaiBugMath()
    {
        Near(MaiBugAdjust.Calculate(150f), 0f, "speed 150 adjustment");
        Near(MaiBugAdjust.Calculate(NoteSpeed), -10f, "speed 600 adjustment");
        Near(MaiBugAdjust.Calculate(NoteSpeed, true), -10f,
            "enabled speed adjustment");
        Near(MaiBugAdjust.Calculate(NoteSpeed, false), 0f,
            "disabled speed adjustment");
        Near(MaiBugAdjust.Calculate(75f), 13.333333f, "speed 75 positive adjustment");
        Near(MaiBugAdjust.CalculateFromDefaultMsec(DefaultMsec), -10f,
            "default-msec adjustment");
        Near(MaiBugAdjust.CalculateFromDefaultMsec(DefaultMsec, false), 0f,
            "disabled default-msec adjustment");
        Near(MaiBugAdjust.CalculateFromVisibleMsec(DefaultMsec * 2f), -10f,
            "visible-msec adjustment");
        Near(MaiBugAdjust.CalculateFromVisibleMsec(DefaultMsec * 2f, false), 0f,
            "disabled visible-msec adjustment");
        Near(MaiBugAdjust.ApplyToAudioMsec(1000f, -10f), 990f,
            "audio-time application");
        Near(MaiBugAdjust.ApplyToAudioMsec(5f, -10f), 0f,
            "negative adjusted audio clamp");

        Near(MaiBugAdjust.Calculate(0f), 0f, "zero speed fallback");
        Near(MaiBugAdjust.Calculate(float.NaN), 0f, "NaN speed fallback");
        Near(MaiBugAdjust.Calculate(float.PositiveInfinity), 0f, "infinite speed fallback");
    }

    private static void TestOneSpeedOriginalParity()
    {
        var data = BuildData(new SflRecord
        {
            Unit = 0,
            Grid = 0,
            Length = 384 * 8,
            Speed = 1f,
            SoflanGroup = 0
        });
        var note = BuildNote(2, 0);
        float noteMsec = 2f * BarMsec;

        // 原版: moveStart = Appear - DefaultMsec - adjust = Appear - 390ms。
        var moveStart = Calculate(data, note, noteMsec - 390f);
        Require(moveStart.MaiBugAdjustEnabled,
            "default calculation should enable the adjustment");
        Near(moveStart.MaiBugAdjustMSec, -10f, "1x adjustment");
        Near(moveStart.MaiBugAdjustedCurrentMsec, noteMsec - DefaultMsec,
            "1x adjusted audio at move start");
        Near(moveStart.DiffTime, DefaultMsec, "1x move-start diff");
        Near(moveStart.SoflanY, StartPos, "1x move-start Y");
        Require(moveStart.NoteStat == NoteStat.Move, "1x move-start state should be Move");

        var scaleStart = Calculate(data, note, noteMsec - 790f);
        Near(scaleStart.DiffTime, DefaultMsec * 2f, "1x scale-start diff");
        Require(scaleStart.NoteStat == NoteStat.Scale, "1x scale-start state should be Scale");

        // 原版高速物件在判定时保留 MaiBug 的细小位置滞后：600 速时为 7px。
        var judgment = Calculate(data, note, noteMsec);
        Near(judgment.DiffTime, 10f, "1x judgment adjusted diff");
        Near(judgment.SoflanY, 393f, "1x judgment Y parity");
    }

    private static void TestConstantAcceleration()
    {
        var data = BuildData(new SflRecord
        {
            Unit = 0,
            Grid = 0,
            Length = 384 * 8,
            Speed = 2f,
            SoflanGroup = 0
        });
        float noteMsec = 2f * BarMsec;

        // 无补偿时 2x 在 200ms 前进入；-10ms 原版补偿应使其在 190ms 前进入。
        var result = Calculate(data, BuildNote(2, 0), noteMsec - 190f);
        Near(result.DiffTime, DefaultMsec, "2x move-start diff");
        Near(result.SoflanY, StartPos, "2x move-start Y");
    }

    private static void TestDisabledAdjustmentUsesRawSoflanTime()
    {
        var data = BuildData(new SflRecord
        {
            Unit = 0,
            Grid = 0,
            Length = 384 * 8,
            Speed = 1f,
            SoflanGroup = 0
        });
        var note = BuildNote(2, 0);
        float noteMsec = 2f * BarMsec;

        var moveStart = Calculate(
            data,
            note,
            noteMsec - DefaultMsec,
            NoteSpeed,
            false);
        Require(!moveStart.MaiBugAdjustEnabled,
            "disabled calculation should report the switch state");
        Near(moveStart.MaiBugAdjustMSec, 0f, "disabled adjustment value");
        Near(moveStart.MaiBugAdjustedCurrentMsec, moveStart.CurrentMsec,
            "disabled adjusted audio should equal raw audio");
        Near(moveStart.CurrentSoflanTime, moveStart.RawCurrentSoflanTime,
            "disabled Soflan current time should remain raw");
        Near(moveStart.DiffTime, DefaultMsec, "disabled move-start diff");
        Near(moveStart.SoflanY, StartPos, "disabled move-start Y");

        var judgment = Calculate(data, note, noteMsec, NoteSpeed, false);
        Near(judgment.DiffTime, 0f, "disabled judgment diff");
        Near(judgment.SoflanY, EndPos, "disabled judgment Y");
    }

    private static void TestSlowNoteSpeedPositiveAdjustment()
    {
        const float slowNoteSpeed = 75f;
        const float slowDefaultMsec = 3200f;
        const float slowAdjustMsec = 13.333333f;
        var data = BuildData(new SflRecord
        {
            Unit = 0,
            Grid = 0,
            Length = 384 * 8,
            Speed = 1f,
            SoflanGroup = 0
        });
        var note = BuildNote(4, 0);
        float noteMsec = 4f * BarMsec;

        // 正偏移同样按音频时间映射：原版移动起点为判定前 D + adjust。
        var moveStart = Calculate(
            data,
            note,
            noteMsec - slowDefaultMsec - slowAdjustMsec,
            slowNoteSpeed);
        Near(moveStart.MaiBugAdjustMSec, slowAdjustMsec, "slow-speed adjustment");
        Near(moveStart.DiffTime, slowDefaultMsec, "slow-speed move-start diff");
        Near(moveStart.SoflanY, StartPos, "slow-speed move-start Y");
        Require(moveStart.NoteStat == NoteStat.Move,
            "slow-speed move-start state should be Move");
    }

    private static void TestConstantDeceleration()
    {
        var data = BuildData(new SflRecord
        {
            Unit = 0,
            Grid = 0,
            Length = 384 * 8,
            Speed = 0.5f,
            SoflanGroup = 0
        });
        float noteMsec = 2f * BarMsec;

        // 无补偿时 0.5x 在 800ms 前进入；-10ms 原版补偿应使其在 790ms 前进入。
        var result = Calculate(data, BuildNote(2, 0), noteMsec - 790f);
        Near(result.DiffTime, DefaultMsec, "0.5x move-start diff");
        Near(result.SoflanY, StartPos, "0.5x move-start Y");
    }

    private static void TestStopConsumesNoAdjustmentDistance()
    {
        var data = BuildData(new SflRecord
        {
            Unit = 1,
            Grid = 0,
            Length = 384,
            Speed = 0f,
            SoflanGroup = 0
        });
        var result = Calculate(data, BuildNote(3, 0), BarMsec * 1.5f);

        Near(result.CurrentSoflanTime, result.RawCurrentSoflanTime,
            "stop should consume no MaiBug Soflan distance");
        Finite(result.DiffTime, "stop diff");
        Finite(result.SoflanY, "stop Y");
    }

    private static void TestReverseFlipsAdjustmentDirection()
    {
        var data = BuildData(new SflRecord
        {
            Unit = 1,
            Grid = 0,
            Length = 384,
            Speed = -1f,
            SoflanGroup = 0
        });
        var result = Calculate(data, BuildNote(3, 0), BarMsec * 1.5f);

        // current-10ms 位于负速段更靠后的位置，因此调整后的 Y 比原始 Y 高 10。
        Near(result.CurrentSoflanTime - result.RawCurrentSoflanTime, 10f,
            "reverse adjustment direction");
        Finite(result.DiffTime, "reverse diff");
        Finite(result.SoflanY, "reverse Y");
    }

    private static void TestAdjustmentCrossesSoflanBoundaryExactly()
    {
        var data = BuildData(new SflRecord
        {
            Unit = 1,
            Grid = 0,
            Length = 384 * 3,
            Speed = 2f,
            SoflanGroup = 0
        });
        var result = Calculate(data, BuildNote(3, 0), BarMsec + 5f);

        // 调整区间 [1915, 1925] 跨过边界：前 5ms 为 1x，后 5ms 为 2x，总 Y 差 15。
        Near(result.RawCurrentSoflanTime - result.CurrentSoflanTime, 15f,
            "boundary-integrated adjustment");
    }

    private static void TestGroupsRemainIndependent()
    {
        var data = BuildData(
            new SflRecord
            {
                Unit = 0,
                Grid = 0,
                Length = 384 * 8,
                Speed = 2f,
                SoflanGroup = 1
            },
            new SflRecord
            {
                Unit = 0,
                Grid = 0,
                Length = 384 * 8,
                Speed = 0.5f,
                SoflanGroup = 2
            });
        float noteMsec = 2f * BarMsec;
        var fast = Calculate(data, BuildNote(2, 1), noteMsec - 190f);
        var slow = Calculate(data, BuildNote(2, 2), noteMsec - 790f);

        Near(fast.DiffTime, DefaultMsec, "group 1 move-start diff");
        Near(slow.DiffTime, DefaultMsec, "group 2 move-start diff");
        Require(Math.Abs(fast.CurrentSoflanSpeed - 2.0) < 0.001,
            "group 1 speed mismatch");
        Require(Math.Abs(slow.CurrentSoflanSpeed - 0.5) < 0.001,
            "group 2 speed mismatch");
    }

    private static void TestVisibilityUsesAdjustedSoflanTime()
    {
        var bpmList = new BpmList { FirstBpm = Bpm };
        var soflanList = new SoflanList();
        var oneSpeed = new Soflan
        {
            TGrid = new TGrid(0, 0),
            EndTGrid = new TGrid(8, 0),
            Speed = 1f,
            SoflanGroup = 0
        };
        soflanList.Add(oneSpeed);

        float noteMsec = 2f * BarMsec;
        float adjust = MaiBugAdjust.Calculate(NoteSpeed);
        var output = new List<SoflanList.VisibleMsecRange>();
        var scratch = new SoflanList.VisibleRangeQueryScratch();

        // 原版 scale 起点：判定前 790ms。使用偏移后的 Soflan 当前时间时，窗口恰好包含 note。
        float visibleStartMsec = noteMsec - 790f;
        double visibleStartY = TGridCalculator.ConvertAudioTimeToY_PreviewMode(
            TimeSpan.FromMilliseconds(visibleStartMsec + adjust),
            soflanList,
            bpmList,
            1);
        soflanList.FillVisibleMsecRangesForGamePreview(
            visibleStartY,
            DefaultMsec * 2f,
            bpmList,
            output,
            scratch);
        Require(Contains(output, noteMsec),
            "MaiBug-adjusted visibility should include the note at visual scale start");

        // 提前一个 5ms grid 时仍不应注册。
        double beforeStartY = TGridCalculator.ConvertAudioTimeToY_PreviewMode(
            TimeSpan.FromMilliseconds(visibleStartMsec - 5f + adjust),
            soflanList,
            bpmList,
            1);
        soflanList.FillVisibleMsecRangesForGamePreview(
            beforeStartY,
            DefaultMsec * 2f,
            bpmList,
            output,
            scratch);
        Require(!Contains(output, noteMsec),
            "MaiBug-adjusted visibility registered the note before visual scale start");

        // 开关关闭时窗口从原始 currentMsec 开始，1x 下应回到判定前 800ms。
        float disabledVisibleStartMsec = noteMsec - DefaultMsec * 2f;
        double disabledVisibleStartY = TGridCalculator.ConvertAudioTimeToY_PreviewMode(
            TimeSpan.FromMilliseconds(disabledVisibleStartMsec),
            soflanList,
            bpmList,
            1);
        soflanList.FillVisibleMsecRangesForGamePreview(
            disabledVisibleStartY,
            DefaultMsec * 2f,
            bpmList,
            output,
            scratch);
        Require(Contains(output, noteMsec),
            "disabled-adjustment visibility should include the note at the raw window start");

        double beforeDisabledStartY = TGridCalculator.ConvertAudioTimeToY_PreviewMode(
            TimeSpan.FromMilliseconds(disabledVisibleStartMsec - 5f),
            soflanList,
            bpmList,
            1);
        soflanList.FillVisibleMsecRangesForGamePreview(
            beforeDisabledStartY,
            DefaultMsec * 2f,
            bpmList,
            output,
            scratch);
        Require(!Contains(output, noteMsec),
            "disabled-adjustment visibility registered the note before the raw window start");
    }

    private static bool Contains(List<SoflanList.VisibleMsecRange> ranges, double msec)
    {
        foreach (var range in ranges)
        {
            if (range.Contain(msec))
                return true;
        }
        return false;
    }

    private static Ma2Data BuildData(params SflRecord[] soflans)
    {
        var data = new Ma2Data
        {
            Resolution = 384,
            FirstBpm = Bpm
        };
        foreach (var soflan in soflans)
            data.Soflans.Add(soflan);
        return data;
    }

    private static NoteRecord BuildNote(int bar, int group)
    {
        return new NoteRecord
        {
            LineNumber = 1,
            Type = "NMTAP",
            Bar = bar,
            Grid = 0,
            Pos = 0,
            SoflanGroup = group
        };
    }

    private static CalcResult Calculate(
        Ma2Data data,
        NoteRecord note,
        float currentMsec,
        float noteSpeed = NoteSpeed,
        bool enableMaiBugAdjust = true)
    {
        return SoflanCalcEngine.Calculate(
            data,
            note,
            currentMsec,
            noteSpeed,
            StartPos,
            EndPos,
            enableMaiBugAdjust);
    }

    private static void Near(float actual, float expected, string name)
    {
        if (Math.Abs(actual - expected) > Epsilon)
            throw new InvalidOperationException(
                name + $": expected {expected:F4}, actual {actual:F4}");
    }

    private static void Finite(float value, string name)
    {
        Require(!float.IsNaN(value) && !float.IsInfinity(value), name + " is not finite");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
