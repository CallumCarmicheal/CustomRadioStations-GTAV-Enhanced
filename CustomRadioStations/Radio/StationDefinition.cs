using System.Collections.Generic;

namespace CustomRadioStations {
    public sealed class StationDefinition {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string IconPath { get; set; }
        public string ConfigPath { get; set; }
        public bool IsLegacyIni { get; set; }
        public IReadOnlyList<ResolvedMediaSource> Tracks { get; set; }
        public IReadOnlyList<ResolvedMediaSource> Commercials { get; set; }
        public PlaybackConfig Playback { get; set; }
        public CommercialBreakConfig CommercialBreaks { get; set; }
        public StationAnalysis Analysis { get; set; }
    }
}
