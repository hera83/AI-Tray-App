using System;
using System.IO;
using System.Text;

namespace TrayApp.Infrastructure
{
    public sealed class FileAppLogger : IAppLogger
    {
        private readonly object _sync = new();

        public string LogFilePath { get; }

        public FileAppLogger(string logFilePath)
        {
            LogFilePath = logFilePath;
            var dir = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
        }

        public void LogInfo(string message) => Write("INFO", message, null);
        public void LogWarning(string message) => Write("WARN", message, null);
        public void LogError(string message, Exception? exception = null) => Write("ERROR", message, exception);

        private void Write(string level, string message, Exception? exception)
        {
            var sb = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
                .Append(" [")
                .Append(level)
                .Append("] ")
                .Append(message);

            if (exception != null)
            {
                sb.AppendLine()
                  .Append(exception.GetType().FullName)
                  .Append(": ")
                  .Append(exception.Message)
                  .AppendLine()
                  .Append(exception.StackTrace);
            }

            lock (_sync)
            {
                File.AppendAllText(LogFilePath, sb.AppendLine().ToString(), Encoding.UTF8);
            }
        }
    }
}
