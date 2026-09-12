namespace CustomRadioStations {
    /// <summary>
    /// Opening must be authorized by GTA's own enabled input and radio HUD. Once the
    /// custom wheel owns the input, it may continue reading the disabled control until
    /// release because the wheel itself disables gameplay controls while visible.
    /// </summary>
    internal static class RadioWheelAvailability {
        internal static bool CanShow(bool radioInputHeld, bool enabledRadioInputHeld,
            bool playerCanControl, bool nativeRadioHudVisible, bool customWheelVisible) {
            if (!radioInputHeld || !playerCanControl)
                return false;
            if (customWheelVisible)
                return true;
            return enabledRadioInputHeld && nativeRadioHudVisible;
        }
    }
}