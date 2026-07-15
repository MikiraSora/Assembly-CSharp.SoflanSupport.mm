// SoflanSupport.SoflanPanelBehaviour — 调试面板 MonoBehaviour (新增类型, 整体复制进 Assembly-CSharp).
//
// 运行时由 GamePlayFumenController.Update 惰性 AddComponent 挂载 (new GameObject + AddComponent +
// DontDestroyOnLoad)。Unity 对运行时创建的 MonoBehaviour 实例正常调度 OnGUI/Update, 规避 patch 新增
// 方法是否被 Unity magic method 扫描调度的不确定性。
//
// 显示: 当前播放时间(ms + mm:ss.fff) / 变速组0 当前倍率 / FPS / (可选)所有 group 倍率。
// F8 切换面板显示/隐藏。位置: 屏幕左上角。
//
// 性能: 数据在 Update 中节流刷新 (OnGUI 一帧多调, 复用缓存); _allSpeeds 复用零 List 分配;
// GroupSpeed 为值类型无堆分配; 面板隐藏时 OnGUI 直接 return。
using System.Collections.Generic;
using MAI2.Util;
using Manager;
using Monitor;
using UnityEngine;

namespace SoflanSupport
{
    public class SoflanPanelBehaviour : MonoBehaviour
    {
#if DEBUG
        private static bool _visible = true;
        private static bool _showAllGroups = false;   // checkbox 状态

        private float _msec;
        private double _speed0;
        private bool _hasData;
        private float _fps;
        private float _fpsSmooth;
        private float _copyFeedbackTime;   // 复制按钮点击时刻, 用于显示"已复制"提示
        private float _nextDataRefreshTime;

        private const float DataRefreshInterval = 0.2f;
        private const int MaxDisplayedGroups = 50;
        private const int MaxSelectHitCount = 128;

        // 复用的 group 倍率缓冲 (避免每帧 new List)
        private readonly List<SoflanManager.GroupSpeed> _allSpeeds = new List<SoflanManager.GroupSpeed>();

        // --- 右键选中 Tap ---
        // 选中 note 的计算数据 (由 patch_NoteBase.GetNoteYPosition_soflan 在 IsSelected 时写入).
        public struct SelectedNoteData
        {
            public int NoteIndex;
            public double DiffTime, AbsDiffTime;
            public float ScaleStartTime, MoveStartTime, MoveProgress, FinalScale;
            public float InsideY, OutsideY, SoflanY, ClipedSoflanY;
            public bool IsFixedSoflanToUnifiedSpeed;
            public float FixedSoflanUnifiedSpeed, FixedMotionProgress, FixedScaleProgress;
            public NoteBase.NoteStatus NoteStat;
        }
        public static SelectedNoteData SelectedData;
        public static bool HasSelectedData;

        // 选中状态集中维护 (避免在 NoteBase 上新增字段导致跨类编译期鸿沟).
        private static NoteBase _selectedNote;
        private static int _cycleIndex;
        private static int _lastHitCount;
        private static readonly Collider2D[] _selectHits = new Collider2D[MaxSelectHitCount];
        private static readonly List<NoteBase> _selectNotes = new List<NoteBase>(MaxSelectHitCount);
        private static readonly System.Comparison<NoteBase> _noteInstanceComparer = CompareNoteInstanceId;

        // patch_NoteBase 查询本实例是否被选中.
        public static bool IsNoteSelected(NoteBase nb) => _selectedNote == nb;
        // note 池化复用时 (Initialize) 清除: 若本实例曾被选中则取消选中.
        public static void OnNoteReinitialized(NoteBase nb) { if (_selectedNote == nb) ClearSelectedNote(); }
        // 被选中的 note 进入 EndNote 时调用: 清选中 + 清面板显示数据.
        public static void OnSelectedNoteEnded() => ClearSelectedNote();

        // 谱面清理、面板销毁、note 复用/结束时统一释放静态 note 引用, 避免跨场景滞留 GameObject 图.
        public static void ClearSelectedNote()
        {
            _selectedNote = null;
            SelectedData = default;
            HasSelectedData = false;
            _cycleIndex = 0;
            _lastHitCount = 0;
        }

        private static void ClearStaleSelectedNote()
        {
            if (_selectedNote == null)
            {
                if (HasSelectedData) ClearSelectedNote();
                return;
            }

            try
            {
                if (!_selectedNote.gameObject.activeInHierarchy)
                    ClearSelectedNote();
            }
            catch
            {
                ClearSelectedNote();
            }
        }

