using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MajSimai;

const decimal DefaultPulseWidthMeasures = 1m / 384m;
const decimal NumericTolerance = 0.000000000001m;
const double ParseTimingToleranceSeconds = 0.00005;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: SdtSoflanConverter <source.sdt> <reference-maidata.txt> <output-maidata.txt>");
    return 2;
}

var sourcePath = Path.GetFullPath(args[0]);
var referencePath = Path.GetFullPath(args[1]);
var outputPath = Path.GetFullPath(args[2]);

var referenceText = File.ReadAllText(referencePath, Encoding.UTF8);
var bpm = ReadWholeBpm(referenceText);
var measureSeconds = 240m / bpm;
var rows = ReadSdt(sourcePath);
var controls = rows.Where(x => x.IsControl).Select(ToControl).ToArray();
var notes = rows.Where(x => !x.IsControl).ToArray();

ValidateSource(notes, controls);

var groupMap = AllocateGroups(notes);
var speedTimeline = BuildSpeedTimeline(notes, controls, groupMap);
var noteEvents = BuildNoteEvents(notes, groupMap, measureSeconds);
var fumen = BuildFumen(notes, noteEvents, speedTimeline.Events, bpm, measureSeconds);

ValidateIntegratedTimeline(notes, controls, groupMap, speedTimeline.Events);
var parsedChart = ValidateWithMajSimai(fumen, notes, groupMap, speedTimeline.Events, measureSeconds);

var outputText = ReplaceChart(referenceText, 6, fumen);
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, outputText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

using (var stream = File.OpenRead(outputPath))
{
    var parsedFile = SimaiParser.Parse(stream, Encoding.UTF8);
    if (parsedFile.Charts[5].IsEmpty)
    {
        throw new InvalidDataException("The generated &inote_6 chart is empty after parsing the complete maidata file.");
    }
}

Console.WriteLine($"Output: {outputPath}");
Console.WriteLine($"BPM: {FormatDecimal(bpm)}");
Console.WriteLine($"SDT records: {rows.Count} ({notes.Length} notes, {controls.Length} controls)");
Console.WriteLine($"Soflan groups: {groupMap.Values.Distinct().Count(x => x != 0)}");
Console.WriteLine($"HS declarations: {speedTimeline.Events.Sum(x => x.Value.Count)} ({speedTimeline.PulseCount} jump pulses)");
Console.WriteLine($"MajSimaiX notes: {parsedChart.NoteTimings.ToArray().Sum(x => x.Notes.Length)}");
return 0;

static decimal ReadWholeBpm(string maidata)
{
    var match = Regex.Match(maidata, @"(?m)^&wholebpm=(?<bpm>[^\r\n]+)\s*$", RegexOptions.CultureInvariant);
    if (!match.Success ||
        !decimal.TryParse(match.Groups["bpm"].Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var bpm) ||
        bpm <= 0)
    {
        throw new InvalidDataException("The reference maidata has no valid positive &wholebpm value.");
    }

    return bpm;
}

