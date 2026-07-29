# 构建与部署

## 产物与依赖模型

主项目 `Assembly-CSharp.SoflanSupport.mm.csproj` 生成面向 .NET Framework 4.7.2 的 MonoMod ahead-of-time patch：

```text
bin/Release/Assembly-CSharp.SoflanSupport.mm.dll
bin/Release/Assembly-CSharp.SoflanSupport.mm.pdb
```

`Dependencies/SimpleSoflanFramework/SimpleSoflanFramework.Core` 通过 Shared Project 的 `.projitems` 直接编入 `.mm.dll`。当前版本不会产生对 `SimpleSoflanFramework.Core.dll` 的运行时程序集引用，也没有 `DependencyAssemblyResolver`；部署时不要再复制旧版 Core DLL。

项目把工具、测试、临时目录和整个 `Dependencies/**` 从 SDK 默认编译项中排除，再在文件末尾显式导入 Core 的 Shared Project。因此：

- 游戏运行时只需要 patch `.mm.dll`，框架源码已包含在其中。
- `SoflanCalculator` 和 `tools/*` 是独立项目，不会进入游戏 patch。
- `CopyLocalLockFileAssemblies=false` 用于保持主输出目录整洁，不复制游戏、Unity、MonoMod 或 Cecil 依赖。

## 前置条件

1. Windows PowerShell 和可构建 `net472` / C# 10 的 .NET SDK。
2. 已初始化 `Dependencies/SimpleSoflanFramework` Git 子模块。
3. 与目标游戏环境匹配的以下程序集：

| 引用 | 当前项目中的默认位置 |
| --- | --- |
| `Assembly-CSharp.dll` | `F:\yourGame\Package\Sinmai_Data\Managed` |
| `UnityEngine*.dll` | `F:\yourGame\Package\Sinmai_Data\Managed` |
| `MonoMod.dll`、`MonoMod.Utils.dll` | `F:\yourGame\Package\BepInEx\core` |
| `Mono.Cecil*.dll` | `F:\yourGame\Package\BepInEx\core` |

`F:\yourGame` 是文档中的脱敏占位符。主项目当前使用本机绝对 `HintPath`；如果游戏安装位置不同，需要先在自己的副本中把这些引用改为实际路径。不要用不同大版本的 NuGet MonoMod 替换游戏自带版本；patch 当前针对 BepInEx 随附的经典 MonoMod `20.5.21.5` / Cecil `0.10.4` 形状构建。

初始化子模块：

```powershell
git submodule update --init --recursive
```

## 构建

从仓库根目录直接构建主项目：

```powershell
dotnet build -c Release .\Assembly-CSharp.SoflanSupport.mm.csproj
dotnet build -c Debug .\Assembly-CSharp.SoflanSupport.mm.csproj
```

在当前引用形状下，Release 构建为 0 warning；Debug 构建可能报告 `MSB3270`，原因是 patch 输出为 MSIL，而引用的 `Assembly-CSharp.dll` 标记为 AMD64。当前构建仍会成功并生成 patch，但该提示不能代表运行时已经验证，部署前仍需完成 MonoMod 应用和游戏内检查。

当前 `SoflanSupport.slnx` 还列有可选的 `SoflanSimulator/SoflanSimulator.csproj`，但该目录不在当前工作树中。因此自动验证应直接构建上面的主 `.csproj`，不要把解决方案构建作为必要入口。

检查产物：

```powershell
Get-ChildItem .\bin\Release
Get-ChildItem .\bin\Debug
```

Release 与 Debug 的运行时差异：

| 能力 | Release | Debug |
| --- | --- | --- |
| Soflan、FixedSoflan、Hold/Touch、可见性 | 有 | 有 |
| `P` 键暂停/恢复 | 有 | 有 |
| 普通 `PatchLog.WriteLine()` INFO 日志 | 编译移除 | 受 `EnablePatchLog` 控制 |
| `PatchLog.Diagnostic()` DIAG 现场日志 | 受 `EnableSoflanDiagnosticLog` 控制 | 受 `EnableSoflanDiagnosticLog` 控制 |
| Soflan Monitor、右键选择、复制面板数据 | 无 | 有 |
| marker/SFL 等错误日志 | 有 | 有 |

