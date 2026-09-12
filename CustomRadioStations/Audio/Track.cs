using Newtonsoft.Json;

using System;
using System.Collections.Generic;

namespace CustomRadioStations {
    class Track {
        [JsonProperty("startTimeMs")]
        public uint StartTime {
            get; set;
        }

        [JsonProperty("artist")]
        public string Artist {
            get; set;
        }

        [JsonProperty("title")]
        public string Title {
            get; set;
        }

        public Track() {
        }

        public Track(uint startTime, string artist, string title) {
            StartTime = startTime;
            Artist = artist;
            Title = title;
        }

        public override string ToString() {
            return "Artist: " + Artist + "\n" + "Title: " + Title + "\n" + "Ms: " + StartTime;
        }
    }

    internal sealed class TracklistConfig {
        public TracklistConfig() {
            Tracks = new List<Track>();
        }

        [JsonProperty("tracks")]
        public List<Track> Tracks {
            get; set;
        }
    }
}