#pragma warning disable CS0626
using MAI2.Util;
using Manager;
using SoflanSupport;
using UnityEngine;

namespace Monitor
{
    public class patch_TouchNoteB : TouchNoteB
    {
        private SoflanManager touchSoflanManager;
        private bool touchIsInSoflan;
        private int touchSoflanGroup;
        private float touchNoteSoflanTime;

        public extern void orig_Initialize(NoteData note);

        public override void Initialize(NoteData note)
        {
            orig_Initialize(note);

            touchSoflanManager = Singleton<SoflanManager>.Instance;
            touchIsInSoflan = touchSoflanManager.containsSoflans();
            if (touchIsInSoflan)
            {
                touchSoflanGroup = touchSoflanManager.getNoteSoflanGroup(NoteIndex);
                touchNoteSoflanTime = touchSoflanManager.ConvertAudioTimeToY_PreviewMode(AppearMsec, touchSoflanGroup);
            }
            else
            {
                touchSoflanGroup = 0;
                touchNoteSoflanTime = AppearMsec;
            }
        }

        protected extern float orig_GetNoteYPosition();

        protected override float GetNoteYPosition()
        {
            if (touchIsInSoflan && CheckSupportSoflan())
                return GetTouchNoteYPositionSoflan();

            return orig_GetNoteYPosition();
        }

        private bool CheckSupportSoflan()
        {
            switch (NoteKind.getBaseType())
            {
                case NotesTypeID.BaseDef.Tap:
                    return true;
                default:
                    return false;
            }
        }

        private float GetTouchNoteYPositionSoflan()
        {
            float currentSoflanTime = touchSoflanManager.GetCurrentSoflanTimeCached(
                NotesManager.GetCurrentMsec(),
                touchSoflanGroup);
            float touchDispTime = DefaultMsec * 0.25f;
            float soflanStartTime = touchNoteSoflanTime - DefaultMsec - touchDispTime;

            NoteStat = NoteStatus.Move;
            if (currentSoflanTime <= soflanStartTime)
            {
                NoteStat = NoteStatus.Init;
                SpriteRender.color = new Color(1f, 1f, 1f, 0f);
                for (int i = 0; i < DefaultCorlsPos.Length; i++)
                {
                    ColorsObject[i].color = new Color(1f, 1f, 1f, 0f);
                }
                return 0f;
            }

            if (currentSoflanTime <= soflanStartTime + touchDispTime)
            {
                NoteStat = NoteStatus.Scale;
                float fadeProgress = (currentSoflanTime - soflanStartTime) / touchDispTime;
                if (fadeProgress > 1f)
                {
                    fadeProgress = 1f;
                }
                SpriteRender.color = new Color(1f, 1f, 1f, 1f);
                for (int j = 0; j < DefaultCorlsPos.Length; j++)
                {
                    ColorsObject[j].color = new Color(1f, 1f, 1f, fadeProgress);
                }
                return fadeProgress;
            }

            NoteStat = NoteStatus.Move;
            float gatherProgress = (currentSoflanTime - (soflanStartTime + touchDispTime) + DispAdjustFlame * 16.666666f) / DefaultMsec;
            gatherProgress = 3.5f * Mathf.Pow(gatherProgress, 4f)
                           - 3.75f * Mathf.Pow(gatherProgress, 3f)
                           + 1.45f * Mathf.Pow(gatherProgress, 2f)
                           - 0.05f * Mathf.Pow(gatherProgress, 1f)
                           + 0.0005f;
            if (gatherProgress > 1f)
            {
                gatherProgress = 1f;
            }
            SpriteRender.color = new Color(1f, 1f, 1f, 1f);
            for (int k = 0; k < DefaultCorlsPos.Length; k++)
            {
                ColorsObject[k].transform.localPosition = Vector3.Lerp(DefaultCorlsPos[k], Vector3.zero, gatherProgress);
                ColorsObject[k].color = new Color(1f, 1f, 1f, 1f);
            }
            if (null != NoticeObject)
            {
                NoticeObject.SetActive(touchNoteSoflanTime <= currentSoflanTime);
            }
            return 1f;
        }
    }
}
