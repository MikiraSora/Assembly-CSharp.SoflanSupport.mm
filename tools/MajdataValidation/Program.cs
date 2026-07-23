using System.Globalization;
using System.Text;
using MajSimai;
using SoflanSupport;

var ma2Path = args.Length > 0
    ? args[0]
    : @"F:\SDEZ_165\Package\Sinmai_Data\StreamingAssets\A000\music\music003999\003999_02.ma2.bak_20260703023239";
var majdataPath = args.Length > 1
    ? args[1]
    : @"F:\SDEZ_165\Package\Sinmai_Data\StreamingAssets\A000\music\music003999\003999_02.ma2.bak_20260703023239.majdata.txt";
var inoteIndex = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 3;

var ma2 = ReadMa2(ma2Path);
using var stream = File.OpenRead(majdataPath);
var simai = SimaiParser.Parse(stream, Encoding.UTF8);
var chart = simai.Charts[inoteIndex - 1];

var parsed = ReadParsedChart(chart, ma2.Resolution);

var noteComparison = CompareMultiset(ma2.Notes, parsed.Notes);
var speedComparison = CompareExpectedWithinActual(ma2.Speeds, parsed.SpeedDeclarations);
var bpmComparison = CompareMultiset(ma2.Bpms, parsed.Bpms);

var report = new
{
    Ma2 = new
    {
        ma2.Resolution,
        Bpms = ma2.Bpms.Count,
        Sfl = ma2.Speeds.Count,
        Notes = ma2.Notes.Count,
    },
    MajdataParsedByMajSimai = new
    {
        NoteTimingPoints = chart.NoteTimings.Length,
        CommaTimingPoints = chart.CommaTimings.Length,
        Bpms = parsed.Bpms.Count,
        SpeedDeclarations = parsed.SpeedDeclarations.Count,
        ExtraSpeedDeclarations = parsed.SpeedDeclarations.Count - ma2.Speeds.Count,
        Notes = parsed.Notes.Count,
    },
    Compare = new
    {
        Bpm = bpmComparison,
        SflStarts = speedComparison,
        Notes = noteComparison,
    }
};

Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions
{
    WriteIndented = true
}));

if (!bpmComparison.Equal || !speedComparison.Equal || !noteComparison.Equal)
{
    Environment.ExitCode = 1;
}

static Ma2Data ReadMa2(string path)
{
    var resolution = 384L;
    var bpms = new List<string>();
    var speeds = new List<string>();
    var notes = new List<string>();

    foreach (var rawLine in File.ReadLines(path, Encoding.UTF8))
    {
        var line = rawLine.Trim();
        if (line.Length == 0)
        {
            continue;
        }

        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            continue;
        }

        switch (parts[0])
        {
            case "RESOLUTION":
                resolution = long.Parse(parts[1], CultureInfo.InvariantCulture);
                break;
            case "BPM":
            {
                var grid = ToTotalGrid(parts[1], parts[2], resolution);
                var bpm = FormatFloat(float.Parse(parts[3], CultureInfo.InvariantCulture));
                bpms.Add($"{grid}|{bpm}");
                break;
            }
            case "SFL":
            {
                var grid = ToTotalGrid(parts[1], parts[2], resolution);
                var speed = FormatFloat(float.Parse(parts[4], CultureInfo.InvariantCulture));
                var group = int.Parse(parts[5], CultureInfo.InvariantCulture);
                speeds.Add($"{grid}|{group}|{speed}");
                break;
            }
            case "NMTAP":
            case "NMSTR":
            case "EXTAP":
            case "EXSTR":
            case "BRTAP":
            case "BRSTR":
            case "BXTAP":
            case "BXSTR":
            {
                var grid = ToTotalGrid(parts[1], parts[2], resolution);
                var pos = int.Parse(parts[3], CultureInfo.InvariantCulture) + 1;
                var group = 0;
                SoflanMarkerParseResult marker;
                string markerReason;
                if (!SoflanMarkerParser.TryParse(parts, out marker, out markerReason))
                {
                    throw new InvalidDataException($"Invalid Soflan marker at MA2 line: {line}; reason={markerReason}");
                }
                if (marker.HasMarker)
                    group = marker.Group;
                notes.Add($"{grid}|{group}|{pos}");
                break;
            }
        }
    }

    return new Ma2Data(resolution, bpms, speeds, notes);
}

