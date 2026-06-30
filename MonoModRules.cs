// MonoModRules — 在 patch 时运行, 通过 PostProcessor 对 4 个目标方法做精确 IL 插入.
// 对应 head 中无法用 orig_ 表达的"方法体中间插入":
//   1. NotesReader.loadMa2Main : calcBPMList 前 __SoflanClearAll ; calcTotal 后 __SoflanLoadComposition
//   2. NotesReader.loadNote    : ret 前 __SoflanLoadNote(noteData, rec, this)
//   3. GameCtrl.UpdateCtrl     : UserOption 后 __SoflanClearCache ; msec 检查前 __SoflanNoteDecision 派发 ;
//                                 第2个 RegistNote 的 break 前 __SoflanLogRegistNoteFailed
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
            MonoModRulesManager.Modder.PostProcessors += PostProcess;
        }

        private static void PostProcess(MonoModder modder)
        {
            var module = modder.Module;
            PatchLoadMa2Main(module);
            PatchLoadNote(module);
            PatchUpdateCtrl(module);
            PatchOnUpdate(module);
            // 最后: 在 4 个目标方法起始注入 call DependencyAssemblyResolver.Register(),
            // 保证在首次引用任何注入类型 (→ SimpleSoflanFramework.Core) 之前挂载运行时依赖解析器.
            // 放最后, 使各方法的 InsertBefore(Instructions[0]) 把 Register() 置为新的首条指令
            // (OnUpdate 此时首条已是 PatchOnUpdate 插入的 __SoflanUpdateGamePlayFumenController,
            //  Register 会插到它之前, 故顺序为 Register(); __SoflanUpdateGamePlayFumenController(); ...).
            InjectResolverRegister(module);
        }

        // ---------------- 5. 运行时依赖解析器 mount ----------------
        private static void InjectResolverRegister(ModuleDefinition module)
        {
            var resolverType = module.GetType("SoflanSupport.DependencyAssemblyResolver");
            if (resolverType == null)
                throw new Exception("[SoflanRules] DependencyAssemblyResolver type not found in target module");
            var register = resolverType.Methods.FirstOrDefault(m => m.Name == "Register")
                           ?? throw new Exception("[SoflanRules] DependencyAssemblyResolver.Register not found");
            var registerRef = module.ImportReference(register);

            // 4 个目标方法均可能成为最早引用注入类型的入口 (OnUpdate=主循环每帧; loadMa2Main/loadNote=
            // 谱面加载; UpdateCtrl=游戏更新). 全部 mount, Register() 幂等, 多次调用无害.
            InjectAtStart(module, "Manager.NotesReader", "loadMa2Main", 3, registerRef);
            InjectAtStart(module, "Manager.NotesReader", "loadNote", 4, registerRef);
            InjectAtStart(module, "Monitor.Game.GameCtrl", "UpdateCtrl", 0, registerRef);
            InjectAtStart(module, "Process.GameProcess", "OnUpdate", 0, registerRef);
        }

        private static void InjectAtStart(ModuleDefinition module, string typeFullName, string methodName, int paramCount, MethodReference registerRef)
        {
            var method = GetMethod(module, typeFullName, methodName, paramCount);
            var il = method.Body.GetILProcessor();
            var first = method.Body.Instructions[0];
            il.InsertBefore(first, il.Create(OpCodes.Call, registerRef));
        }

        // ---------------- helpers ----------------

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

        // ---------------- 1. NotesReader.loadMa2Main ----------------
        private static void PatchLoadMa2Main(ModuleDefinition module)
        {
            const string typeName = "Manager.NotesReader";
            var type = module.GetType(typeName);
            var method = GetMethod(module, typeName, "loadMa2Main", 3);
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
            var method = GetMethod(module, typeName, "loadNote", 4);
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
            var logFailed = GetOwnMethod(type, "__SoflanLogRegistNoteFailed");

            // (a) UserOption 字段赋值后插入 ldarg.0 callvirt __SoflanClearCache
            //     锚点: ldfld GameScoreList::UserOption (唯一). InsertAfter 逆序: [ldarg.0, callvirt]
            var userOptLdfld = body.Instructions.FirstOrDefault(i =>
                i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference fr && fr.Name == "UserOption")
                ?? throw new Exception("[SoflanRules] UpdateCtrl: anchor ldfld UserOption not found");
            il.InsertAfter(userOptLdfld, il.Create(OpCodes.Callvirt, clearCache));
            il.InsertAfter(userOptLdfld, il.Create(OpCodes.Ldarg_0));

            // (b) soflan 可见性派发: 插在原 msec 检查的 GetCurrentMsec 调用前
            //     模式: call GetCurrentMsec; ldloc(note); ldflda TimingBase::time; call get_msec; ldloc(num); sub; blt.un*
            int gcmIdx = -1;
            var instrs = body.Instructions;
            for (int i = 0; i + 6 < instrs.Count; i++)
            {
                if (!IsCallTo(instrs[i], "GetCurrentMsec")) continue;
                if (instrs[i + 1].OpCode != OpCodes.Ldloc && instrs[i + 1].OpCode != OpCodes.Ldloc_S) continue;
                if (!(instrs[i + 2].OpCode == OpCodes.Ldflda && instrs[i + 2].Operand is FieldReference f2 && f2.Name == "time")) continue;
                if (!IsCallTo(instrs[i + 3], "get_msec")) continue;
                if (instrs[i + 4].OpCode != OpCodes.Ldloc && instrs[i + 4].OpCode != OpCodes.Ldloc_S) continue;
                if (instrs[i + 5].OpCode != OpCodes.Sub) continue;
                if (instrs[i + 6].OpCode != OpCodes.Blt_Un && instrs[i + 6].OpCode != OpCodes.Blt_Un_S) continue;
                gcmIdx = i;
                break;
            }
            if (gcmIdx < 0) throw new Exception("[SoflanRules] UpdateCtrl: visibility-check pattern not found");

            var getCurrentMsec = instrs[gcmIdx];
            var noteVar = (VariableDefinition)((instrs[gcmIdx + 1].Operand));
            var numVar = (VariableDefinition)((instrs[gcmIdx + 4].Operand));
            var blt = instrs[gcmIdx + 6];
            var continueTarget = (Instruction)blt.Operand;          // continue (MoveNext)
            var afterTarget = instrs[gcmIdx + 7];                    // 原 if 之后

            // soflan 可见性派发. decision 返回: 0=非soflan(走原msec检查), 1=可见(跳AFTER=RegistNote流程),
            // 2=不可见(跳CONTINUE=MoveNext).
            //
            // 关键: AFTER/CONTINUE 在原方法中位于 leave 指令之后 (跨 EH leave 边界). Mono verifier 拒绝
            // 条件分支(beq)跨越 leave, 故先用短条件跳(beq.s)到紧邻的无条件 br, 再由 br 跨 leave 到目标.
            // br(无条件) 在同 try 内前向跨 leave 合法.
            //
            // 新增局部存 decision 结果, 因 beq.s 消耗操作数后无法复用.
            var decVar = new VariableDefinition(module.TypeSystem.Int32);
            body.Variables.Add(decVar);

            // 先创建跳板标签 (无条件 br), 稍后 InsertBefore 填充.
            var brToAfter = il.Create(OpCodes.Br, afterTarget);
            var brToContinue = il.Create(OpCodes.Br, continueTarget);

            // InsertBefore(getCurrentMsec) 按书写顺序(正序)执行. 期望:
            //   ldarg.0; ldloc note; ldloc num; callvirt decision; stloc decVar
            //   ldloc decVar; ldc.i4.1; beq.s brToAfter
            //   ldloc decVar; ldc.i4.2; beq.s brToContinue
            //   (fall-through 到 getCurrentMsec 原 msec 检查)
            //   brToAfter: br AFTER          (跨 leave)
            //   brToContinue: br CONTINUE    (跨 leave)
            il.InsertBefore(getCurrentMsec, il.Create(OpCodes.Ldarg_0));
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
            // fall-through (decision==0): 跳过两个跳板, 直达原 msec 检查.
            il.InsertBefore(getCurrentMsec, il.Create(OpCodes.Br, getCurrentMsec));
            // 跳板: 由 beq.s 跳入, 再用无条件 br 跨 leave 到目标.
            il.InsertBefore(getCurrentMsec, brToAfter);
            il.InsertBefore(getCurrentMsec, brToContinue);

            // (c) 第 2 个 RegistNote 失败 break 前 插入 ldarg.0 ldloc(note) callvirt __SoflanLogRegistNoteFailed
            //     note = RegistNote 调用前一条 ldloc (note 实参)
            var registNotes = body.Instructions.Where(i => IsCallTo(i, "RegistNote")).ToList();
            if (registNotes.Count < 2) throw new Exception("[SoflanRules] UpdateCtrl: expected >=2 RegistNote calls, got " + registNotes.Count);
            var secondRegist = registNotes[1];
            int sIdx = instrs.IndexOf(secondRegist);
            // 前一条应为 ldloc(note)
            var noteArgInstr = instrs[sIdx - 1];
            if (noteArgInstr.OpCode != OpCodes.Ldloc && noteArgInstr.OpCode != OpCodes.Ldloc_S)
                throw new Exception("[SoflanRules] UpdateCtrl: RegistNote note-arg ldloc not found");
            var noteArgVar = (VariableDefinition)noteArgInstr.Operand;
            // 向后跳过 brtrue/brtrue.s, 找到 leave/leave.s (break)
            int j = sIdx + 1;
            while (j < instrs.Count && (instrs[j].OpCode == OpCodes.Brtrue || instrs[j].OpCode == OpCodes.Brtrue_S)) j++;
            if (j >= instrs.Count || (instrs[j].OpCode != OpCodes.Leave && instrs[j].OpCode != OpCodes.Leave_S))
                throw new Exception("[SoflanRules] UpdateCtrl: RegistNote break(leave) not found");
            var leaveInstr = instrs[j];
            // InsertBefore 正序: [ldarg.0, ldloc note, callvirt]  (书写顺序即执行顺序)
            il.InsertBefore(leaveInstr, il.Create(OpCodes.Ldarg_0));
            il.InsertBefore(leaveInstr, il.Create(OpCodes.Ldloc, noteArgVar));
            il.InsertBefore(leaveInstr, il.Create(OpCodes.Callvirt, logFailed));
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
