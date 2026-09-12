using System;
using System.IO;

namespace CustomRadioStations {
    /// <summary>
    /// Lightweight file logger that remains safe during early Enhanced startup.
    /// </summary>
    internal static class Logger {
        private static void EnsureParentDirectory(string path) {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        }

        internal static void Log(object message, string path = AppPaths.MainLogFile) {
            EnsureParentDirectory(path);
            File.AppendAllText(path, DateTime.Now + " : " + message + Environment.NewLine);
        }

        internal static void Init(string path = AppPaths.MainLogFile) {
            EnsureParentDirectory(path);
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}