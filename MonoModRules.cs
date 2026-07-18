// MonoModRules — 在 patch 时运行, 通过 PostProcessor 对 4 个目标方法做精确 IL 插入.
// 对应 head 中无法用 orig_ 表达的"方法体中间插入":
//   1. NotesReader.loadMa2Main : calcBPMList 前 __SoflanClearAll ; calcTotal 后 __SoflanLoadComposition
//   2. NotesReader.loadNote    : ret 前 __SoflanLoadNote(noteData, rec, this)
//   3. GameCtrl.UpdateCtrl     : UserOption 后 __SoflanClearCache ; msec 检查前 __SoflanNoteDecision 派发
//   4. GameProcess.OnUpdate    : 方法起始 __SoflanUpdateGamePlayFumenController
// 锚点基于被检视的真实 base IL (Mono.Cecil). 插入调用引用的辅助方法由 patch_ 类复制进目标类型.
using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.InlineRT;

namespace MonoMod
{
    public class MonoModRules
    {
        static MonoModRules()
        {
            var modder = MonoModRulesManager.Modder;
            RenameEmbeddedAnonymousTypes(modder);
            modder.PostProcessors += PostProcess;
        }

        private static void PostProcess(MonoModder modder)
        {
            var module = modder.Module;
            PatchLoadMa2Main(module);
            PatchLoadNote(module);
            PatchUpdateCtrl(module);
            PatchOnUpdate(module);
            StripCompilerNullableMetadata(module);
            // 注: SimpleSoflanFramework.Core 源码已通过 Shared Project 直接内置进 .mm.dll,
            // 运行时无需再加载外部 Core.dll, 故原 DependencyAssemblyResolver.Register() 注入已移除.
        }

        // ---------------- helpers ----------------

        // C# 匿名类型位于模块顶层，名称通常从 <>f__AnonymousType0 开始。
        // 多个 .mm.dll 各自编译匿名类型时会得到相同名称；MonoMod 会把它们误判为同一目标类型并合并，
        // 若属性结构不同便留下重复构造函数/方法签名。Rules 静态构造发生在 PrePatch 之前，
        // 因此在这里给 Soflan patch 模块内的匿名类型加唯一前缀，模块内 TypeRef 会随 TypeDefinition
        // 一同使用新名称，且无需修改内嵌 SimpleSoflanFramework 子模块源码。
        private static void RenameEmbeddedAnonymousTypes(MonoModder modder)
        {
            const string patchModuleName = "Assembly-CSharp.SoflanSupport.mm.dll";
            const string anonymousPrefix = "<>f__AnonymousType";
            const string uniquePrefix = "<>f__SoflanSupportAnonymousType";

            if (modder == null)
                throw new Exception("[SoflanRules] MonoModder is unavailable while renaming anonymous types");

            var patchModule = modder.Mods
                .OfType<ModuleDefinition>()
                .FirstOrDefault(m => m.Name == patchModuleName);
            if (patchModule == null)
                throw new Exception($"[SoflanRules] patch module not found: {patchModuleName}");

            foreach (var type in patchModule.Types
                         .Where(t => t.Name.StartsWith(anonymousPrefix, StringComparison.Ordinal))
                         .ToArray())
            {
                var oldName = type.Name;
                type.Name = uniquePrefix + oldName.Substring(anonymousPrefix.Length);
                System.Console.WriteLine($"[SoflanRules] renamed compiler type {oldName} -> {type.Name}");
            }
        }

        private static MethodDefinition GetMethod(ModuleDefinition module, string typeFullName, string methodName, int paramCount)
        {
            var type = module.GetType(typeFullName);
            if (type == null)
                throw new Exception($"[SoflanRules] type not found: {typeFullName}");
            var method = type.Methods.FirstOrDefault(m => m.Name == methodName && m.Parameters.Count == paramCount)
                         ?? type.Methods.FirstOrDefault(m => m.Name == methodName);
            if (method == null || method.Body == null)
                throw new Exception($"[SoflanRules] method not found: {typeFullName}::{methodName}");
            return method;
        }

