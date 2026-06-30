using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using OngekiFumenEditor.Core.Base;
using OngekiFumenEditor.Core.Base.Collections;
using OngekiFumenEditor.Core.Modules.FumenVisualEditor;

namespace SoflanSimulator
{
    class Program
    {
        private const string Prefix = "http://localhost:3721/";
        private static readonly Dictionary<string, Session> _sessions = new Dictionary<string, Session>();
        private static string _wwwPath;

        static void Main(string[] args)
        {
            _wwwPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "www");

            var listener = new HttpListener();
            listener.Prefixes.Add(Prefix);
            listener.Start();
            Console.WriteLine("SoflanSimulator running on " + Prefix);
            Console.WriteLine("Press Ctrl+C to stop.");

            try { Process.Start(new ProcessStartInfo { FileName = Prefix, UseShellExecute = true }); }
            catch { }

            while (true)
            {
                try
                {
                    var ctx = listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(ctx));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Listener error: " + ex.Message);
                }
            }
        }

        static void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                var path = ctx.Request.Url.AbsolutePath;
                var method = ctx.Request.HttpMethod;

                if (path == "/" && method == "GET")
                {
                    ServeFile(ctx, Path.Combine(_wwwPath, "index.html"), "text/html; charset=utf-8");
                }
                else if (path.StartsWith("/api/"))
                {
                    HandleApi(ctx, path, method);
                }
                else
                {
                    var filePath = Path.Combine(_wwwPath, path.TrimStart('/'));
                    var fullPath = Path.GetFullPath(filePath);
                    if (fullPath.StartsWith(_wwwPath, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
                    {
                        ServeFile(ctx, fullPath, GetMimeType(fullPath));
                    }
                    else
                    {
                        SendJson(ctx, 404, Json.Obj("error", "not found"));
                    }
                }
            }
            catch (Exception ex)
            {
                try { SendJson(ctx, 500, Json.Obj("error", ex.Message)); }
                catch { }
            }
        }

        static void HandleApi(HttpListenerContext ctx, string path, string method)
        {
            if (path == "/api/load" && method == "POST")
                ApiLoad(ctx);
            else if (path == "/api/computeCurve" && method == "GET")
                ApiComputeCurve(ctx);
            else if (path == "/api/computeAt" && method == "GET")
                ApiComputeAt(ctx);
            else
                SendJson(ctx, 404, Json.Obj("error", "unknown api"));
        }

        static void ApiLoad(HttpListenerContext ctx)
        {
            string body;
            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                body = reader.ReadToEnd();

            var fileMatch = System.Text.RegularExpressions.Regex.Match(body, @"""file""\s*:\s*""((?:[^""\\]|\\.)*)""");
            if (!fileMatch.Success)
            {
                SendJson(ctx, 400, Json.Obj("error", "missing file parameter"));
                return;
            }
            var filePath = fileMatch.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");

            if (!File.Exists(filePath))
            {
                SendJson(ctx, 400, Json.Obj("error", "file not found: " + filePath));
                return;
            }

            var data = Ma2Parser.Parse(filePath);
            var sessionId = Guid.NewGuid().ToString("N");
            var session = Simulator.BuildSession(sessionId, data);
            lock (_sessions) _sessions[sessionId] = session;

            // BPM list
            var bpmList = new List<object>();
            foreach (var bpm in session.BpmList)
            {
                var timeMs = TGridCalculator.ConvertTGridToAudioTime(bpm.TGrid, session.BpmList).TotalMilliseconds;
                bpmList.Add(Json.Dict("measure", (int)bpm.TGrid.Unit, "grid", bpm.TGrid.Grid, "bpm", bpm.BPM, "timeMs", timeMs));
            }

            // SFL list
            var sflList = new List<object>();
            foreach (var sfl in data.SflRecords)
            {
                var startTGrid = new TGrid(sfl.Unit, sfl.Grid);
                var endTGrid = startTGrid + new GridOffset(0, sfl.GridLength);
                var startMs = TGridCalculator.ConvertTGridToAudioTime(startTGrid, session.BpmList).TotalMilliseconds;
                var endMs = TGridCalculator.ConvertTGridToAudioTime(endTGrid, session.BpmList).TotalMilliseconds;
                sflList.Add(Json.Dict("unit", sfl.Unit, "grid", sfl.Grid, "gridLength", sfl.GridLength, "speed", sfl.Speed, "group", sfl.SoflanGroup, "startMs", startMs, "endMs", endMs));
            }

            // Notes
            var notesList = new List<object>();
            foreach (var note in data.Notes)
            {
                double appearMsec = 0;
                if (note.Active)
                {
                    var entry = session.ActiveNotes.Find(n => n.IndexNote == note.IndexNote);
                    if (entry != null) appearMsec = entry.AppearMsec;
                }
                notesList.Add(Json.Dict("indexNote", note.IndexNote, "type", note.Type, "measure", note.Measure, "grid", note.Grid, "lane", note.Lane, "soflanGroup", note.SoflanGroup, "appearMsec", appearMsec, "active", note.Active));
            }

            // SoFlan groups (speed curves)
            var groups = new Dictionary<string, object>();
            foreach (KeyValuePair<int, SoflanList> kvp in session.SoflanListMap)
            {
                var speedPoints = new List<object>();
                var posList = kvp.Value.GetCachedSoflanPositionList_PreviewMode(session.BpmList);
                foreach (var pt in posList)
                {
                    var timeMs = TGridCalculator.ConvertTGridToAudioTime(pt.TGrid, session.BpmList).TotalMilliseconds;
                    speedPoints.Add(Json.Dict("timeMs", timeMs, "speed", pt.Speed));
                }
                groups[kvp.Key.ToString()] = speedPoints;
            }

            var resp = Json.Obj("sessionId", sessionId, "resolution", data.Resolution, "durationMs", session.DurationMs, "bpmList", bpmList, "sflList", sflList, "notes", notesList, "soflanGroups", groups);
            SendJson(ctx, 200, resp);
        }

        static void ApiComputeCurve(HttpListenerContext ctx)
        {
            var qs = ctx.Request.QueryString;
            if (!TryGetSession(ctx, qs["sessionId"], out var session)) return;

            int noteIndex = int.Parse(qs["noteIndex"]);
            var p = ParseParams(qs);
            float step = float.Parse(qs["step"], CultureInfo.InvariantCulture);

            var note = session.ActiveNotes.Find(n => n.IndexNote == noteIndex);
            if (note == null)
            {
                SendJson(ctx, 400, Json.Obj("error", "note not found or not active"));
                return;
            }

            var results = Simulator.ComputeCurve(session, note, p, step);
            var points = new List<object>();
            foreach (var r in results)
            {
                points.Add(Json.Dict("t", r.CurrentMsec, "diffTime", r.DiffTime, "absDiffTime", r.AbsDiffTime, "screenY", r.ScreenY, "soflanY", r.SoflanY, "noteSoflanTime", r.NoteSoflanTime, "currentSoflanTime", r.CurrentSoflanTime, "guideScale", r.GuideScale, "finalScale", r.FinalScale));
            }

            var resp = Json.Obj("noteIndex", noteIndex, "appearMsec", note.AppearMsec, "soflanGroup", note.SoflanGroup, "points", points);
            SendJson(ctx, 200, resp);
        }

        static void ApiComputeAt(HttpListenerContext ctx)
        {
            var qs = ctx.Request.QueryString;
            if (!TryGetSession(ctx, qs["sessionId"], out var session)) return;

            float time = float.Parse(qs["time"], CultureInfo.InvariantCulture);
            var p = ParseParams(qs);

            var results = Simulator.ComputeAt(session, time, p);
            var notes = new List<object>();
            foreach (var r in results)
            {
                notes.Add(Json.Dict("noteIndex", r.NoteIndex, "diffTime", r.Result.DiffTime, "absDiffTime", r.Result.AbsDiffTime, "screenY", r.Result.ScreenY, "soflanY", r.Result.SoflanY, "noteSoflanTime", r.Result.NoteSoflanTime, "currentSoflanTime", r.Result.CurrentSoflanTime, "finalScale", r.Result.FinalScale, "noteStat", r.Result.NoteStat));
            }

            SendJson(ctx, 200, Json.Obj("time", time, "notes", notes));
        }

        static bool TryGetSession(HttpListenerContext ctx, string sessionId, out Session session)
        {
            session = null;
            if (sessionId == null) { SendJson(ctx, 400, Json.Obj("error", "missing sessionId")); return false; }
            lock (_sessions) { if (!_sessions.TryGetValue(sessionId, out session)) { SendJson(ctx, 400, Json.Obj("error", "invalid sessionId")); return false; } }
            return true;
        }

        static SimParams ParseParams(System.Collections.Specialized.NameValueCollection qs)
        {
            return new SimParams
            {
                DefaultMsec = ParseFloat(qs["defaultMsec"], 2000f),
                MaiBugAdjustMSec = ParseFloat(qs["maiBugAdjust"], 0f),
                StartPos = ParseFloat(qs["startPos"], 120f),
                EndPos = ParseFloat(qs["endPos"], 400f),
                NoteSpeed = ParseFloat(qs["noteSpeed"], 150f)
            };
        }

        static float ParseFloat(string s, float def)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
        }

        static void ServeFile(HttpListenerContext ctx, string path, string mime)
        {
            var bytes = File.ReadAllBytes(path);
            ctx.Response.ContentType = mime;
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }

        static void SendJson(HttpListenerContext ctx, int status, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }

        static string GetMimeType(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".html": return "text/html; charset=utf-8";
                case ".js": return "application/javascript; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                default: return "application/octet-stream";
            }
        }
    }

    static class Json
    {
        public static Dictionary<string, object> Dict(params object[] kvPairs)
        {
            var dict = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kvPairs.Length; i += 2)
                dict[(string)kvPairs[i]] = kvPairs[i + 1];
            return dict;
        }

        // params object[] 形式: key1, val1, key2, val2, ...
        public static string Obj(params object[] kvPairs)
        {
            var sb = new StringBuilder("{");
            for (int i = 0; i + 1 < kvPairs.Length; i += 2)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(Escape((string)kvPairs[i])).Append("\":");
                WriteValue(sb, kvPairs[i + 1]);
            }
            sb.Append('}');
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object val)
        {
            if (val == null) sb.Append("null");
            else if (val is string s) { sb.Append('"'); sb.Append(Escape(s)); sb.Append('"'); }
            else if (val is bool b) sb.Append(b ? "true" : "false");
            else if (val is int i) sb.Append(i);
            else if (val is long l) sb.Append(l);
            else if (val is double d) sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
            else if (val is float f) sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
            else if (val is IDictionary<string, object> dict)
            {
                sb.Append('{');
                bool first = true;
                foreach (var kvp in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"').Append(Escape(kvp.Key)).Append("\":");
                    WriteValue(sb, kvp.Value);
                }
                sb.Append('}');
            }
            else if (val is System.Collections.IEnumerable list)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteValue(sb, item);
                }
                sb.Append(']');
            }
            else { sb.Append('"'); sb.Append(Escape(val.ToString())); sb.Append('"'); }
        }

        static string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}