static List<SdtRow> ReadSdt(string path)
{
    var text = Encoding.ASCII.GetString(File.ReadAllBytes(path)).TrimStart('\0');
    var result = new List<SdtRow>();
    var lineNumber = 0;
    var order = 0;

    foreach (var rawLine in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
    {
        lineNumber++;
        if (string.IsNullOrWhiteSpace(rawLine))
        {
            continue;
        }

        var fields = rawLine.Trim().TrimEnd(',').Replace(" ", string.Empty, StringComparison.Ordinal).Split(',');
        if (fields.Length != 9)
        {
            throw new InvalidDataException($"SDT line {lineNumber} has {fields.Length} fields instead of 9.");
        }

        order++;
        result.Add(new SdtRow(
            lineNumber,
            order,
            ParseDecimal(fields[0], lineNumber),
            ParseDecimal(fields[1], lineNumber),
            ParseDecimal(fields[2], lineNumber),
            ParseInt(fields[3], lineNumber),
            ParseInt(fields[4], lineNumber),
            ParseInt(fields[5], lineNumber),
            ParseInt(fields[6], lineNumber),
            ParseInt(fields[7], lineNumber),
            ParseDecimal(fields[8], lineNumber)));
    }

    return result;
}

static decimal ParseDecimal(string value, int lineNumber)
{
    if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
    {
        throw new InvalidDataException($"Invalid decimal at SDT line {lineNumber}: {value}");
    }

    return result;
}

static int ParseInt(string value, int lineNumber)
{
    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
    {
        throw new InvalidDataException($"Invalid integer at SDT line {lineNumber}: {value}");
    }

    return result;
}

static ControlEvent ToControl(SdtRow row)
{
    if (row.TransformIndex < 0)
    {
        throw new InvalidDataException($"Negative transform index at SDT line {row.LineNumber}.");
    }

    return new ControlEvent(
        row.Duration,
        row.TransformIndex / 2,
        row.TransformIndex % 2 == 0,
        row.Extra,
        row.Order);
}

static void ValidateSource(SdtRow[] notes, ControlEvent[] controls)
{
    var validTypes = new HashSet<int> { 0, 1, 2, 3, 4, 5, 128 };
    foreach (var note in notes)
    {
        if (note.Position is < 0 or > 7)
        {
            throw new InvalidDataException($"Invalid note position at SDT line {note.LineNumber}: {note.Position}");
        }

        if (!validTypes.Contains(note.NoteType))
        {
            throw new InvalidDataException($"Unsupported note type at SDT line {note.LineNumber}: {note.NoteType}");
        }
    }

    var starts = notes.Where(x => x.NoteType == 0).ToArray();
    var ends = notes.Where(x => x.NoteType == 128).ToArray();
    var endById = ends.GroupBy(x => x.SlideId).ToDictionary(x => x.Key, x => x.ToArray());
    foreach (var start in starts)
    {
        if (start.SlideId <= 0 || !endById.TryGetValue(start.SlideId, out var matches) || matches.Length != 1)
        {
            throw new InvalidDataException($"Slide {start.SlideId} at SDT line {start.LineNumber} has no unique end record.");
        }

        if (start.Duration <= 0 || start.Extra < 0 || start.Extra > start.Duration)
        {
            throw new InvalidDataException($"Invalid slide duration/delay at SDT line {start.LineNumber}.");
        }

        var expectedEnd = start.Measure + start.Duration;
        if (Math.Abs(matches[0].Measure - expectedEnd) > 0.0002m)
        {
            throw new InvalidDataException($"Slide {start.SlideId} end timing differs from start + duration.");
        }
    }

    if (starts.Length != ends.Length || starts.Select(x => x.SlideId).Distinct().Count() != starts.Length)
    {
        throw new InvalidDataException("Slide start/end IDs are not one-to-one.");
    }

    foreach (var duplicate in controls.GroupBy(x => new { x.Slot, x.Time, x.IsSlope }).Where(x => x.Count() > 1))
    {
        throw new InvalidDataException($"Duplicate control for slot {duplicate.Key.Slot} at measure {duplicate.Key.Time}.");
    }
}

static Dictionary<GroupKey, int> AllocateGroups(SdtRow[] notes)
{
    var keys = notes
        .Where(IsScatter)
        .Select(x => new GroupKey(x.TransformIndex, x.Extra + 1m))
        .Distinct()
        .OrderBy(x => x.Slot)
        .ThenBy(x => x.RelativeSpeed)
        .ToArray();

    var result = new Dictionary<GroupKey, int>();
    var nextGroup = 1;
    foreach (var key in keys)
    {
        result[key] = key == GroupKey.Default ? 0 : nextGroup++;
    }

    return result;
}

static SpeedTimeline BuildSpeedTimeline(
    SdtRow[] notes,
    ControlEvent[] controls,
    IReadOnlyDictionary<GroupKey, int> groupMap)
{
    var result = new SortedDictionary<decimal, SortedDictionary<int, float>>();
    var pulseCount = 0;
    var maxNoteByKey = notes
        .Where(IsScatter)
        .GroupBy(x => new GroupKey(x.TransformIndex, x.Extra + 1m))
        .ToDictionary(x => x.Key, x => x.Max(y => y.Measure));

    foreach (var slotGroups in groupMap.Where(x => x.Value != 0).GroupBy(x => x.Key.Slot))
    {
        var slot = slotGroups.Key;
        var groups = slotGroups.ToArray();
        var slotControls = controls
            .Where(x => x.Slot == slot)
            .GroupBy(x => x.Time)
            .OrderBy(x => x.Key)
            .ToArray();
        var relevantTimes = controls.Where(x => x.Slot == slot).Select(x => x.Time)
            .Concat(notes.Where(x => IsScatter(x) && x.TransformIndex == slot).Select(x => x.Measure))
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        var slope = 1m;
        var intercept = 0m;
        var zeroControls = slotControls.FirstOrDefault(x => x.Key == 0m);
        if (zeroControls is not null)
        {
            ApplyControls(zeroControls, ref slope, ref intercept);
        }

        foreach (var pair in groups)
        {
            AddSpeed(result, 0m, pair.Value, pair.Key.RelativeSpeed * slope);
        }

        foreach (var controlGroup in slotControls.Where(x => x.Key > 0m))
        {
            var time = controlGroup.Key;
            var oldSlope = slope;
            var oldIntercept = intercept;
            var before = oldSlope * time + oldIntercept;
            ApplyControls(controlGroup, ref slope, ref intercept);
            var after = slope * time + intercept;
            var jump = after - before;

            var activeGroups = groups.Where(x => maxNoteByKey[x.Key] >= time).ToArray();
            if (activeGroups.Length == 0)
            {
                continue;
            }

            if (Math.Abs(jump) > NumericTolerance)
            {
                var previousRelevantTime = relevantTimes.Where(x => x < time).DefaultIfEmpty(0m).Max();
                var available = time - previousRelevantTime;
                var pulseWidth = Math.Min(DefaultPulseWidthMeasures, available / 2m);
                if (pulseWidth <= 0m)
                {
                    throw new InvalidDataException($"Cannot place a jump pulse before slot {slot} at measure {time}.");
                }

                var pulseStart = time - pulseWidth;
                var pulseSlope = oldSlope + jump / pulseWidth;
                foreach (var pair in activeGroups)
                {
                    AddSpeed(result, pulseStart, pair.Value, pair.Key.RelativeSpeed * pulseSlope);
                }
                pulseCount++;
            }

            if (slope != oldSlope || Math.Abs(jump) > NumericTolerance)
            {
                foreach (var pair in activeGroups)
                {
                    AddSpeed(result, time, pair.Value, pair.Key.RelativeSpeed * slope);
                }
            }
        }
    }

    return new SpeedTimeline(result, pulseCount);
}

static void ApplyControls(IEnumerable<ControlEvent> controls, ref decimal slope, ref decimal intercept)
{
    foreach (var control in controls.OrderBy(x => x.Order))
    {
        if (control.IsSlope)
        {
            slope = control.Value;
        }
        else
        {
            intercept = control.Value;
        }
    }
}

static void AddSpeed(
    SortedDictionary<decimal, SortedDictionary<int, float>> events,
    decimal measure,
    int group,
    decimal speed)
{
    var value = checked((float)speed);
    if (!float.IsFinite(value))
    {
        throw new InvalidDataException($"Non-finite HS value for group {group} at measure {measure}.");
    }

    if (!events.TryGetValue(measure, out var byGroup))
    {
        byGroup = new SortedDictionary<int, float>();
        events.Add(measure, byGroup);
    }

    byGroup[group] = value;
}

static SortedDictionary<decimal, List<NoteComponent>> BuildNoteEvents(
    SdtRow[] notes,
    IReadOnlyDictionary<GroupKey, int> groupMap,
    decimal measureSeconds)
{
    var result = new SortedDictionary<decimal, List<NoteComponent>>();
    var slideStarts = notes.Where(x => x.NoteType == 0).ToArray();
    var slideEnds = notes.Where(x => x.NoteType == 128).ToDictionary(x => x.SlideId);
    var startsByHead = slideStarts.GroupBy(x => new HeadKey(x.Measure, x.Position)).ToDictionary(x => x.Key, x => x.OrderBy(y => y.Order).ToArray());
    var starsByHead = notes.Where(x => x.NoteType is 4 or 5).ToDictionary(x => new HeadKey(x.Measure, x.Position));

    foreach (var note in notes.Where(x => x.NoteType is 1 or 3).OrderBy(x => x.Order))
    {
        var token = $"{note.Position + 1}{(note.NoteType == 3 ? "b" : string.Empty)}";
        AddNoteComponent(result, note.Measure, note.Order, WrapInGroup(token, GetGroup(note, groupMap)));
    }

    foreach (var hold in notes.Where(x => x.NoteType == 2).OrderBy(x => x.Order))
    {
        var seconds = hold.Duration * measureSeconds;
        AddNoteComponent(result, hold.Measure, hold.Order, $"{hold.Position + 1}h[#{FormatDecimal(seconds)}]");
    }

    foreach (var star in notes.Where(x => x.NoteType is 4 or 5).OrderBy(x => x.Order))
    {
        var headKey = new HeadKey(star.Measure, star.Position);
        if (!startsByHead.TryGetValue(headKey, out var starts))
        {
            var standalone = $"{star.Position + 1}{(star.NoteType == 5 ? "b" : string.Empty)}$";
            AddNoteComponent(result, star.Measure, star.Order, WrapInGroup(standalone, GetGroup(star, groupMap)));
            continue;
        }

        var head = $"{star.Position + 1}{(star.NoteType == 5 ? "b" : string.Empty)}";
        var token = new StringBuilder(WrapSlideHead(head, GetGroup(star, groupMap)));
        for (var i = 0; i < starts.Length; i++)
        {
            var start = starts[i];
            var end = slideEnds[start.SlideId];
            if (i > 0)
            {
                token.Append('*');
            }
            token.Append(BuildSlidePath(start, end, measureSeconds));
        }
        AddNoteComponent(result, star.Measure, star.Order, token.ToString());
    }

    foreach (var head in startsByHead.Where(x => !starsByHead.ContainsKey(x.Key)))
    {
        var starts = head.Value;
        var token = new StringBuilder($"{head.Key.Position + 1}?");
        for (var i = 0; i < starts.Length; i++)
        {
            if (i > 0)
            {
                token.Append('*');
            }
            token.Append(BuildSlidePath(starts[i], slideEnds[starts[i].SlideId], measureSeconds));
        }
        AddNoteComponent(result, head.Key.Measure, starts[0].Order, token.ToString());
    }

    return result;
}

static string BuildSlidePath(SdtRow start, SdtRow end, decimal measureSeconds)
{
    var pattern = PatternFromInt(start.Pattern, start.Position, end.Position);
    var waitSeconds = start.Extra * measureSeconds;
    var slideSeconds = (start.Duration - start.Extra) * measureSeconds;
    return $"{pattern}{end.Position + 1}[{FormatDecimal(waitSeconds)}##{FormatDecimal(slideSeconds)}]";
}

static string PatternFromInt(int pattern, int start, int end)
{
    return pattern switch
    {
        1 => "-",
        4 => "p",
        5 => "q",
        6 => "s",
        7 => "z",
        8 => "v",
        9 => "pp",
        10 => "qq",
        13 => "w",
        2 or 3 => RingPattern(pattern, start, end),
        11 => $"V{WrapPosition(start - 2) + 1}",
        12 => $"V{WrapPosition(start + 2) + 1}",
        _ => throw new InvalidDataException($"Unsupported slide pattern: {pattern}")
    };
}

static string RingPattern(int pattern, int start, int end)
{
    var clockwise = pattern == 3;
    var distance = SlideDistance(start, end, clockwise);
    if (distance is > 0 and <= 3)
    {
        return "^";
    }

    var top = start is 0 or 1 or 6 or 7;
    if (distance == 0)
    {
        if (top && clockwise) return ">";
        if (top) return "<";
        return clockwise ? "<" : ">";
    }

    return (top && clockwise) || (!top && !clockwise) ? ">" : "<";
}

static int SlideDistance(int start, int end, bool clockwise)
{
    if (clockwise)
    {
        return (end - start + 8) % 8;
    }

    return (start - end + 8) % 8;
}

static int WrapPosition(int position) => (position % 8 + 8) % 8;

static int GetGroup(SdtRow note, IReadOnlyDictionary<GroupKey, int> groupMap)
    => groupMap[new GroupKey(note.TransformIndex, note.Extra + 1m)];

static string WrapInGroup(string token, int group)
    => group == 0 ? token : $"<HS{group}>({token})";

static string WrapSlideHead(string head, int group)
    => group == 0 ? head : $"<HS{group}>({head})";

static void AddNoteComponent(
    SortedDictionary<decimal, List<NoteComponent>> events,
    decimal measure,
    int order,
    string token)
{
    if (!events.TryGetValue(measure, out var components))
    {
        components = new List<NoteComponent>();
        events.Add(measure, components);
    }

    components.Add(new NoteComponent(order, token));
}

static string BuildFumen(
    SdtRow[] notes,
    SortedDictionary<decimal, List<NoteComponent>> noteEvents,
    SortedDictionary<decimal, SortedDictionary<int, float>> speedEvents,
    decimal bpm,
    decimal measureSeconds)
{
    var allTimes = noteEvents.Keys.Concat(speedEvents.Keys).ToHashSet();
    allTimes.Add(0m);

    var lastSourceMeasure = notes.Max(x => x.Measure + (x.NoteType == 2 ? x.Duration : 0m));
    var lastSlideEnd = notes.Where(x => x.NoteType == 128).Select(x => x.Measure).DefaultIfEmpty(0m).Max();
    var terminalMeasure = Math.Max(lastSourceMeasure, lastSlideEnd) + 1m;
    allTimes.Add(terminalMeasure);

    var orderedTimes = allTimes.OrderBy(x => x).ToArray();
    var result = new StringBuilder();
    result.Append('(').Append(FormatDecimal(bpm)).AppendLine(")");

    for (var i = 0; i < orderedTimes.Length; i++)
    {
        var time = orderedTimes[i];
        var deltaSeconds = i + 1 < orderedTimes.Length
            ? (orderedTimes[i + 1] - time) * measureSeconds
            : 0.1m;
        if (deltaSeconds <= 0m)
        {
            throw new InvalidDataException($"Non-positive Simai timing interval at measure {time}.");
        }

        var components = noteEvents.TryGetValue(time, out var noteComponents)
            ? noteComponents.OrderBy(x => x.Order).ToArray()
            : Array.Empty<NoteComponent>();
        var slotCount = Math.Max(1, components.Length);
        for (var slotIndex = 0; slotIndex < slotCount; slotIndex++)
        {
            var slotInterval = slotIndex + 1 < slotCount ? 0m : deltaSeconds;
            result.Append("{#").Append(FormatDecimal(slotInterval)).Append('}');
            if (slotIndex == 0 && speedEvents.TryGetValue(time, out var speeds))
            {
                foreach (var speed in speeds)
                {
                    result.Append("<HS").Append(speed.Key).Append('*').Append(FormatFloat(speed.Value)).Append('>');
                }
            }

            if (components.Length > 0)
            {
                result.Append(components[slotIndex].Token);
            }

            result.AppendLine(",");
        }
    }

    return result.ToString().TrimEnd('\r', '\n');
}

static void ValidateIntegratedTimeline(
    SdtRow[] notes,
    ControlEvent[] controls,
    IReadOnlyDictionary<GroupKey, int> groupMap,
    SortedDictionary<decimal, SortedDictionary<int, float>> speedEvents)
{
    foreach (var note in notes.Where(IsScatter))
    {
        var key = new GroupKey(note.TransformIndex, note.Extra + 1m);
        var group = groupMap[key];
        var sourceStart = EvaluateTransform(controls, key.Slot, 0m);
        var sourceEnd = EvaluateTransform(controls, key.Slot, note.Measure);
        var expected = (double)(key.RelativeSpeed * (sourceEnd - sourceStart));
        var actual = group == 0
            ? (double)note.Measure
            : IntegrateGroup(speedEvents, group, 0m, note.Measure);
        if (Math.Abs(actual - expected) > 0.00001)
        {
            throw new InvalidDataException(
                $"Integrated HS mismatch at SDT line {note.LineNumber}: expected {expected:G17}, actual {actual:G17}.");
        }

        if (Math.Abs(sourceEnd - note.Measure) > 0.0001m)
        {
            throw new InvalidDataException(
                $"Transform slot {key.Slot} does not return to the judgment timing at SDT line {note.LineNumber}.");
        }
    }
}

static decimal EvaluateTransform(ControlEvent[] controls, int slot, decimal time)
{
    var slope = 1m;
    var intercept = 0m;
    foreach (var control in controls.Where(x => x.Slot == slot && x.Time <= time).OrderBy(x => x.Time).ThenBy(x => x.Order))
    {
        if (control.IsSlope) slope = control.Value;
        else intercept = control.Value;
    }

    return slope * time + intercept;
}

static double IntegrateGroup(
    SortedDictionary<decimal, SortedDictionary<int, float>> events,
    int group,
    decimal start,
    decimal end)
{
    var speed = 1d;
    var cursor = start;
    var area = 0d;
    foreach (var timing in events.Where(x => x.Key <= end))
    {
        if (timing.Key <= start)
        {
            if (timing.Value.TryGetValue(group, out var initialSpeed)) speed = initialSpeed;
            continue;
        }

        area += (double)(timing.Key - cursor) * speed;
        cursor = timing.Key;
        if (timing.Value.TryGetValue(group, out var nextSpeed)) speed = nextSpeed;
    }

    area += (double)(end - cursor) * speed;
    return area;
}

static SimaiChart ValidateWithMajSimai(
    string fumen,
    SdtRow[] notes,
    IReadOnlyDictionary<GroupKey, int> groupMap,
    SortedDictionary<decimal, SortedDictionary<int, float>> speedEvents,
    decimal measureSeconds)
{
    var chart = SimaiParser.ParseChart(fumen.AsSpan(), 0, out _);
    var parsedNotes = chart.NoteTimings.ToArray().SelectMany(x => x.Notes.Select(note => new ParsedNote(x.Timing, note))).ToArray();
    var expectedLogicalNotes = notes.Count(x => x.NoteType is 1 or 2 or 3)
        + notes.Count(x => x.NoteType == 0)
        + notes.Count(x => x.NoteType is 4 or 5 && !notes.Any(y => y.NoteType == 0 && y.Measure == x.Measure && y.Position == x.Position));
    if (parsedNotes.Length != expectedLogicalNotes)
    {
        throw new InvalidDataException($"MajSimaiX parsed {parsedNotes.Length} notes; expected {expectedLogicalNotes}.");
    }

    foreach (var source in notes.Where(IsScatter))
    {
        var expectedTiming = (double)(source.Measure * measureSeconds);
        var expectedGroup = GetGroup(source, groupMap);
        var matches = parsedNotes.Where(x =>
            Math.Abs(x.Timing - expectedTiming) < ParseTimingToleranceSeconds &&
            x.Note.StartPosition == source.Position + 1 &&
            MatchesScatterType(x.Note, source.NoteType)).ToArray();
        var matchCountIsValid = source.NoteType is 4 or 5 ? matches.Length >= 1 : matches.Length == 1;
        if (!matchCountIsValid)
        {
            var candidates = parsedNotes.Where(x =>
                    x.Note.StartPosition == source.Position + 1)
                .OrderBy(x => Math.Abs(x.Timing - expectedTiming))
                .Take(5)
                .Select(x => $"dt={(x.Timing - expectedTiming):G17}:{x.Note.Type}:break={x.Note.IsBreak}:noHead={x.Note.IsSlideNoHead}:group={x.Note.SoflanGroup}:raw={x.Note.RawContent}");
            throw new InvalidDataException(
                $"Matched {matches.Length} MajSimaiX notes for SDT scatter at line {source.LineNumber}. Candidates: {string.Join("; ", candidates)}");
        }

        if (matches.Any(x => x.Note.SoflanGroup != expectedGroup))
        {
            throw new InvalidDataException($"Wrong Soflan group at SDT line {source.LineNumber}.");
        }
    }

    foreach (var source in notes.Where(x => x.NoteType == 2))
    {
        var expectedTiming = (double)(source.Measure * measureSeconds);
        var expectedHoldTime = (double)(source.Duration * measureSeconds);
        var matches = parsedNotes.Where(x =>
            Math.Abs(x.Timing - expectedTiming) < ParseTimingToleranceSeconds &&
            x.Note.Type == SimaiNoteType.Hold &&
            x.Note.StartPosition == source.Position + 1 &&
            Math.Abs(x.Note.HoldTime - expectedHoldTime) < ParseTimingToleranceSeconds).ToArray();
        if (matches.Length != 1 || matches[0].Note.SoflanGroup != 0)
        {
            throw new InvalidDataException($"Hold mismatch at SDT line {source.LineNumber}.");
        }
    }

    var remainingSlides = parsedNotes.Where(x => x.Note.Type == SimaiNoteType.Slide).ToList();
    var slideEnds = notes.Where(x => x.NoteType == 128).ToDictionary(x => x.SlideId);
    foreach (var source in notes.Where(x => x.NoteType == 0).OrderBy(x => x.Order))
    {
        var end = slideEnds[source.SlideId];
        var expectedTiming = (double)(source.Measure * measureSeconds);
        var expectedWait = (double)(source.Extra * measureSeconds);
        var expectedSlideTime = (double)((source.Duration - source.Extra) * measureSeconds);
        var pathNeedle = $"{PatternFromInt(source.Pattern, source.Position, end.Position)}{end.Position + 1}[";
        var match = remainingSlides.FirstOrDefault(x =>
            Math.Abs(x.Timing - expectedTiming) < ParseTimingToleranceSeconds &&
            x.Note.StartPosition == source.Position + 1 &&
            Math.Abs((x.Note.SlideStartTime - x.Timing) - expectedWait) < ParseTimingToleranceSeconds &&
            Math.Abs(x.Note.SlideTime - expectedSlideTime) < ParseTimingToleranceSeconds &&
            x.Note.RawContent.Contains(pathNeedle, StringComparison.Ordinal));
        if (match.Note is null)
        {
            throw new InvalidDataException($"Slide mismatch at SDT line {source.LineNumber}.");
        }
        remainingSlides.Remove(match);
    }
    if (remainingSlides.Count != 0)
    {
        throw new InvalidDataException($"MajSimaiX produced {remainingSlides.Count} unmatched Slide records.");
    }

    foreach (var slide in parsedNotes.Where(x => x.Note.Type == SimaiNoteType.Slide))
    {
        if (slide.Note.SlideSoflanGroup != 0)
        {
            throw new InvalidDataException("A Slide body inherited a star-head Soflan group.");
        }
    }

    var parsedSpeedEvents = chart.NoteTimings.ToArray()
        .Where(x => x.SoflanGroup != 0 && x.RawContent.Length == 0)
        .Select(x => new ParsedSpeed(x.Timing, x.SoflanGroup, x.HSpeed))
        .ToArray();
    var expectedSpeedCount = speedEvents.Sum(x => x.Value.Count);
    if (parsedSpeedEvents.Length != expectedSpeedCount)
    {
        throw new InvalidDataException($"MajSimaiX parsed {parsedSpeedEvents.Length} HS declarations; expected {expectedSpeedCount}.");
    }

    foreach (var timing in speedEvents)
    {
        foreach (var speed in timing.Value)
        {
            var expectedTiming = (double)(timing.Key * measureSeconds);
            var matches = parsedSpeedEvents.Where(x =>
                x.Group == speed.Key &&
                Math.Abs(x.Timing - expectedTiming) < ParseTimingToleranceSeconds &&
                Math.Abs(x.Speed - speed.Value) <= Math.Max(0.000001f, Math.Abs(speed.Value) * 0.000001f)).ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidDataException($"Could not match HS group {speed.Key} at measure {timing.Key} after parsing.");
            }
        }
    }

    return chart;
}

