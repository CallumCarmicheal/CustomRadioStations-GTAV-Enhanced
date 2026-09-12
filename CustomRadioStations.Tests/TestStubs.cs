using System.Collections.Generic;

namespace CustomRadioStations
{
    internal static class AppPaths
    {
        internal const string StationJsonFileName = "station.json";
    }

    internal static class Logger
    {
        internal static readonly List<string> Messages = new List<string>();
        internal static void Log(object message) { Messages.Add(message == null ? string.Empty : message.ToString()); }
    }
}
