// notch-relay.cs — High-Speed Native Named Pipe Relay for AI Coding Agents & Notch
// Ported and synthesized from louis-cfm/coucou (windows/hook/src/main.rs)
//
// Hard Rule: NEVER BLOCK THE AI AGENT (Claude Code, Cursor, Antigravity)
// 1. Connection budget is strictly 300ms. If Notch is closed, exits 0 immediately.
// 2. Named pipe path includes the Windows user SID: \\.\pipe\notch-<sid>.
// 3. Fire-and-forget for telemetry (PreInvocation, PostToolUse, Stop).
// 4. Synchronous wait ONLY for PermissionRequest (two-phase decision gate).
// 5. Outputs documented Claude Code hookSpecificOutput JSON on decision.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Notch.Relay
{
    public static class Program
    {
        private const int CONNECT_TIMEOUT_MS = 300;
        private const int DECISION_BUDGET_MS = 110000; // 110s
        private const int MAX_FIELD_LEN = 2000;

        public static int Main(string[] args)
        {
            try
            {
                string eventName = args.Length > 0 ? args[0] : "";
                string stdinPayload = ReadStdin(50); // fast 50ms read

                if (string.IsNullOrWhiteSpace(stdinPayload) && string.IsNullOrEmpty(eventName))
                {
                    return 0;
                }

                var root = MiniJson.Parse(stdinPayload) as Dictionary<string, object>;
                if (root == null) root = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                if (string.IsNullOrEmpty(eventName))
                {
                    object evObj;
                    if (root.TryGetValue("hook_event_name", out evObj) && evObj != null)
                        eventName = evObj.ToString();
                    else if (root.TryGetValue("hookEventName", out evObj) && evObj != null)
                        eventName = evObj.ToString();
                }

                root["hook_event_name"] = eventName;

                // Enrich with terminal and process context
                EnrichContext(root);

                // Filter massive fields
                root.Remove("tool_response");
                root.Remove("transcript_path");

                bool waitsForAnswer = string.Equals(eventName, "PermissionRequest", StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(eventName, "approval-gate", StringComparison.OrdinalIgnoreCase);

                string pipeName = GetPipeName();
                string jsonString = MiniJson.Serialize(root);

                string decision = TalkToPipe(pipeName, jsonString, waitsForAnswer, waitsForAnswer ? DECISION_BUDGET_MS : CONNECT_TIMEOUT_MS);

                if (waitsForAnswer && !string.IsNullOrEmpty(decision))
                {
                    string outputJson = FormatDecisionJson(decision);
                    if (!string.IsNullOrEmpty(outputJson))
                    {
                        Console.WriteLine(outputJson);
                        Console.Out.Flush();
                    }
                }

                return 0;
            }
            catch
            {
                // Silent exit: never disrupt the agent
                return 0;
            }
        }

        private static string GetPipeName()
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

        private static string TalkToPipe(string pipeName, string payload, bool waitForAnswer, int timeoutMs)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    var connectTask = Task.Factory.StartNew(() => client.Connect(CONNECT_TIMEOUT_MS));
                    if (!connectTask.Wait(CONNECT_TIMEOUT_MS))
                    {
                        return null; // Notch closed, exit immediately
                    }

                    byte[] bytes = Encoding.UTF8.GetBytes(payload + "\n");
                    client.Write(bytes, 0, bytes.Length);
                    client.Flush();

                    if (!waitForAnswer) return null;

                    // Read decision line with timeout
                    var reader = new StreamReader(client, Encoding.UTF8);
                    var readTask = reader.ReadLineAsync();
                    if (readTask.Wait(timeoutMs))
                    {
                        string res = readTask.Result;
                        return res != null ? res.Trim() : null;
                    }
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        private static void EnrichContext(Dictionary<string, object> dict)
        {
            try
            {
                if (!dict.ContainsKey("cwd"))
                {
                    dict["cwd"] = Directory.GetCurrentDirectory();
                }

                foreach (var pair in new[]
                {
                    new KeyValuePair<string, string>("term_program", "TERM_PROGRAM"),
                    new KeyValuePair<string, string>("wt_session", "WT_SESSION"),
                    new KeyValuePair<string, string>("vscode_pid", "VSCODE_PID"),
                })
                {
                    if (!dict.ContainsKey(pair.Key))
                    {
                        string val = Environment.GetEnvironmentVariable(pair.Value);
                        if (!string.IsNullOrEmpty(val)) dict[pair.Key] = val;
                    }
                }
            }
            catch { }
        }

        private static string FormatDecisionJson(string decision)
        {
            string clean = decision.ToLowerInvariant().Trim();
            if (clean == "allow" || clean == "always")
            {
                return "{\"hookSpecificOutput\":{\"hookEventName\":\"PermissionRequest\",\"decision\":{\"behavior\":\"allow\"}}}";
            }
            if (clean == "deny")
            {
                return "{\"hookSpecificOutput\":{\"hookEventName\":\"PermissionRequest\",\"decision\":{\"behavior\":\"deny\",\"message\":\"Denied from Notch HUD\"}}}";
            }
            return null;
        }

        private static string ReadStdin(int timeoutMs)
        {
            try
            {
                if (!Console.IsInputRedirected) return "";
                var task = Task.Factory.StartNew(() => Console.In.ReadToEnd());
                if (task.Wait(timeoutMs))
                {
                    string s = task.Result;
                    if (s != null && s.StartsWith("\uFEFF")) s = s.Substring(1); // remove UTF-8 BOM
                    return s;
                }
            }
            catch { }
            return "";
        }
    }

    // ── Self-Contained Zero-Dependency JSON Parser ───────────────────────────────
    public class MiniJson
    {
        private readonly string _json;
        private int _index;

        public MiniJson(string json)
        {
            _json = json ?? string.Empty;
            _index = 0;
        }

        public static object Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            return new MiniJson(json).ParseValue();
        }

        private void SkipWhitespace()
        {
            while (_index < _json.Length && char.IsWhiteSpace(_json[_index])) _index++;
        }

        private object ParseValue()
        {
            SkipWhitespace();
            if (_index >= _json.Length) return null;
            char c = _json[_index];
            if (c == '{') return ParseObject();
            if (c == '[') return ParseArray();
            if (c == '"') return ParseString();
            if (c == 't' || c == 'f') return ParseBoolean();
            if (c == 'n') return ParseNull();
            if (char.IsDigit(c) || c == '-') return ParseNumber();
            return null;
        }

        private Dictionary<string, object> ParseObject()
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            _index++;
            while (_index < _json.Length)
            {
                SkipWhitespace();
                if (_index >= _json.Length) break;
                if (_json[_index] == '}') { _index++; break; }
                string key = ParseString();
                SkipWhitespace();
                if (_index < _json.Length && _json[_index] == ':') _index++;
                object val = ParseValue();
                dict[key] = val;
                SkipWhitespace();
                if (_index < _json.Length && _json[_index] == ',') _index++;
            }
            return dict;
        }

        private List<object> ParseArray()
        {
            var list = new List<object>();
            _index++;
            while (_index < _json.Length)
            {
                SkipWhitespace();
                if (_index >= _json.Length) break;
                if (_json[_index] == ']') { _index++; break; }
                object val = ParseValue();
                list.Add(val);
                SkipWhitespace();
                if (_index < _json.Length && _json[_index] == ',') _index++;
            }
            return list;
        }

        private string ParseString()
        {
            SkipWhitespace();
            if (_index >= _json.Length || _json[_index] != '"') return string.Empty;
            _index++;
            var sb = new StringBuilder();
            while (_index < _json.Length)
            {
                char c = _json[_index++];
                if (c == '"') return sb.ToString();
                if (c == '\\' && _index < _json.Length)
                {
                    char esc = _json[_index++];
                    if (esc == '"') sb.Append('"');
                    else if (esc == '\\') sb.Append('\\');
                    else if (esc == '/') sb.Append('/');
                    else if (esc == 'n') sb.Append('\n');
                    else if (esc == 'r') sb.Append('\r');
                    else if (esc == 't') sb.Append('\t');
                    else sb.Append(esc);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private object ParseNumber()
        {
            int start = _index;
            if (_index < _json.Length && _json[_index] == '-') _index++;
            while (_index < _json.Length && (char.IsDigit(_json[_index]) || _json[_index] == '.' || _json[_index] == 'e' || _json[_index] == 'E' || _json[_index] == '+' || _json[_index] == '-'))
            {
                _index++;
            }
            string s = _json.Substring(start, _index - start);
            long l;
            if (long.TryParse(s, out l)) return l;
            double d;
            if (double.TryParse(s, out d)) return d;
            return s;
        }

        private bool ParseBoolean()
        {
            if (_json.Substring(_index).StartsWith("true", StringComparison.OrdinalIgnoreCase)) { _index += 4; return true; }
            if (_json.Substring(_index).StartsWith("false", StringComparison.OrdinalIgnoreCase)) { _index += 5; return false; }
            _index++; return false;
        }

        private object ParseNull()
        {
            if (_json.Substring(_index).StartsWith("null", StringComparison.OrdinalIgnoreCase)) { _index += 4; }
            return null;
        }

        public static string Serialize(object obj)
        {
            if (obj == null) return "null";
            if (obj is string) return "\"" + Escape((string)obj) + "\"";
            if (obj is bool) return ((bool)obj) ? "true" : "false";
            if (obj is int || obj is long || obj is float || obj is double) return obj.ToString();
            if (obj is Dictionary<string, object>)
            {
                var dict = (Dictionary<string, object>)obj;
                var sb = new StringBuilder("{");
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append("\"").Append(Escape(kv.Key)).Append("\":").Append(Serialize(kv.Value));
                }
                sb.Append("}");
                return sb.ToString();
            }
            if (obj is List<object>)
            {
                var list = (List<object>)obj;
                var sb = new StringBuilder("[");
                bool first = true;
                foreach (var it in list)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append(Serialize(it));
                }
                sb.Append("]");
                return sb.ToString();
            }
            return "\"" + Escape(obj.ToString()) + "\"";
        }

        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '\\') sb.Append("\\\\");
                else if (c == '"') sb.Append("\\\"");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
