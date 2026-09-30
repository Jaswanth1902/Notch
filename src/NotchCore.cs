using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Notch.Core
{
    public class AgentSession
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Project { get; set; }
        public string Tool { get; set; }
        public string State { get; set; } // idle, working, thinking, attention, review
        public string Model { get; set; }
        public int Pid { get; set; }
        public long LastSeenSeconds { get; set; }
        public string Message { get; set; }
    }

    public static class Win32
    {
        public const int MOD_SHIFT = 0x0004;
        public const int VK_RETURN = 0x0D;
        public const int VK_ESCAPE = 0x1B;
        public const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public const int SW_RESTORE = 9;

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        public static IntPtr FindWindowByProcessId(int targetPid)
        {
            IntPtr resultHwnd = IntPtr.Zero;
            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd))
                {
                    uint pid;
                    GetWindowThreadProcessId(hWnd, out pid);
                    if (pid == targetPid)
                    {
                        resultHwnd = hWnd;
                        return false; // Stop enumerating
                    }
                }
                return true;
            }, IntPtr.Zero);
            return resultHwnd;
        }
    }

    public class SessionManager
    {
        private readonly ConcurrentDictionary<string, AgentSession> _sessions = new ConcurrentDictionary<string, AgentSession>();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingGates = new ConcurrentDictionary<string, TaskCompletionSource<string>>();

        public event Action<string> OnStateChanged;

        private static long UtcNowSeconds()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        public void UpdateSession(AgentSession session)
        {
            session.LastSeenSeconds = UtcNowSeconds();
            _sessions[session.Id] = session;
            NotifyChange();
        }

        public void RemoveSession(string id)
        {
            AgentSession removed;
            _sessions.TryRemove(id, out removed);
            NotifyChange();
        }

        public List<AgentSession> GetAllActiveSessions(int ttlSeconds = 180)
        {
            long now = UtcNowSeconds();
            var list = new List<AgentSession>();
            foreach (var kvp in _sessions)
            {
                if (now - kvp.Value.LastSeenSeconds <= ttlSeconds)
                {
                    list.Add(kvp.Value);
                }
            }
            return list;
        }

        public string GetEffectiveState(out AgentSession dominantSession)
        {
            var sessions = GetAllActiveSessions();
            dominantSession = null;

            if (sessions.Count == 0)
                return "idle";

            // Priority 1: Attention (gate pending)
            foreach (var s in sessions)
            {
                if (s.State == "attention" || _pendingGates.ContainsKey(s.Id))
                {
                    dominantSession = s;
                    return "attention";
                }
            }

            // Priority 2: Working
            foreach (var s in sessions)
            {
                if (s.State == "working")
                {
                    dominantSession = s;
                    return "working";
                }
            }

            // Priority 3: Review
            foreach (var s in sessions)
            {
                if (s.State == "review")
                {
                    dominantSession = s;
                    return "review";
                }
            }

            // Priority 4: Thinking
            foreach (var s in sessions)
            {
                if (s.State == "thinking")
                {
                    dominantSession = s;
                    return "thinking";
                }
            }

            dominantSession = sessions[0];
            return dominantSession.State ?? "idle";
        }

        public Task<string> RegisterGate(string sessionId)
        {
            var tcs = new TaskCompletionSource<string>();
            _pendingGates[sessionId] = tcs;
            NotifyChange();
            return tcs.Task;
        }

        public bool ResolveGate(string sessionId, string decision)
        {
            TaskCompletionSource<string> tcs;
            if (_pendingGates.TryRemove(sessionId, out tcs))
            {
                tcs.TrySetResult(decision);
                NotifyChange();
                return true;
            }
            return false;
        }

        public bool ResolveAnyGate(string decision)
        {
            foreach (var key in _pendingGates.Keys)
            {
                if (ResolveGate(key, decision))
                    return true;
            }
            return false;
        }

        private void NotifyChange()
        {
            AgentSession dominant;
            string state = GetEffectiveState(out dominant);
            if (OnStateChanged != null)
            {
                OnStateChanged(state);
            }
        }
    }

    public class NotchCoreServer
    {
        public const string PipeName = "notch_ipc";
        public const int HttpPort = 27182;

        private readonly SessionManager _sessionManager;
        private readonly List<HttpListenerResponse> _sseSubscribers = new List<HttpListenerResponse>();
        private readonly object _sseLock = new object();
        private bool _running = true;

        public NotchCoreServer()
        {
            _sessionManager = new SessionManager();
            _sessionManager.OnStateChanged += BroadcastStateToSse;
        }

        public static string GetUserPipeName()
        {
            string sid = "user";
            try
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                if (identity != null && identity.User != null) sid = identity.User.Value;
                else sid = Environment.UserName;
            }
            catch { sid = Environment.UserName; }
            return "notch-" + sid;
        }

        public void Start()
        {
            Console.WriteLine("[NotchCore] Initializing High-Performance Native Daemon (Coucou & Antigravity Mesh)...");
            
            // 1. Start Named Pipe Listener Threads (legacy notch_ipc and user-isolated notch-<sid>)
            var pipeThread1 = new Thread(() => RunPipeServer(PipeName)) { IsBackground = true };
            pipeThread1.Start();
            var pipeThread2 = new Thread(() => RunPipeServer(GetUserPipeName())) { IsBackground = true };
            pipeThread2.Start();

            // 2. Start HTTP / SSE Bridge Server
            var httpThread = new Thread(RunHttpServer) { IsBackground = true };
            httpThread.Start();

            // 3. Start Global Hotkey Message Loop
            Console.WriteLine("[NotchCore] Services online: Pipes '\\\\.\\pipe\\notch_ipc' & '\\\\.\\pipe\\" + GetUserPipeName() + "' | HTTP '127.0.0.1:" + HttpPort + "'");
            RunHotkeyLoop();
        }

        private void BroadcastRawSse(string payload)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            lock (_sseLock)
            {
                for (int i = _sseSubscribers.Count - 1; i >= 0; i--)
                {
                    var res = _sseSubscribers[i];
                    try
                    {
                        res.OutputStream.Write(bytes, 0, bytes.Length);
                        res.OutputStream.Flush();
                    }
                    catch
                    {
                        _sseSubscribers.RemoveAt(i);
                    }
                }
            }
        }

        private void BroadcastStateToSse(string state)
        {
            AgentSession dominant;
            string effectiveState = _sessionManager.GetEffectiveState(out dominant);
            var sessions = _sessionManager.GetAllActiveSessions();

            var sb = new StringBuilder();
            sb.Append("{\"type\":\"state_update\",\"state\":\"").Append(effectiveState).Append("\",");
            sb.Append("\"sessions\":[");
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                if (i > 0) sb.Append(",");
                sb.Append("{\"id\":\"").Append(s.Id).Append("\",");
                sb.Append("\"name\":\"").Append(s.Name).Append("\",");
                sb.Append("\"project\":\"").Append(s.Project).Append("\",");
                sb.Append("\"tool\":\"").Append(Escape(s.Tool)).Append("\",");
                sb.Append("\"state\":\"").Append(s.State).Append("\",");
                sb.Append("\"model\":\"").Append(s.Model).Append("\",");
                sb.Append("\"pid\":").Append(s.Pid).Append("}");
            }
            sb.Append("]}");

            string payload = "data: " + sb.ToString() + "\n\n";
            BroadcastRawSse(payload);
        }

        private void RunPipeServer(string pipeName)
        {
            while (_running)
            {
                try
                {
                    using (var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                    {
                        pipe.WaitForConnection();
                        HandlePipeClient(pipe);
                    }
                }
                catch (Exception)
                {
                    if (!_running) break;
                    Thread.Sleep(50);
                }
            }
        }

        private void HandlePipeClient(NamedPipeServerStream pipe)
        {
            try
            {
                using (var reader = new StreamReader(pipe, Encoding.UTF8))
                using (var writer = new StreamWriter(pipe, new UTF8Encoding(false)))
                {
                    string line = reader.ReadLine();
                    if (string.IsNullOrEmpty(line)) return;

                    // Parse mini JSON commands & check for approval gates or Claude Code PermissionRequest
                    bool isApproval = line.Contains("\"mode\":\"approval-gate\"") || line.Contains("\"gate\"") || line.Contains("PermissionRequest");
                    if (isApproval)
                    {
                        string id = ExtractJsonValue(line, "conversationId", ExtractJsonValue(line, "request_id", Guid.NewGuid().ToString("N").Substring(0, 12)));
                        string tool = ExtractJsonValue(line, "tool", ExtractJsonValue(line, "tool_name", "tool"));
                        string proj = ExtractJsonValue(line, "project", "Project");
                        string actor = ExtractJsonValue(line, "actor", "Agent");
                        string cmd = ExtractJsonValue(line, "command", ExtractJsonValue(line, "summary", tool));

                        var sess = new AgentSession
                        {
                            Id = id,
                            Name = actor,
                            Project = proj,
                            Tool = tool,
                            State = "attention",
                            Model = "LLM"
                        };
                        _sessionManager.UpdateSession(sess);

                        // Broadcast approval event to SSE so Mochi Dynamic Island pops up
                        string sseEvent = string.Format("data: {{\"type\":\"approval\",\"mode\":\"approval-gate\",\"requestId\":\"{0}\",\"actor\":\"{1}\",\"tool\":\"{2}\",\"command\":\"{3}\"}}\n\n", id, Escape(actor), Escape(tool), Escape(cmd));
                        BroadcastRawSse(sseEvent);

                        // Block until approved or timed out
                        var task = _sessionManager.RegisterGate(id);
                        string decision = "allow";
                        if (task.Wait(110000))
                        {
                            decision = task.Result;
                        }

                        // Bare word for Claude Code/Coucou relay, JSON for Antigravity spool
                        if (line.Contains("PermissionRequest"))
                        {
                            writer.WriteLine(decision);
                        }
                        else
                        {
                            writer.WriteLine("{\"decision\":\"" + decision + "\"}");
                        }
                        writer.Flush();

                        sess.State = "review";
                        _sessionManager.UpdateSession(sess);
                    }
                    else
                    {
                        // Standard state packet
                        string id = ExtractJsonValue(line, "conversationId", ExtractJsonValue(line, "session_id", "default"));
                        string state = ExtractJsonValue(line, "state", "working");
                        string actor = ExtractJsonValue(line, "actor", "Agent");
                        string proj = ExtractJsonValue(line, "project", "Active");
                        string tool = ExtractJsonValue(line, "tool", ExtractJsonValue(line, "hook_event_name", ""));

                        _sessionManager.UpdateSession(new AgentSession
                        {
                            Id = id,
                            Name = actor,
                            Project = proj,
                            Tool = tool,
                            State = state
                        });

                        writer.WriteLine("{\"status\":\"ok\"}");
                        writer.Flush();
                    }
                }
            }
            catch { }
        }

        private void RunHttpServer()
        {
            var listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:" + HttpPort + "/");
            listener.Start();

            while (_running)
            {
                try
                {
                    var context = listener.GetContext();
                    ThreadPool.QueueUserWorkItem((ctxObj) =>
                    {
                        var ctx = (HttpListenerContext)ctxObj;
                        HandleHttpRequest(ctx);
                    }, context);
                }
                catch
                {
                    if (!_running) break;
                }
            }
        }

        private void HandleHttpRequest(HttpListenerContext context)
        {
            var req = context.Request;
            var res = context.Response;

            // CORS headers
            res.Headers.Add("Access-Control-Allow-Origin", "*");
            res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            res.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (req.HttpMethod == "OPTIONS")
            {
                res.StatusCode = 200;
                res.Close();
                return;
            }

            string path = req.Url.AbsolutePath.ToLowerInvariant();

            if (path == "/events" || path == "/api/events")
            {
                // Server-Sent Events stream
                res.ContentType = "text/event-stream";
                res.Headers.Add("Cache-Control", "no-cache");
                res.Headers.Add("Connection", "keep-alive");

                lock (_sseLock)
                {
                    _sseSubscribers.Add(res);
                }

                // Initial burst of current state
                AgentSession dominant;
                string state = _sessionManager.GetEffectiveState(out dominant);
                string initEvent = "data: {\"type\":\"state_update\",\"state\":\"" + state + "\",\"sessions\":[]}\n\n";
                byte[] b = Encoding.UTF8.GetBytes(initEvent);
                res.OutputStream.Write(b, 0, b.Length);
                res.OutputStream.Flush();
                // Keep connection open
                return;
            }

            if (path == "/api/decision" && req.HttpMethod == "POST")
            {
                using (var r = new StreamReader(req.InputStream, req.ContentEncoding))
                {
                    string body = r.ReadToEnd();
                    string reqId = ExtractJsonValue(body, "requestId", "");
                    string decision = ExtractJsonValue(body, "decision", "allow");
                    if (!string.IsNullOrEmpty(reqId))
                    {
                        _sessionManager.ResolveGate(reqId, decision);
                    }
                    else
                    {
                        _sessionManager.ResolveAnyGate(decision);
                    }
                }
                SendJson(res, "{\"status\":\"ok\"}");
                return;
            }

            // Static file serving for Notch UI
            if (path == "/" || path == "/index.html" || path.EndsWith(".js") || path.EndsWith(".css") || path.EndsWith(".html") || path.EndsWith(".svg") || path.EndsWith(".png"))
            {
                string rel = (path == "/" || path == "/index.html") ? "index.html" : path.TrimStart('/');
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string uiDir = Path.Combine(baseDir, "ui");
                if (!Directory.Exists(uiDir)) uiDir = Path.Combine(baseDir, "..", "ui");
                if (!Directory.Exists(uiDir)) uiDir = Path.Combine(baseDir, "..", "..", "ui");

                string file = Path.Combine(uiDir, rel);
                if (File.Exists(file))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    string mime = "text/plain";
                    if (ext == ".html") mime = "text/html; charset=utf-8";
                    else if (ext == ".js") mime = "application/javascript; charset=utf-8";
                    else if (ext == ".css") mime = "text/css; charset=utf-8";
                    else if (ext == ".svg") mime = "image/svg+xml";
                    else if (ext == ".png") mime = "image/png";

                    byte[] b = File.ReadAllBytes(file);
                    res.ContentType = mime;
                    res.ContentLength64 = b.Length;
                    res.OutputStream.Write(b, 0, b.Length);
                    res.Close();
                    return;
                }
            }

            if (path == "/approve" && req.HttpMethod == "POST")
            {
                _sessionManager.ResolveAnyGate("allow");
                SendJson(res, "{\"status\":\"approved\"}");
                return;
            }

            if (path == "/deny" && req.HttpMethod == "POST")
            {
                _sessionManager.ResolveAnyGate("deny");
                SendJson(res, "{\"status\":\"denied\"}");
                return;
            }

            if (path == "/status")
            {
                AgentSession dom;
                string state = _sessionManager.GetEffectiveState(out dom);
                var active = _sessionManager.GetAllActiveSessions();
                SendJson(res, "{\"state\":\"" + state + "\",\"activeCount\":" + active.Count + "}");
                return;
            }

            if (path == "/focus" && req.HttpMethod == "POST")
            {
                using (var r = new StreamReader(req.InputStream, req.ContentEncoding))
                {
                    string body = r.ReadToEnd();
                    string pidStr = ExtractJsonValue(body, "pid", "0");
                    int pid;
                    if (int.TryParse(pidStr, out pid) && pid > 0)
                    {
                        IntPtr hwnd = Win32.FindWindowByProcessId(pid);
                        if (hwnd != IntPtr.Zero)
                        {
                            Win32.ShowWindow(hwnd, Win32.SW_RESTORE);
                            Win32.SetForegroundWindow(hwnd);
                            SendJson(res, "{\"status\":\"focused\",\"pid\":" + pid + "}");
                            return;
                        }
                    }
                }
                SendJson(res, "{\"status\":\"not_found\"}");
                return;
            }

            res.StatusCode = 404;
            res.Close();
        }

        private void SendJson(HttpListenerResponse res, string json)
        {
            res.ContentType = "application/json";
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.Close();
        }

        private void RunHotkeyLoop()
        {
            // Register Shift+Enter (HotKey ID: 101)
            int hotkeyId = 101;
            bool registered = Win32.RegisterHotKey(IntPtr.Zero, hotkeyId, Win32.MOD_SHIFT, Win32.VK_RETURN);
            Console.WriteLine("[NotchCore] System-wide Shift+Enter Hotkey: " + (registered ? "REGISTERED" : "ALREADY_IN_USE"));

            // Native Win32 message pump
            MSG msg;
            while (_running && GetMessage(out msg, IntPtr.Zero, 0, 0))
            {
                if (msg.message == Win32.WM_HOTKEY && (int)msg.wParam == hotkeyId)
                {
                    Console.WriteLine("[NotchCore] Hotkey Shift+Enter detected! Resolving approval gate...");
                    bool resolved = _sessionManager.ResolveAnyGate("allow");
                    if (resolved)
                    {
                        Console.WriteLine("[NotchCore] -> Unblocked waiting tool execution gate.");
                    }
                }
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            Win32.UnregisterHotKey(IntPtr.Zero, hotkeyId);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [DllImport("user32.dll")]
        private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage([In] ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

        private static string ExtractJsonValue(string json, string key, string fallback)
        {
            if (string.IsNullOrEmpty(json)) return fallback;
            string pattern = "\"" + key + "\":\"";
            int idx = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                int start = idx + pattern.Length;
                int end = json.IndexOf("\"", start);
                if (end > start)
                {
                    return json.Substring(start, end - start);
                }
            }
            return fallback;
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
        }

        public static void Main(string[] args)
        {
            Console.WriteLine("===============================================================");
            Console.WriteLine(">> NOTCH 2.0 NATIVE BACKEND DAEMON (Sub-Millisecond IPC)");
            Console.WriteLine("===============================================================");
            var server = new NotchCoreServer();
            server.Start();
        }
    }
}
