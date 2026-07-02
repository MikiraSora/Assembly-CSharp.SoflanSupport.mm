#pragma warning disable CS0626
using DB;
using MAI2.Util;
using Manager;
using OngekiFumenEditor.Core.Utils;
using SoflanSupport;
using UnityEngine;

namespace Monitor
{
    public class patch_HoldNote : HoldNote
    {
        private SoflanManager holdSoflanManager;
        private bool holdIsInSoflan;
        private int holdSoflanGroup;
        private float holdHeadSoflanTime;
        private float holdTailSoflanTime;

        public extern void orig_Initialize(NoteData note);

        public void Initialize(NoteData note)
        {
            orig_Initialize(note);

            holdSoflanManager = Singleton<SoflanManager>.Instance;
            holdIsInSoflan = holdSoflanManager.containsSoflans();
            if (holdIsInSoflan)
            {
                holdSoflanGroup = holdSoflanManager.getNoteSoflanGroup(NoteIndex);
                holdHeadSoflanTime = holdSoflanManager.ConvertAudioTimeToY_PreviewMode(AppearMsec, holdSoflanGroup);
                holdTailSoflanTime = holdSoflanManager.ConvertAudioTimeToY_PreviewMode(TailMsec, holdSoflanGroup);
            }
            else
            {
                holdSoflanGroup = 0;
                holdHeadSoflanTime = AppearMsec;
                holdTailSoflanTime = TailMsec;
            }
        }

        public extern void orig_Execute();

        public void Execute()
        {
            if (holdIsInSoflan && CheckSupportSoflan())
            {
                float currentMsec = NotesManager.GetCurrentMsec();
                float currentSoflanTime = holdSoflanManager.GetCurrentSoflanTimeCached(currentMsec, holdSoflanGroup);

                float headDiffTime = holdHeadSoflanTime - currentSoflanTime;
                float tailDiffTime = holdTailSoflanTime - currentSoflanTime;

                ExecuteSoflanVisual(headDiffTime, tailDiffTime, currentMsec);
                orig_NoteCheck();
                ApplySoflanScale(headDiffTime);
                return;
            }

            orig_Execute();
        }

        protected extern void orig_NoteCheck();

        protected void NoteCheck()
        {
            orig_NoteCheck();

            if (holdIsInSoflan && CheckSupportSoflan())
            {
                float currentSoflanTime = holdSoflanManager.GetCurrentSoflanTimeCached(
                    NotesManager.GetCurrentMsec(),
                    holdSoflanGroup);

                ApplySoflanScale(holdHeadSoflanTime - currentSoflanTime);
            }
        }

        private void ExecuteSoflanVisual(float headDiffTime, float tailDiffTime, float currentMsec)
        {
            if (EndFlag)
            {
                return;
            }

            UpdateHoldEffectVisual();

            float moveStartTime = DefaultMsec - GetMaiBugAdjustMSec();
            float scaleStartTime = 2f * DefaultMsec - GetMaiBugAdjustMSec();
            float headY = GetHoldHeadYPositionSoflan(headDiffTime, moveStartTime, scaleStartTime);

            if (headY >= EndPos)
            {
                headY = EndPos;
            }

            if (headDiffTime > moveStartTime)
            {
                SpriteRender.size = new Vector2(SpriteRender.size.x, DefaultHeight);
                NoteObj.transform.localPosition = new Vector3(0f, headY, GetBaseZPosition());
                EndPointObj.transform.localPosition = new Vector3(0f, headY, GetBaseZPosition());
            }
            else
            {
                if (TailMsec <= currentMsec)
                {
                    NoteObj.transform.localPosition = new Vector3(0f, EndPos, GetBaseZPosition());
                    SpriteRender.size = new Vector2(SpriteRender.size.x, DefaultHeight);
                }
                else if (tailDiffTime <= moveStartTime)
                {
                    if (!EndPointObj.activeSelf)
                    {
                        EndPointObj.SetActive(value: true);
                    }

                    float tailY = GetHoldEndpointYPositionSoflan(tailDiffTime, moveStartTime);
                    float bodyLength = Mathf.Max(0f, headY - tailY);

                    SpriteRender.size = new Vector2(SpriteRender.size.x, bodyLength + DefaultHeight);
                    NoteObj.transform.localPosition = new Vector3(0f, headY - bodyLength / 2f, GetBaseZPosition());
                    EndPointObj.transform.localPosition = new Vector3(0f, tailY, GetBaseZPosition());
                }
                else
                {
                    float bodyLength = Mathf.Max(0f, headY - StartPos);

                    SpriteRender.size = new Vector2(SpriteRender.size.x, bodyLength + DefaultHeight);
                    NoteObj.transform.localPosition = new Vector3(0f, headY - bodyLength / 2f, GetBaseZPosition());
                    EndPointObj.transform.localPosition = new Vector3(0f, StartPos, GetBaseZPosition());
                }
            }

            SpriteRenderEx.size = SpriteRender.size;
            EffectSprite.size = SpriteRender.size;
        }

