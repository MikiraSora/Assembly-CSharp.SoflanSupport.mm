// patch_Process.GameProcess — 对应 head commit 2a7a4a4 中 Process/GameProcess.cs 的改动.
// GameProcess 仅有带参构造 GameProcess(ProcessDataContainer), 派生类无法链式调用; 且辅助方法为
// 静态、无需访问 GameProcess 基类成员, 故用 [MonoModPatch] 显式指定目标且不继承.
// head 在 OnUpdate 的 Play case 内插入 GamePlayFumenController.Update() 调用.
// OnUpdate 含巨型 switch(1597 行 IL), 在 Play case 内精确锚点脆弱, 改为方法起始处每帧调用
// (功能等价: 每帧检查 P 键暂停; 偏差见 patch-diff-report.md).
using MAI2.Util;
using MonoMod;
using SoflanSupport;

namespace Process
{
    [MonoModPatch("global::Process.GameProcess")]
    public class SoflanGameProcessHooks
    {
        // OnUpdate: 方法起始处 — 每帧驱动 GamePlayFumenController (P 键暂停)
        public static void __SoflanUpdateGamePlayFumenController()
        {
            Singleton<GamePlayFumenController>.Instance.Update();
        }
    }
}
