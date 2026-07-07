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
        private int noteSoflanGroup;
        private float noteSoflanTime;
        private bool isFixedSoflanToUnifiedSpeed;
        private float fixedSoflanUnifiedSpeed;

#if DEBUG
        // --- 调试面板选中 (右键点击 Tap) ---
        // 选中状态由 SoflanPanelBehaviour._selectedNote 集中维护 (避免 patch 新增字段跨类访问的编译期鸿沟);
        // 本类通过 SoflanPanelBehaviour.IsNoteSelected(this) 查询。
        private Color _origSpriteColor;         // 选中前的原 sprite color, 取消选中时恢复
        private bool _colorSaved;
#endif

        public extern void orig_Initialize(NoteData note);

        public void Initialize(NoteData note)
        {
            orig_Initialize(note);

#if DEBUG
            // 池化复用时: 若本实例曾被选中, 清除选中 (避免复用实例仍标记为选中)
            SoflanPanelBehaviour.OnNoteReinitialized(this);
            _colorSaved = false;

            // 给视觉物件加 BoxCollider2D 供调试面板右键选中 (所有 note 类型: Tap/Break/Hold...).
            // 用 2D collider: NoteObj.localScale.z=0 会把 3D BoxCollider 压成零厚度薄片;
            // 2D 物理忽略 z, 不受影响。不手动设 size —— AddComponent 时 Unity 自动按 SpriteRenderer
            // 的 sprite bounds 适配 (手动设 sprite.bounds.size 会因它是世界空间而与局部空间 collider 错位)。
            if (NoteObj != null && NoteObj.GetComponent<Collider2D>() == null)
            {
                NoteObj.AddComponent<BoxCollider2D>();
            }
#endif

            //Soflan Support
            soflanManager = Singleton<SoflanManager>.Instance;
            isInSoflan = soflanManager.containsSoflans();
            if (isInSoflan)
            {
                noteSoflanGroup = soflanManager.getNoteSoflanGroup(NoteIndex);
                noteSoflanTime = soflanManager.ConvertAudioTimeToY_PreviewMode(AppearMsec, noteSoflanGroup);
            }
            else
            {
                noteSoflanGroup = 0;
                noteSoflanTime = AppearMsec;
            }

            var fixedNote = (patch_NoteData)note;
            isFixedSoflanToUnifiedSpeed = fixedNote.isFixedSoflanToUnifiedSpeed
                && FixedSoflan.IsSupportedTapKind(note.type.getEnum());
            fixedSoflanUnifiedSpeed = fixedNote.fixedSoflanUnifiedSpeed > 0f
                ? fixedNote.fixedSoflanUnifiedSpeed
                : FixedSoflan.DefaultUnifiedSpeed;
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

                var scale = isFixedSoflanToUnifiedSpeed
                    ? FixedSoflan.GetScaleProgress(absDiffTime, fixedSoflanUnifiedSpeed)
                    : Mathf.Clamp01((2f * DefaultMsec - GetMaiBugAdjustMSec() - absDiffTime) / DefaultMsec);
                scale *= Singleton<GamePlayManager>.Instance.GetGameScore(MonitorId).UserOption.NoteSize.GetValue();
                NoteObj.transform.localScale = new Vector3(scale, scale, 0f);
            }

#if DEBUG
            // 调试选中视觉: 选中时高亮黄 + alpha 0.5~1 呼吸; 取消选中恢复原色 (仅恢复一次).
            if (SpriteRender != null)
            {
                if (SoflanPanelBehaviour.IsNoteSelected(this))
                {
                    if (!_colorSaved) { _origSpriteColor = SpriteRender.color; _colorSaved = true; }
                    float a = Mathf.PingPong(Time.time * 2f, 0.5f) + 0.5f;  // 0.5~1 来回呼吸
                    SpriteRender.color = new Color(1f, 1f, 0f, a);           // 高亮黄
                }
                else if (_colorSaved)
                {
                    SpriteRender.color = _origSpriteColor;
                    _colorSaved = false;
                }
            }
