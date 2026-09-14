using GTA;
using GTA.Native;
using GTA.UI;
using System;
using Control = GTA.Control;
using Keys = System.Windows.Forms.Keys;

namespace CustomRadioStations.UI.Settings {
    internal enum SettingsInputMode {
        Controller,
        Keyboard,
        Mouse
    }

    internal sealed class SettingsInput {
        private const float MouseActivationThresholdVirtualPixels = 3f;
        private float previousMouseX = -1f;
        private float previousMouseY = -1f;
        private DateTime nextVerticalRepeat;
        private DateTime nextHorizontalRepeat;
        private int pendingVerticalMove;
        private int pendingHorizontalMove;
        private int pendingCategoryMove;
        private int pendingDirectCategory = -1;
        private int pendingSelectionBoundary;
        private bool pendingActivate;
        private bool pendingBack;
        private bool pendingReset;
        private bool pendingToggle;
        private bool pendingKeyboardInput;

        internal SettingsInputMode Mode { get; private set; } = SettingsInputMode.Controller;
        internal float MouseX { get; private set; }
        internal float MouseY { get; private set; }
        internal bool MouseClicked { get; private set; }
        internal bool MousePressed { get; private set; }
        internal bool MouseReleased { get; private set; }
        internal int MouseWheel { get; private set; }

        internal int VerticalMove { get; private set; }
        internal int HorizontalMove { get; private set; }
        internal int CategoryMove { get; private set; }
        internal int DirectCategory { get; private set; } = -1;
        internal int SelectionBoundary { get; private set; }
        internal bool Activate { get; private set; }
        internal bool Back { get; private set; }
        internal bool Reset { get; private set; }
        internal bool ToggleMenu { get; private set; }

        internal void Update() {
            ClearFrameState();
            DateTime now = DateTime.UtcNow;

            ReadMouse();
            ReadController(now);
            ApplyPendingKeyboard();
        }

        internal void HandleKeyDown(Keys key) {
            Mode = SettingsInputMode.Keyboard;
            pendingKeyboardInput = true;
            switch (key) {
            case Keys.Up:
                pendingVerticalMove = -1;
                break;
            case Keys.Down:
                pendingVerticalMove = 1;
                break;
            case Keys.Left:
                pendingHorizontalMove = -1;
                break;
            case Keys.Right:
                pendingHorizontalMove = 1;
                break;
            case Keys.PageUp:
            case Keys.Q:
                pendingCategoryMove = -1;
                break;
            case Keys.PageDown:
            case Keys.E:
                pendingCategoryMove = 1;
                break;
            case Keys.D1:
            case Keys.NumPad1:
                pendingDirectCategory = 0;
                break;
            case Keys.D2:
            case Keys.NumPad2:
                pendingDirectCategory = 1;
                break;
            case Keys.D3:
            case Keys.NumPad3:
                pendingDirectCategory = 2;
                break;
            case Keys.D4:
            case Keys.NumPad4:
                pendingDirectCategory = 3;
                break;
            case Keys.D5:
            case Keys.NumPad5:
                pendingDirectCategory = 4;
                break;
            case Keys.D6:
            case Keys.NumPad6:
                pendingDirectCategory = 5;
                break;
            case Keys.Home:
                pendingSelectionBoundary = -1;
                break;
            case Keys.End:
                pendingSelectionBoundary = 1;
                break;
            case Keys.Enter:
            case Keys.Space:
                pendingActivate = true;
                break;
            case Keys.Back:
            case Keys.Delete:
                pendingReset = true;
                break;
            case Keys.Escape:
                pendingBack = true;
                break;
            }
        }

        internal void MarkToggleKey() {
            Mode = SettingsInputMode.Keyboard;
            pendingKeyboardInput = true;
            pendingToggle = true;
        }

        internal void ResetForOpen(SettingsInputMode mode) {
            Mode = mode;
            previousMouseX = -1f;
            previousMouseY = -1f;
            nextVerticalRepeat = DateTime.MinValue;
            nextHorizontalRepeat = DateTime.MinValue;
            pendingVerticalMove = 0;
            pendingHorizontalMove = 0;
            pendingCategoryMove = 0;
            pendingDirectCategory = -1;
            pendingSelectionBoundary = 0;
            pendingActivate = false;
            pendingBack = false;
            pendingReset = false;
            pendingToggle = false;
            pendingKeyboardInput = false;
            ClearFrameState();
        }

        internal bool TryCaptureControllerControl(Control[] candidates, out Control control) {
            control = default(Control);
            if (candidates == null)
                return false;

            foreach (Control candidate in candidates) {
                if (IsDisabledJustPressed(candidate)) {
                    control = candidate;
                    Mode = SettingsInputMode.Controller;
                    return true;
                }
            }
            return false;
        }


        private void ApplyPendingKeyboard() {
            if (pendingKeyboardInput)
                Mode = SettingsInputMode.Keyboard;

            if (pendingVerticalMove != 0)
                VerticalMove = pendingVerticalMove;
            if (pendingHorizontalMove != 0)
                HorizontalMove = pendingHorizontalMove;
            if (pendingCategoryMove != 0)
                CategoryMove = pendingCategoryMove;
            if (pendingDirectCategory >= 0)
                DirectCategory = pendingDirectCategory;
            if (pendingSelectionBoundary != 0)
                SelectionBoundary = pendingSelectionBoundary;
            if (pendingActivate)
                Activate = true;
            if (pendingBack)
                Back = true;
            if (pendingReset)
                Reset = true;
            if (pendingToggle)
                ToggleMenu = true;

            pendingVerticalMove = 0;
            pendingHorizontalMove = 0;
            pendingCategoryMove = 0;
            pendingDirectCategory = -1;
            pendingSelectionBoundary = 0;
            pendingActivate = false;
            pendingBack = false;
            pendingReset = false;
            pendingToggle = false;
            pendingKeyboardInput = false;
        }

