using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Globalization;

namespace CustomRadioStations {
    public sealed class StationConfig {
        public StationConfig() {
            Tracks = new List<MediaSourceConfig> { new MediaSourceConfig("*") };
            Commercials = new List<MediaSourceConfig> { new MediaSourceConfig("*") };
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
        public List<MediaSourceConfig> Tracks { get; set; }

        [JsonProperty("commercials")]
        public List<MediaSourceConfig> Commercials { get; set; }

        [JsonProperty("playback")]
        public PlaybackConfig Playback { get; set; }

        [JsonProperty("commercialBreaks")]
        public CommercialBreakConfig CommercialBreaks { get; set; }
    }

    [JsonConverter(typeof(MediaSourceConfigConverter))]
    public sealed class MediaSourceConfig {
        public MediaSourceConfig() { }

        public MediaSourceConfig(string file) {
            File = file;
        }

        [JsonProperty("file")]
        public string File { get; set; }

        /// <summary>
        /// Optional CUE sheet. Its FILE directives define the physical audio file(s), resolved
        /// relative to the CUE sheet when necessary. A media entry cannot specify both file and cue.
        /// </summary>
        [JsonProperty("cue")]
        public string Cue { get; set; }

        /// <summary>
        /// "split" expands each CUE TRACK into an independent radio item.
        /// "continuous" keeps each physical file as one item and exposes CUE TRACKs as sub-tracks.
        /// </summary>
        [JsonProperty("cueMode")]
        public string CueMode { get; set; }

        [JsonProperty("start")]
        public string Start { get; set; }

        [JsonProperty("end")]
        public string End { get; set; }

        [JsonProperty("artist")]
        public string Artist { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonIgnore]
        public uint? StartMs { get; internal set; }

        [JsonIgnore]
        public uint? EndMs { get; internal set; }

        public override string ToString() {
            return !string.IsNullOrWhiteSpace(Cue) ? Cue : File ?? string.Empty;
        }
    }

    public sealed class MediaSourceConfigConverter : JsonConverter {
        public override bool CanConvert(Type objectType) {
            return objectType == typeof(MediaSourceConfig);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
            JToken token = JToken.Load(reader);
            if (token.Type == JTokenType.String)
                return new MediaSourceConfig(token.Value<string>());
            if (token.Type == JTokenType.Null)
                return null;
            if (token.Type != JTokenType.Object)
                throw new JsonSerializationException("Media source entries must be a path string or an object.");

            var obj = (JObject)token;
            return new MediaSourceConfig {
                File = obj.Value<string>("file"),
                Cue = obj.Value<string>("cue"),
                CueMode = obj.Value<string>("cueMode"),
                Start = obj.Value<string>("start"),
                End = obj.Value<string>("end"),
                Artist = obj.Value<string>("artist"),
                Title = obj.Value<string>("title")
            };
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
            var source = value as MediaSourceConfig;
            if (source == null) {
                writer.WriteNull();
                return;
            }

            if (source.Cue == null && source.CueMode == null && source.Start == null && source.End == null &&
                source.Artist == null && source.Title == null) {
                writer.WriteValue(source.File);
                return;
            }

            writer.WriteStartObject();
            WriteProperty(writer, "file", source.File);
            WriteProperty(writer, "cue", source.Cue);
            WriteProperty(writer, "cueMode", source.CueMode);
            WriteProperty(writer, "start", source.Start);
            WriteProperty(writer, "end", source.End);
            WriteProperty(writer, "artist", source.Artist);
            WriteProperty(writer, "title", source.Title);
            writer.WriteEndObject();
        }

        private static void WriteProperty(JsonWriter writer, string name, string value) {
            if (value == null)
                return;
            writer.WritePropertyName(name);
            writer.WriteValue(value);
        }
    }

    public static class MediaTimeParser {
        public static bool TryParse(string value, out uint milliseconds) {
            milliseconds = 0;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string[] parts = value.Trim().Split(':');
            if (parts.Length != 2 && parts.Length != 3)
                return false;

            int hours = 0;
            int minutes;
            double seconds;
            if (parts.Length == 3) {
                if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out hours) || hours < 0)
                    return false;
                if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minutes) || minutes < 0 || minutes > 59)
                    return false;
            } else {
                if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out minutes) || minutes < 0)
                    return false;
            }

            string secondsPart = parts[parts.Length - 1];
            if (!double.TryParse(secondsPart, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out seconds) ||
                seconds < 0d || seconds >= 60d)
                return false;

            double totalMilliseconds = (((hours * 60d) + minutes) * 60d + seconds) * 1000d;
            if (totalMilliseconds < 0d || totalMilliseconds > uint.MaxValue)
                return false;
            milliseconds = (uint)Math.Round(totalMilliseconds, MidpointRounding.AwayFromZero);
            return true;
        }
    }

    public sealed class PlaybackConfig {
        public PlaybackConfig() {
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

    public sealed class CommercialBreakConfig {
        public CommercialBreakConfig() {
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
