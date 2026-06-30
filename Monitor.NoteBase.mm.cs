#pragma warning disable CS0626
// patch_Monitor.NoteBase — 对应 head commit 2a7a4a4 中 Monitor/NoteBase.cs 的改动.
// 所有被访问的 NoteBase 成员均为 protected, patch_NoteBase : NoteBase 可直接访问, 无需公开化.
// 改动:
// - 新增字段 soflanManager / isInSoflan / noteSoflanTime (在 Initialize 中赋值)
// - Initialize() 末尾追加 soflan 初始化 (orig_ 包装)
// - NoteCheck() 末尾追加 soflan 缩放重算 (orig_ 包装)
// - EndNote() 末尾追加日志 (orig_ 包装)
// - GetNoteYPosition() 开头追加 soflan 早返回 (orig_ 包装)
// - 新增 checkSupportSoflan / GetSoflanTimeDiff / GetNoteYPosition_soflan (verbatim)
// - 放弃 DumpCurrent (依赖 GameCtrl.DumpCurrent 的 private 字段访问)
using DB;
using MAI2.Util;
using Manager;
using OngekiFumenEditor.Core.Utils;
using SoflanSupport;
using System;
using UnityEngine;

namespace Monitor
{
    public abstract class patch_NoteBase : NoteBase
    {
        private SoflanManager soflanManager;
        private bool isInSoflan;
        private float noteSoflanTime;

        public extern void orig_Initialize(NoteData note);

        public void Initialize(NoteData note)
        {
            orig_Initialize(note);

            //Soflan Support
            soflanManager = Singleton<SoflanManager>.Instance;
            isInSoflan = soflanManager.containsSoflans();
            noteSoflanTime = soflanManager.ConvertAudioTimeToY_PreviewMode(AppearMsec, soflanManager.getNoteSoflanGroup(NoteIndex));
        }

        protected extern void orig_NoteCheck();

        protected void NoteCheck()
        {
            orig_NoteCheck();

            if (isInSoflan && !EndFlag)
            {
                //recalculate scale in soflan
                /* absDiffTime数值含义:

                           scale=0       -----  2 * DefaultMsec - GetMaiBugAdjustMSec()
                                           |
                                           |
                                           |
                           scale=1       -----      DefaultMsec - GetMaiBugAdjustMSec()
                                           |
                                           |
                                           |
                           scale=1       -----      0
                */
                var absDiffTime = Math.Abs(GetSoflanTimeDiff());

                var scale = Mathf.Clamp01((2f * DefaultMsec - GetMaiBugAdjustMSec() - absDiffTime) / DefaultMsec);
                NoteObj.transform.localScale = new Vector3(scale, scale, 0f);
            }
        }

        private float GetSoflanTimeDiff()
        {
            var currentSoflanTime = soflanManager.ConvertAudioTimeToY_PreviewMode(NotesManager.GetCurrentMsec(), soflanManager.getNoteSoflanGroup(NoteIndex));
            return noteSoflanTime - currentSoflanTime;
        }

        protected extern void orig_EndNote();

        protected void EndNote()
        {
            orig_EndNote();
        }

        protected extern float orig_GetNoteYPosition();

        protected float GetNoteYPosition()
        {
            if (isInSoflan && checkSupportSoflan())
                return GetNoteYPosition_soflan();

            return orig_GetNoteYPosition();
        }

        private bool checkSupportSoflan()
        {
            switch (NoteKind.getBaseType())
            {
                case NotesTypeID.BaseDef.Tap:
                    return true;
                default:
                    return false;
            }
        }

        protected float GetNoteYPosition_soflan()
        {
            /* diffTime数值含义:
                         guideScale=0    -----   inf
                                           |
                                           |
                                           |
                         guideScale=1    -----   scaleStartTime = 2 * DefaultMsec - GetMaiBugAdjustMSec()
                                           |
                                           |
                                           |
              y=120      guideScale=1    -----   moveStartTime = DefaultMsec - GetMaiBugAdjustMSec()
                                           |
                                           |
                                           |
              y=400       scale=1        -----      0
                                           |
                                           |
                                           |
              y=680       scale=1        -----   -moveStartTime = -(DefaultMsec - GetMaiBugAdjustMSec())


            */
            var currentTime = NotesManager.GetCurrentMsec();
            var diffTime = GetSoflanTimeDiff();
            var absDiffTime = Math.Abs(GetSoflanTimeDiff());

            var scaleStartTime = 2 * DefaultMsec - GetMaiBugAdjustMSec();
            var moveStartTime = DefaultMsec - GetMaiBugAdjustMSec();

            var offsetYAdj = 0f;

            if (absDiffTime > scaleStartTime)
            {
                if (NoteGuideTrans != null)
                {
                    GuideObj.SetAlpha(0);
                }
            }
            else if (absDiffTime > moveStartTime)
            {
                NoteStat = NoteStatus.Scale;
                if (NoteGuideTrans != null)
                {
                    var scaleProgress = MathUtils.MapValue(absDiffTime, scaleStartTime, moveStartTime, 0, 1);
                    NoteGuideTrans.localScale = new Vector3(0.25f, 0.25f, 1f);
                    GuideObj.SetAlpha(scaleProgress);
                }
            }
            else
            {
                if (NoteGuideTrans != null)
                {
                    GuideObj.SetAlpha(1);
                }
            }

            NoteStat = NoteStatus.Move;
            var speedRatio = Singleton<GamePlayManager>.Instance
                          .GetGameScore(MonitorId)
                          .UserOption
                          .GetNoteSpeed
                          .GetValue() / 150f;

            /*  强制重新计算Guide物件缩放
                diffTime = moveStartTime             0             -moveStartTime
                             ---|--------------------|--------------------|---
                  finalScale = 0.25                  1                   0.175
                             StartPos              EndPos      EndPos + (EndPos - StartPos)
             */

            offsetYAdj = (EndPos - StartPos) * (-1f / 120f) * (speedRatio - 1f);
            var guideScaleAdj = 0; //(-1f / 120f) * (speedRatio - 1f) * 0.75f;

            var moveProgress = MathUtils.MapValue(diffTime, 0, moveStartTime, 1, 0, false);
            moveProgress = Math.Max(0, moveProgress); // always >= 0

            var guideScale = 0.75f * moveProgress;
            var adjustedGuideScale = guideScale + guideScaleAdj;
            var finalScale = 0.25f + adjustedGuideScale;

            if (NoteGuideTrans != null)
            {
                NoteGuideTrans.localScale = new Vector3(finalScale, finalScale, 1f);
                GuideObj.SetAlpha(1f);
            }

            /*  强制重新计算物件pos位置
                diffTime = moveStartTime             0             -moveStartTime
                             ---|--------------------|--------------------|---
                     soflanY = 120                  400                  680
                             StartPos              EndPos      EndPos + (EndPos - StartPos)
             */
            var insideY = StartPos;
            var outsideY = EndPos + (EndPos - StartPos);

            var soflanY = MathUtils.MapValue(diffTime, -moveStartTime, moveStartTime, outsideY, insideY);
            //todo 可能有问题
            var sign = 0;// currentTime > AppearMsec ? 0 : Math.Sign(diffTime);
            var adjustedSoflanY = soflanY + sign * offsetYAdj;

            var clipedSoflanY = Mathf.Clamp(adjustedSoflanY, 120, 680);
            return clipedSoflanY;
        }
    }
}
