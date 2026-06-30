// SoflanSupport.PatchLog — 新增类型, verbatim 自 head commit 2a7a4a4.
using System;
using System.IO;
using System.Threading;

namespace SoflanSupport
{
    internal static class PatchLog
    {
        public const string FilePath = "dpSoflanSupport.log";
        private static object locker = new object();

        static PatchLog()
        {
            try
            {
                File.Delete(FilePath);
            }
            catch { }
        }

        public static void WriteLine(string msg)
        {
            if (!Setting.EnablePatchLog)
                return;
            lock (locker)
            {
                int threadId = Thread.CurrentThread.ManagedThreadId;
                File.AppendAllText(FilePath, $"[Thread: {threadId}]{msg}{Environment.NewLine}");
            }
            UnityEngine.Debug.Log(msg);
        }
    }
}
