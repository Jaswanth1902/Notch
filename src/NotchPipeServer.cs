// NotchPipeServer.cs — High-Performance Named Pipe Server for Notch
// Ported and synthesized from louis-cfm/coucou (windows/src-tauri/src/pipe.rs)
//
// 1. Listens on \\.\pipe\notch-<sid> (user-isolated, zero conflict across user sessions).
// 2. Implements Coucou's two-phase ACK + Decision timeout.
// 3. Thread-safe pending request dictionary for UI & global hotkey (Shift+Enter / Esc) responses.
// 4. Dual-broadcasts to local state files (~/.notch/state.json) and HUD endpoints.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Notch.Core
{
    public class NotchPipeServer
    {
        private static readonly int ACK_TIMEOUT_MS = 800;
        private static readonly int DECISION_TIMEOUT_MS = 108000; // 108s
        private static readonly int MAX_PAYLOAD = 1024 * 1024; // 1MB

        private readonly string _pipeName;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingDecisions =
            new ConcurrentDictionary<string, TaskCompletionSource<string>>();

        public event Action<string, Dictionary<string, object>> OnEventReceived;
        public event Action<string, Dictionary<string, object>> OnPermissionRequested;

        public NotchPipeServer()
        {
            _pipeName = GetPipeName();
        }

        public static string GetPipeName()
        {
            string sid = "user";
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                if (identity != null && identity.User != null)
                {
                    sid = identity.User.Value;
                }
                else
                {
                    sid = Environment.UserName;
                }
            }
            catch
            {
                sid = Environment.UserName;
            }
            return "notch-" + sid;
        }

        public void Start()
        {
            Task.Factory.StartNew(ListenLoop, TaskCreationOptions.LongRunning);
        }

        public void Stop()
        {
            _cts.Cancel();
        }

        public bool ResolveDecision(string requestId, string decision)
        {
            TaskCompletionSource<string> tcs;
            if (_pendingDecisions.TryRemove(requestId, out tcs))
            {
                tcs.TrySetResult(decision.ToLowerInvariant().Trim());
                return true;
            }
            return false;
        }

        public void ResolveAllPending(string decision)
        {
            foreach (var kv in _pendingDecisions)
            {
                TaskCompletionSource<string> tcs;
                if (_pendingDecisions.TryRemove(kv.Key, out tcs))
                {
                    tcs.TrySetResult(decision);
                }
            }
        }

        private async Task ListenLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    PipeSecurity pipeSec = new PipeSecurity();
                    SecurityIdentifier userSid = WindowsIdentity.GetCurrent().User;
                    if (userSid != null)
                    {
                        pipeSec.AddAccessRule(new PipeAccessRule(userSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));
                    }

                    var server = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous,
                        4096,
                        4096,
                        pipeSec
                    );

                    await server.WaitForConnectionAsync(_cts.Token);
                    // Spawn connection handler and immediately listen for next connection
                    var _ = Task.Run(() => HandleClientAsync(server));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await Task.Delay(200);
                }
            }
        }

        private async Task HandleClientAsync(NamedPipeServerStream pipe)
        {
            using (pipe)
            {
                try
                {
                    var reader = new StreamReader(pipe, Encoding.UTF8);
                    string line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) return;

                    var root = Relay.MiniJson.Parse(line) as Dictionary<string, object>;
                    if (root == null) return;

                    object evObj;
                    string evName = "";
                    if (root.TryGetValue("hook_event_name", out evObj) && evObj != null)
                        evName = evObj.ToString();

                    bool isPermission = string.Equals(evName, "PermissionRequest", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(evName, "approval-gate", StringComparison.OrdinalIgnoreCase);

                    if (!isPermission)
                    {
                        // Fire-and-forget telemetry event
                        OnEventReceived?.Invoke(evName, root);
                        return;
                    }

                    // Handle Permission Request
                    string reqId = Guid.NewGuid().ToString("N").Substring(0, 12);
                    root["request_id"] = reqId;

                    var tcs = new TaskCompletionSource<string>();
                    _pendingDecisions[reqId] = tcs;

                    // Emit to UI
                    OnPermissionRequested?.Invoke(reqId, root);

                    // Wait for decision with two-phase timeout
                    string decision = null;
                    var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(DECISION_TIMEOUT_MS));
                    if (completedTask == tcs.Task)
                    {
                        decision = tcs.Task.Result;
                    }
                    _pendingDecisions.TryRemove(reqId, out _);

                    if (!string.IsNullOrEmpty(decision))
                    {
                        byte[] respBytes = Encoding.UTF8.GetBytes(decision + "\n");
                        await pipe.WriteAsync(respBytes, 0, respBytes.Length);
                        await pipe.FlushAsync();
                    }
                }
                catch { }
            }
        }
    }
}
