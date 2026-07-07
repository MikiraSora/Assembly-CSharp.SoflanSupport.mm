using Manager;
using UnityEngine;

namespace SoflanSupport
{
    public static class FixedSoflan
    {
        public const float DefaultUnifiedSpeed = 600f;

        public static bool IsSupportedTapKind(NotesTypeID.Def noteKind)
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
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsEnabledForNote(NoteData note)
        {
            if (note == null)
                return false;

            var fixedNote = (patch_NoteData)note;
            return fixedNote.isFixedSoflanToUnifiedSpeed
                && fixedNote.fixedSoflanUnifiedSpeed > 0f
                && IsSupportedTapKind(note.type.getEnum());
        }

        public static float GetUnifiedSpeed(NoteData note)
        {
            var speed = ((patch_NoteData)note).fixedSoflanUnifiedSpeed;
            return speed > 0f ? speed : DefaultUnifiedSpeed;
        }

        public static float GetDefaultMsec(float unifiedSpeed)
        {
            return 240000f / unifiedSpeed;
        }

        public static float GetMaiBugAdjustMSec(float unifiedSpeed)
        {
            float speedRatio = unifiedSpeed / 150f;
            return (speedRatio - 1f) * (-0.5f / speedRatio) * 1.6f * 1000f / 60f;
        }

        public static float GetMoveStartTime(float unifiedSpeed)
        {
            return GetDefaultMsec(unifiedSpeed) - GetMaiBugAdjustMSec(unifiedSpeed);
        }

        public static float GetScaleStartTime(float unifiedSpeed)
        {
            return 2f * GetDefaultMsec(unifiedSpeed) - GetMaiBugAdjustMSec(unifiedSpeed);
        }

        public static float GetVisibleMsec(float unifiedSpeed)
        {
            return GetDefaultMsec(unifiedSpeed) * 2f;
        }

        public static float GetMotionProgress(float diffTime, float unifiedSpeed)
        {
            float moveStartTime = GetMoveStartTime(unifiedSpeed);
            return Mathf.Clamp01((moveStartTime - diffTime) / (2f * moveStartTime));
        }

        public static float GetScaleProgress(float absDiffTime, float unifiedSpeed)
        {
            return Mathf.Clamp01((GetScaleStartTime(unifiedSpeed) - absDiffTime) / GetDefaultMsec(unifiedSpeed));
        }

        public static float GetYFromMotionProgress(float startPos, float endPos, float motionProgress)
        {
            float outsideY = endPos + (endPos - startPos);
            return Mathf.Lerp(startPos, outsideY, motionProgress);
        }
    }
}
