using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Notch.HUD
{
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
            var parser = new MiniJson(json);
            return parser.ParseValue();
        }

        private void SkipWhitespace()
        {
            while (_index < _json.Length && char.IsWhiteSpace(_json[_index]))
                _index++;
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
            _index++; // skip '{'

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
            _index++; // skip '['

            while (_index < _json.Length)
            {
                SkipWhitespace();
                if (_index >= _json.Length) break;
                if (_json[_index] == ']') { _index++; break; }

                list.Add(ParseValue());

                SkipWhitespace();
                if (_index < _json.Length && _json[_index] == ',') _index++;
            }
            return list;
        }

        private string ParseString()
        {
            SkipWhitespace();
            if (_index >= _json.Length || _json[_index] != '"') return string.Empty;
            _index++; // skip open quote

            var sb = new StringBuilder();
            while (_index < _json.Length)
            {
                char c = _json[_index++];
                if (c == '"') break;
                if (c == '\\' && _index < _json.Length)
                {
                    char esc = _json[_index++];
                    if (esc == '"') sb.Append('"');
                    else if (esc == '\\') sb.Append('\\');
                    else if (esc == '/') sb.Append('/');
                    else if (esc == 'b') sb.Append('\b');
                    else if (esc == 'f') sb.Append('\f');
                    else if (esc == 'n') sb.Append('\n');
                    else if (esc == 'r') sb.Append('\r');
                    else if (esc == 't') sb.Append('\t');
                    else if (esc == 'u' && _index + 4 <= _json.Length)
                    {
                        string hex = _json.Substring(_index, 4);
                        _index += 4;
                        int code;
                        if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out code))
                            sb.Append((char)code);
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private bool ParseBoolean()
        {
            if (_json.Substring(_index).StartsWith("true", StringComparison.OrdinalIgnoreCase))
            {
                _index += 4;
                return true;
            }
            if (_json.Substring(_index).StartsWith("false", StringComparison.OrdinalIgnoreCase))
            {
                _index += 5;
                return false;
            }
            return false;
        }

        private object ParseNull()
        {
            if (_json.Substring(_index).StartsWith("null", StringComparison.OrdinalIgnoreCase))
            {
                _index += 4;
            }
            return null;
        }

        private object ParseNumber()
        {
            int start = _index;
            if (_index < _json.Length && _json[_index] == '-') _index++;
            while (_index < _json.Length && (char.IsDigit(_json[_index]) || _json[_index] == '.' || _json[_index] == 'e' || _json[_index] == 'E' || _json[_index] == '+' || _json[_index] == '-'))
            {
                _index++;
            }
            string numStr = _json.Substring(start, _index - start);
            long l;
            if (long.TryParse(numStr, out l)) return l;
            double d;
            if (double.TryParse(numStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d)) return d;
            return 0;
        }

        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 4);
            foreach (char c in s)
            {
                if (c == '\\') sb.Append("\\\\");
                else if (c == '"') sb.Append("\\\"");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < 32) sb.AppendFormat("\\u{0:x4}", (int)c);
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }

    public static class Program
    {
        private static readonly string UserHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        
        // Spool 1: .notch
        private static readonly string AgySpool = Path.Combine(UserHome, ".notch");
        private static readonly string AgySessions = Path.Combine(AgySpool, "sessions");
        private static readonly string AgyPending = Path.Combine(AgySpool, "pending");
        private static readonly string AgyDecisions = Path.Combine(AgySpool, "decisions");
        private static readonly string AgyState = Path.Combine(AgySpool, "state.json");
        private static readonly string AgyAutoFlag = Path.Combine(AgySpool, "auto_mode.flag");

        // Spool 2: .notch (Jaswanth's open-source standalone format)
        private static readonly string NotchSpool = Path.Combine(UserHome, ".notch");
        private static readonly string NotchSessions = Path.Combine(NotchSpool, "sessions");
        private static readonly string NotchState = Path.Combine(NotchSpool, "state.json");

        private static void EnsureDirs()
        {
            try
            {
                if (!Directory.Exists(AgySessions)) Directory.CreateDirectory(AgySessions);
                if (!Directory.Exists(AgyPending)) Directory.CreateDirectory(AgyPending);
                if (!Directory.Exists(AgyDecisions)) Directory.CreateDirectory(AgyDecisions);
                if (!Directory.Exists(NotchSessions)) Directory.CreateDirectory(NotchSessions);
            }
            catch { }
        }

        private static long UtcNowSeconds()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        private static string GetString(Dictionary<string, object> dict, string key, string fallback = "")
        {
            if (dict == null) return fallback;
            object val;
            if (dict.TryGetValue(key, out val) && val != null) return val.ToString();
            return fallback;
        }

        private static Dictionary<string, object> GetDict(Dictionary<string, object> dict, string key)
        {
            if (dict == null) return null;
            object val;
            if (dict.TryGetValue(key, out val) && val is Dictionary<string, object>) return (Dictionary<string, object>)val;
            return null;
        }

        private static List<object> GetList(Dictionary<string, object> dict, string key)
        {
            if (dict == null) return null;
            object val;
            if (dict.TryGetValue(key, out val) && val is List<object>) return (List<object>)val;
            return null;
        }

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        private static void WriteFileAtomic(string filePath, string content)
        {
            try
            {
                string tmp = filePath + ".tmp." + Guid.NewGuid().ToString("N");
                File.WriteAllText(tmp, content, Utf8NoBom);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                File.Move(tmp, filePath);
            }
            catch
            {
                try { File.WriteAllText(filePath, content, Utf8NoBom); } catch { }
            }
        }

        private static bool TrySendNamedPipe(string payload, out string response, int timeoutMs = 80)
        {
            response = "";
            try
            {
                using (var pipe = new System.IO.Pipes.NamedPipeClientStream(".", "notch_ipc", System.IO.Pipes.PipeDirection.InOut))
                {
                    pipe.Connect(timeoutMs);
                    using (var writer = new StreamWriter(pipe, new UTF8Encoding(false)))
                    using (var reader = new StreamReader(pipe, Encoding.UTF8))
                    {
                        writer.WriteLine(payload);
                        writer.Flush();
                        response = reader.ReadLine();
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private static void BroadcastState(string convId, string stateJson)
        {
            EnsureDirs();
            // Fast Path: Direct Win32 Named Pipe to NotchCore Daemon (<0.2ms)
            string pipeResp;
            TrySendNamedPipe(stateJson, out pipeResp, 60);

            // Resilient Fallback Spool
            if (!string.IsNullOrEmpty(convId))
            {
                WriteFileAtomic(Path.Combine(AgySessions, convId + ".json"), stateJson);
                WriteFileAtomic(Path.Combine(NotchSessions, convId + ".json"), stateJson);
            }
            WriteFileAtomic(AgyState, stateJson);
            WriteFileAtomic(NotchState, stateJson);
        }

        public static int Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            try
            {
                Console.OutputEncoding = Utf8NoBom;
            }
            catch { }

            try
            {
                return Run(mode);
            }
            catch
            {
                // Blanket guarantee: Never exit non-zero; never break CLI turn
                if (mode.Contains("gate") || mode.Contains("pre-tool"))
                {
                    Console.WriteLine("{\"decision\":\"allow\"}");
                }
                else
                {
                    Console.WriteLine("{}");
                }
                return 0;
            }
        }

        private static int Run(string mode)
        {
            EnsureDirs();

            string exeName = Path.GetFileNameWithoutExtension(Process.GetCurrentProcess().MainModule.FileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(mode))
            {
                if (exeName.Contains("gate")) mode = "approval-gate";
                else if (exeName.Contains("pre-invoc")) mode = "pre-invocation";
                else if (exeName.Contains("pre-tool")) mode = "pre-tool-use";
                else if (exeName.Contains("post")) mode = "post-tool-use";
                else if (exeName.Contains("stop")) mode = "stop";
                else mode = "pre-tool-use";
            }

            string stdin = "";
            try
            {
                if (Console.IsInputRedirected)
                {
                    var readTask = Task.Factory.StartNew(() => Console.In.ReadToEnd());
                    if (readTask.Wait(50)) // 50ms strict bound
                    {
                        stdin = readTask.Result;
                    }
                }
            }
            catch { }

            var root = MiniJson.Parse(stdin) as Dictionary<string, object>;
            string convId = GetString(root, "conversationId", "default");
            string modelName = GetString(root, "modelName", "Antigravity");

            string project = "";
            var paths = GetList(root, "workspacePaths");
            if (paths != null && paths.Count > 0 && paths[0] != null)
            {
                project = Path.GetFileName(paths[0].ToString().TrimEnd('\\', '/'));
            }
            if (string.IsNullOrEmpty(project))
            {
                project = Path.GetFileName(Directory.GetCurrentDirectory().TrimEnd('\\', '/'));
            }

            switch (mode)
            {
                case "pre-invocation":
                case "pre":
                    return HandlePreInvocation(convId, modelName, project);

                case "pre-tool-use":
                case "pre-tool":
                case "approval-gate":
                case "gate":
                    return HandlePreToolUse(root, convId, modelName, project);

                case "post-tool-use":
                case "post":
                case "post-tool":
                    return HandlePostToolUse(root, convId, modelName, project);

                case "stop":
                    return HandleStop(convId, modelName, project);

                default:
                    Console.WriteLine("{}");
                    return 0;
            }
        }

        private static int HandlePreInvocation(string convId, string modelName, string project)
        {
            string stateJson = string.Format(
                "{{\"state\":\"thinking\",\"message\":\"Thinking...\",\"timestamp\":{0},\"conversation_id\":\"{1}\",\"project\":\"{2}\",\"model\":\"{3}\",\"agent\":\"antigravity\"}}",
                UtcNowSeconds(), MiniJson.Escape(convId), MiniJson.Escape(project), MiniJson.Escape(modelName)
            );

            BroadcastState(convId, stateJson);
            Console.WriteLine("{}");
            return 0;
        }

        private static int HandlePreToolUse(Dictionary<string, object> root, string convId, string modelName, string project)
        {
            string tool = "tool";
            Dictionary<string, object> targs = null;
            var tc = GetDict(root, "toolCall");
            if (tc != null)
            {
                tool = GetString(tc, "name", "tool");
                targs = GetDict(tc, "args");
            }

            string summary = "Running " + tool;
            if (tool == "run_command" && targs != null)
            {
                string cmd = GetString(targs, "CommandLine", "");
                if (cmd.Length > 45) cmd = cmd.Substring(0, 42) + "...";
                summary = "Run: " + cmd;
            }
            else if (tool == "view_file" && targs != null)
            {
                string p = GetString(targs, "AbsolutePath", "");
                summary = "Read: " + Path.GetFileName(p);
            }
            else if ((tool == "replace_file_content" || tool == "write_to_file") && targs != null)
            {
                string p = GetString(targs, "TargetFile", "");
                summary = "Edit: " + Path.GetFileName(p);
            }
            else if (tool == "grep_search" && targs != null)
            {
                string q = GetString(targs, "Query", "");
                if (q.Length > 30) q = q.Substring(0, 27) + "...";
                summary = "Grep: " + q;
            }
            else if (tool == "search_web" && targs != null)
            {
                string q = GetString(targs, "query", "");
                if (q.Length > 30) q = q.Substring(0, 27) + "...";
                summary = "Web: " + q;
            }

            string stateJson = string.Format(
                "{{\"state\":\"working\",\"message\":\"{0}\",\"timestamp\":{1},\"conversation_id\":\"{2}\",\"tool_name\":\"{3}\",\"project\":\"{4}\",\"model\":\"{5}\",\"agent\":\"antigravity\"}}",
                MiniJson.Escape(summary), UtcNowSeconds(), MiniJson.Escape(convId), MiniJson.Escape(tool), MiniJson.Escape(project), MiniJson.Escape(modelName)
            );

            BroadcastState(convId, stateJson);

            // PreToolUse contract requires a decision
            Console.WriteLine("{\"decision\":\"allow\"}");
            return 0;
        }

        private static int HandlePostToolUse(Dictionary<string, object> root, string convId, string modelName, string project)
        {
            string err = GetString(root, "error", "");
            if (!string.IsNullOrEmpty(err))
            {
                string stateJson = string.Format(
                    "{{\"state\":\"error\",\"message\":\"Tool failed: {0}\",\"timestamp\":{1},\"conversation_id\":\"{2}\",\"project\":\"{3}\",\"model\":\"{4}\",\"agent\":\"antigravity\"}}",
                    MiniJson.Escape(err), UtcNowSeconds(), MiniJson.Escape(convId), MiniJson.Escape(project), MiniJson.Escape(modelName)
                );
                BroadcastState(convId, stateJson);
            }

            Console.WriteLine("{}");
            return 0;
        }

        private static int HandleStop(string convId, string modelName, string project)
        {
            string stateJson = string.Format(
                "{{\"state\":\"review\",\"message\":\"Task completed - Ready for review\",\"timestamp\":{0},\"conversation_id\":\"{1}\",\"project\":\"{2}\",\"model\":\"{3}\",\"agent\":\"antigravity\"}}",
                UtcNowSeconds(), MiniJson.Escape(convId), MiniJson.Escape(project), MiniJson.Escape(modelName)
            );

            BroadcastState(convId, stateJson);
            Console.WriteLine("{}");
            return 0;
        }
    }
}
