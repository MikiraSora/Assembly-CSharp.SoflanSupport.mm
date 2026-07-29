using System;
using System.IO;
using System.Text;
using System.Threading;
using SoflanSupport;

internal static class Program
{
    private const string EnabledDiagnosticMessage = "diagnostic marker enabled";
    private const string DisabledDiagnosticMessage = "diagnostic marker disabled";
    private const string ErrorMessage = "mixed modifier marker failure";

    private static int Main()
    {
        var originalDirectory = Environment.CurrentDirectory;
        var directory = Path.Combine(Path.GetTempPath(), "SoflanLogTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            Environment.CurrentDirectory = directory;

            Setting.EnableSoflanDiagnosticLog = true;
            PatchLog.Diagnostic(EnabledDiagnosticMessage);

            Setting.EnableSoflanDiagnosticLog = false;
            PatchLog.Diagnostic(DisabledDiagnosticMessage);
            Setting.EnableSoflanDiagnosticLog = true;

            PatchLog.Error(ErrorMessage);

            var path = Path.Combine(directory, PatchLog.FilePath);
            WaitForLog(path, EnabledDiagnosticMessage, ErrorMessage);

            var bytes = File.ReadAllBytes(path);
            var text = new UTF8Encoding(false, true).GetString(bytes);

            DiagnosticWritesDiagLevelAndMessageInRelease(text);
            DisabledDiagnosticDoesNotWrite(text);
            LogUsesUtf8WithoutBom(bytes);
            ErrorStillWritesErrorLevelAndMessage(text);

            Console.WriteLine("SoflanLogTests: PASS");
            Console.WriteLine(path);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("SoflanLogTests: FAIL");
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
        }
    }

    private static void DiagnosticWritesDiagLevelAndMessageInRelease(string text)
    {
        Require(text.Contains("[DIAG]" + EnabledDiagnosticMessage),
            "Enabled Soflan diagnostic message is not written at DIAG level");
    }

    private static void DisabledDiagnosticDoesNotWrite(string text)
    {
        Require(!text.Contains(DisabledDiagnosticMessage),
            "Disabled Soflan diagnostic message was unexpectedly written");
    }

    private static void LogUsesUtf8WithoutBom(byte[] bytes)
    {
        Require(bytes.Length >= 3, "Soflan log is empty");
        Require(!(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF),
            "Soflan log contains a UTF-8 BOM");

        _ = new UTF8Encoding(false, true).GetString(bytes);
    }

    private static void ErrorStillWritesErrorLevelAndMessage(string text)
    {
        Require(text.Contains("[ERROR]" + ErrorMessage),
            "Soflan error message is not written at ERROR level");
    }

    private static void WaitForLog(string path, params string[] expectedMessages)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (File.Exists(path))
                {
                    var text = File.ReadAllText(path, new UTF8Encoding(false, true));
                    var complete = true;
                    for (var i = 0; i < expectedMessages.Length; i++)
                    {
                        if (!text.Contains(expectedMessages[i]))
                        {
                            complete = false;
                            break;
                        }
                    }

                    if (complete)
                        return;
                }
            }
            catch (IOException)
            {
            }

            Thread.Sleep(20);
        }

        throw new InvalidOperationException("Timed out waiting for the Soflan log batch");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

namespace SoflanSupport
{
    internal static class Setting
    {
        internal static bool EnablePatchLog { get; set; } = true;
        internal static bool EnableSoflanDiagnosticLog { get; set; } = true;
    }
}

namespace UnityEngine
{
    internal static class Debug
    {
        internal static void Log(object message)
        {
        }

        internal static void LogError(object message)
        {
        }
    }
}