        // MonoMod PR #208 会在多个 .mm.dll 包装同一方法时保留 orig_/patched_ 调用链。
        // PostProcessor 运行于所有 patch_ 方法复制完成之后，因此最外层同名方法可能只剩包装逻辑，
        // 原版 IL 则位于 orig_method 或 patched_method[_n]。按方法名会选错目标，必须再以原始 IL
        // 的稳定特征筛选唯一方法体，才能与 Mine 等其他补丁共存。
        private static MethodDefinition GetPatchChainMethodByBody(
            ModuleDefinition module,
            string typeFullName,
            string methodName,
            int paramCount,
            Func<MethodDefinition, bool> bodyPredicate,
            string expectedBodyDescription)
        {
            var type = module.GetType(typeFullName);
            if (type == null)
                throw new Exception($"[SoflanRules] type not found: {typeFullName}");

            var origName = "orig_" + methodName;
            var patchedName = "patched_" + methodName;
            var candidates = type.Methods
                .Where(m => m.Parameters.Count == paramCount
                            && m.Body != null
                            && (m.Name == methodName
                                || m.Name == origName
                                || m.Name == patchedName
                                || m.Name.StartsWith(patchedName + "_", StringComparison.Ordinal)))
                .ToArray();
            var matches = candidates.Where(bodyPredicate).ToArray();
            if (matches.Length != 1)
            {
                var candidateNames = candidates.Length == 0
                    ? "<none>"
                    : string.Join(", ", candidates.Select(m => m.Name));
                var matchedNames = matches.Length == 0
                    ? "<none>"
                    : string.Join(", ", matches.Select(m => m.Name));
                throw new Exception(
                    $"[SoflanRules] expected exactly one {typeFullName}::{methodName}/{paramCount} " +
                    $"patch-chain body with {expectedBodyDescription}; candidates=[{candidateNames}], " +
                    $"matches=[{matchedNames}]");
            }

            var selected = matches[0];
            if (selected.Name != methodName)
                System.Console.WriteLine(
                    $"[SoflanRules] {typeFullName}::{methodName} original IL selected from {selected.Name}");
            return selected;
        }

        private static MethodDefinition GetOwnMethod(TypeDefinition type, string name)
        {
            var m = type.Methods.FirstOrDefault(x => x.Name == name);
            if (m == null)
                throw new Exception($"[SoflanRules] helper method not found on {type.FullName}: {name}");
            return m;
        }

        private static bool IsCallTo(Instruction ins, string calleeName)
        {
            return (ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt)
                   && ins.Operand is MethodReference mr && mr.Name == calleeName;
        }

        // C# record types emit NullableAttribute metadata even when nullable reference
        // analysis is disabled. The old Mono.Cecil bundled with BepInEx 5.4 does not
        // import those copied attribute constructors when it writes the patched module.
        // They are compile-time nullability hints only, so remove them from every
        // target member before MonoMod writes the final Assembly-CSharp module.
        private static void StripCompilerNullableMetadata(ModuleDefinition module)
        {
            var removed = 0;
            removed += StripNullableAttributes(module);
            if (module.Assembly != null)
                removed += StripNullableAttributes(module.Assembly);

            foreach (var type in module.GetTypes())
            {
                removed += StripNullableAttributes(type);

                foreach (var genericParameter in type.GenericParameters)
                    removed += StripNullableAttributes(genericParameter);

                foreach (var interfaceImplementation in type.Interfaces)
                    removed += StripNullableAttributes(interfaceImplementation);

                foreach (var field in type.Fields)
                    removed += StripNullableAttributes(field);

                foreach (var property in type.Properties)
                    removed += StripNullableAttributes(property);

                foreach (var eventDefinition in type.Events)
                    removed += StripNullableAttributes(eventDefinition);

                foreach (var method in type.Methods)
                {
                    removed += StripNullableAttributes(method);
                    removed += StripNullableAttributes(method.MethodReturnType);

                    foreach (var genericParameter in method.GenericParameters)
                        removed += StripNullableAttributes(genericParameter);

                    foreach (var parameter in method.Parameters)
                        removed += StripNullableAttributes(parameter);
                }
            }

            if (removed > 0)
                System.Console.WriteLine($"[SoflanRules] stripped {removed} compiler nullable attributes");
        }

        private static int StripNullableAttributes(ICustomAttributeProvider provider)
        {
            if (provider == null || !provider.HasCustomAttributes)
                return 0;

            var removed = 0;
            for (var i = provider.CustomAttributes.Count - 1; i >= 0; i--)
            {
                var fullName = provider.CustomAttributes[i].AttributeType.FullName;
                if (fullName != "System.Runtime.CompilerServices.NullableAttribute"
                    && fullName != "System.Runtime.CompilerServices.NullableContextAttribute")
                    continue;

                provider.CustomAttributes.RemoveAt(i);
                removed++;
            }

            return removed;
        }

