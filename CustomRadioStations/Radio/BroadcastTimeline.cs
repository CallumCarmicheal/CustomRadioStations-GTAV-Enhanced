using System;
using System.Collections.Generic;

namespace CustomRadioStations {
    internal struct BroadcastPosition {
        internal BroadcastPosition(int index, uint position, bool finished) {
            Index = index;
            Position = position;
            Finished = finished;
        }

        internal int Index {
            get;
        }
        internal uint Position {
            get;
        }
        internal bool Finished {
            get;
        }
    }

    internal static class BroadcastTimeline {
        internal static BroadcastPosition Advance(
            IReadOnlyList<uint> lengths,
            int startIndex,
            uint startPosition,
            ulong elapsedMilliseconds,
            bool loop) {

            if (lengths == null || lengths.Count == 0)
                return new BroadcastPosition(-1, 0u, true);

            int index = Math.Max(0, Math.Min(startIndex, lengths.Count - 1));
            uint position = Math.Min(startPosition, lengths[index] > 0 ? lengths[index] - 1 : 0u);

            if (loop) {
                ulong totalLength = 0;
                for (int i = 0; i < lengths.Count; i++)
                    totalLength += lengths[i];
                if (totalLength > 0)
                    elapsedMilliseconds %= totalLength;
            }

            int zeroLengthVisits = 0;
            while (true) {
                uint length = lengths[index];
                if (length == 0) {
                    zeroLengthVisits++;
                    if (zeroLengthVisits >= lengths.Count)
                        return new BroadcastPosition(-1, 0u, true);
                } else {
                    zeroLengthVisits = 0;
                    ulong remaining = length > position ? (ulong)(length - position) : 0UL;
                    if (elapsedMilliseconds < remaining)
                        return new BroadcastPosition(index, position + (uint)elapsedMilliseconds, false);
                    elapsedMilliseconds -= remaining;
                }

                if (index >= lengths.Count - 1) {
                    if (!loop)
                        return new BroadcastPosition(-1, 0u, true);
                    index = 0;
                } else {
                    index++;
                }
                position = 0u;
            }
        }
    }
}
