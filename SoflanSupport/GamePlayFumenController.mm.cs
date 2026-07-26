// SoflanSupport.GamePlayFumenController — 新增类型, 基于 head commit 2a7a4a4.
// 偏差: 去掉 L 键 DumpCurrent 调试路径(依赖 GameCtrl.DumpCurrent, 访问 private 字段, 已放弃);
//       仅保留 P 键暂停功能。类型改为 public 以满足 Singleton<T> 的 new() 约束。
using MAI2.Util;
using Manager;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SoflanSupport
{
    public class GamePlayFumenController
    {
#if DEBUG
        private static bool _panelMounted;
#endif

        public void Update()
        {
#if DEBUG
            MountPanelIfNeeded();
#endif
            if (DebugInput.GetKeyDown(UnityEngine.KeyCode.P))
            {
                PauseOrResumeGamePlay();
            }
        }

#if DEBUG
        // 一次性惰性挂载调试面板 MonoBehaviour。本 Update 每帧由 GameProcess.OnUpdate 起始的
        // __SoflanUpdateGamePlayFumenController 驱动, 谱面进入后即触发首帧挂载。
        private static void MountPanelIfNeeded()
        {
            if (_panelMounted) return;
            _panelMounted = true;
            try
            {
                var go = new GameObject("SoflanPanel");
                go.AddComponent<SoflanPanelBehaviour>();
                UnityEngine.Object.DontDestroyOnLoad(go);
            }
            catch
            {
                // 挂载失败不阻断游戏; 可视情况记 PatchLog。
            }
        }
#endif

        private static void PauseOrResumeGamePlay()
        {
            Singleton<GamePlayManager>.Instance.SetPauseGame(!Singleton<GamePlayManager>.Instance.IsPauseGame());
        }
    }
}
