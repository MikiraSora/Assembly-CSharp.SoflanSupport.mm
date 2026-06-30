#pragma warning disable CS0626
// patch_Monitor.TapNote — 给 Tap 视觉物件加 BoxCollider2D, 供调试面板右键选中.
// 用 2D collider: NoteObj.localScale.z=0 (NoteBase.Initialize 设为 scale,scale,0) 会把 3D BoxCollider
// 压成零厚度薄片导致 Physics.Raycast 命中不稳定; 2D 物理忽略 z, 不受影响, 且 BoxCollider2D 配合
// SpriteRenderer 可按 sprite bounds 适配 size.
using Manager;
using UnityEngine;

namespace Monitor
{
    public class patch_TapNote : TapNote
    {
        public extern void orig_Initialize(NoteData note);

        public override void Initialize(NoteData note)
        {
            orig_Initialize(note);
            // 仅 Tap 加 collider (GetNoteYPosition_soflan 只对 Tap 调用, checkSupportSoflan 仅 Tap true).
            if (NoteObj != null && NoteObj.GetComponent<Collider2D>() == null)
            {
                var col = NoteObj.AddComponent<BoxCollider2D>();
                if (SpriteRender != null && SpriteRender.sprite != null)
                    col.size = SpriteRender.sprite.bounds.size;  // 显式按 sprite 局部尺寸, 防自动适配未生效
            }
        }
    }
}
