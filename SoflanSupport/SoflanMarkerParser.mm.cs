using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SoflanSupport
{
    internal readonly struct SoflanMarkerParseResult
    {
        internal SoflanMarkerParseResult(
            bool hasMarker,
            string marker,
            int group,
            bool isFixedSoflan,
            bool hasFixedSpeed,
            float fixedSpeed)
        {
            HasMarker = hasMarker;
            Marker = marker ?? string.Empty;
            Group = group;
            IsFixedSoflan = isFixedSoflan;
            HasFixedSpeed = hasFixedSpeed;
            FixedSpeed = fixedSpeed;
        }

        internal bool HasMarker { get; }

        internal string Marker { get; }

        internal int Group { get; }

        internal bool IsFixedSoflan { get; }

        internal bool HasFixedSpeed { get; }

        internal float FixedSpeed { get; }
    }

    internal static class SoflanMarkerParser
    {
        private const string FloatPattern = @"[+-]?(?:(?:\d+(?:\.\d*)?)|(?:\.\d+))(?:[eE][+-]?\d+)?";

        private static readonly Regex CandidateRegex = new Regex(
            @"#[^!]*(?=!|$)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex FormatRegex = new Regex(
            @"\A#(?<group>[+-]?\d+)?(?:(?<fixed>[Ff])(?<speed>" + FloatPattern + @")?)?\z",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        internal static bool TryParse(
            IList<string> fields,
            out SoflanMarkerParseResult result,
            out string reason)
        {
            result = default;
            reason = string.Empty;
            if (fields == null)
                return true;

            var found = false;
            for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
            {
                var field = fields[fieldIndex];
                if (string.IsNullOrEmpty(field))
                    continue;

                var matches = CandidateRegex.Matches(field);
                for (var matchIndex = 0; matchIndex < matches.Count; matchIndex++)
                {
                    var marker = matches[matchIndex].Value;
                    if (found)
                    {
                        reason = $"multiple soflan markers are not allowed: first={result.Marker}, second={marker}";
                        return false;
                    }

                    if (!TryParseMarker(marker, out result, out reason))
                        return false;

                    found = true;
                }
            }

            return true;
        }

        private static bool TryParseMarker(
            string marker,
            out SoflanMarkerParseResult result,
            out string reason)
        {
            result = new SoflanMarkerParseResult(true, marker, 0, false, false, 0f);
            reason = string.Empty;

            var match = FormatRegex.Match(marker);
            if (!match.Success)
            {
                reason = "invalid soflan marker";
                return false;
            }

            var groupCapture = match.Groups["group"];
            var fixedCapture = match.Groups["fixed"];
            if (!groupCapture.Success && !fixedCapture.Success)
            {
                reason = "empty soflan marker";
                return false;
            }

            var group = 0;
            if (groupCapture.Success
                && !int.TryParse(groupCapture.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out group))
            {
                reason = "invalid soflan group";
                return false;
            }

            var speedCapture = match.Groups["speed"];
            var hasFixedSpeed = speedCapture.Success;
            var fixedSpeed = 0f;
            if (hasFixedSpeed
                && (!float.TryParse(speedCapture.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out fixedSpeed)
                    || float.IsNaN(fixedSpeed)
                    || float.IsInfinity(fixedSpeed)
                    || fixedSpeed <= 0f))
            {
                reason = "fixed soflan speed must be a positive finite number";
                return false;
            }

            result = new SoflanMarkerParseResult(
                true,
                marker,
                group,
                fixedCapture.Success,
                hasFixedSpeed,
                fixedSpeed);
            return true;
        }
    }
}
