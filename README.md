# Soflan Support

这是一个面向 `Assembly-CSharp.dll` 的 MonoMod patch，为游戏加入 MA2 Soflan 视觉变速支持。

Soflan 只改变物件的出现、移动、缩放和部分动画时间轴，不改变歌曲播放速度，也不改变判定时间。

## 主要功能

- 读取 MA2 `SFL` 行，支持加速、减速、停车、负速反向和弹跳类视觉效果。
- 使用 `#group` marker 为单个物件指定 Soflan group；未指定时使用 group `0`。
- 支持多个玩家、多个 group 的独立 BPM、SFL、时间偏移和缓存。
- 在停车、反向和折返时重算可见范围，避免物件未注册或提前消失。
- 支持 Tap、Break、Star、Hold、BreakHold、TouchNoteB 和 TouchNoteC 的 Soflan 视觉。
- 提供 FixedSoflan，使 Tap 系物件按声明的固定视觉速度运行，不受玩家物件速度影响。
- Debug 构建提供 Soflan Monitor、group 倍率、右键选中 Tap 和诊断数据复制。
- 附带 Soflan 计算器、MA2 转 Majdata 脚本及 marker、日志、时间轴验证工具。

## 支持范围

| 对象 | 普通 Soflan | FixedSoflan |
| --- | --- | --- |
| Tap / Break / ExTap / ExBreakTap | 支持 | 支持 |
| Star / BreakStar / ExStar / ExBreakStar | 支持位置和缩放 | 支持 |
| Hold / ExHold | 支持 | 不支持 |
| BreakHold / ExBreakHold | 支持 | 不支持 |
| TouchNoteB / TouchNoteC | 支持 TouchTap 动画 | 不支持 |
| TouchHold | 不支持 | 不支持 |
| Slide 路径及内部星标 | 不支持 | 不支持 |

MajSimai `<HS...>` 不会被运行时直接解析，需要先由编辑器或转换工具导出为 MA2 `SFL`。

## 安装

已构建的最小部署只需要一个文件：

```text
Assembly-CSharp.SoflanSupport.mm.dll
```

使用方法：

1. 关闭游戏，并备份现有 patch 和游戏程序集。
2. 将 `.mm.dll` 放入游戏的 `BepInEx\monomod\`。
3. 删除同目录中的旧版本，避免重复加载。
4. 启动游戏，由 MonoMod.Loader 自动应用 patch。

`SimpleSoflanFramework.Core` 已通过 Shared Project 编入 `.mm.dll`，不需要额外部署 `SimpleSoflanFramework.Core.dll`。

## 构建

要求：

- Windows PowerShell。
- 支持 `net472` 和 C# 10 的 .NET SDK。
- 目标游戏自带的 `Assembly-CSharp`、Unity、MonoMod 和 Cecil 程序集。

初始化子模块：

```powershell
git submodule update --init --recursive
```

构建 Release：

```powershell
dotnet build -c Release .\Assembly-CSharp.SoflanSupport.mm.csproj `
  -p:GameRoot='F:\yourGame'
```

`F:\yourGame` 是游戏根目录占位符，目录下应存在 `Package\Sinmai_Data\Managed` 和 `Package\BepInEx\core`。产物位于：

```text
bin\Release\Assembly-CSharp.SoflanSupport.mm.dll
```

## 谱面用法

### SFL

MA2 `SFL` 必须使用 tab 分隔：

```text
SFL    unit    grid    length    speed    group
```

例如，group `1` 从谱面开头停车 `384` grid：

```text
SFL	0	0	384	0	1
```

常用 speed：

- `1.0`：正常速度。
- `2.0`：两倍视觉速度。
- `0`：停车。
- 负数：视觉时间轴反向。

### Note group

在 note record 的扩展字段中加入 marker：

```text
#1
!m#1
#1!m!y
```

`#1` 表示该物件使用 group `1`。marker 可以与 `!m`、`!y` 等私有修饰前后组合，但 token 内不能有空白，同一 record 只能有一个 Soflan marker。

### FixedSoflan

在 group 后添加 `Fspeed`：

```text
#1F        # 等价于 #1F600
#1F750     # 固定视觉速度 750
#F600      # group 0，固定视觉速度 600
```

FixedSoflan 只对 Tap、Break、Star 等 Tap 系物件生效。它不会改变判定时间。

## 可配置选项

在游戏当前目录的 `mai2.ini` 中配置：

```ini
[Patches]
EnablePatchLog=1
EnableSoflanMaiBugAdjust=1
```

| 选项 | 默认值 | 说明 |
| --- | ---: | --- |
| `EnablePatchLog` | `1` | 控制 Debug 构建的 INFO 日志；ERROR 始终保留 |
| `EnableSoflanMaiBugAdjust` | `1` | 控制 Tap/Break/Star/Hold 族的 MaiBug 视觉偏移是否进入 Soflan 时间轴 |

配置首次使用时读取一次，修改后需要完整重启游戏。`EnableSoflanMaiBugAdjust` 只影响视觉，不影响歌曲或判定。

日志写入游戏当前目录下的 `dpSoflanSupport.log`，编码为 UTF-8 without BOM。

## 快捷键与调试

| 输入 | 构建 | 功能 |
| --- | --- | --- |
| `P` | Release / Debug | 暂停或恢复游戏 |
| `F8` | Debug | 显示或隐藏 Soflan Monitor |
| 鼠标右键 | Debug | 选择 Tap；重叠时循环切换 |

Debug 面板可显示当前 group 倍率、ChartOffset、MaiBug 状态、Tap 的 Soflan 时间差、Y、缩放和 FixedSoflan 进度。

## 注意事项

- 谱面至少要有一条有效 `SFL`，否则物件视觉回到原版逻辑。
- note 指向不存在的 group 时，该 group 通常按默认 `1.0x` 运行。
- 非法或重复 marker 会写 ERROR 日志并中断对应加载路径。
- patch 依赖目标 `Assembly-CSharp.dll` 的类型和 IL 形状，不保证跨游戏版本兼容。
- Debug 面板用于诊断，不适合作为性能基准。

## 文档

- [文档索引](docs/README.md)
- [Soflan 系统说明](docs/soflan-system.md)
- [FixedSoflan 说明](docs/fixed-soflan.md)
- [构建与部署](docs/build-and-deployment.md)
- [配置、日志与调试](docs/configuration-and-debugging.md)
- [离线工具与验证](docs/tools.md)
