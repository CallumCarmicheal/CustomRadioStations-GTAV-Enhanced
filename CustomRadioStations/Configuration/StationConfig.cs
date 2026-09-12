using Newtonsoft.Json;
using System.Collections.Generic;

namespace CustomRadioStations
{
    internal sealed class StationConfig
    {
        public StationConfig()
        {
            Tracks = new List<string> { "*" };
            Commercials = new List<string> { "*" };
            Playback = new PlaybackConfig();
            CommercialBreaks = new CommercialBreakConfig();
        }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("icon")]
        public string Icon { get; set; }

        [JsonProperty("tracks")]
        public List<string> Tracks { get; set; }

        [JsonProperty("commercials")]
        public List<string> Commercials { get; set; }

        [JsonProperty("playback")]
        public PlaybackConfig Playback { get; set; }

        [JsonProperty("commercialBreaks")]
        public CommercialBreakConfig CommercialBreaks { get; set; }
    }

    internal sealed class PlaybackConfig
    {
        public PlaybackConfig()
        {
            Mode = "broadcast";
            Shuffle = true;
            Volume = 1.0f;
            Loop = true;
        }

        [JsonProperty("mode")]
        public string Mode { get; set; }

        [JsonProperty("shuffle")]
        public bool Shuffle { get; set; }

        [JsonProperty("volume")]
        public float Volume { get; set; }

        [JsonProperty("loop")]
        public bool Loop { get; set; }
    }

    internal sealed class CommercialBreakConfig
    {
        public CommercialBreakConfig()
        {
            Enabled = true;
            MinTracksBetween = 3;
            MaxTracksBetween = 6;
            MinCommercials = 1;
            MaxCommercials = 2;
        }

        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        [JsonProperty("minTracksBetween")]
        public int MinTracksBetween { get; set; }

        [JsonProperty("maxTracksBetween")]
        public int MaxTracksBetween { get; set; }

        [JsonProperty("minCommercials")]
        public int MinCommercials { get; set; }

        [JsonProperty("maxCommercials")]
        public int MaxCommercials { get; set; }
    }
}