#endif
        }

        private float GetSoflanTimeDiff()
        {
            var currentSoflanTime = soflanManager.GetCurrentSoflanTimeCached(NotesManager.GetCurrentMsec(), noteSoflanGroup);
            return noteSoflanTime - currentSoflanTime;
        }

        protected extern void orig_EndNote();

        protected void EndNote()
        {
            orig_EndNote();

#if DEBUG
            // 被选中的 note 结束时: 恢复原色 + 通知面板清选中与显示数据.
            if (SoflanPanelBehaviour.IsNoteSelected(this))
            {
                if (_colorSaved && SpriteRender != null)
                {
                    SpriteRender.color = _origSpriteColor;
                    _colorSaved = false;
                }
                SoflanPanelBehaviour.OnSelectedNoteEnded();
            }
#endif
        }

        protected extern float orig_GetNoteYPosition();

        protected virtual float GetNoteYPosition()
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
            var absDiffTime = Math.Abs(diffTime);

            var scaleStartTime = isFixedSoflanToUnifiedSpeed
                ? FixedSoflan.GetScaleStartTime(fixedSoflanUnifiedSpeed)
                : 2 * DefaultMsec - GetMaiBugAdjustMSec();
            var moveStartTime = isFixedSoflanToUnifiedSpeed
                ? FixedSoflan.GetMoveStartTime(fixedSoflanUnifiedSpeed)
                : DefaultMsec - GetMaiBugAdjustMSec();
            var fixedMotionProgress = isFixedSoflanToUnifiedSpeed
                ? FixedSoflan.GetMotionProgress(diffTime, fixedSoflanUnifiedSpeed)
                : 0f;
            var fixedScaleProgress = isFixedSoflanToUnifiedSpeed
                ? FixedSoflan.GetScaleProgress(absDiffTime, fixedSoflanUnifiedSpeed)
                : 0f;

            var speedRatio = Singleton<GamePlayManager>.Instance
                          .GetGameScore(MonitorId)
                          .UserOption
                          .GetNoteSpeed
                          .GetValue() / 150f;

            /*  强制重新计算Guide物件缩放
                diffTime = moveStartTime             0             -moveStartTime
                             ---|--------------------|--------------------|---
                  finalScale = 0.25                  1                   1.75
                              StartPos              EndPos      EndPos + (EndPos - StartPos)
             */

            var offsetYAdj = (EndPos - StartPos) * (-1f / 120f) * (speedRatio - 1f);
            var guideScaleAdj = 0; //(-1f / 120f) * (speedRatio - 1f) * 0.75f;

            /*  强制重新计算物件pos位置
                diffTime = moveStartTime             0             -moveStartTime
                             ---|--------------------|--------------------|---
                      soflanY = 120                  400                  680
                             StartPos              EndPos      EndPos + (EndPos - StartPos)
             */
            var insideY = StartPos;
            var outsideY = EndPos + (EndPos - StartPos);

            var soflanY = isFixedSoflanToUnifiedSpeed
                ? FixedSoflan.GetYFromMotionProgress(StartPos, EndPos, fixedMotionProgress)
                : MathUtils.MapValue(diffTime, -moveStartTime, moveStartTime, outsideY, insideY);
            //todo 可能有问题
            var sign = 0;// currentTime > AppearMsec ? 0 : Math.Sign(diffTime);
            var adjustedSoflanY = soflanY + sign * offsetYAdj;

            var clipedSoflanY = Mathf.Clamp(adjustedSoflanY, 120, 680);

            var moveProgress = (clipedSoflanY - StartPos) / (EndPos - StartPos);
            moveProgress = Math.Max(0, moveProgress); // always >= 0

            var guideScale = 0.75f * moveProgress;
            var adjustedGuideScale = guideScale + guideScaleAdj;
            var finalScale = 0.25f + adjustedGuideScale;

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
                    var scaleProgress = isFixedSoflanToUnifiedSpeed
                        ? fixedScaleProgress
                        : MathUtils.MapValue(absDiffTime, scaleStartTime, moveStartTime, 0, 1);
                    NoteGuideTrans.localScale = new Vector3(finalScale, finalScale, 1f);
                    GuideObj.SetAlpha(scaleProgress);
                }
            }
            else
            {
                NoteStat = NoteStatus.Move;
                if (NoteGuideTrans != null)
                {
                    NoteGuideTrans.localScale = new Vector3(finalScale, finalScale, 1f);
                    GuideObj.SetAlpha(1);
                }
            }

#if DEBUG
            // 调试面板: 选中本 note 时, 把所有计算变量导出到面板 (struct 值类型, 零堆分配).
            if (SoflanPanelBehaviour.IsNoteSelected(this))
            {
                SoflanPanelBehaviour.SelectedData = new SoflanPanelBehaviour.SelectedNoteData
                {
                    NoteIndex = NoteIndex,
                    DiffTime = diffTime,
                    AbsDiffTime = absDiffTime,
                    ScaleStartTime = scaleStartTime,
                    MoveStartTime = moveStartTime,
                    NoteStat = NoteStat,
                    MoveProgress = moveProgress,
                    FinalScale = finalScale,
                    InsideY = insideY,
                    OutsideY = outsideY,
                    SoflanY = soflanY,
                    ClipedSoflanY = clipedSoflanY,
                    IsFixedSoflanToUnifiedSpeed = isFixedSoflanToUnifiedSpeed,
                    FixedSoflanUnifiedSpeed = fixedSoflanUnifiedSpeed,
                    FixedMotionProgress = fixedMotionProgress,
                    FixedScaleProgress = fixedScaleProgress,
                };
                SoflanPanelBehaviour.HasSelectedData = true;
            }
#endif

            return clipedSoflanY;
        }
    }
}
