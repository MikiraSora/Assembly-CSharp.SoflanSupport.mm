using System;
using SoflanSupport;

internal static class Program
{
    private static int Main()
    {
        try
        {
            AssertMarker(new[] { "#1" }, 1, false, false, 0f);
            AssertMarker(new[] { "!m#1" }, 1, false, false, 0f);
            AssertMarker(new[] { "#1!m" }, 1, false, false, 0f);
            AssertMarker(new[] { "#1!m!y" }, 1, false, false, 0f);
            AssertMarker(new[] { "!y!m#1" }, 1, false, false, 0f);
            AssertMarker(new[] { "!m#12F600!y" }, 12, true, true, 600f);
            AssertMarker(new[] { "#F!m!y" }, 0, true, false, 0f);
            AssertMarker(new[] { "!y#-2f750.5!m" }, -2, true, true, 750.5f);
            AssertNoMarker(new[] { "!m!y" });

            AssertRejected(new[] { "#" }, "empty");
            AssertRejected(new[] { "#A" }, "invalid");
            AssertRejected(new[] { "#1F0" }, "positive finite");
            AssertRejected(new[] { "#1FNaN" }, "invalid");
            AssertRejected(new[] { "#1#2" }, "invalid");
            AssertRejected(new[] { "#1", "!m#2" }, "multiple");
            AssertRejected(new[] { "#1!m#2!y" }, "multiple");

            Console.WriteLine("SoflanMarkerTests: PASS");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("SoflanMarkerTests: FAIL");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void AssertMarker(
        string[] fields,
        int group,
        bool isFixed,
        bool hasFixedSpeed,
        float fixedSpeed)
    {
        SoflanMarkerParseResult result;
        string reason;
        Require(SoflanMarkerParser.TryParse(fields, out result, out reason), reason);
        Require(result.HasMarker, "expected a Soflan marker");
        Require(result.Group == group, $"expected group {group}, actual {result.Group}");
        Require(result.IsFixedSoflan == isFixed, "fixed-soflan flag mismatch");
        Require(result.HasFixedSpeed == hasFixedSpeed, "fixed speed presence mismatch");
        if (hasFixedSpeed)
            Require(Math.Abs(result.FixedSpeed - fixedSpeed) < 0.0001f, "fixed speed mismatch");
    }

    private static void AssertNoMarker(string[] fields)
    {
        SoflanMarkerParseResult result;
        string reason;
        Require(SoflanMarkerParser.TryParse(fields, out result, out reason), reason);
        Require(!result.HasMarker, "unexpected Soflan marker");
    }

    private static void AssertRejected(string[] fields, string reasonFragment)
    {
        SoflanMarkerParseResult result;
        string reason;
        Require(!SoflanMarkerParser.TryParse(fields, out result, out reason),
            "invalid Soflan marker was accepted");
        Require(reason.IndexOf(reasonFragment, StringComparison.OrdinalIgnoreCase) >= 0,
            "unexpected rejection reason: " + reason);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
