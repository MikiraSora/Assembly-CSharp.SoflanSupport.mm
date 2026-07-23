// SoflanSupport.PatchLog — 新增类型, verbatim 自 head commit 2a7a4a4.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace SoflanSupport
{
    internal static class PatchLog
    {
        public const string FilePath = "dpSoflanSupport.log";
        private const int MaxBatchSize = 128;
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly ConcurrentQueue<LogEntry> queue = new ConcurrentQueue<LogEntry>();
        private static readonly AutoResetEvent queueSignal = new AutoResetEvent(false);
        private static readonly Thread workerThread;

        static PatchLog()
        {
            workerThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "Soflan PatchLog Writer"
            };
            workerThread.Start();
        }

        [Conditional("DEBUG")]
        public static void WriteLine(string msg)
        {
            if (!Setting.EnablePatchLog)
                return;
            Enqueue("INFO", msg);
        }

        public static void Error(string msg)
        {
            Enqueue("ERROR", msg);
        }

        private static void Enqueue(string level, string msg)
        {
            queue.Enqueue(new LogEntry(Thread.CurrentThread.ManagedThreadId, level, msg ?? string.Empty));
            queueSignal.Set();
        }

        private static void WorkerLoop()
        {
            try
            {
                File.Delete(FilePath);
            }
            catch { }

            var batch = new List<LogEntry>(MaxBatchSize);
            while (true)
            {
                queueSignal.WaitOne(100);
                DrainQueue(batch);
            }
        }

        private static void DrainQueue(List<LogEntry> batch)
        {
            while (true)
            {
                while (batch.Count < MaxBatchSize && queue.TryDequeue(out var entry))
                {
                    batch.Add(entry);
                }

                if (batch.Count == 0)
                    return;

                FlushBatch(batch);
            }
        }

        private static void FlushBatch(List<LogEntry> batch)
        {
            try
            {
                var text = new StringBuilder(batch.Count * 128);
                for (var i = 0; i < batch.Count; i++)
                {
                    var entry = batch[i];
                    text.Append("[Thread: ");
                    text.Append(entry.ThreadId);
                    text.Append("]");
                    text.Append("[");
                    text.Append(entry.Level);
                    text.Append("]");
                    text.Append(entry.Message);
                    text.Append(Environment.NewLine);
                }
                File.AppendAllText(FilePath, text.ToString(), Utf8NoBom);
            }
            catch { }

            for (var i = 0; i < batch.Count; i++)
            {
                try
                {
                    if (batch[i].Level == "ERROR")
                        UnityEngine.Debug.LogError(batch[i].Message);
                    else
                        UnityEngine.Debug.Log(batch[i].Message);
                }
                catch { }
            }

            batch.Clear();
        }

        private struct LogEntry
        {
            public readonly int ThreadId;
            public readonly string Level;
            public readonly string Message;

            public LogEntry(int threadId, string level, string message)
            {
                ThreadId = threadId;
                Level = level;
                Message = message;
            }
        }
    }
}
