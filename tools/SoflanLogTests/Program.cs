using System;
using System.IO;
using System.Text;
using System.Threading;
using SoflanSupport;

internal static class Program
{
    private static int Main()
    {
        var originalDirectory = Environment.CurrentDirectory;
        var directory = Path.Combine(Path.GetTempPath(), "SoflanLogTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            Environment.CurrentDirectory = directory;
            PatchLog.Error("mixed modifier marker failure");

            var path = Path.Combine(directory, PatchLog.FilePath);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while ((!File.Exists(path) || new FileInfo(path).Length == 0) && DateTime.UtcNow < deadline)
                Thread.Sleep(20);

            Require(File.Exists(path), "Soflan error log was not created");
            var bytes = File.ReadAllBytes(path);
            Require(bytes.Length >= 3, "Soflan error log is empty");
            Require(!(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF),
                "Soflan error log contains a UTF-8 BOM");

            var text = new UTF8Encoding(false, true).GetString(bytes);
            Require(text.Contains("[ERROR]"), "Soflan error log has no ERROR level");
            Require(text.Contains("mixed modifier marker failure"), "Soflan error message is missing");

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
