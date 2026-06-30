using System;
using System.Collections.Generic;
using System.IO;

namespace SoflanSimulator
{
    public class BpmRecord
    {
        public int Measure;
        public int Grid;
        public double Bpm;
    }

    public class SflRecord
    {
        public int Unit;
        public int Grid;
        public int GridLength;
        public float Speed;
        public int SoflanGroup;
    }

    public class NoteRecord
    {
        public int IndexNote;
        public string Type;
        public int Measure;
        public int Grid;
        public int Lane;
        public int SoflanGroup;
        public bool Active;
    }

    public class Ma2Data
    {
        public int Resolution = 384;
        public double FirstBpm = 240;
        public List<BpmRecord> BpmRecords = new List<BpmRecord>();
        public List<SflRecord> SflRecords = new List<SflRecord>();
        public List<NoteRecord> Notes = new List<NoteRecord>();
    }

    public static class Ma2Parser
    {
        public static Ma2Data Parse(string filePath)
        {
            var data = new Ma2Data();
            int noteIndex = 0;

            foreach (var rawLine in File.ReadAllLines(filePath))
            {
                var line = rawLine.TrimEnd('\r', '\n');
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split('\t');
                if (parts.Length == 0) continue;

                var cmd = parts[0].Trim();
                if (string.IsNullOrEmpty(cmd)) continue;

                switch (cmd)
                {
                    case "RESOLUTION":
                        if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out var res))
                            data.Resolution = res;
                        break;

                    case "BPM_DEF":
                        if (parts.Length >= 2 && double.TryParse(parts[1].Trim(), out var defBpm))
                            data.FirstBpm = defBpm;
                        break;

                    case "BPM":
                        if (parts.Length >= 4
                            && int.TryParse(parts[1].Trim(), out var bMeasure)
                            && int.TryParse(parts[2].Trim(), out var bGrid)
                            && double.TryParse(parts[3].Trim(), out var bBpm))
                        {
                            data.BpmRecords.Add(new BpmRecord { Measure = bMeasure, Grid = bGrid, Bpm = bBpm });
                        }
                        break;

                    case "SFL":
                        ParseSfl(parts, data);
                        break;

                    default:
                        if (IsNoteType(cmd) && parts.Length >= 4
                            && int.TryParse(parts[1].Trim(), out var nMeasure)
                            && int.TryParse(parts[2].Trim(), out var nGrid))
                        {
                            int lane = 0;
                            if (parts.Length >= 4) int.TryParse(parts[3].Trim(), out lane);

                            int soflanGroup = ParseSoflanGroup(parts);

                            var note = new NoteRecord
                            {
                                IndexNote = noteIndex++,
                                Type = cmd,
                                Measure = nMeasure,
                                Grid = nGrid,
                                Lane = lane,
                                SoflanGroup = soflanGroup,
                                Active = IsActiveType(cmd)
                            };
                            data.Notes.Add(note);
                        }
                        break;
                }
            }

            return data;
        }

        private static void ParseSfl(string[] parts, Ma2Data data)
        {
            try
            {
                var unit = int.Parse(parts[1].Trim());
                var grid = int.Parse(parts[2].Trim());
                var gridLength = int.Parse(parts[3].Trim());
                var speed = float.Parse(parts[4].Trim());
                var group = 0;
                if (parts.Length >= 6 && !string.IsNullOrWhiteSpace(parts[5]))
                    group = int.Parse(parts[5].Trim());

                data.SflRecords.Add(new SflRecord
                {
                    Unit = unit,
                    Grid = grid,
                    GridLength = gridLength,
                    Speed = speed,
                    SoflanGroup = group
                });
            }
            catch
            {
                // skip malformed SFL line
            }
        }

        // 与 SoflanManager.loadNote 一致: 逆序遍历所有字段, 查找以 # 开头的字段.
        // parse 失败则保持 group=0 (游戏代码 return, 不设置).
        private static int ParseSoflanGroup(string[] parts)
        {
            int group = 0;
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                var str = parts[i];
                if (str != null && str.StartsWith("#"))
                {
                    var trimmed = str.TrimStart('#').Trim();
                    if (!int.TryParse(trimmed, out group))
                        return 0; // parse failed, 保持 group=0
                    // 游戏代码不 break, 继续逆序遍历 (后找到的覆盖先找到的)
                }
            }
            return group;
        }

        // *TAP 和 *STR 行类型, 排除元数据行 (T_*, TTM_*)
        private static bool IsNoteType(string type)
        {
            if (string.IsNullOrEmpty(type) || type.Length < 3)
                return false;
            if (type.StartsWith("T_") || type.StartsWith("TTM_"))
                return false;
            return type.EndsWith("TAP") || type.EndsWith("STR");
        }

        // checkSupportSoflan 仅对 BaseDef.Tap 返回 true, 对应 NMTAP
        private static bool IsActiveType(string type)
        {
            return type == "NMTAP";
        }
    }
}