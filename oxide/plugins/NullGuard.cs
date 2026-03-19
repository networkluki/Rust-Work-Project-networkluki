// NullGuard.cs — Oxide/uMod plugin for Rust
// Catches NullReferenceException globally, logs origin, stack trace,
// and frequency statistics to oxide/logs/NullGuard/
//
// Logs:    oxide/logs/NullGuard/

using Oxide.Core;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NullGuard", "networkluki", "1.2.0")]
    [Description("Diagnoses and logs NullReferenceException errors with full context")]
    public class NullGuard : RustPlugin
    {
        #region Configuration

        private Configuration _config;

        private class Configuration
        {
            // Log to oxide/logs/ files
            public bool LogToFile { get; set; } = true;

            // Also print to server console (rcon visible)
            public bool LogToConsole { get; set; } = true;

            // Track per-source frequency stats
            public bool TrackFrequency { get; set; } = true;

            // Suppress repeated identical errors within this window (seconds)
            // Prevents log spam from rapid-fire errors in OnTick etc.
            public float DedupeWindowSeconds { get; set; } = 5.0f;

            // Max unique error signatures to track (memory cap)
            public int MaxTrackedSignatures { get; set; } = 500;
        }

        protected override void LoadDefaultConfig()
        {
            _config = new Configuration();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>() ?? new Configuration();
            }
            catch
            {
                PrintWarning("Config file corrupt or unreadable — loading defaults.");
                _config = new Configuration();
            }
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        #endregion

        #region State

        // Tracks how many times each unique error signature has occurred
        private readonly Dictionary<string, ErrorStats> _errorFrequency
            = new Dictionary<string, ErrorStats>();

        // Deduplication: last time a given signature was logged
        private readonly Dictionary<string, float> _lastLogTime
            = new Dictionary<string, float>();

        private class ErrorStats
        {
            public int Count;
            public string FirstSeen;
            public string LastSeen;
            public string SampleTrace;
            public string SourcePlugin;
        }

        #endregion

        #region Hooks

        private void OnServerInitialized()
        {
            // Hook into Unity's log callback to catch ALL exceptions,
            // including those from other plugins and native Rust/Unity code.
            Application.logMessageReceived += OnLogMessageReceived;

            Puts("[NullGuard] Active — monitoring for NullReferenceException errors.");
        }

        private void Unload()
        {
            Application.logMessageReceived -= OnLogMessageReceived;

            // Dump final frequency report on unload
            if (_config.TrackFrequency && _errorFrequency.Count > 0)
            {
                LogFrequencyReport();
            }
        }

        #endregion

        #region Core Logic

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            // Only process errors and exceptions
            if (type != LogType.Exception && type != LogType.Error)
                return;

            // Filter: must be a NullReferenceException
            if (condition == null || !condition.Contains("NullReferenceException"))
                return;

            ProcessNullReference(condition, stackTrace);
        }

        private void ProcessNullReference(string message, string stackTrace)
        {
            string signature = GenerateSignature(stackTrace);

            // --- Deduplication ---
            float now = Time.realtimeSinceStartup;
            if (_lastLogTime.TryGetValue(signature, out float lastTime))
            {
                if (now - lastTime < _config.DedupeWindowSeconds)
                {
                    // Still within dedupe window — count it but don't log again
                    IncrementStats(signature, stackTrace, message);
                    return;
                }
            }
            _lastLogTime[signature] = now;

            // --- Parse the stack trace ---
            ParsedError parsed = ParseStackTrace(message, stackTrace);

            // --- Update frequency tracking ---
            int occurrences = IncrementStats(signature, stackTrace, message);

            // --- Build the log entry ---
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("╔══════════════════════════════════════════════════════════");
            sb.AppendLine("║  NULLREFERENCEEXCEPTION CAUGHT");
            sb.AppendLine("╠══════════════════════════════════════════════════════════");
            sb.AppendLine($"║  Time (UTC) : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"║  Time (Local): {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"║  Signature   : {signature}");
            sb.AppendLine($"║  Occurrences : {occurrences}");
            sb.AppendLine("╠══════════════════════════════════════════════════════════");
            sb.AppendLine($"║  Source Plugin : {parsed.SourcePlugin ?? "Unknown / Native"}");
            sb.AppendLine($"║  Source Method : {parsed.SourceMethod ?? "Unknown"}");
            sb.AppendLine($"║  Source File   : {parsed.SourceFile ?? "N/A"}");
            sb.AppendLine($"║  Source Line   : {parsed.SourceLine ?? "N/A"}");
            sb.AppendLine("╠══════════════════════════════════════════════════════════");
            sb.AppendLine("║  EXCEPTION MESSAGE:");
            sb.AppendLine($"║  {message}");
            sb.AppendLine("╠══════════════════════════════════════════════════════════");
            sb.AppendLine("║  FULL STACK TRACE:");

            if (!string.IsNullOrEmpty(stackTrace))
            {
                string[] lines = stackTrace.Split('\n');
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length > 0)
                    {
                        // Highlight Oxide plugin frames for easy spotting
                        string prefix = trimmed.Contains("Oxide.Plugins") ? "║  >>> " : "║      ";
                        sb.AppendLine($"{prefix}{trimmed}");
                    }
                }
            }
            else
            {
                sb.AppendLine("║      (no stack trace available)");
            }

            sb.AppendLine("╠══════════════════════════════════════════════════════════");
            sb.AppendLine("║  CALL CHAIN (Oxide plugins in trace):");

            if (parsed.PluginFrames.Count > 0)
            {
                for (int i = 0; i < parsed.PluginFrames.Count; i++)
                {
                    sb.AppendLine($"║    [{i + 1}] {parsed.PluginFrames[i]}");
                }
            }
            else
            {
                sb.AppendLine("║    (no Oxide plugin frames found — likely native/Unity code)");
            }

            sb.AppendLine("╚══════════════════════════════════════════════════════════");

            string logEntry = sb.ToString();

            // --- Output ---
            if (_config.LogToConsole)
            {
                PrintError(logEntry);
            }

            if (_config.LogToFile)
            {
                // Per-day log file: oxide/logs/NullGuard/NullGuard_2026-03-19.txt
                string dateTag = DateTime.UtcNow.ToString("yyyy-MM-dd");
                LogToFile("NullGuard", $"{dateTag}\n{logEntry}", this);
            }
        }

        #endregion

        #region Stack Trace Parsing

        private class ParsedError
        {
            public string SourcePlugin;
            public string SourceMethod;
            public string SourceFile;
            public string SourceLine;
            public List<string> PluginFrames = new List<string>();
        }

        // Matches: Oxide.Plugins.SomePlugin.SomeMethod (args) [0x00000] in <hash>:123
        private static readonly Regex PluginFrameRegex = new Regex(
            @"Oxide\.Plugins\.(\w+)\.(\w+)\s*\(",
            RegexOptions.Compiled
        );

        // Matches file:line at end of stack frame
        private static readonly Regex FileLineRegex = new Regex(
            @"in\s+(.+?):(\d+)\s*$",
            RegexOptions.Compiled
        );

        // Matches any method signature in a stack frame
        private static readonly Regex MethodRegex = new Regex(
            @"at\s+(.+?)\s*\(",
            RegexOptions.Compiled
        );

        private ParsedError ParseStackTrace(string message, string stackTrace)
        {
            ParsedError parsed = new ParsedError();

            if (string.IsNullOrEmpty(stackTrace))
                return parsed;

            string[] lines = stackTrace.Split('\n');
            bool foundFirst = false;

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;

                // Check if this frame is from an Oxide plugin
                var pluginMatch = PluginFrameRegex.Match(line);
                if (pluginMatch.Success)
                {
                    string pluginName = pluginMatch.Groups[1].Value;
                    string methodName = pluginMatch.Groups[2].Value;

                    parsed.PluginFrames.Add($"{pluginName}.{methodName}");

                    // First plugin frame = most likely source
                    if (!foundFirst)
                    {
                        foundFirst = true;
                        parsed.SourcePlugin = pluginName;
                        parsed.SourceMethod = methodName;

                        var fileMatch = FileLineRegex.Match(line);
                        if (fileMatch.Success)
                        {
                            parsed.SourceFile = fileMatch.Groups[1].Value;
                            parsed.SourceLine = fileMatch.Groups[2].Value;
                        }
                    }
                }

                // If no plugin frame found yet, grab the top-most method
                if (!foundFirst && parsed.SourceMethod == null)
                {
                    var methodMatch = MethodRegex.Match(line);
                    if (methodMatch.Success)
                    {
                        parsed.SourceMethod = methodMatch.Groups[1].Value;

                        var fileMatch = FileLineRegex.Match(line);
                        if (fileMatch.Success)
                        {
                            parsed.SourceFile = fileMatch.Groups[1].Value;
                            parsed.SourceLine = fileMatch.Groups[2].Value;
                        }
                    }
                }
            }

            return parsed;
        }

        #endregion

        #region Frequency Tracking

        /// <summary>
        /// Generates a short signature from the top frames of the stack trace.
        /// Two errors with the same signature are considered "the same bug."
        /// </summary>
        private string GenerateSignature(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace))
                return "no-trace";

            // Use the first 2 meaningful stack frames as the signature
            string[] lines = stackTrace.Split('\n');
            StringBuilder sig = new StringBuilder();
            int count = 0;

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || !line.StartsWith("at ") && !line.Contains("("))
                    continue;

                // Strip memory addresses and hashes that change between runs
                string clean = Regex.Replace(line, @"\[0x[0-9a-fA-F]+\]", "");
                clean = Regex.Replace(clean, @"<[0-9a-fA-F]+>", "");
                clean = clean.Trim();

                sig.Append(clean);
                count++;
                if (count >= 2) break;
            }

            // Simple hash to keep the signature short
            string raw = sig.ToString();
            return raw.Length > 0
                ? $"SIG-{raw.GetHashCode():X8}"
                : "no-trace";
        }

        private int IncrementStats(string signature, string stackTrace, string message)
        {
            if (!_config.TrackFrequency)
                return 1;

            // Memory safety: cap tracked signatures
            if (!_errorFrequency.ContainsKey(signature)
                && _errorFrequency.Count >= _config.MaxTrackedSignatures)
            {
                return 1;
            }

            string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            if (_errorFrequency.TryGetValue(signature, out ErrorStats stats))
            {
                stats.Count++;
                stats.LastSeen = now;
                return stats.Count;
            }

            var parsed = ParseStackTrace(message, stackTrace);
            _errorFrequency[signature] = new ErrorStats
            {
                Count = 1,
                FirstSeen = now,
                LastSeen = now,
                SampleTrace = stackTrace?.Length > 500
                    ? stackTrace.Substring(0, 500) + "..."
                    : stackTrace,
                SourcePlugin = parsed.SourcePlugin ?? "Unknown"
            };

            return 1;
        }

        #endregion

        #region Commands

        [ChatCommand("nullguard")]
        private void CmdNullGuard(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                player.ChatMessage("<color=#ff4444>NullGuard: Admin only.</color>");
                return;
            }

            if (args.Length == 0)
            {
                ShowHelp(player);
                return;
            }

            switch (args[0].ToLower())
            {
                case "stats":
                    ShowStats(player);
                    break;
                case "top":
                    ShowTopErrors(player);
                    break;
                case "clear":
                    _errorFrequency.Clear();
                    _lastLogTime.Clear();
                    player.ChatMessage("<color=#44ff44>NullGuard: Stats cleared.</color>");
                    break;
                case "report":
                    LogFrequencyReport();
                    player.ChatMessage("<color=#44ff44>NullGuard: Report written to logs.</color>");
                    break;
                default:
                    ShowHelp(player);
                    break;
            }
        }

        [ConsoleCommand("nullguard.stats")]
        private void CcmdStats(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin) return;
            ShowConsoleStats();
        }

        [ConsoleCommand("nullguard.report")]
        private void CcmdReport(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin) return;
            LogFrequencyReport();
            Puts("[NullGuard] Frequency report written to log file.");
        }

        [ConsoleCommand("nullguard.clear")]
        private void CcmdClear(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin) return;
            _errorFrequency.Clear();
            _lastLogTime.Clear();
            Puts("[NullGuard] Stats cleared.");
        }

        #endregion

        #region Output Helpers

        private void ShowHelp(BasePlayer player)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<color=#ffaa00>═══ NullGuard Commands ═══</color>");
            sb.AppendLine("<color=#cccccc>/nullguard stats</color>  — Summary of tracked errors");
            sb.AppendLine("<color=#cccccc>/nullguard top</color>   — Top 10 most frequent errors");
            sb.AppendLine("<color=#cccccc>/nullguard clear</color> — Reset all counters");
            sb.AppendLine("<color=#cccccc>/nullguard report</color> — Write full report to log file");
            player.ChatMessage(sb.ToString());
        }

        private void ShowStats(BasePlayer player)
        {
            if (_errorFrequency.Count == 0)
            {
                player.ChatMessage("<color=#44ff44>NullGuard: No NullReferenceExceptions recorded.</color>");
                return;
            }

            int totalErrors = 0;
            HashSet<string> plugins = new HashSet<string>();

            foreach (var kvp in _errorFrequency)
            {
                totalErrors += kvp.Value.Count;
                plugins.Add(kvp.Value.SourcePlugin);
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<color=#ffaa00>═══ NullGuard Stats ═══</color>");
            sb.AppendLine($"Unique signatures: <color=#ff4444>{_errorFrequency.Count}</color>");
            sb.AppendLine($"Total occurrences: <color=#ff4444>{totalErrors}</color>");
            sb.AppendLine($"Plugins involved:  <color=#ff4444>{plugins.Count}</color>");

            foreach (string p in plugins)
            {
                sb.AppendLine($"  — {p}");
            }

            player.ChatMessage(sb.ToString());
        }

        private void ShowTopErrors(BasePlayer player)
        {
            if (_errorFrequency.Count == 0)
            {
                player.ChatMessage("<color=#44ff44>NullGuard: No errors recorded.</color>");
                return;
            }

            // Sort by count descending
            var sorted = new List<KeyValuePair<string, ErrorStats>>(_errorFrequency);
            sorted.Sort((a, b) => b.Value.Count.CompareTo(a.Value.Count));

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<color=#ffaa00>═══ Top NullRef Errors ═══</color>");

            int shown = 0;
            foreach (var kvp in sorted)
            {
                if (shown >= 10) break;
                sb.AppendLine($"<color=#ff4444>[{kvp.Value.Count}x]</color> {kvp.Value.SourcePlugin} — {kvp.Key}");
                sb.AppendLine($"      Last: {kvp.Value.LastSeen}");
                shown++;
            }

            player.ChatMessage(sb.ToString());
        }

        private void ShowConsoleStats()
        {
            if (_errorFrequency.Count == 0)
            {
                Puts("[NullGuard] No NullReferenceExceptions recorded.");
                return;
            }

            var sorted = new List<KeyValuePair<string, ErrorStats>>(_errorFrequency);
            sorted.Sort((a, b) => b.Value.Count.CompareTo(a.Value.Count));

            Puts("=== NullGuard — Error Frequency ===");
            foreach (var kvp in sorted)
            {
                Puts($"  [{kvp.Value.Count,5}x] {kvp.Value.SourcePlugin,-20} {kvp.Key}  (first: {kvp.Value.FirstSeen}, last: {kvp.Value.LastSeen})");
            }
        }

        private void LogFrequencyReport()
        {
            if (_errorFrequency.Count == 0) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("╔══════════════════════════════════════════════════════════");
            sb.AppendLine("║  NULLGUARD — FREQUENCY REPORT");
            sb.AppendLine($"║  Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine("╠══════════════════════════════════════════════════════════");

            var sorted = new List<KeyValuePair<string, ErrorStats>>(_errorFrequency);
            sorted.Sort((a, b) => b.Value.Count.CompareTo(a.Value.Count));

            int rank = 1;
            foreach (var kvp in sorted)
            {
                var s = kvp.Value;
                sb.AppendLine($"║  #{rank++}  {kvp.Key}");
                sb.AppendLine($"║       Plugin     : {s.SourcePlugin}");
                sb.AppendLine($"║       Count      : {s.Count}");
                sb.AppendLine($"║       First seen : {s.FirstSeen}");
                sb.AppendLine($"║       Last seen  : {s.LastSeen}");
                sb.AppendLine($"║       Sample     : {s.SampleTrace}");
                sb.AppendLine("║  ──────────────────────────────────────────────────────");
            }

            sb.AppendLine("╚══════════════════════════════════════════════════════════");

            string dateTag = DateTime.UtcNow.ToString("yyyy-MM-dd");
            LogToFile("NullGuard", $"REPORT_{dateTag}\n{sb}", this);
        }

        #endregion
    }
}
