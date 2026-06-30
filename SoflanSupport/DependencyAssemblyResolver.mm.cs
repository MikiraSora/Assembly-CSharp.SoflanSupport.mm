// SoflanSupport.DependencyAssemblyResolver — 运行时依赖解析器 (BepInEx + MonoMod 形态).
//
// 背景: 注入的 SoflanSupport 类型引用外部 SimpleSoflanFramework.Core.dll (提供 OngekiFumenEditor.Core.*
// 命名空间). BepInEx/monomod/ 只是 patch 输入目录, 不是运行时程序集探测路径; 修补后的 Assembly-CSharp
// 在运行时首次触碰注入类型时会因找不到 OngekiFumenEngine.Core.* 而 TypeLoadException.
//
// 策略 (主动预加载为主, AssemblyResolve 为辅):
//   Register() 在 4 个目标方法起始被调用 (早于任何注入类型的首次 JIT). 它立即用 Assembly.LoadFrom 把
//   白名单依赖从 resolver 自身目录或 BepInEx/monomod/ 加载进 AppDomain. 这样后续 SoflanManager 等类型
//   解析字段类型时, mono_domain_assembly_lookup 直接命中已加载 assembly, 不依赖 AssemblyResolve 触发
//   (Mono 在类型加载内部路径上对托管 AssemblyResolve 的触发不可靠).
//   同时仍挂 AppDomain.AssemblyResolve 作为兜底, 处理预加载之后的按需解析.
//
// Mount: 由 MonoModRules 在 4 个目标方法 (loadMa2Main/loadNote/UpdateCtrl/OnUpdate) 起始注入
//        call Register(), 保证在首次引用任何注入类型之前挂载. 不使用 [RuntimeInitializeOnLoadMethod]
//        —— 该属性在 BepInEx 修补后的程序集上不会触发.
//
// 引用约束: 本类型只引用 mscorlib/System/UnityEngine (运行时始终可用), 不引用 SimpleSoflanFramework,
//          故 JIT 本类型 / Register() / LoadFrom 不会触发依赖加载.
//
// 日志: 同时 UnityEngine.Debug.Log 与写文件 SoflanDepResolver.log (相对工作目录), 双保险, 避免依赖
//       BepInEx 是否路由 Debug.Log.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SoflanSupport
{
    internal static class DependencyAssemblyResolver
    {
        // 仅解析 patch 自己的依赖 (简单名, 不区分大小写). 其余交给默认解析器, 避免劫持其他 mod.
        private static readonly HashSet<string> OwnDependencies =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "SimpleSoflanFramework.Core",
            };

        private const string LogFile = "SoflanDepResolver.log";

        private static string[] _searchDirs;
        private static bool _registered;

        // 由 patch 注入的调用点在首次引用注入类型前调用. 幂等.
        internal static void Register()
        {
            if (_registered) return;
            _registered = true;
            _searchDirs = ResolveSearchDirs();
            Log("Register: searchDirs=[" +
                (_searchDirs == null ? "<null>" : string.Join(", ", _searchDirs)) + "]");

            // 主动预加载: 用 Assembly.Load(简单名) 把依赖装入【主上下文】, 使 Mono 字段类型加载器
            // (mono_domain_assembly_lookup) 能命中. 预先 AppendPrivatePath(monomod + Managed) 让主上下文
            // 的 Load 能从这些目录解析. 注意: 早期用 LoadFrom 失败 —— LoadFrom 进的是 LoadFrom 上下文,
            // 反射可见但字段类型解析器的主 lookup 查不到, 导致 "Could not load type of field" TypeLoadException.
            AppendSearchPaths();
            foreach (var dep in OwnDependencies)
                TryPreloadMain(dep);

            // 兜底: 若主 Load 仍漏 (如版本号微差), handler 从 monomod/ 手动 LoadFrom 服务.
            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromLocal;
        }

        // 把依赖目录注册为私有探测路径, 使主上下文 Assembly.Load(简单名) 能从中解析.
        // AppendPrivatePath 在 Mono 上有效 (MonoMod/BepInEx 生态广泛使用); obsolete 仅是 .NET Core 提示.
        private static void AppendSearchPaths()
        {
            var dirs = _searchDirs;
            if (dirs == null) return;
            foreach (var dir in dirs)
            {
                try { AppDomain.CurrentDomain.AppendPrivatePath(dir); Log("AppendPrivatePath: " + dir); }
                catch (Exception ex) { Log("AppendPrivatePath FAILED " + dir + " : " + ex.Message); }
            }
        }

        // 主上下文预加载: Assembly.Load(简单名). 成功后该 assembly 进入主 lookup 表,
        // 字段类型解析器即可命中.
        private static void TryPreloadMain(string dep)
        {
            try
            {
                var a = Assembly.Load(new AssemblyName(dep));
                Log("preloaded(main) '" + dep + "' -> " + a.FullName);
                return;
            }
            catch (Exception ex) { Log("preload(main) Load '" + dep + "' FAILED: " + ex.GetType().Name + ": " + ex.Message); }

            // 主 Load 失败则退回 LoadFrom (至少让反射可见, 并寄望 AssemblyResolve 兜底).
            var dirs = _searchDirs;
            if (dirs == null) return;
            foreach (var dir in dirs)
            {
                var path = Path.Combine(dir, dep + ".dll");
                if (File.Exists(path))
                {
                    try { var a = Assembly.LoadFrom(path); Log("preloaded(loadfrom) '" + dep + "' from " + path); return; }
                    catch (Exception ex) { Log("preload(loadfrom) '" + dep + "' from " + path + " FAILED: " + ex.Message); }
                }
            }
            Log("preload NOT FOUND '" + dep + "' in any search dir");
        }

        private static void TryPreload(string dep)
        {
            var dirs = _searchDirs;
            if (dirs == null) { Log("preload SKIP '" + dep + "': no search dirs"); return; }
            foreach (var dir in dirs)
            {
                var path = Path.Combine(dir, dep + ".dll");
                if (File.Exists(path))
                {
                    try
                    {
                        var a = Assembly.LoadFrom(path);
                        Log("preloaded '" + dep + "' from " + path + " (asm=" + a.FullName + ")");
                        return;
                    }
                    catch (Exception ex) { Log("preload FAILED '" + dep + "' from " + path + " : " + ex); }
                }
            }
            Log("preload NOT FOUND '" + dep + "' in any search dir");
        }

        private static Assembly ResolveFromLocal(object sender, ResolveEventArgs e)
        {
            var name = new AssemblyName(e.Name).Name;
            if (!OwnDependencies.Contains(name)) return null;   // 交还默认解析器

            // 优先主上下文 Load (与字段类型解析器同一 lookup 表).
            try { var a = Assembly.Load(new AssemblyName(name)); Log("resolve(main) '" + name + "' -> " + a.FullName); return a; }
            catch { }

            var dirs = _searchDirs;
            if (dirs == null) return null;
            foreach (var dir in dirs)
            {
                var path = Path.Combine(dir, name + ".dll");
                if (File.Exists(path))
                {
                    try { var a = Assembly.LoadFrom(path); Log("resolve(loadfrom) '" + name + "' from " + path); return a; }
                    catch (Exception ex) { Log("resolve failed '" + name + "' from " + path + " : " + ex.Message); }
                }
            }
            Log("resolve NOT FOUND '" + name + "' in any search dir");
            return null;
        }

        private static string[] ResolveSearchDirs()
        {
            var dirs = new List<string>(2);
            // ① resolver 自身程序集目录 (LoadDumpedAssemblies=true 时 patched 程序集从 DumpedAssemblies/ 加载,
            //    此处即 patched Assembly-CSharp 所在目录; 候选 ② 才是真正命中 monomod/ 的关键).
            var selfDir = GetOwnAssemblyDir();
            if (selfDir != null) dirs.Add(selfDir);
            // ② BepInEx/monomod/ —— 依赖 DLL 与 .mm.dll 同放此处.
            var monoDir = GetMonomodDir();
            if (monoDir != null && !dirs.Contains(monoDir)) dirs.Add(monoDir);
            return dirs.ToArray();
        }

        private static string GetOwnAssemblyDir()
        {
            try
            {
                var loc = typeof(DependencyAssemblyResolver).Assembly.Location;
                if (!string.IsNullOrEmpty(loc))
                {
                    var d = Path.GetDirectoryName(loc);
                    if (Directory.Exists(d)) return d;
                }
            }
            catch { }
            return null;
        }

        private static string GetMonomodDir()
        {
            // 优先反射 BepInEx.Paths.BepInExRootPath (避免硬编译期引用 BepInEx.dll).
            try
            {
                var t = Type.GetType("BepInEx.Paths, BepInEx");
                var p = t != null ? t.GetProperty("BepInExRootPath", BindingFlags.Public | BindingFlags.Static) : null;
                var root = p != null ? p.GetValue(null, null) as string : null;
                if (!string.IsNullOrEmpty(root))
                {
                    var d = Path.Combine(root, "monomod");
                    if (Directory.Exists(d)) return d;
                }
            }
            catch { }
            // fallback: dataPath = <gameRoot>/<Game>_Data; monomod = <gameRoot>/BepInEx/monomod
            try
            {
                var gameRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var d = Path.GetFullPath(Path.Combine(gameRoot, "BepInEx", "monomod"));
                if (Directory.Exists(d)) return d;
            }
            catch { }
            return null;
        }

        private static void Log(string msg)
        {
            // 双通道: Debug.Log (BepInEx 路由到 LogOutput.log) + 文件 (兜底, 不依赖路由).
            try { Debug.Log("[SoflanDepResolver] " + msg); } catch { }
            try { File.AppendAllText(LogFile, "[SoflanDepResolver] " + msg + Environment.NewLine); } catch { }
        }
    }
}