static bool MatchesScatterType(SimaiNote note, int sourceType)
{
    return sourceType switch
    {
        1 => note.Type == SimaiNoteType.Tap && !note.IsBreak && !note.IsForceStar,
        3 => note.Type == SimaiNoteType.Tap && note.IsBreak,
        4 => note.Type == SimaiNoteType.Slide && !note.IsSlideNoHead && !note.IsBreak,
        5 => note.Type == SimaiNoteType.Slide && !note.IsSlideNoHead && note.IsBreak,
        _ => false
    };
}

static string ReplaceChart(string referenceText, int chartIndex, string fumen)
{
    var chartHeader = Regex.Match(
        referenceText,
        $@"(?m)^&inote_{chartIndex}=.*(?:\r?\n|$)",
        RegexOptions.CultureInvariant);
    if (!chartHeader.Success)
    {
        throw new InvalidDataException($"Reference maidata has no &inote_{chartIndex}= section.");
    }

    var searchStart = chartHeader.Index + chartHeader.Length;
    var nextCommand = Regex.Match(referenceText[searchStart..], @"(?m)^&[A-Za-z0-9_]+=.*$");
    var suffixStart = nextCommand.Success ? searchStart + nextCommand.Index : referenceText.Length;

    var prefix = referenceText[..chartHeader.Index].TrimEnd('\r', '\n');
    var suffix = referenceText[suffixStart..].TrimStart('\r', '\n');
    var builder = new StringBuilder();
    builder.Append(prefix).Append("\r\n\r\n")
        .Append("&inote_").Append(chartIndex).Append("=\r\n")
        .Append(fumen.Replace("\n", "\r\n", StringComparison.Ordinal).Replace("\r\r\n", "\r\n", StringComparison.Ordinal));
    if (suffix.Length > 0)
    {
        builder.Append("\r\n\r\n").Append(suffix);
    }
    return builder.ToString();
}

