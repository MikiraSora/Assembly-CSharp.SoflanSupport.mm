#pragma warning disable CS0626
using MonoMod;
#if DEBUG
using SoflanSupport;
using System;
#endif

namespace Monitor
{
    [MonoModPatch("global::Monitor.NoteGuide")]
    public class patch_NoteGuide : NoteGuide
    {
#if DEBUG
        private extern void orig_Awake();

        private void Awake()
        {
            orig_Awake();
            GuideDiagnostics.OnGuideAwake(this);
        }

        private void OnEnable()
        {
            GuideDiagnostics.OnGuideEnabled(this);
        }

        private void OnDisable()
        {
            GuideDiagnostics.OnGuideDisabled(this);
        }

        public extern void orig_Initialize(int angle, int eachIndex);

        public void Initialize(int angle, int eachIndex)
        {
            GuideDiagnostics.OnGuideInitializeBefore(this, angle, eachIndex);
            try
            {
                orig_Initialize(angle, eachIndex);
            }
            catch (Exception exception)
            {
                GuideDiagnostics.OnGuideInitializeFailed(this, angle, eachIndex, exception);
                throw;
            }
            GuideDiagnostics.OnGuideInitializeAfter(this, angle, eachIndex);
        }

        public extern void orig_HideEachGuide();

        public void HideEachGuide()
        {
            GuideDiagnostics.OnGuideHideEachBefore(this);
            orig_HideEachGuide();
            GuideDiagnostics.OnGuideHideEachAfter(this);
        }
#endif

        public extern void orig_ReturnToBase();

        public void ReturnToBase()
        {
#if DEBUG
            GuideDiagnostics.OnGuideReturnBefore(this);
#endif
            HideEachGuide();
            orig_ReturnToBase();
#if DEBUG
            GuideDiagnostics.OnGuideReturnAfter(this);
#endif
        }
    }
}
