#pragma warning disable CS0626
using MAI2.Util;
using Manager;
using MonoMod;
using SoflanSupport;
using System.Collections.Generic;

namespace Monitor
{
    public class patch_SlideRoot : SlideRoot
    {
        [MonoModIgnore]
        private List<SlideManager.HitArea> _hitAreaList;

        [MonoModIgnore]
        private int _hitIndex;

        [MonoModIgnore]
        private bool _hitIn;

        [MonoModIgnore]
        private int _hitSubIndex;

        public void __SoflanLogInitialize(NoteData note)
        {
            var manager = Singleton<SoflanManager>.Instance;
            var group = manager.getNoteSoflanGroup(MonitorId, note);
            var rawNoteMsec = manager.getNoteAudioMsecForSoflan(MonitorId, note);
            var noteSoflanTime = manager.containsSoflans(MonitorId)
                ? manager.ConvertAudioTimeToY_PreviewMode(MonitorId, rawNoteMsec, group)
                : AppearMsec;
            SoflanDiagnostic.ObjectInitialized(
                MonitorId,
                note,
                AppearMsec,
                TailMsec,
                DefaultMsec,
                group,
                false,
                FixedSoflan.DefaultUnifiedSpeed,
                noteSoflanTime,
                0f,
                "SlideRoot.Initialize");
        }

        public void __SoflanLogProgress()
        {
            SoflanDiagnostic.SlideProgress(
                MonitorId,
                NoteIndex,
                NotesType.getEnum(),
                _hitIndex,
                _hitAreaList?.Count ?? 0,
                _hitIn,
                _hitSubIndex,
                TailMsec,
                lastWaitTime,
                JudgeResult,
                EndFlag,
                JudgeTimingDiffMsec,
                string.Empty,
                "SlideRoot.NoteCheck");
        }
    }
}