        private static bool TryGetLdlocVariable(Mono.Cecil.Cil.MethodBody body, Instruction ins, out VariableDefinition variable)
        {
            variable = null;
            if (ins.OpCode == OpCodes.Ldloc || ins.OpCode == OpCodes.Ldloc_S)
            {
                variable = ins.Operand as VariableDefinition;
                return variable != null;
            }

            int index;
            if (ins.OpCode == OpCodes.Ldloc_0) index = 0;
            else if (ins.OpCode == OpCodes.Ldloc_1) index = 1;
            else if (ins.OpCode == OpCodes.Ldloc_2) index = 2;
            else if (ins.OpCode == OpCodes.Ldloc_3) index = 3;
            else return false;

            if (index < 0 || index >= body.Variables.Count)
                return false;

            variable = body.Variables[index];
            return true;
        }

        // ---------------- 1. NotesReader.loadMa2Main ----------------
        private static void PatchLoadMa2Main(ModuleDefinition module)
        {
            const string typeName = "Manager.NotesReader";
            var type = module.GetType(typeName);
            var method = GetPatchChainMethodByBody(
                module,
                typeName,
                "loadMa2Main",
                3,
                m => m.Body.Instructions.Any(i => IsCallTo(i, "calcBPMList"))
                     && m.Body.Instructions.Any(i => IsCallTo(i, "calcTotal")),
                "calls to calcBPMList and calcTotal");
            var body = method.Body;
            var il = body.GetILProcessor();

            var clearAll = GetOwnMethod(type, "__SoflanClearAll");
            var loadComp = GetOwnMethod(type, "__SoflanLoadComposition");

            // (a) calcBPMList 调用前插入 call __SoflanClearAll()
            var calcBPM = body.Instructions.FirstOrDefault(i => IsCallTo(i, "calcBPMList"));
            if (calcBPM == null) throw new Exception("[SoflanRules] loadMa2Main: anchor calcBPMList not found");
            il.InsertBefore(calcBPM, il.Create(OpCodes.Call, clearAll));

            // (b) calcTotal 调用后插入 ldarg.2(records) ldarg.0(this) call __SoflanLoadComposition
            var calcTotal = body.Instructions.FirstOrDefault(i => IsCallTo(i, "calcTotal"));
            if (calcTotal == null) throw new Exception("[SoflanRules] loadMa2Main: anchor calcTotal not found");
            // InsertAfter 需逆序以保持正序: 目标序列 [ldarg.2, ldarg.0, call]
            il.InsertAfter(calcTotal, il.Create(OpCodes.Call, loadComp));
            il.InsertAfter(calcTotal, il.Create(OpCodes.Ldarg_0));
            il.InsertAfter(calcTotal, il.Create(OpCodes.Ldarg_2));
        }

        // ---------------- 2. NotesReader.loadNote ----------------
        private static void PatchLoadNote(ModuleDefinition module)
        {
            const string typeName = "Manager.NotesReader";
            var type = module.GetType(typeName);
            var method = GetPatchChainMethodByBody(
                module,
                typeName,
                "loadNote",
                4,
                m => m.Body.Variables.Any(v => v.VariableType.FullName == "Manager.NoteData"),
                "a Manager.NoteData local");
            var body = method.Body;
            var il = body.GetILProcessor();
            var loadNoteHelper = GetOwnMethod(type, "__SoflanLoadNote");

            // noteData 局部: 类型 Manager.NoteData
            var noteDataVar = body.Variables.FirstOrDefault(v => v.VariableType.FullName == "Manager.NoteData")
                              ?? throw new Exception("[SoflanRules] loadNote: NoteData local not found");

            // 锚点: 末尾 ret (loadNote 仅单个 ret)
            var ret = body.Instructions.LastOrDefault(i => i.OpCode == OpCodes.Ret)
                      ?? throw new Exception("[SoflanRules] loadNote: ret not found");
            // InsertBefore 连续调用产生书写顺序(正序), 故按期望执行顺序书写:
            //   [ldloc noteData, ldarg.1(rec), ldarg.0(this), call]
            // 先插入的指令离 ret 较远(先执行), 调用指令紧贴 ret(最后执行).
            il.InsertBefore(ret, il.Create(OpCodes.Ldloc, noteDataVar));
            il.InsertBefore(ret, il.Create(OpCodes.Ldarg_1));
            il.InsertBefore(ret, il.Create(OpCodes.Ldarg_0));
            il.InsertBefore(ret, il.Create(OpCodes.Call, loadNoteHelper));
        }

