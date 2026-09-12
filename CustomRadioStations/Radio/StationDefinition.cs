using System.Collections.Generic;

namespace CustomRadioStations
{
    internal sealed class StationDefinition
    {
        internal string Id { get; set; }
        internal string Name { get; set; }
        internal string Description { get; set; }
        internal string IconPath { get; set; }
        internal string ConfigPath { get; set; }
        internal bool IsLegacyIni { get; set; }
        internal IReadOnlyList<string> Tracks { get; set; }
        internal IReadOnlyList<string> Commercials { get; set; }
        internal PlaybackConfig Playback { get; set; }
        internal CommercialBreakConfig CommercialBreaks { get; set; }
    }
}
