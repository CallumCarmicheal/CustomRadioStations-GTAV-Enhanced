using System;

namespace CustomRadioStations {
    internal sealed class HoldRepeatState {
        private readonly TimeSpan initialDelay;
        private readonly TimeSpan repeatInterval;
        private DateTime? nextRepeatAt;

        internal HoldRepeatState(TimeSpan initialDelay, TimeSpan repeatInterval) {
            this.initialDelay = initialDelay;
            this.repeatInterval = repeatInterval;
        }

        internal bool ShouldFire(bool justPressed, bool isPressed, DateTime now) {
            if (!isPressed) {
                Reset();
                return false;
            }

            if (justPressed) {
                nextRepeatAt = now + initialDelay;
                return true;
            }

            if (!nextRepeatAt.HasValue || now < nextRepeatAt.Value)
                return false;

            // Emit at most one step per game frame. If a frame stalls, advance the
            // deadline past "now" instead of applying a burst of queued volume jumps.
            do {
                nextRepeatAt = nextRepeatAt.Value + repeatInterval;
            }
            while (nextRepeatAt.Value <= now);
            return true;
        }

        internal void Reset() {
            nextRepeatAt = null;
        }
    }
}