        private void OnDestroy()
        {
            ClearSelectedNote();
        }

        private void Update()
        {
            if (!_visible && _selectedNote == null)
                return;

            ClearStaleSelectedNote();

            // FPS 平滑
            float dt = Time.deltaTime;
            if (dt > 0f)
            {
                float inst = 1f / dt;
                _fpsSmooth = _fpsSmooth <= 0f ? inst : Mathf.Lerp(_fpsSmooth, inst, 0.1f);
                _fps = _fpsSmooth;
            }

            if (Time.realtimeSinceStartup < _nextDataRefreshTime)
            {
                if (Input.GetMouseButtonDown(1))
                    HandleSelectClick();
                return;
            }

            _nextDataRefreshTime = Time.realtimeSinceStartup + DataRefreshInterval;

            try
            {
                _msec = NotesManager.GetCurrentMsec();
                var sm = Singleton<SoflanManager>.Instance;
                _hasData = sm != null && sm.containsSoflans();
                if (_hasData)
                {
                    _speed0 = sm.GetCurrentSpeed(0, _msec);
                    if (_showAllGroups) sm.FillCurrentSpeeds(_msec, _allSpeeds, MaxDisplayedGroups);
                }
                else
                {
                    _speed0 = 1.0;
                    _allSpeeds.Clear();
                }
            }
            catch
            {
                _hasData = false;
                _speed0 = 1.0;
                _allSpeeds.Clear();
            }

            // 右键点击选中 Tap (不干扰左键触摸判定)
            if (Input.GetMouseButtonDown(1))
                HandleSelectClick();
        }

        // 右键: 把鼠标屏幕坐标转到 note 平面 (z=-6) 的世界 XY, 用 Physics2D.OverlapPointAll 命中
        // 带 BoxCollider2D 的 Tap 视觉物件, 过滤出 NoteBase, 按 GetInstanceID 稳定排序后循环选择.
        // 多个 Tap 重叠时, 每次右键在命中列表里取下一个 (依次选择).
        private static void HandleSelectClick()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            const float noteZ = -6f;  // note 平面 z (GetBaseZPosition ≈ -6)
            if (Mathf.Approximately(ray.direction.z, 0f)) return;
            float t = (noteZ - ray.origin.z) / ray.direction.z;
            if (t < 0f) return;
            Vector2 world2d = new Vector2(ray.origin.x + ray.direction.x * t, ray.origin.y + ray.direction.y * t);

            int hitCount = Physics2D.OverlapPointNonAlloc(world2d, _selectHits);
            _selectNotes.Clear();
            for (var i = 0; i < hitCount; i++)
            {
                var c = _selectHits[i];
                _selectHits[i] = null;
                var nb = c.GetComponentInParent<NoteBase>();
                if (nb != null) _selectNotes.Add(nb);
            }
            if (_selectNotes.Count == 0) return;
            _selectNotes.Sort(_noteInstanceComparer);

            // 命中集合数量变 → 从头选; 否则循环到下一个 (依次选择重叠 note).
            if (_selectNotes.Count != _lastHitCount) _cycleIndex = 0;
            else _cycleIndex = (_cycleIndex + 1) % _selectNotes.Count;
            _lastHitCount = _selectNotes.Count;

            // 仅切换 _selectedNote; 旧 note 的呼吸色由其 NoteCheck 检测 IsNoteSelected==false 后自动恢复.
            _selectedNote = _selectNotes[_cycleIndex];
            _selectNotes.Clear();
            HasSelectedData = false;  // 清旧数据, 等 GetNoteYPosition_soflan 重新填充
        }

