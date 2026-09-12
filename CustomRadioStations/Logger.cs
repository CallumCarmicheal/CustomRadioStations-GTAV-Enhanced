using System;
using System.IO;

/// <summary>
/// Static logger class that allows direct logging of anything to a text file.
/// Logging must remain safe during early Enhanced startup, before the scripts
/// directory tree necessarily exists.
/// </summary>
public static class Logger
{
    private static void EnsureParentDirectory(string path)
    {
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
    }

    public static void Log(object message, string path = "scripts\\Custom Radio Stations\\CustomRadioStations.log")
    {
        EnsureParentDirectory(path);
        File.AppendAllText(path, DateTime.Now + " : " + message + Environment.NewLine);
    }

    public static void Init(string path = "scripts\\Custom Radio Stations\\CustomRadioStations.log")
    {
        EnsureParentDirectory(path);
        if (File.Exists(path)) File.Delete(path);
    }
}
