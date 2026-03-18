using System;
using System.IO;
using System.Text;

namespace AngryAudio
{
    public enum LogLevel
    {
        DEBUG,
        INFO,
        WARN,
        ERROR
    }

    public static class Logger
    {
        private static readonly object _lock = new object();
        private static string _logFilePath;
        private static bool _initialized;
        private const int MaxLines = 1000;

        public static void Initialize()
        {
            if (_initialized) return;

            try
            {
                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Angry Audio");

                if (!Directory.Exists(appDataDir))
                    Directory.CreateDirectory(appDataDir);

                _logFilePath = Path.Combine(appDataDir, "Angry Audio.log");
                TrimLogFile();
                _initialized = true;

                Info("Logger initialized. Log file: " + _logFilePath);
            }
            catch
            {
                // If we can't even initialize logging, we're in trouble.
                // But we don't crash the app over it.
                _initialized = false;
            }
        }

        public static void Debug(string message) { Write(LogLevel.DEBUG, message); }
        public static void Info(string message) { Write(LogLevel.INFO, message); }
        public static void Warn(string message) { Write(LogLevel.WARN, message); }
        public static void Error(string message) { Write(LogLevel.ERROR, message); }

        public static void Error(string message, Exception ex)
        {
            if (ex == null) { Write(LogLevel.ERROR, message); return; }
            Write(LogLevel.ERROR, message + " | " + ex.GetType().Name + ": " + ex.Message);
            Write(LogLevel.ERROR, "Stack trace: " + ex.StackTrace);
        }

        public static string GetLogFilePath()
        {
            return _logFilePath;
        }

        private static void Write(LogLevel level, string message)
        {
            if (!_initialized) return;

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string line = string.Format("[{0}] [{1}] {2}", timestamp, level, message);

            lock (_lock)
            {
                try
                {
                    if (_buffer == null) _buffer = new System.Collections.Generic.List<string>(64);
                    _buffer.Add(line);
                    if (_buffer.Count >= 32 || level >= LogLevel.ERROR)
                        FlushBuffer();
                    else if (_flushTimer == null)
                    {
                        _flushTimer = new System.Threading.Timer(_ => { lock (_lock) { FlushBuffer(); } }, null, 5000, System.Threading.Timeout.Infinite);
                    }
                }
                catch { }
            }
        }

        private static System.Collections.Generic.List<string> _buffer;
        private static System.Threading.Timer _flushTimer;

        private static void FlushBuffer()
        {
            // Must be called inside _lock
            if (_buffer == null || _buffer.Count == 0) return;
            try
            {
                using (var writer = new StreamWriter(_logFilePath, true, Encoding.UTF8))
                {
                    foreach (var l in _buffer) writer.WriteLine(l);
                }
                _buffer.Clear();
            }
            catch { }
            if (_flushTimer != null) { _flushTimer.Dispose(); _flushTimer = null; }
        }

        private static void TrimLogFile()
        {
            try
            {
                if (!File.Exists(_logFilePath)) return;

                string[] lines = File.ReadAllLines(_logFilePath, Encoding.UTF8);
                if (lines.Length <= MaxLines) return;

                // Keep only the last MaxLines lines
                string[] trimmed = new string[MaxLines];
                Array.Copy(lines, lines.Length - MaxLines, trimmed, 0, MaxLines);
                File.WriteAllLines(_logFilePath, trimmed, Encoding.UTF8);
            }
            catch
            {
                // If we can't trim, just continue. The file will grow until next startup.
            }
        }
    }
}
