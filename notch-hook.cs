using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

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
                    else sb.Append(esc);
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
            SkipWhitespace();
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
            SkipWhitespace();
            if (_json.Substring(_index).StartsWith("null", StringComparison.OrdinalIgnoreCase))
            {
                _index += 4;
            }
            return null;
        }

        private object ParseNumber()
        {
            SkipWhitespace();
            int start = _index;
            while (_index < _json.Length && (char.IsDigit(_json[_index]) || _json[_index] == '.' || _json[_index] == '-' || _json[_index] == '+' || _json[_index] == 'e' || _json[_index] == 'E'))
                _index++;
            string numStr = _json.Substring(start, _index - start);
            long l;
            if (long.TryParse(numStr, out l)) return l;
            double d;
            if (double.TryParse(numStr, out d)) return d;
            return 0;
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
                else if (c < 32) sb.AppendFormat("\\u{0:x4}", (int)c);
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }

    public class Program
    {
        private static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        private static readonly string SpoolDir = Path.Combine(UserProfile, ".notch");
        private static readonly string SessionsDir = Path.Combine(SpoolDir, "sessions");
        private static readonly string PendingDir = Path.Combine(SpoolDir, "pending");
        private static readonly string DecisionsDir = Path.Combine(SpoolDir, "decisions");
        private static readonly string PidFile = Path.Combine(SpoolDir, "hud.pid");
        private static readonly string AutoFlagFile = Path.Combine(SpoolDir, "auto_mode.flag");
        private static readonly string AutoLogFile = Path.Combine(SpoolDir, "auto_execution.log");

        static string GetString(Dictionary<string, object> dict, string key, string defVal)
        {
            if (dict == null || !dict.ContainsKey(key) || dict[key] == null) return defVal;
            return dict[key].ToString();
        }

        static Dictionary<string, object> GetDict(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.ContainsKey(key) || dict[key] == null) return null;
            return dict[key] as Dictionary<string, object>;
        }

        static List<object> GetList(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.ContainsKey(key) || dict[key] == null) return null;
            return dict[key] as List<object>;
        }

        static void EnsureDirs()
        {
            if (!Directory.Exists(SpoolDir)) Directory.CreateDirectory(SpoolDir);
            if (!Directory.Exists(SessionsDir)) Directory.CreateDirectory(SessionsDir);
            if (!Directory.Exists(PendingDir)) Directory.CreateDirectory(PendingDir);
            if (!Directory.Exists(DecisionsDir)) Directory.CreateDirectory(DecisionsDir);
        }

        static bool IsHudRunning()
        {
            try
            {
                if (File.Exists(PidFile))
                {
                    string raw = File.ReadAllText(PidFile).Trim();
                    var dict = MiniJson.Parse(raw) as Dictionary<string, object>;
                    if (dict != null && dict.ContainsKey("pid"))
                    {
                        int pid = Convert.ToInt32(dict["pid"]);
                        Process p = Process.GetProcessById(pid);
                        if (p != null && !p.HasExited)
                        {
                            string pName = p.ProcessName.ToLowerInvariant();
                            if (pName.Contains("powershell") || pName.Contains("pwsh"))
                                return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        static long UtcNowSeconds()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        static void WriteFileAtomic(string filePath, string content)
        {
            try
            {
                File.WriteAllText(filePath, content, Encoding.UTF8);
            }
            catch
            {
                Thread.Sleep(10);
                try { File.WriteAllText(filePath, content, Encoding.UTF8); } catch { }
            }
        }

        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            EnsureDirs();

            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            string exeName = Path.GetFileNameWithoutExtension(Process.GetCurrentProcess().MainModule.FileName).ToLowerInvariant();

            if (string.IsNullOrEmpty(mode))
            {
                if (exeName.Contains("gate")) mode = "approval-gate";
                else if (exeName.Contains("pre")) mode = "pre-invocation";
                else if (exeName.Contains("post")) mode = "post-tool-use";
                else if (exeName.Contains("stop")) mode = "stop";
                else mode = "approval-gate";
            }

            string stdin = "";
            try
            {
                if (Console.IsInputRedirected)
                {
                    stdin = Console.In.ReadToEnd();
                }
            }
            catch { }

            switch (mode)
            {
                case "approval-gate":
                case "gate":
                    return HandleApprovalGate(stdin);

                case "pre-invocation":
                case "pre":
                    return HandlePreInvocation(stdin);

                case "post-tool-use":
                case "post":
                    return HandlePostToolUse(stdin);

                case "stop":
                    return HandleStop(stdin);

                default:
                    Console.WriteLine("Usage: agy-hook <approval-gate|pre-invocation|post-tool-use|stop>");
                    return 0;
            }
        }

        private static int HandlePreInvocation(string stdin)
        {
            try
            {
                var root = MiniJson.Parse(stdin) as Dictionary<string, object>;
                string convId = GetString(root, "conversationId", "default");
                string modelName = GetString(root, "modelName", "");
                
                string project = "";
                var paths = GetList(root, "workspacePaths");
                if (paths != null && paths.Count > 0 && paths[0] != null)
                {
                    project = Path.GetFileName(paths[0].ToString());
                }
                if (string.IsNullOrEmpty(project)) project = Path.GetFileName(Directory.GetCurrentDirectory());

                string stateJson = string.Format(
                    "{{\"state\":\"thinking\",\"message\":\"Thinking...\",\"timestamp\":{0},\"conversation_id\":\"{1}\",\"project\":\"{2}\",\"model\":\"{3}\",\"agent\":\"antigravity\"}}",
                    UtcNowSeconds(), MiniJson.Escape(convId), MiniJson.Escape(project), MiniJson.Escape(modelName)
                );

                WriteFileAtomic(Path.Combine(SessionsDir, convId + ".json"), stateJson);
            }
            catch { }

            Console.WriteLine("{}");
            return 0;
        }

        private static int HandlePostToolUse(string stdin)
        {
            try
            {
                var root = MiniJson.Parse(stdin) as Dictionary<string, object>;
                string convId = GetString(root, "conversationId", "default");
                string modelName = GetString(root, "modelName", "");

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
                    if (cmd.Length > 60) cmd = cmd.Substring(0, 57) + "...";
                    summary = "Ran: " + cmd;
                }
                else if (tool == "view_file" && targs != null)
                {
                    string p = GetString(targs, "AbsolutePath", "");
                    summary = "Read: " + Path.GetFileName(p);
                }
                else if ((tool == "replace_file_content" || tool == "write_to_file") && targs != null)
                {
                    string p = GetString(targs, "TargetFile", "");
                    summary = "Edited: " + Path.GetFileName(p);
                }
                else if (tool == "grep_search" && targs != null)
                {
                    string q = GetString(targs, "Query", "");
                    if (q.Length > 40) q = q.Substring(0, 37) + "...";
                    summary = "Search: " + q;
                }
                else if (tool == "search_web" && targs != null)
                {
                    string q = GetString(targs, "query", "");
                    if (q.Length > 40) q = q.Substring(0, 37) + "...";
                    summary = "Web: " + q;
                }

                string project = "";
                var paths = GetList(root, "workspacePaths");
                if (paths != null && paths.Count > 0 && paths[0] != null)
                {
                    project = Path.GetFileName(paths[0].ToString());
                }
                if (string.IsNullOrEmpty(project)) project = Path.GetFileName(Directory.GetCurrentDirectory());

                string stateJson = string.Format(
                    "{{\"state\":\"working\",\"message\":\"{0}\",\"timestamp\":{1},\"conversation_id\":\"{2}\",\"tool_name\":\"{3}\",\"project\":\"{4}\",\"model\":\"{5}\",\"agent\":\"antigravity\"}}",
                    MiniJson.Escape(summary), UtcNowSeconds(), MiniJson.Escape(convId), MiniJson.Escape(tool), MiniJson.Escape(project), MiniJson.Escape(modelName)
                );

                WriteFileAtomic(Path.Combine(SessionsDir, convId + ".json"), stateJson);
            }
            catch { }

            Console.WriteLine("{}");
            return 0;
        }

        private static int HandleStop(string stdin)
        {
            try
            {
                var root = MiniJson.Parse(stdin) as Dictionary<string, object>;
                string convId = GetString(root, "conversationId", "default");

                string stateJson = string.Format(
                    "{{\"state\":\"review\",\"message\":\"Task completed - Ready for review\",\"timestamp\":{0},\"conversation_id\":\"{1}\",\"agent\":\"antigravity\"}}",
                    UtcNowSeconds(), MiniJson.Escape(convId)
                );

                WriteFileAtomic(Path.Combine(SessionsDir, convId + ".json"), stateJson);
            }
            catch { }

            Console.WriteLine("{}");
            return 0;
        }

        private static int HandleApprovalGate(string stdin)
        {
            Console.WriteLine("{\"decision\":\"allow\"}");
            return 0;
        }
    }
}
