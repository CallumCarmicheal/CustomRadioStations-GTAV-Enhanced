using GTA;
using GTA.Native;

namespace GTAVFunctions {
    /// <summary>
    /// Preserves the original mod's explicit input-group behaviour while targeting SHVDN3.
    /// SHVDN3's Game control convenience methods use input group 0; this mod historically
    /// uses group 2 for its wheel/radio controls.
    /// </summary>
    internal static class ControlInput {
        internal const int PlayerInputGroup = 0;
        internal const int WheelInputGroup = 2;

        internal static bool IsPressed(Control control) {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, WheelInputGroup, (int)control);
        }

        internal static bool IsEnabledPressed(Control control) {
            return Function.Call<bool>(Hash.IS_CONTROL_PRESSED, WheelInputGroup, (int)control);
        }

        internal static bool IsJustPressed(Control control) {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, WheelInputGroup, (int)control);
        }

        internal static bool IsJustReleased(Control control) {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_RELEASED, WheelInputGroup, (int)control);
        }

        internal static float GetValueNormalized(Control control) {
            return Function.Call<float>(Hash.GET_CONTROL_NORMAL, WheelInputGroup, (int)control);
        }

        internal static void DisableThisFrame(Control control) {
            Function.Call(Hash.DISABLE_CONTROL_ACTION, WheelInputGroup, (int)control, true);
        }

        internal static void EnableThisFrame(Control control) {
            Function.Call(Hash.ENABLE_CONTROL_ACTION, WheelInputGroup, (int)control, true);
        }

        internal static void EnablePlayerThisFrame(Control control) {
            Function.Call(Hash.ENABLE_CONTROL_ACTION, PlayerInputGroup, (int)control, true);
        }

        internal static void DisableAllThisFrame() {
            Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, WheelInputGroup);
        }

        internal static void DisableAllPlayerThisFrame() {
            Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, PlayerInputGroup);
        }
    }
}