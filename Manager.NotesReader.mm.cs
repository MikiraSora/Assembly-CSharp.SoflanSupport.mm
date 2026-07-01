// patch_Manager.NotesReader — 对应 head commit 2a7a4a4 中 Manager/NotesReader.cs 的改动.
// 由于 loadMa2Main 的 clearAll 为方法体中间插入、loadNote 末尾插入需访问 noteData 局部,
// orig_ 包装无法表达, 改用 MonoModRules PostProcessor 做 IL 精确插入, 调用以下静态辅助方法.
// 这些辅助方法会被 MonoMod 复制进目标 NotesReader 类型.
using MAI2.Util;
using SoflanSupport;

namespace Manager
{
    public class patch_NotesReader : NotesReader
    {
        // loadMa2Main: calcBPMList 调用前 — 清空 soflan 状态 (对应 head 中 clearAll 调用)
        public static void __SoflanClearAll()
        {
            SoflanPanelBehaviour.ClearSelectedNote();
            Singleton<SoflanManager>.Instance.clearAll();
        }

        // loadMa2Main: calcTotal 调用后 — 从谱面文件加载 soflan 区间 (对应 head 中 loadComposition 调用)
        public static void __SoflanLoadComposition(MA2RecordList records, NotesReader sr)
        {
            Singleton<SoflanManager>.Instance.loadComposition(records, sr);
        }

        // loadNote: return 前 — 注册 note 的 soflan 分组 (对应 head 中 SoflanManager.loadNote 调用)
        public static void __SoflanLoadNote(NoteData noteData, MA2Record rec, NotesReader sr)
        {
            Singleton<SoflanManager>.Instance.loadNote(noteData, rec, sr);
        }
    }
}
