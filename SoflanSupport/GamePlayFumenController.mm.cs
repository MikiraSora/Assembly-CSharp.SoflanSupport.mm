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
        public void Update()
        {
            if (DebugInput.GetKeyDown(UnityEngine.KeyCode.P))
            {
                PauseOrResumeGamePlay();
            }
        }

        private static void PauseOrResumeGamePlay()
        {
            Singleton<GamePlayManager>.Instance.SetPauseGame(!Singleton<GamePlayManager>.Instance.IsPauseGame());
        }
    }
}
