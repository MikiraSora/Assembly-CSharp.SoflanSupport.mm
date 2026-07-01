// Ma2Parser — 解析 ma2 谱面文件, 提取 RESOLUTION / BPM_DEF / BPM / SFL / Note 记录.
// 与游戏 NotesReader + SoflanManager.loadComposition/loadNote 的解析逻辑一致.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SoflanCalculator
{
    /// <summary>
    /// ma2 文件中的一条 BPM 变更记录 (BPM 行).
    /// </summary>
    public struct BpmRecord
    {
        public int Bar;
        public int Grid;
        public float Bpm;
    }

    /// <summary>
    /// ma2 文件中的一条 SFL (soflan) 记录.
    /// 字段含义与 SoflanManager.tryParseSoflan 完全一致.
    /// </summary>
    public struct SflRecord
    {
        public int Unit;       // bar
        public int Grid;       // within-bar grid
        public int Length;     // grid offset (duration)
        public float Speed;
        public int SoflanGroup;
    }

    /// <summary>
    /// ma2 文件中的一条 Note 记录, 附带物理行号 (1-based).
    /// </summary>
    public struct NoteRecord
    {
        public int LineNumber;   // 1-based physical line in file
        public string Type;      // e.g. "NMTAP"
        public int Bar;
        public int Grid;
        public int Pos;
        public int SoflanGroup;  // from #N suffix, default 0
    }

    /// <summary>
    /// ma2 文件解析结果.
    /// </summary>
    public class Ma2Data
    {
        public int Resolution = 384;
        public float FirstBpm = 120f;
        public List<BpmRecord> BpmChanges = new List<BpmRecord>();
        public List<SflRecord> Soflans = new List<SflRecord>();
        public List<NoteRecord> Notes = new List<NoteRecord>();
    }

    public static class Ma2Parser
    {
        // 所有 note 类型 tag (MA2_Note category).
        // 来源: Ma2fileRecordID.s_Ma2fileRecord_Data 中 category == MA2_Note 的条目.
        private static readonly HashSet<string> NoteTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // 旧格式
            "TAP","BRK","XTP","HLD","XHO","STR","BST","XST","TTP","THO",
            "SI_","SCL","SCR","SUL","SUR","SSL","SSR","SV_","SXL","SXR","SLL","SLR","SF_",
            // 新格式 — Tap 系
            "NMTAP","BRTAP","EXTAP","BXTAP",
            // 新格式 — Hold 系
            "NMHLD","BRHLD","EXHLD","BXHLD",
            // 新格式 — Star 系
            "NMSTR","BRSTR","EXSTR","BXSTR",
            // 新格式 — Touch 系
            "NMTTP","NMTHO",
            // 新格式 — Slide 系 (直线/外周/U字/雷/V字/〆字/L字/扇)
            "NMSI_","BRSI_","EXSI_","BXSI_","CNSI_",
            "NMSCL","BRSCL","EXSCL","BXSCL","CNSCL",
            "NMSCR","BRSCR","EXSCR","BXSCR","CNSCR",
            "NMSUL","BRSUL","EXSUL","BXSUL","CNSUL",
            "NMSUR","BRSUR","EXSUR","BXSUR","CNSUR",
            "NMSSL","BRSSL","EXSSL","BXSSL","CNSSL",
            "NMSSR","BRSSR","EXSSR","BXSSR","CNSSR",
            "NMSV_","BRSV_","EXSV_","BXSV_","CNSV_",
            "NMSXL","BRSXL","EXSXL","BXSXL","CNSXL",
            "NMSXR","BRSXR","EXSXR","BXSXR","CNSXR",
            "NMSLL","BRSLL","EXSLL","BXSLL","CNSLL",
            "NMSLR","BRSLR","EXSLR","BXSLR","CNSLR",
            "NMSF_","BRSF_","EXSF_","BXSF_","CNSF_",
        };

        /// <summary>
        /// 判断指定 tag 是否为 note 记录类型.
        /// </summary>
        public static bool IsNoteTag(string tag)
        {
            return NoteTags.Contains(tag);
        }

        /// <summary>
        /// 解析 ma2 文件, 返回完整的 Ma2Data.
        /// </summary>
        public static Ma2Data Parse(string filePath)
        {
            var data = new Ma2Data();
            var lines = File.ReadAllLines(filePath);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Tab 分割, 与游戏 MA2Record.init 一致
                var fields = line.Split('\t');
                if (fields.Length == 0)
                    continue;

                // 去除各字段首尾空白
                for (int j = 0; j < fields.Length; j++)
                    fields[j] = fields[j].Trim();

                string tag = fields[0];
                if (string.IsNullOrEmpty(tag))
                    continue;

                int lineNumber = i + 1; // 1-based

                switch (tag.ToUpperInvariant())
                {
                    case "RESOLUTION":
                        data.Resolution = ParseInt(fields, 1, 384);
                        break;

                    case "BPM_DEF":
                        // BPM_DEF  firstBPM  defaultBPM  maxBPM  minBPM
                        data.FirstBpm = ParseFloat(fields, 1, 120f);
                        break;

                    case "BPM":
                        // BPM  bar  grid  bpm
                        data.BpmChanges.Add(new BpmRecord
                        {
                            Bar = ParseInt(fields, 1, 0),
                            Grid = ParseInt(fields, 2, 0),
                            Bpm = ParseFloat(fields, 3, 120f)
                        });
                        break;

                    case "SFL":
                        // SFL  unit  grid  length  speed  [soflanGroup]
                        if (TryParseSfl(fields, out var sfl))
                            data.Soflans.Add(sfl);
                        break;

                    default:
                        if (IsNoteTag(tag))
                        {
                            var note = new NoteRecord
                            {
                                LineNumber = lineNumber,
                                Type = tag,
                                Bar = ParseInt(fields, 1, 0),
                                Grid = ParseInt(fields, 2, 0),
                                Pos = ParseInt(fields, 3, 0),
                                SoflanGroup = ExtractSoflanGroup(fields)
                            };
                            data.Notes.Add(note);
                        }
                        break;
                }
            }

            return data;
        }

        /// <summary>
        /// 解析 SFL 行, 逻辑与 SoflanManager.tryParseSoflan 一致.
        /// SFL  unit(bar)  grid  length  speed  [soflanGroup]
        /// </summary>
        private static bool TryParseSfl(string[] p, out SflRecord sfl)
        {
            try
            {
                sfl = new SflRecord
                {
                    Unit = int.Parse(p[1]),
                    Grid = int.Parse(p[2]),
                    Length = int.Parse(p[3]),
                    Speed = float.Parse(p[4]),
                    SoflanGroup = 0
                };
                var group = p.Length > 5 ? p[5] : null;
                if (!string.IsNullOrWhiteSpace(group))
                    sfl.SoflanGroup = int.Parse(group);
                return true;
            }
            catch
            {
                sfl = default;
                return false;
            }
        }

        /// <summary>
        /// 从 note 记录字段末尾提取 #N soflan 组号.
        /// 逻辑与 SoflanManager.loadNote 一致: Reverse 遍历找第一个 "#" 开头的字段.
        /// </summary>
        private static int ExtractSoflanGroup(string[] fields)
        {
            for (int i = fields.Length - 1; i >= 0; i--)
            {
                if (fields[i] != null && fields[i].StartsWith("#"))
                {
                    if (int.TryParse(fields[i].TrimStart('#').Trim(), out var group))
                        return group;
                    // 解析失败则停止查找 (与游戏一致)
                    return 0;
                }
            }
            return 0;
        }

        private static int ParseInt(string[] fields, int index, int defaultVal)
        {
            if (index < 0 || index >= fields.Length)
                return defaultVal;
            if (string.IsNullOrEmpty(fields[index]))
                return defaultVal;
            return int.TryParse(fields[index], out var v) ? v : defaultVal;
        }

        private static float ParseFloat(string[] fields, int index, float defaultVal)
        {
            if (index < 0 || index >= fields.Length)
                return defaultVal;
            if (string.IsNullOrEmpty(fields[index]))
                return defaultVal;
            return float.TryParse(fields[index], out var v) ? v : defaultVal;
        }
    }
}
