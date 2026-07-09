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
            queue.Enqueue(new LogEntry(Thread.CurrentThread.ManagedThreadId, msg ?? string.Empty));
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
                    text.Append(entry.Message);
                    text.Append(Environment.NewLine);
                }
                File.AppendAllText(FilePath, text.ToString());
            }
            catch { }

            for (var i = 0; i < batch.Count; i++)
            {
                try { UnityEngine.Debug.Log(batch[i].Message); }
                catch { }
            }

            batch.Clear();
        }

        private struct LogEntry
        {
            public readonly int ThreadId;
            public readonly string Message;

            public LogEntry(int threadId, string message)
            {
                ThreadId = threadId;
                Message = message;
            }
        }
    }
}