        private void ClearFrameState() {
            VerticalMove = 0;
            HorizontalMove = 0;
            CategoryMove = 0;
            DirectCategory = -1;
            SelectionBoundary = 0;
            Activate = false;
            Back = false;
            Reset = false;
            ToggleMenu = false;
            MouseClicked = false;
            MousePressed = false;
            MouseReleased = false;
            MouseWheel = 0;
        }

        private void ReadMouse() {
            float x = GetDisabledNormal(Control.CursorX);
            float y = GetDisabledNormal(Control.CursorY);
            if (x < 0f || x > 1f || y < 0f || y > 1f)
                return;

            MouseX = x;
            MouseY = y;
            if (previousMouseX >= 0f && previousMouseY >= 0f) {
                float deltaX = Math.Abs(MouseX - previousMouseX) * Screen.ScaledWidth;
                float deltaY = Math.Abs(MouseY - previousMouseY) * SettingsLayout.VirtualHeight;
                if (deltaX > MouseActivationThresholdVirtualPixels ||
                    deltaY > MouseActivationThresholdVirtualPixels) {
                    Mode = SettingsInputMode.Mouse;
                }
            }
            previousMouseX = MouseX;
            previousMouseY = MouseY;

            MousePressed = IsDisabledPressed(Control.CursorAccept);
            MouseReleased = IsDisabledJustReleased(Control.CursorAccept);
            if (IsDisabledJustPressed(Control.CursorAccept)) {
                Mode = SettingsInputMode.Mouse;
                MouseClicked = true;
            }
            if (IsDisabledJustPressed(Control.CursorCancel)) {
                Mode = SettingsInputMode.Mouse;
                pendingBack = true;
            }
            if (IsDisabledJustPressed(Control.CursorScrollUp)) {
                Mode = SettingsInputMode.Mouse;
                MouseWheel = -1;
            } else if (IsDisabledJustPressed(Control.CursorScrollDown)) {
                Mode = SettingsInputMode.Mouse;
                MouseWheel = 1;
            }
        }

        private void ReadController(DateTime now) {
            if (Game.LastInputMethod != InputMethod.GamePad)
                return;

            bool controllerUsed = false;
            if (Repeat(Control.FrontendUp, now, ref nextVerticalRepeat)) {
                VerticalMove = -1;
                controllerUsed = true;
            } else if (Repeat(Control.FrontendDown, now, ref nextVerticalRepeat)) {
                VerticalMove = 1;
                controllerUsed = true;
            }

            if (Repeat(Control.FrontendLeft, now, ref nextHorizontalRepeat)) {
                HorizontalMove = -1;
                controllerUsed = true;
            } else if (Repeat(Control.FrontendRight, now, ref nextHorizontalRepeat)) {
                HorizontalMove = 1;
                controllerUsed = true;
            }

            if (IsDisabledJustPressed(Control.FrontendLb)) {
                CategoryMove = -1;
                controllerUsed = true;
            } else if (IsDisabledJustPressed(Control.FrontendRb)) {
                CategoryMove = 1;
                controllerUsed = true;
            }

            if (IsDisabledJustPressed(Control.FrontendAccept)) {
                Activate = true;
                controllerUsed = true;
            }
            if (IsDisabledJustPressed(Control.FrontendCancel)) {
                Back = true;
                controllerUsed = true;
            }
            if (IsDisabledJustPressed(Control.FrontendX)) {
                Reset = true;
                controllerUsed = true;
            }
            if (CanToggleMenuWhileOpen(Config.GP_OpenSettings) && IsDisabledJustPressed(Config.GP_OpenSettings)) {
                ToggleMenu = true;
                controllerUsed = true;
            }

            if (controllerUsed)
                Mode = SettingsInputMode.Controller;
        }

        private static bool CanToggleMenuWhileOpen(Control control) {
            // The open-settings binding remains configurable, but controls that overlap
            // core menu navigation keep their navigation meaning once the menu is open.
            // B / Circle always remains available to close the menu.
            switch (control) {
            case Control.ScriptSelect:
            case Control.FrontendSelect:
            case Control.FrontendY:
            case Control.FrontendLt:
            case Control.FrontendRt:
            case Control.FrontendLs:
            case Control.FrontendRs:
                return true;
            default:
                return false;
            }
        }

        private static bool Repeat(Control control, DateTime now, ref DateTime nextRepeat) {
            if (IsDisabledJustPressed(control)) {
                nextRepeat = now.AddMilliseconds(Config.SettingsMenuHoldDelayMs);
                return true;
            }
            if (!IsDisabledPressed(control)) {
                nextRepeat = DateTime.MinValue;
                return false;
            }
            if (nextRepeat != DateTime.MinValue && now >= nextRepeat) {
                nextRepeat = now.AddMilliseconds(Config.SettingsMenuRepeatRateMs);
                return true;
            }
            return false;
        }

        private static float GetDisabledNormal(Control control) {
            try { return Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 2, (int)control); }
            catch { return 0f; }
        }

        private static bool IsDisabledPressed(Control control) {
            try { return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 2, (int)control); }
            catch { return false; }
        }

        private static bool IsDisabledJustPressed(Control control) {
            try { return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 2, (int)control); }
            catch { return false; }
        }

        private static bool IsDisabledJustReleased(Control control) {
            try { return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_RELEASED, 2, (int)control); }
            catch { return false; }
        }
    }
}
