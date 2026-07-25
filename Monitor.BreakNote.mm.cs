#pragma warning disable CS0626
using DB;
using MAI2.Util;
using Manager;
using SoflanSupport;
using System;
using UnityEngine;

namespace Monitor
{
    public class patch_BreakNote : BreakNote
    {
        private SoflanManager breakSoflanManager;
        private bool breakIsInSoflan;
        private int breakSoflanGroup;
        private float breakNoteSoflanTime;
        private bool breakIsFixedSoflanToUnifiedSpeed;
        private float breakFixedSoflanUnifiedSpeed;
        private float breakVisualDefaultMsec;
        private float breakMaiBugAdjustMsec;

        public extern void orig_Initialize(NoteData note);

        public void Initialize(NoteData note)
        {
            orig_Initialize(note);

            breakSoflanManager = Singleton<SoflanManager>.Instance;
            breakIsInSoflan = breakSoflanManager.containsSoflans(MonitorId);
            if (breakIsInSoflan)
            {
                breakSoflanGroup = breakSoflanManager.getNoteSoflanGroup(MonitorId, NoteIndex);
                var noteAudioMsec = breakSoflanManager.getNoteAudioMsecForSoflan(
                    MonitorId,
                    NoteIndex,
                    AppearMsec);
                breakNoteSoflanTime = breakSoflanManager.ConvertAudioTimeToY_PreviewMode(
                    MonitorId,
                    noteAudioMsec,
                    breakSoflanGroup);
            }
            else
            {
                breakSoflanGroup = 0;
                breakNoteSoflanTime = AppearMsec;
            }

            var fixedNote = (patch_NoteData)note;
            breakIsFixedSoflanToUnifiedSpeed = fixedNote.isFixedSoflanToUnifiedSpeed
                && FixedSoflan.IsSupportedTapKind(note.type.getEnum());
            breakFixedSoflanUnifiedSpeed = fixedNote.fixedSoflanUnifiedSpeed > 0f
                ? fixedNote.fixedSoflanUnifiedSpeed
                : FixedSoflan.DefaultUnifiedSpeed;
            breakVisualDefaultMsec = breakIsFixedSoflanToUnifiedSpeed
                ? FixedSoflan.GetDefaultMsec(breakFixedSoflanUnifiedSpeed)
                : DefaultMsec;
            breakMaiBugAdjustMsec = SoflanVisualTiming.GetMaiBugAdjustMsec(
                note.type.getEnum(),
                2f * breakVisualDefaultMsec);
        }

        protected extern void orig_NoteCheck();

        protected void NoteCheck()
        {
            orig_NoteCheck();

            if (breakIsInSoflan && CheckSupportSoflan() && !EndFlag)
            {
                var absDiffTime = Math.Abs(GetBreakSoflanTimeDiff());
                var scale = Mathf.Clamp01(
                    (2f * breakVisualDefaultMsec - absDiffTime) / breakVisualDefaultMsec);
                scale *= Singleton<GamePlayManager>.Instance.GetGameScore(MonitorId).UserOption.NoteSize.GetValue();
                NoteObj.transform.localScale = new Vector3(scale, scale, 0f);
            }
        }

        private float GetBreakSoflanTimeDiff()
        {
            var currentSoflanTime = breakSoflanManager.GetCurrentSoflanTimeWithOffsetsCached(
                MonitorId,
                NotesManager.GetCurrentMsec(),
                breakMaiBugAdjustMsec,
                breakSoflanGroup);
            return breakNoteSoflanTime - currentSoflanTime;
        }

        private bool CheckSupportSoflan()
        {
            switch (NoteKind.getBaseType())
            {
                case NotesTypeID.BaseDef.Tap:
                    return true;
                default:
                    return false;
            }
        }
    }
}
