using System;

namespace CustomRadioStations {
    /// <summary>
    /// Process-lifetime state that survives SHVDNE script AppDomain reloads
    /// but is automatically cleared when GTA5.exe exits.
    /// </summary>
    internal static class RuntimeState {
        private const string LoadedOnceEnvironmentVariable = "CRSSH_HAS_LOADED_ONCE";

        internal static bool CatalogLoadCompleted { get; set; }

        internal static bool HasLoadedOnce {
            get {
                return string.Equals(
                    Environment.GetEnvironmentVariable(
                        LoadedOnceEnvironmentVariable,
                        EnvironmentVariableTarget.Process),
                    "1",
                    StringComparison.Ordinal);
            }
            set {
                Environment.SetEnvironmentVariable(
                    LoadedOnceEnvironmentVariable,
                    value ? "1" : null,
                    EnvironmentVariableTarget.Process);
            }
        }
    }
}