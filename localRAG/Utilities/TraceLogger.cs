using System;
using System.IO;
using Spectre.Console;

namespace localRAG.Utilities
{
    public static class TraceLogger
    {
        private static readonly object SyncRoot = new();
        private static readonly string LogDirectory = Path.Combine(Directory.GetCurrentDirectory(), "trace");
        private static string? _logFilePath;
        private static bool _initialized;

        private static void EnsureLogFile()
        {
            if (_initialized)
            {
                return;
            }

            lock (SyncRoot)
            {
                if (_initialized)
                {
                    return;
                }

                Directory.CreateDirectory(LogDirectory);
                _logFilePath = Path.Combine(LogDirectory, $"import-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
                File.AppendAllText(_logFilePath, $"# localRAG import trace {DateTime.UtcNow:O}{Environment.NewLine}");
                _initialized = true;
            }
        }

        public static void Log(string message, bool echoToConsole = false, string? consoleMarkup = null)
        {
            EnsureLogFile();
            var logLine = $"{DateTime.UtcNow:O} {message}";

            lock (SyncRoot)
            {
                if (_logFilePath != null)
                {
                    File.AppendAllText(_logFilePath, logLine + Environment.NewLine);
                }
            }

            if (echoToConsole)
            {
                var markup = consoleMarkup ?? Markup.Escape(logLine);
                AnsiConsole.MarkupLine(markup);
            }
        }

        public static string? CurrentLogFile
        {
            get
            {
                EnsureLogFile();
                return _logFilePath;
            }
        }
    }
}
