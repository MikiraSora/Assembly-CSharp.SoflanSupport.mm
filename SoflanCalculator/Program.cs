// Program — SoflanCalculator 入口.
// 用法: SoflanCalculator.exe <ma2文件路径> <物件行号(1-based)> <当前时间msec>
//       进入后可交互输入 line= / ma2= / time= / speed= 切换并重新计算.
using System;
using System.IO;
using System.Linq;

namespace SoflanCalculator
{
    internal static class Program
    {
        // ===== 游戏画面常量 =====
        // 从 Unity prefab (NoteStart/NoteEnd transform) 读取, 典型值.
        const float StartPos = 120f;
        const float EndPos = 400f;

        // 物件速度值 (对应 OptionNotespeedID 表中的 Value 字段):
        //   Speed1.0=200, Speed5.0=600, Speed10.0=1100, Sonic=5000
        // 运行时可由 speed= 命令修改.
        private static float s_noteSpeedValue = 600f;  // 默认 Speed 5.0

        private static int Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("用法: SoflanCalculator.exe <ma2文件路径> <物件行号(1-based)> <当前时间msec>");
                Console.Error.WriteLine("示例: SoflanCalculator.exe \"D:\\charts\\008.ma2\" 42 2500");
                return 1;
            }

            string filePath = args[0];
            if (!int.TryParse(args[1], out int lineNumber))
            {
                Console.Error.WriteLine($"错误: 物件行号无效 -> {args[1]}");
                return 1;
            }
            if (!float.TryParse(args[2], out float currentMsec))
            {
                Console.Error.WriteLine($"错误: 当前时间msec无效 -> {args[2]}");
                return 1;
            }

            // --- 加载 ma2 ---
            Ma2Data data = LoadMa2(filePath);
            if (data == null)
                return 1;

            // --- 首次计算 + 输出 ---
            if (!TryComputeAndPrint(data, filePath, lineNumber, currentMsec))
                return 1;

