namespace SoflanSupport
{
    /// <summary>
    /// 原版运行时毫秒轴与 MA2 原始毫秒轴之间的统一换算。
    /// Soflan 的 BPM/SFL/TGrid 都位于 MA2 原始时间轴，因此运行时当前时间进入
    /// Soflan 积分前必须先移除 GetAdjustMSec()，再应用可选的视觉音频偏移。
    /// </summary>
    public static class SoflanRuntimeTime
    {
        public static float NormalizeRuntimeChartOffsetMsec(float runtimeChartOffsetMsec)
        {
            return IsFinite(runtimeChartOffsetMsec) ? runtimeChartOffsetMsec : 0f;
        }

        public static float ToRawChartAudioMsec(
            float runtimeCurrentMsec,
            float runtimeChartOffsetMsec,
            float visualAudioOffsetMsec)
        {
            if (!IsFinite(runtimeCurrentMsec))
                return 0f;

            var normalizedChartOffsetMsec = NormalizeRuntimeChartOffsetMsec(runtimeChartOffsetMsec);
            var normalizedVisualOffsetMsec = IsFinite(visualAudioOffsetMsec)
                ? visualAudioOffsetMsec
                : 0f;
            var rawChartAudioMsec = runtimeCurrentMsec
                - normalizedChartOffsetMsec
                + normalizedVisualOffsetMsec;

            // TGridCalculator 对负音频时间没有有效 BPM timing point；必须在完成
            // t - GetAdjustMSec + visualOffset 后再钳制，不能提前钳制运行时钟。
            return !IsFinite(rawChartAudioMsec) || rawChartAudioMsec < 0f
                ? 0f
                : rawChartAudioMsec;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
