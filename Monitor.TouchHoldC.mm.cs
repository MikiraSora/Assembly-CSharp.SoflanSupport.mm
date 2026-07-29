#pragma warning disable CS0626
using DB;
using Manager;
using SoflanSupport;

namespace Monitor
{
    public class patch_TouchHoldC : TouchHoldC
    {
        private SoflanDiagnostic.JudgeProbe __SoflanBeginNoteCheckDiagnostics()
        {
            return SoflanDiagnostic.BeforeJudgeCheck(
                MonitorId,
                NoteIndex,
                NoteKind,
                ButtonId,
                SoflanDiagnostic.GetTouchAreaIndex(TouchArea, ButtonId),
                false,
                AppearMsec,
                TailMsec,
                JudgeType,
                GetJudgeStartMsec(),
                GetJudgeEndMsec(),
                JudgeResult,
                GetJudgeHeadResult(),
                EndFlag,
                IsJudgeNote(),
                JudgeTimingDiffMsec,
                "TouchHoldC.NoteCheck");
        }

        private void __SoflanEndNoteCheckDiagnostics(
            SoflanDiagnostic.JudgeProbe diagnosticProbe)
        {
            SoflanDiagnostic.AfterJudgeCheck(
                diagnosticProbe,
                JudgeResult,
                GetJudgeHeadResult(),
                EndFlag,
                JudgeTimingDiffMsec);
            SoflanDiagnostic.HoldState(
                MonitorId,
                NoteIndex,
                GetJudgeHeadResult(),
                HeadJudged,
                BodyOn,
                LastHoldState,
                TriggerOn,
                HoldReleaseTime,
                EndFlag,
                "TouchHoldC.NoteCheck");
        }
    }
}