            // --- 交互循环 ---
            InteractiveLoop(data, filePath, lineNumber, currentMsec);
            return 0;
        }

        // ==================== 交互循环 ====================

        private static void InteractiveLoop(Ma2Data data, string filePath, int lineNumber, float currentMsec)
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine($"[当前] ma2={Path.GetFileName(filePath)}  line={lineNumber}  time={currentMsec:F0}  speed={s_noteSpeedValue:F0}({SpeedLabel(s_noteSpeedValue)})");
                Console.Write("> ");
                string input = Console.ReadLine();
                if (input == null)
                    break; // EOF

                input = input.Trim();
                if (input.Length == 0)
                    continue;

                if (input.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
                    input.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                    input.Equals("q", StringComparison.OrdinalIgnoreCase))
                    break;

                // 解析命令: line=xxx / ma2=xxxx / time=xxx / speed=xxx (可组合, 空格分隔)
                string newMa2 = null;
                int? newLine = null;
                float? newTime = null;
                float? newSpeed = null;

                bool parseError = false;
                foreach (var token in input.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = token.IndexOf('=');
                    if (eq <= 0)
                    {
                        Console.Error.WriteLine($"  无法识别: {token}  (格式: line=42 / ma2=path / time=2500 / speed=600)");
                        parseError = true;
                        continue;
                    }
                    string key = token.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = token.Substring(eq + 1).Trim();

                    switch (key)
                    {
                        case "line":
                            if (int.TryParse(val, out int lv))
                                newLine = lv;
                            else
                            {
                                Console.Error.WriteLine($"  line 解析失败: {val}");
                                parseError = true;
                            }
                            break;

                        case "ma2":
                            newMa2 = val;
                            break;

                        case "time":
                            if (float.TryParse(val, out float tv))
                                newTime = tv;
                            else
                            {
                                Console.Error.WriteLine($"  time 解析失败: {val}");
                                parseError = true;
                            }
                            break;

                        case "speed":
                            if (float.TryParse(val, out float sv))
                                newSpeed = sv;
                            else
                            {
                                Console.Error.WriteLine($"  speed 解析失败: {val}");
                                parseError = true;
                            }
                            break;

                        default:
                            Console.Error.WriteLine($"  未知命令: {key}  (可用: line / ma2 / time / speed)");
                            parseError = true;
                            break;
                    }
                }

                if (parseError)
                    continue;

                // 应用变更
                if (newMa2 != null)
                {
                    // 可能路径含空格但用户未加引号; 尝试把 ma2= 之后的所有内容当路径
                    // (如果 ma2= 是最后一个 token, 上面已正确; 否则需要从原始 input 提取)
                    if (newMa2.Length == 0 || !File.Exists(newMa2))
                    {
                        // 尝试从原始输入中提取 ma2= 之后的完整路径
                        int ma2Idx = input.IndexOf("ma2=", StringComparison.OrdinalIgnoreCase);
                        if (ma2Idx >= 0)
                        {
                            string afterMa2 = input.Substring(ma2Idx + 4).Trim();
                            // 如果后面还有其他命令, 截断到下一个命令
                            int nextCmd = FindNextCommand(afterMa2);
                            if (nextCmd >= 0)
                                afterMa2 = afterMa2.Substring(0, nextCmd).Trim();
                            if (File.Exists(afterMa2))
                                newMa2 = afterMa2;
                        }
                    }

                    var newData = LoadMa2(newMa2);
                    if (newData == null)
                        continue;
                    data = newData;
                    filePath = newMa2;
                }

                if (newLine.HasValue)
                    lineNumber = newLine.Value;

                if (newTime.HasValue)
                    currentMsec = newTime.Value;

                if (newSpeed.HasValue)
                    s_noteSpeedValue = newSpeed.Value;

                TryComputeAndPrint(data, filePath, lineNumber, currentMsec);
            }
        }

        /// <summary>
        /// 在字符串中查找下一个命令关键字 (line= / ma2= / time= / speed=) 的起始位置,
        /// 用于从 ma2= 的值中截断后续命令.
        /// </summary>
        private static int FindNextCommand(string s)
        {
            int earliest = -1;
            foreach (var cmd in new[] { "line=", "ma2=", "time=", "speed=" })
            {
                int idx = s.IndexOf(cmd, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0 && (earliest < 0 || idx < earliest))
                    earliest = idx;
            }
            return earliest;
        }

        // ==================== 加载 / 计算 / 输出 ====================

        private static Ma2Data LoadMa2(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Console.Error.WriteLine($"错误: 文件不存在 -> {filePath}");
                return null;
            }
            try
            {
                return Ma2Parser.Parse(filePath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"错误: 解析 ma2 文件失败 -> {ex.Message}");
                return null;
            }
        }

        private static bool TryComputeAndPrint(Ma2Data data, string filePath, int lineNumber, float currentMsec)
        {
            var note = data.Notes.FirstOrDefault(n => n.LineNumber == lineNumber);
            if (note.LineNumber == 0)
            {
                Console.Error.WriteLine($"错误: 第 {lineNumber} 行不是 note 记录, 或该行不存在。");
                Console.Error.WriteLine($"  文件共 {data.Notes.Count} 条 note 记录。");
                if (data.Notes.Count > 0)
                {
                    Console.Error.WriteLine("  可用 note 行号:");
                    foreach (var n in data.Notes.Take(20))
                        Console.Error.WriteLine($"    行 {n.LineNumber}: {n.Type}  bar={n.Bar}  grid={n.Grid}  pos={n.Pos}  group={n.SoflanGroup}");
                    if (data.Notes.Count > 20)
                        Console.Error.WriteLine($"    ... 还有 {data.Notes.Count - 20} 条");
                }
                return false;
            }

            CalcResult r;
            try
            {
                r = SoflanCalcEngine.Calculate(data, note, currentMsec, s_noteSpeedValue, StartPos, EndPos);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"错误: 计算失败 -> {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                return false;
            }

            PrintResult(r, Path.GetFileName(filePath));
            return true;
        }

        private static void PrintResult(CalcResult r, string fileName)
        {
            Console.WriteLine("=== Soflan Calculator ===");
            Console.WriteLine($"File: {fileName}");
            Console.WriteLine($"Line: {r.LineNumber} ({r.NoteType}  bar={r.Bar}  grid={r.Grid}  pos={r.Pos}  soflanGroup={r.SoflanGroup})");
            Console.WriteLine();

            Console.WriteLine("--- Parameters ---");
            Console.WriteLine($"NoteSpeedValue:   {r.NoteSpeedValue:F1}  (Speed {SpeedLabel(r.NoteSpeedValue)})");
            Console.WriteLine($"speedRatio:       {r.SpeedRatio:F3}");
            Console.WriteLine($"DefaultMsec:      {r.DefaultMsec:F3}");
            Console.WriteLine($"MaiBugAdjustMSec: {r.MaiBugAdjustMSec:F3}");
            Console.WriteLine($"StartPos:         {r.StartPos:F3}");
            Console.WriteLine($"EndPos:            {r.EndPos:F3}");
            Console.WriteLine();

            Console.WriteLine("--- Note Timing ---");
            Console.WriteLine($"AppearMsec:        {r.AppearMsec:F3}");
            Console.WriteLine($"noteSoflanTime:    {r.NoteSoflanTime:F3}");
            Console.WriteLine($"currentMsec:       {r.CurrentMsec:F3}");
            Console.WriteLine($"currentSoflanTime: {r.CurrentSoflanTime:F3}");
            Console.WriteLine($"currentSoflanSpeed: {r.CurrentSoflanSpeed:F3}x  (group {r.SoflanGroup})");
            Console.WriteLine();

            Console.WriteLine("--- Computed Values ---");
            Console.WriteLine($"diffTime:          {r.DiffTime:F3}");
            Console.WriteLine($"absDiffTime:       {r.AbsDiffTime:F3}");
            Console.WriteLine($"scaleStartTime:    {r.ScaleStartTime:F3}");
            Console.WriteLine($"moveStartTime:     {r.MoveStartTime:F3}");
            Console.WriteLine($"NoteStat:          {r.NoteStat}");
            Console.WriteLine($"moveProgress:      {r.MoveProgress:F3}");
            Console.WriteLine($"finalScale:        {r.FinalScale:F3}");
            Console.WriteLine($"insideY:           {r.InsideY:F3}");
            Console.WriteLine($"outsideY:          {r.OutsideY:F3}");
            Console.WriteLine($"soflanY:           {r.SoflanY:F3}");
            Console.WriteLine($"clipedSoflanY:     {r.ClipedSoflanY:F3}");

            if (!r.ContainsSoflans)
            {
                Console.WriteLine();
                Console.WriteLine("(谱面无 SFL 记录, soflanTime == 原始 msec)");
            }
        }

        private static string SpeedLabel(float noteSpeedValue)
        {
            // 对应 OptionNotespeedID 枚举的 Value 值
            if (Math.Abs(noteSpeedValue - 200f) < 0.1f) return "1.0";
            if (Math.Abs(noteSpeedValue - 600f) < 0.1f) return "5.0";
            if (Math.Abs(noteSpeedValue - 1100f) < 0.1f) return "10.0";
            if (Math.Abs(noteSpeedValue - 5000f) < 0.1f) return "Sonic";
            return noteSpeedValue.ToString("F1");
        }
    }
}
