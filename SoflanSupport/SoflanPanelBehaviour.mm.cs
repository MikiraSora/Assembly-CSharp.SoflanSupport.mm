// SoflanSupport.SoflanPanelBehaviour — 调试面板 MonoBehaviour (新增类型, 整体复制进 Assembly-CSharp).
//
// 运行时由 GamePlayFumenController.Update 惰性 AddComponent 挂载 (new GameObject + AddComponent +
// DontDestroyOnLoad)。Unity 对运行时创建的 MonoBehaviour 实例正常调度 OnGUI/Update, 规避 patch 新增
// 方法是否被 Unity magic method 扫描调度的不确定性。
//
// 显示: 当前播放时间(ms + mm:ss.fff) / 变速组0 当前倍率 / FPS / (可选)所有 group 倍率。
// F8 切换面板显示/隐藏。位置: 屏幕左上角。
//
// 性能: 数据在 Update 每帧更新一次 (OnGUI 一帧多调, 复用缓存); _allSpeeds 复用零 List 分配;
// GroupSpeed 为值类型无堆分配; 面板隐藏时 OnGUI 直接 return。
using System.Collections.Generic;
using MAI2.Util;
using Manager;
using UnityEngine;

namespace SoflanSupport
{
    public class SoflanPanelBehaviour : MonoBehaviour
    {
        private static bool _visible = true;
        private static bool _showAllGroups = false;   // checkbox 状态

        private float _msec;
        private double _speed0;
        private bool _hasData;
        private float _fps;
        private float _fpsSmooth;

        // 复用的 group 倍率缓冲 (避免每帧 new List)
        private readonly List<SoflanManager.GroupSpeed> _allSpeeds = new List<SoflanManager.GroupSpeed>();

        private void Update()
        {
            // FPS 平滑
            float dt = Time.deltaTime;
            if (dt > 0f)
            {
                float inst = 1f / dt;
                _fpsSmooth = _fpsSmooth <= 0f ? inst : Mathf.Lerp(_fpsSmooth, inst, 0.1f);
                _fps = _fpsSmooth;
            }

            try
            {
                _msec = NotesManager.GetCurrentMsec();
                var sm = Singleton<SoflanManager>.Instance;
                _hasData = sm != null && sm.containsSoflans();
                if (_hasData)
                {
                    _speed0 = sm.GetCurrentSpeed(0, _msec);
                    if (_showAllGroups) sm.FillCurrentSpeeds(_msec, _allSpeeds);
                }
                else
                {
                    _speed0 = 1.0;
                }
            }
            catch
            {
                _hasData = false;
                _speed0 = 1.0;
            }
        }

        private void OnGUI()
        {
            var e = Event.current;
            if (e != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.F8)
                _visible = !_visible;
            if (!_visible) return;

            // 时分秒格式
            var span = System.TimeSpan.FromMilliseconds(_msec);
            string timeStr = $"{(int)span.TotalMinutes:D2}:{span.Seconds:D2}.{span.Milliseconds:D3}";

            // 右上角: x = 屏幕宽 - 面板宽 - 右边距 10
            float panelW = 300f;
            GUILayout.BeginArea(new Rect(Screen.width - panelW - 10f, 10f, panelW, 220f), "Soflan Monitor (F8)", GUI.skin.box);
            GUILayout.Label($"PlayTime: {_msec:F1} ms  ({timeStr})");
            GUILayout.Label($"SoflanGroup0 Speed: {_speed0:F3}x" + (_hasData ? "" : " (no data)"));
            GUILayout.Label($"FPS: {_fps:F1}");
            _showAllGroups = GUILayout.Toggle(_showAllGroups, "显示所有 group 倍率");
            if (_showAllGroups && _hasData)
            {
                foreach (var kv in _allSpeeds)
                    GUILayout.Label($"  group{kv.Group}: {kv.Speed:F3}x");
            }
            GUILayout.EndArea();
        }
    }
}
