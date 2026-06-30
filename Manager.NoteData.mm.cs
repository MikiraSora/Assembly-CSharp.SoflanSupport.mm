#pragma warning disable CS0626
// patch_Manager.NoteData — 对应 head commit 2a7a4a4 中 Manager/NoteData.cs 的改动.
// - 新增字段 noteSoflanTimeOpt (private)
// - clear() 末尾追加 noteSoflanTimeOpt = default (orig_ 包装)
// - 新增 virtual checkVisibleInSoflan (verbatim)
using System.Collections.Generic;

namespace Manager
{
    public class patch_NoteData : NoteData
    {
        private float? noteSoflanTimeOpt;

        public extern void orig_clear();

        public void clear()
        {
            orig_clear();
            noteSoflanTimeOpt = default;
        }

        public virtual bool checkVisibleInSoflan(float soflanTime, float defaultMsec)
        {
            var appearMsec = time.msec;
            var startMsec = appearMsec - defaultMsec * 2f;

            return false;
        }
    }
}
