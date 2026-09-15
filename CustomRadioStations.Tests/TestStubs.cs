using System.Collections.Generic;
using System.IO;

namespace CustomRadioStations {
    internal static class AppPaths {
        internal static string RootDirectory;
        internal static string TrackRatingsFile { get { return Path.Combine(RootDirectory, "track-ratings.json"); } }
        internal const string StationJsonFileName = "station.json";
    }

    internal static class Logger {
        internal static readonly List<string> Messages = new List<string>();
        internal static void Log(object message) {
            Messages.Add(message == null ? string.Empty : message.ToString());
        }
    }
}
