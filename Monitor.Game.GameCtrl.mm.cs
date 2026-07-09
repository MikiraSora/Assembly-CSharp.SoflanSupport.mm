// patch_Monitor.Game.GameCtrl — 对应 head commit 2a7a4a4 中 Monitor/Game/GameCtrl.cs 的改动.
// GameCtrl 仅有 private 0 参构造, 派生类无法链式调用; 且辅助方法无需访问 GameCtrl 基类成员,
// 故用 [MonoModPatch] 显式指定目标且不继承 GameCtrl. MonoMod 仍将字段与辅助方法复制进 GameCtrl.
// UpdateCtrl 的 soflan 可见性逻辑为方法体中间插入且改写既有 continue 控制流, orig_ 无法表达,
// 改用 MonoModRules PostProcessor 做 IL 精确插入, 调用以下实例辅助方法.
// 放弃 GameCtrl.DumpCurrent (访问 private 字段 _xxxObjectList/apperMsecTap/NoteMng).
using MAI2.Util;
using Manager;
using MonoMod;
using SoflanSupport;

namespace Monitor.Game
{
    [MonoModPatch("global::Monitor.Game.GameCtrl")]
    public class SoflanGameCtrlHooks
    {
        // UpdateCtrl: UserOption 赋值后 — 清空 SoflanManager 的共享每帧 soflan 时间缓存
        public void __SoflanClearCache()
        {
            Singleton<SoflanManager>.Instance.clearCurrentSoflanTimeCache();
        }

        // UpdateCtrl: 原 msec 可见性检查前 — soflan 可见性判定
        // 返回 0 = 非 soflan(走原始 msec 检查), 1 = soflan 可见(处理, 跳过原始检查), 2 = soflan 不可见(continue)
        public int __SoflanNoteDecision(NoteData note, float num)
        {
            var soflanManager = Singleton<SoflanManager>.Instance;
            if (!soflanManager.containsSoflans())
                return 0;
            if (!SoflanManager.IsSupportedVisualSoflanKind(note.type.getEnum()))
                return 0;

            var currentMsec = NotesManager.GetCurrentMsec();
            var noteSoflanGroup = soflanManager.getNoteSoflanGroup(note);
            var soflanTime = soflanManager.GetCurrentSoflanTimeCached(currentMsec, noteSoflanGroup);
            var visibleMsec = FixedSoflan.IsEnabledForNote(note)
                ? FixedSoflan.GetVisibleMsec(FixedSoflan.GetUnifiedSpeed(note))
                : num;
            if (!soflanManager.checkNoteVisible(note, currentMsec, visibleMsec, noteSoflanGroup, soflanTime))
                return 2;
            return 1;
        }
    }
}
