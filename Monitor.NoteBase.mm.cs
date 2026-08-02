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
        private float visualDefaultMsec;
        private float maiBugAdjustMsec;

#if DEBUG
        // --- 调试面板选中 (右键点击 Tap) ---
        // 选中状态由 SoflanPanelBehaviour._selectedNote 集中维护 (避免 patch 新增字段跨类访问的编译期鸿沟);
        // 本类通过 SoflanPanelBehaviour.IsNoteSelected(this) 查询。
        private Color _origSpriteColor;         // 选中前的原 sprite color, 取消选中时恢复
        private bool _colorSaved;
        private float _rawCurrentSoflanTime;
        private float _adjustedCurrentSoflanTime;
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
            isInSoflan = soflanManager.containsSoflans(MonitorId);
            if (isInSoflan)
            {
                noteSoflanGroup = soflanManager.getNoteSoflanGroup(MonitorId, NoteIndex);
                var noteAudioMsec = soflanManager.getNoteAudioMsecForSoflan(
                    MonitorId,
                    NoteIndex,
                    AppearMsec);
                noteSoflanTime = soflanManager.ConvertAudioTimeToY_PreviewMode(
                    MonitorId,
                    noteAudioMsec,
                    noteSoflanGroup);
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
            visualDefaultMsec = isFixedSoflanToUnifiedSpeed
                ? FixedSoflan.GetDefaultMsec(fixedSoflanUnifiedSpeed)
                : DefaultMsec;
            maiBugAdjustMsec = SoflanVisualTiming.GetMaiBugAdjustMsec(
                note.type.getEnum(),
                2f * visualDefaultMsec);

            RestoreOriginalLaneJudgeOrder();

            SoflanDiagnostic.ObjectInitialized(
                MonitorId,
                note,
                AppearMsec,
                TailMsec,
                visualDefaultMsec,
                noteSoflanGroup,
                isFixedSoflanToUnifiedSpeed,
                fixedSoflanUnifiedSpeed,
                noteSoflanTime,
                maiBugAdjustMsec,
                "NoteBase.Initialize");

        }

        private void RestoreOriginalLaneJudgeOrder()
        {
            if (!isInSoflan)
                return;

            var noteTransform = transform;
            var laneTransform = noteTransform.parent;
            if (laneTransform == null)
                return;

            // 原版按 NoteData/indexNote 顺序注册，并依赖 launcher 的 sibling 顺序
            // 决定同 lane 里哪个物件能判定。反向 Soflan 会打乱注册时间，因此在
            // 物件创建后恢复与原版 NoteIndex 一致的 sibling 顺序。
            var siblingIndex = SoflanJudgeOrder.GetSiblingIndex(
                NoteIndex,
                laneTransform.childCount,
                index =>
                {
                    var siblingTransform = laneTransform.GetChild(index);
                    if (siblingTransform == noteTransform)
                        return null;

                    var siblingNote = siblingTransform.GetComponent<NoteBase>();
                    return siblingNote != null && siblingNote.gameObject.activeSelf
                        ? siblingNote.GetNoteIndex()
                        : (int?)null;
                });
            noteTransform.SetSiblingIndex(siblingIndex);
        }

        protected extern void orig_NoteCheck();

        protected void NoteCheck()
        {
            var diagnosticProbe = SoflanDiagnostic.BeforeJudgeCheck(
                MonitorId,
                NoteIndex,
                NoteKind,
                ButtonId,
                -1,
                true,
                AppearMsec,
                TailMsec,
                JudgeType,
                GetJudgeStartMsec(),
                GetJudgeEndMsec(),
                JudgeResult,
                NoteJudge.ETiming.End,
                EndFlag,
                IsJudgeNote(),
                JudgeTimingDiffMsec,
                "NoteBase.NoteCheck");
            orig_NoteCheck();
            SoflanDiagnostic.AfterJudgeCheck(
                diagnosticProbe,
                JudgeResult,
                NoteJudge.ETiming.End,
                EndFlag,
                JudgeTimingDiffMsec);

            if (isInSoflan && checkSupportSoflan() && !EndFlag)
            {
                //recalculate scale in soflan
                /* absDiffTime数值含义:

                           scale=0       -----  2 * visualDefaultMsec
                                           |
                                           |
                                           |
                           scale=1       -----      visualDefaultMsec
                                           |
                                           |
                                           |
                           scale=1       -----      0
                */
                var absDiffTime = Math.Abs(GetSoflanTimeDiff());

                var scale = Mathf.Clamp01((2f * visualDefaultMsec - absDiffTime) / visualDefaultMsec);
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
            return GetSoflanTimeDiff(NotesManager.GetCurrentMsec());
        }

        private float GetSoflanTimeDiff(float currentMsec)
        {
            var currentSoflanTime = soflanManager.GetCurrentSoflanTimeWithOffsetsCached(
                MonitorId,
                currentMsec,
                maiBugAdjustMsec,
                noteSoflanGroup);
#if DEBUG
            _rawCurrentSoflanTime = soflanManager.GetCurrentSoflanTimeCached(
                MonitorId,
                currentMsec,
                noteSoflanGroup);
            _adjustedCurrentSoflanTime = currentSoflanTime;
#endif
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
                         guideScale=1    -----   scaleStartTime = 2 * visualDefaultMsec
                                           |
                                           |
                                           |
              y=120      guideScale=1    -----   moveStartTime = visualDefaultMsec
                                           |
                                           |
                                           |
              y=400       scale=1        -----      0
                                           |
                                           |
                                           |
              y=680       scale=1        -----   -moveStartTime = -visualDefaultMsec


            */
            var currentTime = NotesManager.GetCurrentMsec();
            var diffTime = GetSoflanTimeDiff(currentTime);
            var absDiffTime = Math.Abs(diffTime);

            var scaleStartTime = 2f * visualDefaultMsec;
            var moveStartTime = visualDefaultMsec;
            var fixedMotionProgress = isFixedSoflanToUnifiedSpeed
                ? FixedSoflan.GetMotionProgress(diffTime, fixedSoflanUnifiedSpeed)
                : 0f;
            var fixedScaleProgress = isFixedSoflanToUnifiedSpeed
                ? FixedSoflan.GetScaleProgress(absDiffTime, fixedSoflanUnifiedSpeed)
                : 0f;

            /*  强制重新计算Guide物件缩放
                diffTime = moveStartTime             0             -moveStartTime
                             ---|--------------------|--------------------|---
                  finalScale = 0.25                  1                   1.75
                              StartPos              EndPos      EndPos + (EndPos - StartPos)
             */

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
            // MaiBug 的音频毫秒偏移已在 GetSoflanTimeDiff 中经过 Soflan 时间轴映射；
            // 这里不再叠加独立坐标偏移，否则会重复补偿。
            var adjustedSoflanY = soflanY;

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
                    NoteGuideTrans.localScale = new Vector3(0f, 0f, 1f);
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
                    MaiBugAdjustEnabled = Setting.EnableSoflanMaiBugAdjust,
                    MaiBugAdjustMsec = maiBugAdjustMsec,
                    MonitorId = MonitorId,
                    RuntimeCurrentMsec = currentTime,
                    RuntimeChartOffsetMsec = soflanManager.getRuntimeChartOffsetMsec(MonitorId),
                    AdjustedRawCurrentMsec = SoflanRuntimeTime.ToRawChartAudioMsec(
                        currentTime,
                        soflanManager.getRuntimeChartOffsetMsec(MonitorId),
                        maiBugAdjustMsec),
                    RawCurrentSoflanTime = _rawCurrentSoflanTime,
                    AdjustedCurrentSoflanTime = _adjustedCurrentSoflanTime,
                };
                SoflanPanelBehaviour.HasSelectedData = true;
            }
#endif

            SoflanDiagnostic.VisualSample(
                MonitorId,
                NoteIndex,
                NoteKind,
                noteSoflanGroup,
                currentTime,
                noteSoflanTime - diffTime,
                noteSoflanTime,
                diffTime,
                clipedSoflanY,
                scaleStartTime,
                moveStartTime,
                (int)NoteStat,
                isFixedSoflanToUnifiedSpeed,
                "NoteBase.GetNoteYPosition");

            return clipedSoflanY;
        }
    }
}
