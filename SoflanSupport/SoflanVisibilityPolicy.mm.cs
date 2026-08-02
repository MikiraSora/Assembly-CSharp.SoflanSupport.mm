namespace SoflanSupport
{
    /// <summary>
    /// Soflan 提前可见与原版注册窗口之间的共享决策规则。
    /// </summary>
    public static class SoflanVisibilityPolicy
    {
        public static bool IsNormallyDue(
            float runtimeMsec,
            float runtimeNoteMsec,
            float normalVisibleMsec)
        {
            return runtimeMsec >= runtimeNoteMsec - normalVisibleMsec;
        }

        public static bool ShouldRegisterNote(
            bool soflanVisible,
            float runtimeMsec,
            float runtimeNoteMsec,
            float normalVisibleMsec)
        {
            return soflanVisible
                || IsNormallyDue(runtimeMsec, runtimeNoteMsec, normalVisibleMsec);
        }
    }
}