static ParsedChartData ReadParsedChart(SimaiChart chart, long resolution)
{
    var notes = new List<string>();
    var speedDeclarations = new List<string>();
    var bpms = new List<string>();

    var commaTimings = chart.CommaTimings.ToArray()
        .OrderBy(x => x.Timing)
        .ThenBy(x => x.RawTextPosition)
        .ToArray();

    if (commaTimings.Length > 0)
    {
        var currentBpm = commaTimings[0].Bpm;
        var bpmBaseGrid = 0L;
        var bpmBaseTime = 0d;
        bpms.Add($"0|{FormatFloat(currentBpm)}");

        foreach (var tp in commaTimings)
        {
            var grid = CalculateGrid(tp.Timing, bpmBaseTime, bpmBaseGrid, currentBpm, resolution);
            if (Math.Abs(tp.Bpm - currentBpm) > float.Epsilon)
            {
                bpms.Add($"{grid}|{FormatFloat(tp.Bpm)}");
                currentBpm = tp.Bpm;
                bpmBaseTime = tp.Timing;
                bpmBaseGrid = grid;
            }
        }
    }

    var noteTimings = chart.NoteTimings.ToArray()
        .OrderBy(x => x.Timing)
        .ThenBy(x => x.RawTextPosition)
        .ToArray();

    var currentGridBpm = commaTimings.Length > 0 ? commaTimings[0].Bpm : noteTimings.FirstOrDefault()?.Bpm ?? 0f;
    var currentGridBase = 0L;
    var currentGridBaseTime = 0d;
    var commaIndex = 0;

    foreach (var tp in noteTimings)
    {
        while (commaIndex < commaTimings.Length && commaTimings[commaIndex].Timing <= tp.Timing)
        {
            var comma = commaTimings[commaIndex];
            var commaGrid = CalculateGrid(comma.Timing, currentGridBaseTime, currentGridBase, currentGridBpm, resolution);
            if (Math.Abs(comma.Bpm - currentGridBpm) > float.Epsilon)
            {
                currentGridBpm = comma.Bpm;
                currentGridBaseTime = comma.Timing;
                currentGridBase = commaGrid;
            }
            commaIndex++;
        }

        var grid = CalculateGrid(tp.Timing, currentGridBaseTime, currentGridBase, currentGridBpm, resolution);

        if (tp.SoflanGroup != 0 && tp.RawContent.Length == 0)
        {
            var speed = FormatFloat(tp.HSpeed);
            speedDeclarations.Add($"{grid}|{tp.SoflanGroup}|{speed}");
        }

        foreach (var note in tp.Notes)
        {
            if (note.Type != SimaiNoteType.Tap)
            {
                continue;
            }

            notes.Add($"{grid}|{note.SoflanGroup}|{note.StartPosition}");
        }
    }

    return new ParsedChartData(bpms, speedDeclarations, notes);
}

static long ToTotalGrid(string bar, string grid, long resolution)
{
    return long.Parse(bar, CultureInfo.InvariantCulture) * resolution
        + long.Parse(grid, CultureInfo.InvariantCulture);
}

static long CalculateGrid(double time, double baseTime, long baseGrid, float bpm, long resolution)
{
    var delta = time - baseTime;
    if (delta == 0)
    {
        return baseGrid;
    }

    return baseGrid + (long)Math.Round(delta * (resolution * bpm) / 240d);
}

static string FormatFloat(float value)
{
    return value.ToString("G9", CultureInfo.InvariantCulture);
}

static ComparisonResult CompareMultiset(IEnumerable<string> expectedItems, IEnumerable<string> actualItems)
{
    var expected = BuildMap(expectedItems);
    var actual = BuildMap(actualItems);
    var missing = new List<string>();
    var extra = new List<string>();

    foreach (var (key, count) in expected)
    {
        actual.TryGetValue(key, out var actualCount);
        for (var i = actualCount; i < count; i++)
        {
            missing.Add(key);
        }
    }

    foreach (var (key, count) in actual)
    {
        expected.TryGetValue(key, out var expectedCount);
        for (var i = expectedCount; i < count; i++)
        {
            extra.Add(key);
        }
    }

    return new ComparisonResult(missing.Count == 0 && extra.Count == 0, missing.Count, extra.Count, missing.Take(10).ToArray(), extra.Take(10).ToArray());
}

static ComparisonResult CompareExpectedWithinActual(IEnumerable<string> expectedItems, IEnumerable<string> actualItems)
{
    var expected = BuildMap(expectedItems);
    var actual = BuildMap(actualItems);
    var missing = new List<string>();
    var extra = new List<string>();

    foreach (var (key, count) in expected)
    {
        actual.TryGetValue(key, out var actualCount);
        for (var i = actualCount; i < count; i++)
        {
            missing.Add(key);
        }
    }

    foreach (var (key, count) in actual)
    {
        expected.TryGetValue(key, out var expectedCount);
        for (var i = expectedCount; i < count; i++)
        {
            extra.Add(key);
        }
    }

    return new ComparisonResult(missing.Count == 0, missing.Count, extra.Count, missing.Take(10).ToArray(), extra.Take(10).ToArray());
}

static Dictionary<string, int> BuildMap(IEnumerable<string> items)
{
    var map = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var item in items)
    {
        map[item] = map.TryGetValue(item, out var count) ? count + 1 : 1;
    }
    return map;
}

sealed record Ma2Data(long Resolution, List<string> Bpms, List<string> Speeds, List<string> Notes);
sealed record ParsedChartData(List<string> Bpms, List<string> SpeedDeclarations, List<string> Notes);
sealed record ComparisonResult(bool Equal, int MissingCount, int ExtraCount, string[] MissingSample, string[] ExtraSample);
