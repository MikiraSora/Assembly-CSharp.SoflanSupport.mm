#pragma warning disable CS0626
using Manager;
using MonoMod;
using SoflanSupport;
using System.Collections.Generic;

namespace Monitor
{
    public class patch_SlideFan : SlideFan
    {
        [MonoModIgnore]
        private bool[] _hitIns;

        [MonoModIgnore]
        private readonly List<SlideManager.HitArea>[] _hitAreaList;

        [MonoModIgnore]
        private readonly int[] _hitIndex;

        [MonoModIgnore]
        private readonly int[] _hitSubIndex;

        public void __SoflanLogProgress()
        {
            var hit0 = Value(_hitIndex, 0);
            var hit1 = Value(_hitIndex, 1);
            var hit2 = Value(_hitIndex, 2);
            var count0 = Count(_hitAreaList, 0);
            var count1 = Count(_hitAreaList, 1);
            var count2 = Count(_hitAreaList, 2);
            var in0 = Value(_hitIns, 0);
            var in1 = Value(_hitIns, 1);
            var in2 = Value(_hitIns, 2);
            var sub0 = Value(_hitSubIndex, 0);
            var sub1 = Value(_hitSubIndex, 1);
            var sub2 = Value(_hitSubIndex, 2);
            SoflanDiagnostic.SlideProgress(
                MonitorId,
                NoteIndex,
                NotesType.getEnum(),
                hit0 + hit1 + hit2,
                count0 + count1 + count2,
                in0 || in1 || in2,
                sub0 + sub1 + sub2,
                TailMsec,
                lastWaitTime,
                JudgeResult,
                EndFlag,
                JudgeTimingDiffMsec,
                $"hit={hit0}/{count0},{hit1}/{count1},{hit2}/{count2};in={in0},{in1},{in2};sub={sub0},{sub1},{sub2}",
                "SlideFan.NoteCheck");
        }

        private static int Value(int[] values, int index)
        {
            return values != null && index >= 0 && index < values.Length ? values[index] : -1;
        }

        private static bool Value(bool[] values, int index)
        {
            return values != null && index >= 0 && index < values.Length && values[index];
        }

        private static int Count(List<SlideManager.HitArea>[] values, int index)
        {
            return values != null
                && index >= 0
                && index < values.Length
                && values[index] != null
                ? values[index].Count
                : 0;
        }
    }
}
