namespace SoflanSupport
{
    /// <summary>
    /// 原版 NoteBase.GetMaiBugAdjustMSec 的纯计算版本。
    /// 返回值是音频毫秒偏移；Soflan 代码必须先把偏移应用到音频时间，
    /// 再把该时间映射到 Soflan Y，不能把它直接当作 Soflan Y 距离。
    /// </summary>
    public static class MaiBugAdjust
    {
        public const float BaseNoteSpeed = 150f;
        public const float DefaultMsecNumerator = 240000f;

        public static float Calculate(float noteSpeed)
        {
            if (!IsPositiveFinite(noteSpeed))
                return 0f;

            float speedRatio = noteSpeed / BaseNoteSpeed;
            return (speedRatio - 1f) * (-0.5f / speedRatio) * 1.6f * 1000f / 60f;
        }

        public static float Calculate(float noteSpeed, bool enabled)
        {
            return enabled ? Calculate(noteSpeed) : 0f;
        }

        public static float CalculateFromDefaultMsec(float defaultMsec)
        {
            if (!IsPositiveFinite(defaultMsec))
                return 0f;

            return Calculate(DefaultMsecNumerator / defaultMsec);
        }

        public static float CalculateFromDefaultMsec(float defaultMsec, bool enabled)
        {
            return enabled ? CalculateFromDefaultMsec(defaultMsec) : 0f;
        }

        public static float CalculateFromVisibleMsec(float visibleMsec)
        {
            if (!IsPositiveFinite(visibleMsec))
                return 0f;

            return CalculateFromDefaultMsec(visibleMsec * 0.5f);
        }

        public static float CalculateFromVisibleMsec(float visibleMsec, bool enabled)
        {
            return enabled ? CalculateFromVisibleMsec(visibleMsec) : 0f;
        }

        public static float ApplyToAudioMsec(float audioMsec, float adjustMsec)
        {
            float adjustedAudioMsec = audioMsec + adjustMsec;
            // TGridCalculator 对负音频时间没有有效 BPM timing point；谱面起点前统一钳到 0，
            // 避免开场几毫秒因负 MaiBug 偏移得到 null TGrid。
            return adjustedAudioMsec < 0f ? 0f : adjustedAudioMsec;
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