static bool IsScatter(SdtRow row) => row.NoteType is 1 or 3 or 4 or 5;

static string FormatDecimal(decimal value)
    => value.ToString("0.############################", CultureInfo.InvariantCulture);

static string FormatFloat(float value)
    => value.ToString("G9", CultureInfo.InvariantCulture);

readonly record struct SdtRow(
    int LineNumber,
    int Order,
    decimal MeasureWhole,
    decimal MeasureFraction,
    decimal Duration,
    int Position,
    int NoteType,
    int SlideId,
    int Pattern,
    int TransformIndex,
    decimal Extra)
{
    public bool IsControl => Position == -1;
    public decimal Measure => MeasureWhole + MeasureFraction;
}

readonly record struct ControlEvent(decimal Time, int Slot, bool IsSlope, decimal Value, int Order);
readonly record struct GroupKey(int Slot, decimal RelativeSpeed)
{
    public static GroupKey Default => new(0, 1m);
}
readonly record struct HeadKey(decimal Measure, int Position);
readonly record struct NoteComponent(int Order, string Token);
readonly record struct ParsedNote(double Timing, SimaiNote Note);
readonly record struct ParsedSpeed(double Timing, int Group, float Speed);
sealed record SpeedTimeline(SortedDictionary<decimal, SortedDictionary<int, float>> Events, int PulseCount);
