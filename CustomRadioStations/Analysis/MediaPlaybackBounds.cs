using System;

namespace CustomRadioStations {
    public struct MediaPlaybackBounds {
        public MediaPlaybackBounds(uint startMs, uint endMs) {
            StartMs = startMs;
            EndMs = endMs;
        }

        public uint StartMs { get; }
        public uint EndMs { get; }
        public uint LengthMs { get { return EndMs > StartMs ? EndMs - StartMs : 0u; } }

        public static MediaPlaybackBounds Calculate(uint physicalLengthMs, uint? manualStartMs, uint? manualEndMs, TrackAnalysis analysis) {
            return Calculate(physicalLengthMs, manualStartMs, manualEndMs, analysis, false);
        }

        public static MediaPlaybackBounds Calculate(uint physicalLengthMs, uint? manualStartMs, uint? manualEndMs,
            TrackAnalysis analysis, bool allowAnalysisTrimWithinBounds) {
            uint detectedStart = analysis == null ? 0u : analysis.AudioStartMs;
            uint detectedEnd = analysis == null ? 0u : analysis.AudioEndMs;
            uint start;
            uint end;

            if (allowAnalysisTrimWithinBounds) {
                uint outerStart = manualStartMs ?? 0u;
                uint outerEnd = manualEndMs ?? physicalLengthMs;
                start = analysis == null ? outerStart : Math.Max(outerStart, detectedStart);
                uint analyzedEnd = detectedEnd == 0u ? outerEnd : detectedEnd;
                end = analysis == null ? outerEnd : Math.Min(outerEnd, analyzedEnd);
            } else {
                start = manualStartMs ?? detectedStart;
                end = manualEndMs ?? detectedEnd;
            }

            if (end == 0u)
                end = physicalLengthMs;
            if (physicalLengthMs > 0u) {
                start = Math.Min(start, physicalLengthMs - 1u);
                end = Math.Min(end, physicalLengthMs);
            }

            if (end <= start) {
                start = 0u;
                end = physicalLengthMs;
            }
            return new MediaPlaybackBounds(start, end);
        }
    }
}
