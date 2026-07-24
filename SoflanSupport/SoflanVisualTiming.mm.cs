using Manager;

namespace SoflanSupport
{
    /// <summary>
    /// Soflan 视觉时间轴与原版 NoteBase 视觉时序之间的共享规则。
    /// </summary>
    public static class SoflanVisualTiming
    {
        public static bool UsesMaiBugAdjustment(NotesTypeID.Def noteKind)
        {
            switch (noteKind)
            {
                case NotesTypeID.Def.Begin:
                case NotesTypeID.Def.Break:
                case NotesTypeID.Def.ExTap:
                case NotesTypeID.Def.Star:
                case NotesTypeID.Def.BreakStar:
                case NotesTypeID.Def.ExStar:
                case NotesTypeID.Def.ExBreakTap:
                case NotesTypeID.Def.ExBreakStar:
                case NotesTypeID.Def.Hold:
                case NotesTypeID.Def.ExHold:
                case NotesTypeID.Def.BreakHold:
                case NotesTypeID.Def.ExBreakHold:
                    return true;
                default:
                    return false;
            }
        }

        public static float GetMaiBugAdjustMsec(NotesTypeID.Def noteKind, float visibleMsec)
        {
            return UsesMaiBugAdjustment(noteKind)
                ? MaiBugAdjust.CalculateFromVisibleMsec(
                    visibleMsec,
                    Setting.EnableSoflanMaiBugAdjust)
                : 0f;
        }
    }
}
