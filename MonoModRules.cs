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
                    if (instrs[i + 1].OpCode != OpCodes.Ldloc && instrs[i + 1].OpCode != OpCodes.Ldloc_S) continue;
                    if (!(instrs[i + 2].OpCode == OpCodes.Ldflda && instrs[i + 2].Operand is FieldReference f2 && f2.Name == "time")) continue;
                    if (!IsCallTo(instrs[i + 3], "get_msec")) continue;
                    if (instrs[i + 4].OpCode != OpCodes.Ldloc && instrs[i + 4].OpCode != OpCodes.Ldloc_S) continue;
                    if (instrs[i + 5].OpCode != OpCodes.Sub) continue;

                    // 旧: sub; blt.un* CONTINUE  (blt 跳 CONTINUE=不可见; fall-through=AFTER=可见)
                    if (instrs[i + 6].OpCode == OpCodes.Blt_Un || instrs[i + 6].OpCode == OpCodes.Blt_Un_S)
                    {
                        gcmIdx = i;
                        noteVar = (VariableDefinition)instrs[i + 1].Operand;
                        numVar = (VariableDefinition)instrs[i + 4].Operand;
                        continueTarget = (Instruction)instrs[i + 6].Operand;
                        afterTarget = instrs[i + 7];
                        break;
                    }
                    // 新: sub; clt.un; stloc; ldloc; brfalse.s AFTER; br CONTINUE
                    if (instrs[i + 6].OpCode == OpCodes.Clt_Un && i + 9 < instrs.Count
                        && (instrs[i + 7].OpCode == OpCodes.Stloc || instrs[i + 7].OpCode == OpCodes.Stloc_S)
                        && (instrs[i + 8].OpCode == OpCodes.Ldloc || instrs[i + 8].OpCode == OpCodes.Ldloc_S)
                        && (instrs[i + 9].OpCode == OpCodes.Brfalse || instrs[i + 9].OpCode == OpCodes.Brfalse_S)
                        && i + 10 < instrs.Count
                        && (instrs[i + 10].OpCode == OpCodes.Br || instrs[i + 10].OpCode == OpCodes.Br_S))
                    {
                        gcmIdx = i;
                        noteVar = (VariableDefinition)instrs[i + 1].Operand;
                        numVar = (VariableDefinition)instrs[i + 4].Operand;
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

            // (c) 第 2 个 RegistNote 失败 break 前 插入 ldarg.0 ldloc(note) callvirt __SoflanLogRegistNoteFailed
            //     note = RegistNote 调用前一条 ldloc (note 实参). 失败时 graceful skip.
            try
            {
                var instrs = body.Instructions;
                var registNotes = body.Instructions.Where(i => IsCallTo(i, "RegistNote")).ToList();
                if (registNotes.Count < 2)
                {
                    System.Console.WriteLine("[SoflanRules] UpdateCtrl: expected >=2 RegistNote calls, got " + registNotes.Count + ", skip (c)");
                }
                else
                {
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
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[SoflanRules] UpdateCtrl (c) logFailed failed: " + ex.Message);
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
