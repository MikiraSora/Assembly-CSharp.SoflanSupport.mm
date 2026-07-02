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
using System.Collections.Generic;

namespace Monitor.Game
{
    [MonoModPatch("global::Monitor.Game.GameCtrl")]
    public class SoflanGameCtrlHooks
    {
        // 每帧 soflan 时间缓存 (head 中为字段初始化; patch 字段初始化器不会被复制, 故惰性初始化)
        private Dictionary<int, float> cachedSoflanTimeMap;
        private float cachedSoflanTimeMsec = float.MinValue;

        // UpdateCtrl: UserOption 赋值后 — 清空每帧 soflan 时间缓存
        public void __SoflanClearCache()
        {
            if (cachedSoflanTimeMap == null)
                cachedSoflanTimeMap = new Dictionary<int, float>();
            cachedSoflanTimeMap.Clear();
            cachedSoflanTimeMsec = float.MinValue;
        }

        // UpdateCtrl: 原 msec 可见性检查前 — soflan 可见性判定
        // 返回 0 = 非 soflan(走原始 msec 检查), 1 = soflan 可见(处理, 跳过原始检查), 2 = soflan 不可见(continue)
        public int __SoflanNoteDecision(NoteData note, float num)
        {
            var soflanManager = Singleton<SoflanManager>.Instance;
            if (!soflanManager.containsSoflans())
                return 0;
            var currentMsec = NotesManager.GetCurrentMsec();
            if (cachedSoflanTimeMap == null)
                cachedSoflanTimeMap = new Dictionary<int, float>();
            if (cachedSoflanTimeMsec != currentMsec)
            {
                cachedSoflanTimeMap.Clear();
                cachedSoflanTimeMsec = currentMsec;
            }
            var noteSoflanGroup = soflanManager.getNoteSoflanGroup(note);
            if (!cachedSoflanTimeMap.TryGetValue(noteSoflanGroup, out var soflanTime))
                cachedSoflanTimeMap[noteSoflanGroup] = soflanTime = soflanManager.ConvertAudioTimeToY_PreviewMode(currentMsec, noteSoflanGroup);
            if (!soflanManager.checkNoteVisible(note, currentMsec, num, noteSoflanGroup, soflanTime))
                return 2;
            return 1;
        }

        // UpdateCtrl: 第 2 个 RegistNote 失败 break 前 — 记录日志
        public void __SoflanLogRegistNoteFailed(NoteData note)
        {
            PatchLog.WriteLine($"RegistNote() failed, NoteIndex:{note.indexNote}, NoteIndex:{note.type.getEnumName()}, NoteTime:{note.time.msec}ms/{note.time.grid}grid, StartButtonPos:{note.startButtonPos}");
        }
    }
}
