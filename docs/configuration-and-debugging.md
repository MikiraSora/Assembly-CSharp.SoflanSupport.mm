# 配置、日志与调试

## mai2.ini

本 patch 从游戏进程当前目录下的 `mai2.ini` 读取 `[Patches]`：

```ini
[Patches]
EnablePatchLog=1
EnableSoflanMaiBugAdjust=1
```

| 配置 | 默认值 | 作用 |
| --- | ---: | --- |
| `EnablePatchLog` | `1` | 仅控制 Debug 构建中的普通 INFO 日志；不屏蔽 ERROR |
| `EnableSoflanMaiBugAdjust` | `1` | 控制 Tap/Break/Star/Hold 族的原版 MaiBug 视觉毫秒偏移是否先映射进 Soflan 时间轴 |

配置由 `Setting` 首次使用时读取一次。修改后需要完整重启游戏；运行中不会热加载。

`EnableSoflanMaiBugAdjust=0` 只移除 MaiBug 视觉偏移，不移除玩家 `GetAdjustMSec()` 的运行时时间轴还原，也不改变音频、判定或 FixedSoflan 的固定时间窗。TouchTap 本来就不应用 MaiBug，因此该开关不影响 Touch 动画。

## 日志

日志文件名固定为：

```text
dpSoflanSupport.log
```

路径相对于游戏进程当前目录，通常是游戏根目录。编码为 UTF-8 without BOM，每行包含线程 ID、级别和消息：

```text
[Thread: 1][ERROR]message
```

实现使用单独后台线程和并发队列写盘，每次最多批量处理 128 条；写文件或转发 Unity 日志失败时会吞掉异常，避免日志系统阻断游戏。日志组件首次初始化时会尝试删除旧文件。

级别行为：

| 调用 | Release | Debug | `EnablePatchLog=0` |
| --- | --- | --- | --- |
| `PatchLog.WriteLine` / INFO | 调用被条件编译移除 | 写文件并转发 `UnityEngine.Debug.Log` | Debug 中也跳过 |
| `PatchLog.Error` / ERROR | 始终入队 | 始终入队 | 不受该开关影响 |

因此 `EnablePatchLog=0` 不是“完全禁用日志”。marker 格式错误、SFL 解析错误和 `GetAdjustMSec()` 读取失败仍会记录 ERROR，并尝试调用 `UnityEngine.Debug.LogError`。

主要错误策略：

- MA2 文件不存在：该玩家不加载 SFL，当前实现不额外抛异常。
- 某条 `SFL` 解析失败：写 ERROR，停止扫描后续 `SFL` 行；已经加入的前序 SFL 保留。
- note marker 非法或同一 record 有多个 marker：写 ERROR，并抛 `FormatException` 中断该加载路径。
- `GetAdjustMSec()` 异常、NaN 或 Infinity：写 ERROR，并以 `0ms` 回退。

## 运行时快捷键

| 输入 | 构建 | 行为 |
| --- | --- | --- |
| `P` | Release / Debug | 调用 `GamePlayManager.SetPauseGame()`，切换暂停与恢复 |
| `F8` | Debug | 显示或隐藏 Soflan Monitor |
| 鼠标右键 | Debug | 在 note 平面命中并循环选择重叠的 `NoteBase`；只有进入 Tap Soflan 计算的对象会持续产生选中数据 |

`P` 键检查由 `GameProcess.OnUpdate` 方法起始处每帧驱动，不限于有 SFL 的谱面，也没有独立配置开关。早期移植记录中的 `L` 键 DumpCurrent 路径没有进入当前 patch。

## Debug Soflan Monitor

Debug 构建第一次执行 `GamePlayFumenController.Update()` 时，会创建常驻的 `SoflanPanel` GameObject 并挂载 `SoflanPanelBehaviour`。Release 构建不创建面板。

面板默认显示在屏幕右上角，默认可见，数据约每 `0.2s` 刷新一次。它显示：

- 当前播放时间、平滑 FPS、显示中的 monitor 和该玩家 `ChartOffset`。
- group `0` 当前倍率和 `EnableSoflanMaiBugAdjust` 状态。
- 可选的 group 倍率列表，最多显示 50 个当前 map 迭代到的 group。
- 选中 Tap 的 note index、状态、Soflan 时间差、移动/缩放门槛、Y、FixedSoflan 进度和完整时间轴偏移数据。
- “复制面板内容到剪贴板”按钮及短暂复制反馈。

monitor 选择规则需要注意：未选中 note 时面板显示 monitor `0`；选中 P2 note 后才改用该 note 的 `MonitorId`。因此验证双玩家状态时应分别选择对应玩家的 Tap。

右键选择的实现限制：

- Debug 构建会在 `NoteBase.Initialize()` 给视觉物件补 `BoxCollider2D`。
- 单次点击使用固定 128 项的 NonAlloc 命中缓冲；极端重叠超过该上限时不会看到全部候选。
- 重叠候选按 Unity instance ID 排序，重复右键循环选择。
- 被选对象结束、回池、谱面重新加载、面板销毁或对象失活时会清理静态引用。
- 选择高亮是 Debug-only 的黄色呼吸效果，不参与 Release 视觉。

面板本身使用 IMGUI。它适合诊断，不适合作为性能基准；显示面板、展开 group 列表或频繁右键都会产生额外 CPU/分配开销。

## NoteGuide 池化清理

`Monitor.NoteGuide.ReturnToBase()` 在调用原版回池逻辑前执行 `HideEachGuide()`。该修复用于避免 Soflan 缩放窗口之外或对象池复用后残留 each guide；Release 和 Debug 都包含此行为，它不是面板开关的一部分。

## 排障顺序

1. 确认部署的是当前 `Assembly-CSharp.SoflanSupport.mm.dll`，并移除重复旧版 patch。
2. 检查 MonoMod 启动输出是否有 `[SoflanRules]` 锚点或 visibility dispatch 失败。
3. 确认 MA2 中至少有一条可解析的 tab 分隔 `SFL`；只有 marker 而没有任何 SFL 时，视觉仍回到原版。
4. 检查 note marker 是否只有一个，且与 `!m` / `!y` 等修饰之间没有空白或其它字符粘连。
5. 检查 note 的 group 是否有对应 `SFL`；缺失 group 会按默认 `1.0x` 运行。
6. 检查 `mai2.ini` 的节名、键名和整数布尔值，并在修改后重启。
7. Debug 构建下用 F8 面板核对 monitor、ChartOffset、group speed、MaiBug 状态和选中 Tap 数据。
8. 查看 `dpSoflanSupport.log`；即使关闭 INFO，ERROR 仍会保留。

谱面语法、类型支持与时间轴公式见 [Soflan 变速系统](soflan-system.md)；构建和部署问题见 [构建与部署](build-and-deployment.md)。