        private static int CompareNoteInstanceId(NoteBase a, NoteBase b)
        {
            return a.GetInstanceID().CompareTo(b.GetInstanceID());
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
            GUILayout.BeginArea(new Rect(Screen.width - panelW - 10f, 10f, panelW, 440f), "Soflan Monitor (F8 | 右键选Tap)", GUI.skin.box);
            GUILayout.Label($"PlayTime: {_msec:F1} ms  ({timeStr})");
            GUILayout.Label($"SoflanGroup0 Speed: {_speed0:F3}x" + (_hasData ? "" : " (no data)"));
            GUILayout.Label($"FPS: {_fps:F1}");
            _showAllGroups = GUILayout.Toggle(_showAllGroups, "显示所有 group 倍率");
            if (_showAllGroups && _hasData)
            {
                foreach (var kv in _allSpeeds)
                    GUILayout.Label($"  group{kv.Group}: {kv.Speed:F3}x");
                if (_allSpeeds.Count >= MaxDisplayedGroups)
                    GUILayout.Label($"  showing first {MaxDisplayedGroups} groups");
            }

            if (HasSelectedData)
            {
                var d = SelectedData;
                GUILayout.Label($"--- Selected Tap (右键循环切换) ---");
                GUILayout.Label($"NoteIndex: {d.NoteIndex}  NoteStat: {d.NoteStat}");
                GUILayout.Label($"diffTime: {d.DiffTime:F3}  absDiffTime: {d.AbsDiffTime:F3}");
                GUILayout.Label($"scaleStartTime: {d.ScaleStartTime:F3}  moveStartTime: {d.MoveStartTime:F3}");
                GUILayout.Label($"moveProgress: {d.MoveProgress:F3}  finalScale: {d.FinalScale:F3}");
                GUILayout.Label($"Fixed: {d.IsFixedSoflanToUnifiedSpeed}  FixedSpd: {d.FixedSoflanUnifiedSpeed:F3}");
                GUILayout.Label($"FixedMoveP: {d.FixedMotionProgress:F3}  FixedScaleP: {d.FixedScaleProgress:F3}");
                GUILayout.Label($"insideY: {d.InsideY:F2}  outsideY: {d.OutsideY:F2}");
                GUILayout.Label($"soflanY: {d.SoflanY:F2}  clipedSoflanY: {d.ClipedSoflanY:F2}");
            }

            if (GUILayout.Button("复制面板内容到剪贴板"))
            {
                GUIUtility.systemCopyBuffer = BuildClipboardText(timeStr);
                _copyFeedbackTime = Time.realtimeSinceStartup;
            }
            if (_copyFeedbackTime > 0f && Time.realtimeSinceStartup - _copyFeedbackTime < 2f)
                GUILayout.Label("✓ 已复制到剪贴板");

            GUILayout.EndArea();
        }

        // 把面板所有文本格式化为多行字符串, 供复制到剪贴板.
        private string BuildClipboardText(string timeStr)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== Soflan Monitor ===");
            sb.AppendLine($"PlayTime: {_msec:F1} ms  ({timeStr})");
            sb.AppendLine($"SoflanGroup0 Speed: {_speed0:F3}x" + (_hasData ? "" : " (no data)"));
            sb.AppendLine($"FPS: {_fps:F1}");
            if (_showAllGroups && _hasData)
            {
                sb.AppendLine("All group speeds:");
                foreach (var kv in _allSpeeds)
                    sb.AppendLine($"  group{kv.Group}: {kv.Speed:F3}x");
                if (_allSpeeds.Count >= MaxDisplayedGroups)
                    sb.AppendLine($"  showing first {MaxDisplayedGroups} groups");
            }
            if (HasSelectedData)
            {
                var d = SelectedData;
                sb.AppendLine("--- Selected Tap ---");
                sb.AppendLine($"NoteIndex: {d.NoteIndex}  NoteStat: {d.NoteStat}");
                sb.AppendLine($"diffTime: {d.DiffTime:F3}  absDiffTime: {d.AbsDiffTime:F3}");
                sb.AppendLine($"scaleStartTime: {d.ScaleStartTime:F3}  moveStartTime: {d.MoveStartTime:F3}");
                sb.AppendLine($"moveProgress: {d.MoveProgress:F3}  finalScale: {d.FinalScale:F3}");
                sb.AppendLine($"Fixed: {d.IsFixedSoflanToUnifiedSpeed}  FixedSpd: {d.FixedSoflanUnifiedSpeed:F3}");
                sb.AppendLine($"FixedMoveP: {d.FixedMotionProgress:F3}  FixedScaleP: {d.FixedScaleProgress:F3}");
                sb.AppendLine($"insideY: {d.InsideY:F2}  outsideY: {d.OutsideY:F2}");
                sb.AppendLine($"soflanY: {d.SoflanY:F2}  clipedSoflanY: {d.ClipedSoflanY:F2}");
            }
            return sb.ToString();
        }
#else
        public static bool IsNoteSelected(NoteBase nb) => false;
        public static void OnNoteReinitialized(NoteBase nb) { }
        public static void OnSelectedNoteEnded() { }
        public static void ClearSelectedNote() { }
#endif
    }
}