        private float GetHoldHeadYPositionSoflan(float diffTime, float moveStartTime, float scaleStartTime)
        {
            if (diffTime > scaleStartTime)
            {
                if (NoteGuideTrans != null)
                {
                    GuideObj.SetAlpha(0f);
                }
            }
            else if (diffTime > moveStartTime)
            {
                NoteStat = NoteStatus.Scale;
                if (NoteGuideTrans != null)
                {
                    float scaleProgress = MathUtils.MapValue(diffTime, scaleStartTime, moveStartTime, 0f, 1f);
                    NoteGuideTrans.localScale = new Vector3(0.25f, 0.25f, 1f);
                    GuideObj.SetAlpha(scaleProgress);
                }
            }
            else
            {
                NoteStat = NoteStatus.Move;
                if (NoteGuideTrans != null)
                {
                    float moveProgress = MathUtils.MapValue(diffTime, 0f, moveStartTime, 1f, 0f, false);
                    moveProgress = Mathf.Max(0f, moveProgress);
                    float finalScale = 0.25f + 0.75f * moveProgress;
                    float guideScale = !GuideStop || finalScale <= 1f ? finalScale : 1f;

                    NoteGuideTrans.localScale = new Vector3(guideScale, guideScale, 1f);
                    GuideObj.SetAlpha(1f);
                }
            }

            return GetHoldYPositionSoflan(diffTime, moveStartTime);
        }

        private float GetHoldEndpointYPositionSoflan(float diffTime, float moveStartTime)
        {
            return GetHoldYPositionSoflan(diffTime, moveStartTime);
        }

        private float GetHoldYPositionSoflan(float diffTime, float moveStartTime)
        {
            float insideY = StartPos;
            float outsideY = EndPos + (EndPos - StartPos);
            float y = MathUtils.MapValue(diffTime, -moveStartTime, moveStartTime, outsideY, insideY);

            return Mathf.Clamp(y, StartPos, EndPos);
        }

        private void ApplySoflanScale(float headDiffTime)
        {
            if (EndFlag)
            {
                return;
            }

            float moveStartTime = DefaultMsec - GetMaiBugAdjustMSec();
            float scaleStartTime = 2f * DefaultMsec - GetMaiBugAdjustMSec();
            float scale = headDiffTime <= moveStartTime
                ? 1f
                : Mathf.Clamp01((scaleStartTime - headDiffTime) / DefaultMsec);
            float noteSize = Singleton<GamePlayManager>.Instance.GetGameScore(MonitorId).UserOption.NoteSize.GetValue();

            NoteObj.transform.localScale = new Vector3(scale * noteSize, scale, 0f);
            EndPointObj.transform.localScale = new Vector3(scale, scale, 1f);
        }

        private void UpdateHoldEffectVisual()
        {
            if (HoldBodyOnFlg)
            {
                EffectSprite.color = new Color(1f, 1f, 1f, Mathf.Sin(GameManager.GetGameFrame() * 0.4f) * 0.25f + 0.25f);
            }
            else
            {
                EffectSprite.color = new Color(1f, 1f, 1f, 0f);
            }
        }

        private bool CheckSupportSoflan()
        {
            switch (NoteKind.getBaseType())
            {
                case NotesTypeID.BaseDef.Hold:
                    return true;
                default:
                    return false;
            }
        }
    }
}
