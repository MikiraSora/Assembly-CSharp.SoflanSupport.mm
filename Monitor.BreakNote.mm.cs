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
        private float breakNoteSoflanTime;

        public extern void orig_Initialize(NoteData note);

        public void Initialize(NoteData note)
        {
            orig_Initialize(note);

            breakSoflanManager = Singleton<SoflanManager>.Instance;
            breakIsInSoflan = breakSoflanManager.containsSoflans();
            breakNoteSoflanTime = breakIsInSoflan
                ? breakSoflanManager.ConvertAudioTimeToY_PreviewMode(AppearMsec, breakSoflanManager.getNoteSoflanGroup(NoteIndex))
                : AppearMsec;
        }

        protected extern void orig_NoteCheck();

        protected void NoteCheck()
        {
            orig_NoteCheck();

            if (breakIsInSoflan && CheckSupportSoflan() && !EndFlag)
            {
                var absDiffTime = Math.Abs(GetBreakSoflanTimeDiff());
                var scale = Mathf.Clamp01((2f * DefaultMsec - GetMaiBugAdjustMSec() - absDiffTime) / DefaultMsec);
                NoteObj.transform.localScale = new Vector3(scale, scale, 0f);
            }
        }

        private float GetBreakSoflanTimeDiff()
        {
            var currentSoflanTime = breakSoflanManager.ConvertAudioTimeToY_PreviewMode(
                NotesManager.GetCurrentMsec(),
                breakSoflanManager.getNoteSoflanGroup(NoteIndex));
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
