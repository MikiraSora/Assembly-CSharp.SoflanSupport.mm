#pragma warning disable CS0626
using DB;
using MonoMod;
using SoflanSupport;

namespace Manager
{
    [MonoModPatch("global::Manager.GameScoreList")]
    public class SoflanGameScoreHooks
    {
        [MonoModIgnore]
        private readonly int _monitorIndex;

        [MonoModIgnore]
        public bool IsTrackSkip;

        public bool __SoflanGetIsJudged(int index)
        {
            return SoflanDiagnostic.GetIsJudged(_monitorIndex, index);
        }

        public void __SoflanScoreResult(
            int index,
            NoteScore.EScoreType kind,
            NoteJudge.ETiming timing,
            bool wasJudged)
        {
            SoflanDiagnostic.ScoreResult(
                _monitorIndex,
                index,
                kind,
                timing,
                IsTrackSkip,
                wasJudged);
        }
    }
}