## 部署

标准 BepInEx MonoMod.Loader 部署方式：

1. 关闭游戏。
2. 备份现有 patch 和游戏程序集。
3. 将 `bin\Release\Assembly-CSharp.SoflanSupport.mm.dll` 放入游戏的 `BepInEx\monomod\`。
4. 删除同目录中本项目的旧版 `.mm.dll`，避免同一功能被加载两次。
5. 不复制 `SimpleSoflanFramework.Core.dll`；当前 patch 已内嵌所需源码。
6. 启动游戏，让 MonoMod.Loader 对 `Assembly-CSharp.dll` 应用 patch。

`.pdb` 不是运行所必需。需要符号诊断时可随 patch 保留；仅部署 `.mm.dll` 是最小配置。

`mai2.ini` 不属于构建产物。运行配置见 [配置、日志与调试](configuration-and-debugging.md)。

## MonoMod 接入点

普通 `orig_` 包装用于可直接表达的类和方法；方法体中间插入由 `MonoModRules` 的 PostProcessor 完成：

| 目标方法 | 插入行为 |
| --- | --- |
| `NotesReader.loadMa2Main` | `calcBPMList` 前清理当前 player；`calcTotal` 后加载 BPM/SFL 和玩家时间偏移 |
| `NotesReader.loadNote` | 返回前保存 note TGrid、尾 TGrid、group 和 FixedSoflan 字段 |
| `GameCtrl.UpdateCtrl` | 读取玩家 option 后清当前玩家帧缓存；原可见性检查前派发 Soflan 可见性 |
| `GameProcess.OnUpdate` | 每帧驱动 `GamePlayFumenController`，提供 `P` 键暂停/恢复和 Debug 面板挂载 |

为兼容同一目标上其它 `.mm.dll`，Rules 还会：

- 在 `method` / `orig_method` / `patched_method[_n]` 链中按 IL 特征选择唯一原始方法体。
- 给本 patch 内嵌源码生成的匿名类型加 `SoflanSupport` 唯一前缀，避免多个 patch 合并同名匿名类型。
- 移除旧 Cecil 无法可靠写回的 `NullableAttribute` / `NullableContextAttribute` 编译期元数据。
- 兼容已知的两种 `GameCtrl.UpdateCtrl` 可见性条件 IL 形状。

大多数必要锚点找不到时，patch 应用会抛出 `[SoflanRules]` 错误并停止，避免产生半套运行时。唯独可见性派发子步骤采用 graceful skip：找不到其条件模式时会向 patcher 控制台输出 `visibility-check pattern not found` 或 `dispatch failed`，游戏可能仍能启动，但停车、反向和折返谱面会缺少正确注册逻辑。因此升级游戏程序集或混用其它 patch 后，必须检查 MonoMod 启动输出并做静态/游戏内验证。

## 兼容边界

- patch 针对当前 `Assembly-CSharp.dll` 的类型、字段和 IL 形状，不保证跨游戏版本直接兼容。
- 目标程序集、Unity 模块和 MonoMod/Cecil 应来自同一游戏安装；混合版本可能在编译、patch 应用或运行时失败。
- patch 只改变视觉时间轴和辅助调试行为，不修改歌曲播放速度或 note 判定窗口。
- 无 `SFL` 的谱面会回到原版 note 视觉路径；`P` 键和 Debug-only 工具仍属于全局 patch 行为。

## 验证入口

推荐在部署前执行：

```powershell
dotnet build -c Release .\Assembly-CSharp.SoflanSupport.mm.csproj
dotnet build -c Debug .\Assembly-CSharp.SoflanSupport.mm.csproj
dotnet run --project .\tools\SoflanMarkerTests\SoflanMarkerTests.csproj -c Release
dotnet run --project .\tools\SoflanLogTests\SoflanLogTests.csproj -c Release
dotnet run --project .\tools\SoflanMaiBugTests\SoflanMaiBugTests.csproj -c Release
```

各工具的输入、输出和限制见 [离线工具与验证](tools.md)。完整验收还需要至少覆盖无 SFL、1x、停车、负速、多 group、Hold、Touch、FixedSoflan 和 P1/P2 独立状态的游戏内场景。
