#pragma warning disable CS0626
using MonoMod;

namespace Monitor
{
    [MonoModPatch("global::Monitor.NoteGuide")]
    public class patch_NoteGuide : NoteGuide
    {
        public extern void orig_ReturnToBase();

        public void ReturnToBase()
        {
            HideEachGuide();
            orig_ReturnToBase();
        }
    }
}