        // ---------------- 3. GameCtrl.UpdateCtrl ----------------
        private static void PatchUpdateCtrl(ModuleDefinition module)
        {
            const string typeName = "Monitor.Game.GameCtrl";
            var type = module.GetType(typeName);
            var method = GetMethod(module, typeName, "UpdateCtrl", 0);
            var body = method.Body;
            var il = body.GetILProcessor();
            var clearCache = GetOwnMethod(type, "__SoflanClearCache");
            var decision = GetOwnMethod(type, "__SoflanNoteDecision");

            // (a) UserOption 字段赋值后插入 ldarg.0 callvirt __SoflanClearCache
            //     锚点: ldfld GameScoreList::UserOption (唯一). InsertAfter 逆序: [ldarg.0, callvirt]
            var userOptLdfld = body.Instructions.FirstOrDefault(i =>
                i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference fr && fr.Name == "UserOption")
                ?? throw new Exception("[SoflanRules] UpdateCtrl: anchor ldfld UserOption not found");
            il.InsertAfter(userOptLdfld, il.Create(OpCodes.Callvirt, clearCache));
            il.InsertAfter(userOptLdfld, il.Create(OpCodes.Ldarg_0));

            // (b) soflan 可见性派发: 插在原 msec 可见性检查的 GetCurrentMsec 调用前.
            //     锚点模式 (兼容两种编译器输出):
            //       call GetCurrentMsec; ldloc(note); ldflda time; call get_msec; ldloc(num); sub;
            //       然后是条件跳转 — 旧编译器直接 blt.un*; 新编译器 clt.un; stloc; ldloc; brfalse.s + br.
            //     语义: GetCurrentMsec() - note.time.msec < num → 不可见(continue); 否则可见(after=RegistNote).
            //     AFTER/CONTINUE 跨 leave, 用 beq.s→br 跳板 (见下).
            //     锚点匹配失败时 graceful skip (不抛异常), 避免整个 patch 应用失败导致游戏无法启动.
            try
            {
                int gcmIdx = -1;
                Instruction continueTarget = null;
                Instruction afterTarget = null;
                VariableDefinition noteVar = null, numVar = null;
                var instrs = body.Instructions;
                for (int i = 0; i + 6 < instrs.Count; i++)
                {
                    if (!IsCallTo(instrs[i], "GetCurrentMsec")) continue;
                    if (!TryGetLdlocVariable(body, instrs[i + 1], out var candidateNoteVar)) continue;
                    if (!(instrs[i + 2].OpCode == OpCodes.Ldflda && instrs[i + 2].Operand is FieldReference f2 && f2.Name == "time")) continue;
                    if (!IsCallTo(instrs[i + 3], "get_msec")) continue;
                    if (!TryGetLdlocVariable(body, instrs[i + 4], out var candidateNumVar)) continue;
                    if (instrs[i + 5].OpCode != OpCodes.Sub) continue;

                    // 旧: sub; blt.un* CONTINUE  (blt 跳 CONTINUE=不可见; fall-through=AFTER=可见)
                    if (instrs[i + 6].OpCode == OpCodes.Blt_Un || instrs[i + 6].OpCode == OpCodes.Blt_Un_S)
                    {
                        gcmIdx = i;
                        noteVar = candidateNoteVar;
                        numVar = candidateNumVar;
                        continueTarget = (Instruction)instrs[i + 6].Operand;
                        afterTarget = instrs[i + 7];
                        break;
                    }
                    // 新: sub; clt.un; stloc; ldloc; brfalse.s AFTER; br CONTINUE
                    if (instrs[i + 6].OpCode == OpCodes.Clt_Un && i + 9 < instrs.Count
                        && (instrs[i + 7].OpCode == OpCodes.Stloc || instrs[i + 7].OpCode == OpCodes.Stloc_S)
                        && TryGetLdlocVariable(body, instrs[i + 8], out _)
                        && (instrs[i + 9].OpCode == OpCodes.Brfalse || instrs[i + 9].OpCode == OpCodes.Brfalse_S)
                        && i + 10 < instrs.Count
                        && (instrs[i + 10].OpCode == OpCodes.Br || instrs[i + 10].OpCode == OpCodes.Br_S))
                    {
                        gcmIdx = i;
                        noteVar = candidateNoteVar;
                        numVar = candidateNumVar;
                        afterTarget = (Instruction)instrs[i + 9].Operand;      // brfalse 目标 = 可见(AFTER)
                        continueTarget = (Instruction)instrs[i + 10].Operand;  // br 目标 = 不可见(CONTINUE)
                        break;
                    }
                }

                if (gcmIdx >= 0)
                {
                    var getCurrentMsec = instrs[gcmIdx];

                    // soflan 可见性派发. decision 返回: 0=非soflan(走原msec检查), 1=可见(跳AFTER=RegistNote流程),
                    // 2=不可见(跳CONTINUE=MoveNext).
                    // AFTER/CONTINUE 在原方法中位于 leave 之后 (跨 EH leave). Mono verifier 拒绝条件分支跨 leave,
                    // 故用 beq.s 跳到紧邻的无条件 br, 再由 br 跨 leave (无条件 br 同 try 内前向跨 leave 合法).
                    var decVar = new VariableDefinition(module.TypeSystem.Int32);
                    body.Variables.Add(decVar);

                    var dispatchStart = il.Create(OpCodes.Ldarg_0);
                    var fallbackToOriginalCheck = il.Create(OpCodes.Br, getCurrentMsec);
                    var brToAfter = il.Create(OpCodes.Br, afterTarget);
                    var brToContinue = il.Create(OpCodes.Br, continueTarget);

                    il.InsertBefore(getCurrentMsec, dispatchStart);
                    il.InsertBefore(getCurrentMsec, il.Create(OpCodes.Ldloc, noteVar));
                    il.InsertBefore(getCurrentMsec, il.Create(OpCodes.Ldloc, numVar));
                    il.InsertBefore(getCurrentMsec, il.Create(OpCodes.Callvirt, decision));
                    il.InsertBefore(getCurrentMsec, il.Create(OpCodes.Stloc, decVar));
                    il.InsertBefore(getCurrentMsec, il.Create(OpCodes.Ldloc, decVar));
                    il.InsertBefore(getCurrentMsec, il.Create(OpCodes.Ldc_I4_1));
                    il.InsertBefore(getCurrentMsec, il.Create(OpCodes.Beq_S, brToAfter));
                    il.InsertBefore(getCurrentMsec, il.Create(OpCodes.Ldloc, decVar));
                    il.InsertBefore(getCurrentMsec, il.Create(OpCodes.Ldc_I4_2));
                    il.InsertBefore(getCurrentMsec, il.Create(OpCodes.Beq_S, brToContinue));
                    il.InsertBefore(getCurrentMsec, fallbackToOriginalCheck);
                    il.InsertBefore(getCurrentMsec, brToAfter);
                    il.InsertBefore(getCurrentMsec, brToContinue);

                    foreach (var instr in body.Instructions)
                    {
                        if (instr == fallbackToOriginalCheck)
                            continue;

                        if (instr.Operand == getCurrentMsec)
                        {
                            instr.Operand = dispatchStart;
                        }
                        else if (instr.Operand is Instruction[] targets)
                        {
                            for (int t = 0; t < targets.Length; t++)
                            {
                                if (targets[t] == getCurrentMsec)
                                    targets[t] = dispatchStart;
                            }
                        }
                    }
                }
                else
                {
                    System.Console.WriteLine("[SoflanRules] UpdateCtrl: visibility-check pattern not found, skip (b) dispatch");
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[SoflanRules] UpdateCtrl (b) dispatch failed: " + ex.Message);
            }
        }

        // ---------------- 4. GameProcess.OnUpdate ----------------
        private static void PatchOnUpdate(ModuleDefinition module)
        {
            const string typeName = "Process.GameProcess";
            var type = module.GetType(typeName);
            var method = GetMethod(module, typeName, "OnUpdate", 0);
            var body = method.Body;
            var il = body.GetILProcessor();
            var helper = GetOwnMethod(type, "__SoflanUpdateGamePlayFumenController");

            var first = body.Instructions[0];
            il.InsertBefore(first, il.Create(OpCodes.Call, helper));
        }
    }
}
