using System;
using GTA;

namespace CustomRadioStations
{
    /// <summary>
    /// Session state used to detect a SHVDN script reload in the current GTA process.
    ///
    /// The original mod stored this flag in a GTA decorator and pattern-scanned the
    /// game executable to temporarily unlock decorator registration. That private
    /// memory pattern is edition/build-specific and is not safe on GTA V Enhanced.
    /// A process-scoped environment variable survives SHVDN AppDomain/script reloads
    /// but disappears when GTA exits, which gives us the same behaviour without
    /// touching game memory.
    /// </summary>
    internal static class Decorators
    {
        private const string LoadedOnceEnvironmentVariable = "CRSSH_HAS_LOADED_ONCE";

        // Kept for source compatibility with the old call sites. No GTA decorator
        // is created or accessed by the Enhanced port.
        internal static Entity DEntity;

        internal static void Init(Entity entity)
        {
            DEntity = entity;
            ScriptHasLoadedOnce = true;
        }

        internal static bool ScriptHasLoadedOnce
        {
            get
            {
                return string.Equals(
                    Environment.GetEnvironmentVariable(
                        LoadedOnceEnvironmentVariable,
                        EnvironmentVariableTarget.Process),
                    "1",
                    StringComparison.Ordinal);
            }
            set
            {
                Environment.SetEnvironmentVariable(
                    LoadedOnceEnvironmentVariable,
                    value ? "1" : null,
                    EnvironmentVariableTarget.Process);
            }
        }
    }
}
