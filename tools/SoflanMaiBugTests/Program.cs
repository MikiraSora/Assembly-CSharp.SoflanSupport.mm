using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OngekiFumenEditor.Core.Base;
using OngekiFumenEditor.Core.Base.Collections;
using OngekiFumenEditor.Core.Base.OngekiObjects;
using OngekiFumenEditor.Core.Modules.FumenVisualEditor;
using OngekiFumenEditor.Core.Utils;
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
    private const float DefaultRuntimeChartOffsetMsec = 59.9999962f;

    private static int Main(string[] args)
    {
        try
        {
            TestPureMaiBugMath();
            TestRuntimeChartTimeMath();
            TestReverseSoflanRegistrationPreservesLaneJudgeOrder();
            TestOneSpeedOriginalParity();
            TestOneSpeedOriginalParityWithRuntimeChartOffset();
            TestMissingRuntimeChartOffsetRegressionEvidence();
            TestDisabledAdjustmentUsesRawSoflanTime();
            TestSlowNoteSpeedPositiveAdjustment();
            TestConstantAcceleration();
            TestConstantDeceleration();
            TestStopConsumesNoAdjustmentDistance();
            TestReverseFlipsAdjustmentDirection();
            TestAdjustmentCrossesSoflanBoundaryExactly();
            TestGroupsRemainIndependent();
            TestVisibilityUsesAdjustedSoflanTime();
            TestComplexTimelineTranslationInvariance();
            TestComplexVisibilityTranslationInvariance();
            TestTwoPlayerRuntimeChartOffsetsRemainIndependent();

            if (args.Length > 0)
                TestRealChartTranslationInvariance(args[0]);
            if (args.Length > 1)
                TestOneSpeedBaselineChartParity(args[1], args[0]);

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

    private static void TestRuntimeChartTimeMath()
    {
        Near(
            SoflanRuntimeTime.ToRawChartAudioMsec(1060f, 60f, 0f),
            1000f,
            "runtime chart offset removal");
        Near(
            SoflanRuntimeTime.ToRawChartAudioMsec(1060f, 60f, -10f),
            990f,
            "runtime chart offset then MaiBug application");
        Near(
            SoflanRuntimeTime.ToRawChartAudioMsec(50f, 60f, -10f),
            0f,
            "combined offset clamps after conversion");
        Near(
            SoflanRuntimeTime.NormalizeRuntimeChartOffsetMsec(float.NaN),
            0f,
            "invalid runtime chart offset fallback");
        Near(
            SoflanRuntimeTime.ToRawChartAudioMsec(float.PositiveInfinity, 60f, 0f),
            0f,
            "invalid runtime current fallback");
    }

    private static void TestReverseSoflanRegistrationPreservesLaneJudgeOrder()
    {
        // 016800_01.ma2 group 13, lane 0. Extreme reverse speed makes the
        // visual registration order differ from the original note order.
        var registrationOrder = new[] { 192, 216, 360, 240, 336, 264, 312, 288 };
        var expectedJudgeOrder = new[] { 192, 216, 240, 264, 288, 312, 336, 360 };
        var siblingNoteIndices = new List<int>();

        for (var i = 0; i < registrationOrder.Length; i++)
        {
            var noteIndex = registrationOrder[i];
            var siblingIndex = SoflanJudgeOrder.GetSiblingIndex(
                noteIndex,
                siblingNoteIndices.Count,
                index => siblingNoteIndices[index]);
            siblingNoteIndices.Insert(siblingIndex, noteIndex);
        }

        var actualJudgeOrder = new List<int>();
        while (siblingNoteIndices.Count > 0)
        {
            var judgeHeadIndex = siblingNoteIndices.Count - 1;
            actualJudgeOrder.Add(siblingNoteIndices[judgeHeadIndex]);
            siblingNoteIndices.RemoveAt(judgeHeadIndex);
        }

        Require(actualJudgeOrder.SequenceEqual(expectedJudgeOrder),
            "reverse Soflan registration changed the lane judgment order: "
            + string.Join(",", actualJudgeOrder));
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

    private static void TestOneSpeedOriginalParityWithRuntimeChartOffset()
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
        float rawNoteMsec = 2f * BarMsec;
        float runtimeNoteMsec = rawNoteMsec + DefaultRuntimeChartOffsetMsec;

        var moveStart = Calculate(
            data,
            note,
            runtimeNoteMsec - 390f,
            NoteSpeed,
            true,
            DefaultRuntimeChartOffsetMsec);
        Near(moveStart.RawChartCurrentMsec, rawNoteMsec - 390f,
            "1x runtime-offset raw current at move start", 0.05f);
        Near(moveStart.MaiBugAdjustedCurrentMsec, rawNoteMsec - DefaultMsec,
            "1x runtime-offset adjusted current at move start", 0.05f);
        Near(moveStart.DiffTime, DefaultMsec,
            "1x runtime-offset move-start diff", 0.05f);
        Near(moveStart.SoflanY, StartPos,
            "1x runtime-offset move-start Y", 0.05f);

        var enabledJudgment = Calculate(
            data,
            note,
            runtimeNoteMsec,
            NoteSpeed,
            true,
            DefaultRuntimeChartOffsetMsec);
        Near(enabledJudgment.DiffTime, 10f,
            "1x runtime-offset enabled judgment diff", 0.05f);
        Near(enabledJudgment.SoflanY, 393f,
            "1x runtime-offset enabled judgment Y", 0.05f);

        var disabledJudgment = Calculate(
            data,
            note,
            runtimeNoteMsec,
            NoteSpeed,
            false,
            DefaultRuntimeChartOffsetMsec);
        Near(disabledJudgment.DiffTime, 0f,
            "1x runtime-offset disabled judgment diff", 0.05f);
        Near(disabledJudgment.SoflanY, EndPos,
            "1x runtime-offset disabled judgment Y", 0.05f);
    }

    private static void TestMissingRuntimeChartOffsetRegressionEvidence()
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
        float rawNoteMsec = 2f * BarMsec;
        float runtimeNoteMsec = rawNoteMsec + DefaultRuntimeChartOffsetMsec;

        var correctedEnabled = Calculate(
            data, note, runtimeNoteMsec, NoteSpeed, true,
            DefaultRuntimeChartOffsetMsec);
        var missingChartOffsetEnabled = Calculate(
            data, note, runtimeNoteMsec, NoteSpeed, true, 0f);
        Near(
            correctedEnabled.DiffTime - missingChartOffsetEnabled.DiffTime,
            60f,
            "missing chart offset enabled regression magnitude",
            0.05f);

        var missingChartOffsetDisabled = Calculate(
            data, note, runtimeNoteMsec, NoteSpeed, false, 0f);
        Near(
            correctedEnabled.DiffTime - missingChartOffsetDisabled.DiffTime,
            70f,
            "missing chart offset plus disabled MaiBug regression magnitude",
            0.05f);
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

    private static void TestComplexTimelineTranslationInvariance()
    {
        var data = BuildComplexData();
        var notes = new[]
        {
            BuildNote(6, 7),
            BuildNote(6, 8)
        };
        var speeds = new[] { 75f, NoteSpeed, 1100f };

        foreach (var note in notes)
        {
            foreach (var speed in speeds)
            {
                foreach (var enabled in new[] { false, true })
                {
                    for (float rawCurrentMsec = 0f; rawCurrentMsec <= 12000f; rawCurrentMsec += 83.333336f)
                    {
                        var rawAxisReference = Calculate(
                            data,
                            note,
                            rawCurrentMsec,
                            speed,
                            enabled,
                            0f);
                        var correctedRuntimeAxis = Calculate(
                            data,
                            note,
                            rawCurrentMsec + DefaultRuntimeChartOffsetMsec,
                            speed,
                            enabled,
                            DefaultRuntimeChartOffsetMsec);

                        AssertVisualResultParity(
                            correctedRuntimeAxis,
                            rawAxisReference,
                            $"complex timeline group={note.SoflanGroup} speed={speed} enabled={enabled} raw={rawCurrentMsec:F3}");
                    }
                }
            }
        }
    }

    private static void TestComplexVisibilityTranslationInvariance()
    {
        var data = BuildComplexData();
        var timeline = BuildTimeline(data);

        foreach (var group in new[] { 7, 8 })
        {
            var soflanList = timeline.SoflanMap[group];
            foreach (var visualAudioOffsetMsec in new[] { 0f, -10f, 13.333333f })
            {
                for (float rawCurrentMsec = 0f; rawCurrentMsec <= 12000f; rawCurrentMsec += 191.25f)
                {
                    var rawReferenceMsec = SoflanRuntimeTime.ToRawChartAudioMsec(
                        rawCurrentMsec,
                        0f,
                        visualAudioOffsetMsec);
                    var correctedRawMsec = SoflanRuntimeTime.ToRawChartAudioMsec(
                        rawCurrentMsec + DefaultRuntimeChartOffsetMsec,
                        DefaultRuntimeChartOffsetMsec,
                        visualAudioOffsetMsec);
                    var referenceY = ConvertAudioTimeToY(rawReferenceMsec, soflanList, timeline.BpmList);
                    var correctedY = ConvertAudioTimeToY(correctedRawMsec, soflanList, timeline.BpmList);
                    Near(correctedY, referenceY,
                        $"complex visibility current Y group={group} raw={rawCurrentMsec:F3}", 0.05f);

                    AssertVisibleRangesEqual(
                        soflanList,
                        timeline.BpmList,
                        referenceY,
                        correctedY,
                        800f,
                        $"complex visibility ranges group={group} raw={rawCurrentMsec:F3}");
                }
            }
        }
    }

    private static void TestTwoPlayerRuntimeChartOffsetsRemainIndependent()
    {
        var data = BuildComplexData();
        var note = BuildNote(6, 7);
        const float player0Offset = 59.9999962f;
        const float player1Offset = 76.666664f;

        for (float rawCurrentMsec = 0f; rawCurrentMsec <= 12000f; rawCurrentMsec += 137.5f)
        {
            var player0 = Calculate(
                data,
                note,
                rawCurrentMsec + player0Offset,
                NoteSpeed,
                true,
                player0Offset);
            var player1 = Calculate(
                data,
                note,
                rawCurrentMsec + player1Offset,
                NoteSpeed,
                true,
                player1Offset);

            AssertVisualResultParity(
                player1,
                player0,
                $"two-player independent offsets raw={rawCurrentMsec:F3}");
        }
    }

    private static void TestRealChartTranslationInvariance(string filePath)
    {
        Require(File.Exists(filePath), "real chart comparison file does not exist: " + filePath);
        var data = Ma2Parser.Parse(filePath);
        Require(data.Soflans.Count > 0, "real chart comparison requires SFL records");

        var timeline = BuildTimeline(data);
        var groups = new HashSet<int>();
        foreach (var soflan in data.Soflans)
            groups.Add(soflan.SoflanGroup);
        foreach (var note in data.Notes)
            groups.Add(note.SoflanGroup);

        var samples = BuildRealChartSamples(data, timeline.BpmList);
        double maxRawInputDelta = 0d;
        double maxSoflanYDelta = 0d;
        long timelineComparisons = 0;
        long visibilityComparisons = 0;

        foreach (var group in groups.OrderBy(x => x))
        {
            var soflanList = timeline.SoflanMap[group];
            foreach (var visualAudioOffsetMsec in new[] { 0f, -10f, 13.333333f })
            {
                for (var sampleIndex = 0; sampleIndex < samples.Count; sampleIndex++)
                {
                    var rawCurrentMsec = samples[sampleIndex];
                    var rawReferenceMsec = SoflanRuntimeTime.ToRawChartAudioMsec(
                        rawCurrentMsec,
                        0f,
                        visualAudioOffsetMsec);
                    var correctedRawMsec = SoflanRuntimeTime.ToRawChartAudioMsec(
                        rawCurrentMsec + DefaultRuntimeChartOffsetMsec,
                        DefaultRuntimeChartOffsetMsec,
                        visualAudioOffsetMsec);
                    var rawInputDelta = Math.Abs((double)correctedRawMsec - rawReferenceMsec);
                    if (rawInputDelta > maxRawInputDelta)
                        maxRawInputDelta = rawInputDelta;

                    var referenceY = ConvertAudioTimeToY(rawReferenceMsec, soflanList, timeline.BpmList);
                    var correctedY = ConvertAudioTimeToY(correctedRawMsec, soflanList, timeline.BpmList);
                    var soflanYDelta = Math.Abs((double)correctedY - referenceY);
                    if (soflanYDelta > maxSoflanYDelta)
                        maxSoflanYDelta = soflanYDelta;

                    Near(correctedY, referenceY,
                        $"real chart timeline group={group} offset={visualAudioOffsetMsec} raw={rawCurrentMsec:F3}",
                        0.05f);
                    timelineComparisons++;

                    var visibilityStride = Math.Max(1, samples.Count / 24);
                    if (sampleIndex % visibilityStride != 0)
                        continue;

                    AssertVisibleRangesEqual(
                        soflanList,
                        timeline.BpmList,
                        referenceY,
                        correctedY,
                        800f,
                        $"real chart visibility group={group} offset={visualAudioOffsetMsec} raw={rawCurrentMsec:F3}");
                    visibilityComparisons++;
                }
            }
        }

        Console.WriteLine(
            "RealChartComparison: " + Path.GetFileName(filePath) +
            $" groups={groups.Count} samples={samples.Count}" +
            $" timelineComparisons={timelineComparisons}" +
            $" visibilityComparisons={visibilityComparisons}" +
            $" maxRawInputDelta={maxRawInputDelta:F9}ms" +
            $" maxSoflanYDelta={maxSoflanYDelta:F9}");
    }

    private static void TestOneSpeedBaselineChartParity(
        string baselineFilePath,
        string soflanFilePath)
    {
        Require(File.Exists(baselineFilePath),
            "baseline chart comparison file does not exist: " + baselineFilePath);
        Require(File.Exists(soflanFilePath),
            "Soflan chart comparison file does not exist: " + soflanFilePath);

        var baselineData = Ma2Parser.Parse(baselineFilePath);
        var soflanData = Ma2Parser.Parse(soflanFilePath);
        Require(baselineData.Soflans.Count == 0,
            "baseline chart must not contain SFL records");
        Require(!soflanData.Soflans.Any(x => x.SoflanGroup == 0),
            "Soflan chart group 0 must remain an identity track for baseline parity");

        var baselineByLine = baselineData.Notes.ToDictionary(x => x.LineNumber);
        var timeline = BuildTimeline(soflanData);
        var identityList = timeline.SoflanMap[0];
        var sampleOffsets = new[] { -900f, -800f, -600f, -400f, -200f, -70f, 0f, 200f };

        var comparedNotes = 0;
        var comparedFrames = 0;
        var stateMismatchCount = 0;
        double maxDiffTimeDelta = 0d;
        double maxYDelta = 0d;
        double maxObjectScaleDelta = 0d;
        double maxGuideScaleDelta = 0d;
        double maxGuideAlphaDelta = 0d;
        var maiBugAdjustMsec = MaiBugAdjust.Calculate(NoteSpeed);

        foreach (var soflanNote in soflanData.Notes)
        {
            if (soflanNote.SoflanGroup != 0 || !IsTapFamilyRecord(soflanNote.Type))
                continue;
            if (!baselineByLine.TryGetValue(soflanNote.LineNumber, out var baselineNote))
                continue;
            if (baselineNote.Type != soflanNote.Type
                || baselineNote.Bar != soflanNote.Bar
                || baselineNote.Grid != soflanNote.Grid
                || baselineNote.Pos != soflanNote.Pos)
                continue;

            var rawNoteMsec = (float)TGridCalculator.ConvertTGridToAudioTime(
                new TGrid(soflanNote.Bar, soflanNote.Grid),
                timeline.BpmList).TotalMilliseconds;
            var noteSoflanTime = ConvertAudioTimeToY(
                rawNoteMsec,
                identityList,
                timeline.BpmList);

            foreach (var sampleOffsetMsec in sampleOffsets)
            {
                var runtimeCurrentMsec = rawNoteMsec
                    + DefaultRuntimeChartOffsetMsec
                    + sampleOffsetMsec;
                var correctedRawCurrentMsec = SoflanRuntimeTime.ToRawChartAudioMsec(
                    runtimeCurrentMsec,
                    DefaultRuntimeChartOffsetMsec,
                    maiBugAdjustMsec);
                var correctedCurrentSoflanTime = ConvertAudioTimeToY(
                    correctedRawCurrentMsec,
                    identityList,
                    timeline.BpmList);
                var corrected = BuildTapVisualSnapshot(
                    noteSoflanTime - correctedCurrentSoflanTime,
                    DefaultMsec);

                // 原版无 SFL 路径：note.time.msec 与当前时钟都在运行时轴，
                // GetMaiBugAdjustMSec 只加到当前视觉时钟。
                var originalDiffTime = rawNoteMsec
                    + DefaultRuntimeChartOffsetMsec
                    - (runtimeCurrentMsec + maiBugAdjustMsec);
                var original = BuildTapVisualSnapshot(originalDiffTime, DefaultMsec);

                maxDiffTimeDelta = Math.Max(
                    maxDiffTimeDelta,
                    Math.Abs((double)corrected.DiffTime - original.DiffTime));
                maxYDelta = Math.Max(
                    maxYDelta,
                    Math.Abs((double)corrected.Y - original.Y));
                maxObjectScaleDelta = Math.Max(
                    maxObjectScaleDelta,
                    Math.Abs((double)corrected.ObjectScale - original.ObjectScale));
                maxGuideScaleDelta = Math.Max(
                    maxGuideScaleDelta,
                    Math.Abs((double)corrected.GuideScale - original.GuideScale));
                maxGuideAlphaDelta = Math.Max(
                    maxGuideAlphaDelta,
                    Math.Abs((double)corrected.GuideAlpha - original.GuideAlpha));
                if (corrected.NoteStat != original.NoteStat)
                    stateMismatchCount++;
                comparedFrames++;
            }

            comparedNotes++;
        }

        Require(comparedNotes > 0, "no common identity-group Tap-family notes were compared");
        Require(maxDiffTimeDelta <= 2.1d,
            $"identity-group diff delta too large: {maxDiffTimeDelta:F9}ms");
        Require(maxYDelta <= 1.5d,
            $"identity-group Y delta too large: {maxYDelta:F9}px");
        Require(maxObjectScaleDelta <= 0.006d,
            $"identity-group object-scale delta too large: {maxObjectScaleDelta:F9}");
        Require(maxGuideScaleDelta <= 0.006d,
            $"identity-group guide-scale delta too large: {maxGuideScaleDelta:F9}");
        Require(maxGuideAlphaDelta <= 0.006d,
            $"identity-group guide-alpha delta too large: {maxGuideAlphaDelta:F9}");
        Require(stateMismatchCount == 0,
            $"identity-group note-state mismatch count: {stateMismatchCount}");

        Console.WriteLine(
            "OneSpeedBaselineComparison: " +
            Path.GetFileName(baselineFilePath) + " vs " + Path.GetFileName(soflanFilePath) +
            $" notes={comparedNotes} frames={comparedFrames}" +
            $" maxDiffDelta={maxDiffTimeDelta:F9}ms" +
            $" maxYDelta={maxYDelta:F9}px" +
            $" maxObjectScaleDelta={maxObjectScaleDelta:F9}" +
            $" maxGuideScaleDelta={maxGuideScaleDelta:F9}" +
            $" maxGuideAlphaDelta={maxGuideAlphaDelta:F9}" +
            $" stateMismatches={stateMismatchCount}");
    }

    private static bool IsTapFamilyRecord(string type)
    {
        switch ((type ?? string.Empty).ToUpperInvariant())
        {
            case "TAP":
            case "BRK":
            case "XTP":
            case "STR":
            case "BST":
            case "XST":
            case "NMTAP":
            case "BRTAP":
            case "EXTAP":
            case "BXTAP":
            case "NMSTR":
            case "BRSTR":
            case "EXSTR":
            case "BXSTR":
                return true;
            default:
                return false;
        }
    }

    private static TapVisualSnapshot BuildTapVisualSnapshot(float diffTime, float defaultMsec)
    {
        var absDiffTime = Math.Abs(diffTime);
        var moveStartTime = defaultMsec;
        var scaleStartTime = 2f * defaultMsec;
        var outsideY = EndPos + (EndPos - StartPos);
        var y = MathUtils.MapValue(
            diffTime,
            -moveStartTime,
            moveStartTime,
            outsideY,
            StartPos);
        y = Math.Max(120f, Math.Min(680f, y));
        var moveProgress = Math.Max(0f, (y - StartPos) / (EndPos - StartPos));
        var guideScale = 0.25f + 0.75f * moveProgress;
        var objectScale = Math.Max(
            0f,
            Math.Min(1f, (scaleStartTime - absDiffTime) / defaultMsec));

        NoteStat noteStat;
        float guideAlpha;
        if (absDiffTime > scaleStartTime)
        {
            noteStat = NoteStat.Init;
            guideAlpha = 0f;
        }
        else if (absDiffTime > moveStartTime)
        {
            noteStat = NoteStat.Scale;
            guideAlpha = MathUtils.MapValue(
                absDiffTime,
                scaleStartTime,
                moveStartTime,
                0f,
                1f);
        }
        else
        {
            noteStat = NoteStat.Move;
            guideAlpha = 1f;
        }

        return new TapVisualSnapshot(
            diffTime,
            y,
            objectScale,
            guideScale,
            guideAlpha,
            noteStat);
    }

    private readonly struct TapVisualSnapshot
    {
        public readonly float DiffTime;
        public readonly float Y;
        public readonly float ObjectScale;
        public readonly float GuideScale;
        public readonly float GuideAlpha;
        public readonly NoteStat NoteStat;

        public TapVisualSnapshot(
            float diffTime,
            float y,
            float objectScale,
            float guideScale,
            float guideAlpha,
            NoteStat noteStat)
        {
            DiffTime = diffTime;
            Y = y;
            ObjectScale = objectScale;
            GuideScale = guideScale;
            GuideAlpha = guideAlpha;
            NoteStat = noteStat;
        }
    }

    private static Ma2Data BuildComplexData()
    {
        var data = new Ma2Data
        {
            Resolution = 384,
            FirstBpm = 217f
        };
        data.BpmChanges.Add(new BpmRecord { Bar = 2, Grid = 0, Bpm = 173f });
        data.BpmChanges.Add(new BpmRecord { Bar = 4, Grid = 192, Bpm = 256f });

        data.Soflans.Add(new SflRecord { Unit = 0, Grid = 0, Length = 384, Speed = 1f, SoflanGroup = 7 });
        data.Soflans.Add(new SflRecord { Unit = 1, Grid = 0, Length = 192, Speed = 2f, SoflanGroup = 7 });
        data.Soflans.Add(new SflRecord { Unit = 1, Grid = 192, Length = 192, Speed = 0f, SoflanGroup = 7 });
        data.Soflans.Add(new SflRecord { Unit = 2, Grid = 0, Length = 384, Speed = -1f, SoflanGroup = 7 });
        data.Soflans.Add(new SflRecord { Unit = 3, Grid = 0, Length = 192, Speed = 0.5f, SoflanGroup = 7 });
        data.Soflans.Add(new SflRecord { Unit = 3, Grid = 192, Length = 384, Speed = 1.75f, SoflanGroup = 7 });

        data.Soflans.Add(new SflRecord { Unit = 0, Grid = 0, Length = 768, Speed = 0.75f, SoflanGroup = 8 });
        data.Soflans.Add(new SflRecord { Unit = 2, Grid = 0, Length = 384, Speed = 3f, SoflanGroup = 8 });
        data.Soflans.Add(new SflRecord { Unit = 3, Grid = 0, Length = 384, Speed = -0.5f, SoflanGroup = 8 });
        data.Soflans.Add(new SflRecord { Unit = 4, Grid = 0, Length = 768, Speed = 0f, SoflanGroup = 8 });

        return data;
    }

    private static TimelineContext BuildTimeline(Ma2Data data)
    {
        var bpmList = new BpmList { FirstBpm = data.FirstBpm };
        foreach (var bpm in data.BpmChanges)
        {
            bpmList.Add(new BPMChange
            {
                BPM = bpm.Bpm,
                TGrid = new TGrid(bpm.Bar, bpm.Grid)
            });
        }

        var soflanMap = new SoflanListMap();
        foreach (var record in data.Soflans)
        {
            var soflan = new Soflan
            {
                TGrid = new TGrid(record.Unit, record.Grid),
                Speed = record.Speed,
                SoflanGroup = record.SoflanGroup
            };
            soflan.EndTGrid = soflan.TGrid + new GridOffset(0, record.Length);
            soflanMap.Add(soflan);
        }

        return new TimelineContext(bpmList, soflanMap);
    }

    private static List<float> BuildRealChartSamples(Ma2Data data, BpmList bpmList)
    {
        var samples = new SortedSet<float> { 0f };
        float chartEndMsec = 0f;

        foreach (var note in data.Notes)
        {
            var noteMsec = (float)TGridCalculator.ConvertTGridToAudioTime(
                new TGrid(note.Bar, note.Grid), bpmList).TotalMilliseconds;
            samples.Add(noteMsec);
            if (noteMsec > chartEndMsec)
                chartEndMsec = noteMsec;
        }

        foreach (var record in data.Soflans)
        {
            var start = new TGrid(record.Unit, record.Grid);
            var end = start + new GridOffset(0, record.Length);
            AddBoundarySamples(samples,
                (float)TGridCalculator.ConvertTGridToAudioTime(start, bpmList).TotalMilliseconds);
            AddBoundarySamples(samples,
                (float)TGridCalculator.ConvertTGridToAudioTime(end, bpmList).TotalMilliseconds);
        }

        foreach (var bpm in data.BpmChanges)
        {
            AddBoundarySamples(samples,
                (float)TGridCalculator.ConvertTGridToAudioTime(
                    new TGrid(bpm.Bar, bpm.Grid), bpmList).TotalMilliseconds);
        }

        chartEndMsec += 1000f;
        var regularStep = Math.Max(16.666666f, chartEndMsec / 600f);
        for (float msec = 0f; msec <= chartEndMsec; msec += regularStep)
            samples.Add(msec);
        samples.Add(chartEndMsec);

        return samples.ToList();
    }

    private static void AddBoundarySamples(SortedSet<float> samples, float boundaryMsec)
    {
        foreach (var delta in new[] { -100f, -10f, -1f, 0f, 1f, 10f, 100f })
            samples.Add(Math.Max(0f, boundaryMsec + delta));
    }

    private static float ConvertAudioTimeToY(float msec, SoflanList soflanList, BpmList bpmList)
    {
        return (float)TGridCalculator.ConvertAudioTimeToY_PreviewMode(
            TimeSpan.FromMilliseconds(msec), soflanList, bpmList, 1);
    }

    private static void AssertVisibleRangesEqual(
        SoflanList soflanList,
        BpmList bpmList,
        float referenceY,
        float correctedY,
        float visibleMsec,
        string name)
    {
        var referenceRanges = new List<SoflanList.VisibleMsecRange>();
        var correctedRanges = new List<SoflanList.VisibleMsecRange>();
        soflanList.FillVisibleMsecRangesForGamePreview(
            referenceY,
            visibleMsec,
            bpmList,
            referenceRanges,
            new SoflanList.VisibleRangeQueryScratch());
        soflanList.FillVisibleMsecRangesForGamePreview(
            correctedY,
            visibleMsec,
            bpmList,
            correctedRanges,
            new SoflanList.VisibleRangeQueryScratch());

        Require(referenceRanges.Count == correctedRanges.Count,
            name + $": range count expected {referenceRanges.Count}, actual {correctedRanges.Count}");
        for (var i = 0; i < referenceRanges.Count; i++)
        {
            Near((float)correctedRanges[i].MinMsec, (float)referenceRanges[i].MinMsec,
                name + $" range[{i}] min", 0.05f);
            Near((float)correctedRanges[i].MaxMsec, (float)referenceRanges[i].MaxMsec,
                name + $" range[{i}] max", 0.05f);
        }
    }

    private static void AssertVisualResultParity(CalcResult actual, CalcResult expected, string name)
    {
        Near(actual.RawChartCurrentMsec, expected.RawChartCurrentMsec,
            name + " raw chart current", 0.05f);
        Near(actual.MaiBugAdjustedCurrentMsec, expected.MaiBugAdjustedCurrentMsec,
            name + " visual adjusted current", 0.05f);
        Near(actual.RawCurrentSoflanTime, expected.RawCurrentSoflanTime,
            name + " raw Soflan time", 0.05f);
        Near(actual.CurrentSoflanTime, expected.CurrentSoflanTime,
            name + " current Soflan time", 0.05f);
        Near(actual.DiffTime, expected.DiffTime,
            name + " diff", 0.05f);
        Near(actual.AbsDiffTime, expected.AbsDiffTime,
            name + " abs diff", 0.05f);
        Near(actual.SoflanY, expected.SoflanY,
            name + " Y", 0.05f);
        Near(actual.ClipedSoflanY, expected.ClipedSoflanY,
            name + " clipped Y", 0.05f);
        Near(actual.MoveProgress, expected.MoveProgress,
            name + " move progress", 0.0002f);
        Near(actual.FinalScale, expected.FinalScale,
            name + " final scale", 0.0002f);
        Near(actual.ObjectScaleProgress, expected.ObjectScaleProgress,
            name + " object scale", 0.0002f);
        Near(actual.GuideAlpha, expected.GuideAlpha,
            name + " guide alpha", 0.0002f);
        Require(actual.NoteStat == expected.NoteStat,
            name + $": note state expected {expected.NoteStat}, actual {actual.NoteStat}");
        Require(Math.Abs(actual.CurrentSoflanSpeed - expected.CurrentSoflanSpeed) < 0.0001,
            name + $": speed expected {expected.CurrentSoflanSpeed}, actual {actual.CurrentSoflanSpeed}");
    }

    private sealed class TimelineContext
    {
        public readonly BpmList BpmList;
        public readonly SoflanListMap SoflanMap;

        public TimelineContext(BpmList bpmList, SoflanListMap soflanMap)
        {
            BpmList = bpmList;
            SoflanMap = soflanMap;
        }
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
        bool enableMaiBugAdjust = true,
        float runtimeChartOffsetMsec = 0f)
    {
        return SoflanCalcEngine.Calculate(
            data,
            note,
            currentMsec,
            noteSpeed,
            StartPos,
            EndPos,
            enableMaiBugAdjust,
            runtimeChartOffsetMsec);
    }

    private static void Near(float actual, float expected, string name)
    {
        Near(actual, expected, name, Epsilon);
    }

    private static void Near(float actual, float expected, string name, float epsilon)
    {
        if (Math.Abs(actual - expected) > epsilon)
            throw new InvalidOperationException(
                name + $": expected {expected:F6}, actual {actual:F6}, epsilon {epsilon:F6}");
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
