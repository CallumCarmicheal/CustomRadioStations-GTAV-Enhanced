using GTA;
using GTA.Native;
using GTA.UI;

using SelectorWheel;

using GTAVFunctions;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

using Control = GTA.Control;
using Keys = System.Windows.Forms.Keys;

using GtaFont = GTA.UI.Font;
using UIScreen = GTA.UI.Screen;

namespace CustomRadioStations.UI.Settings {
    internal sealed class SettingsMenu {
        private enum BindingCaptureKind {
            None,
            KeyboardOpenSettings,
            ControllerOpenSettings
        }

        // Keep the in-menu capture list to sensible digital buttons. Advanced users can
        // still specify any GTA Control value directly in settings.json if they need one.
        private static readonly Control[] ControllerBindingCandidates = {
            Control.ScriptSelect,
            Control.FrontendSelect,
            Control.FrontendAccept,
            Control.FrontendX,
            Control.FrontendY,
            Control.FrontendLb,
            Control.FrontendRb,
            Control.FrontendLt,
            Control.FrontendRt,
            Control.FrontendLs,
            Control.FrontendRs,
            Control.ScriptRUp,
            Control.ScriptRDown,
            Control.ScriptRLeft,
            Control.ScriptRRight,
            Control.ScriptLB,
            Control.ScriptRB,
            Control.ScriptLT,
            Control.ScriptRT,
            Control.ScriptLS,
            Control.ScriptRS,
            Control.ScriptPadUp,
            Control.ScriptPadDown,
            Control.ScriptPadLeft,
            Control.ScriptPadRight,
            Control.VehicleDuck,
            Control.VehicleHandbrake
        };
        private readonly SettingsInput input = new SettingsInput();
        private readonly List<SettingsPage> pages = new List<SettingsPage>();
        private readonly Action reloadStations;
        private readonly Action reloadSettings;
        private readonly Action<bool> closed;
        private int selectedPage;
        private bool dirty;
        private DateTime saveAt;
        private DateTime savedIndicatorUntil;
        private bool saveFailed;
        private string statusMessage = string.Empty;
        private DateTime statusUntil;
        private bool statusIsError;
        private DateTime ignoreToggleUntil;
        private SliderSettingsItem draggedSlider;
        private bool draggingScrollbar;
        private float scrollbarGrabOffset;
        private int scrollbarSelectionOffset;
        private BindingCaptureKind bindingCapture;
        private string bindingCaptureTitle = string.Empty;
        private bool resetConfirmation;
        private bool resetConfirmationYes;

        internal SettingsMenu(Action reloadStations, Action reloadSettings, Action<bool> closed) {
            this.reloadStations = reloadStations;
            this.reloadSettings = reloadSettings;
            this.closed = closed;
            BuildPages();
        }

        internal bool IsOpen { get; private set; }
        internal bool ReturnToWheel { get; private set; }

        internal void Open(bool returnToWheel, SettingsInputMode initialMode) {
            if (IsOpen)
                return;
            IsOpen = true;
            ReturnToWheel = returnToWheel;
            input.ResetForOpen(initialMode);
            saveFailed = false;
            savedIndicatorUntil = DateTime.MinValue;
            if (dirty)
                saveAt = DateTime.UtcNow.AddMilliseconds(350);
            resetConfirmation = false;
            resetConfirmationYes = false;
            ignoreToggleUntil = DateTime.UtcNow.AddMilliseconds(250);
            selectedPage = Config.RememberSettingsPage ? FindPageIndex(Config.LastSettingsPage) : 0;
            ClampSelection();
        }

        internal void Close() {
            if (!IsOpen)
                return;
            FlushSave();
            IsOpen = false;
            bool returnToWheel = ReturnToWheel;
            ReturnToWheel = false;
            draggedSlider = null;
            draggingScrollbar = false;
            bindingCapture = BindingCaptureKind.None;
            bindingCaptureTitle = string.Empty;
            resetConfirmation = false;
            resetConfirmationYes = false;
            closed?.Invoke(returnToWheel);
        }

        internal void HandleKeyDown(Keys key) {
            if (!IsOpen)
                return;

            if (bindingCapture == BindingCaptureKind.KeyboardOpenSettings) {
                if (key == Keys.Escape) {
                    CancelBindingCapture();
                    return;
                }
                if (key == Keys.None || IsModifierOnlyKey(key))
                    return;

                Config.KB_OpenSettings = key;
                CompleteBindingCapture("Keyboard settings key: " + key);
                return;
            }

            if (bindingCapture == BindingCaptureKind.ControllerOpenSettings) {
                if (key == Keys.Escape)
                    CancelBindingCapture();
                return;
            }

            if (resetConfirmation) {
                input.HandleKeyDown(key);
                return;
            }

            if (key == Config.KB_OpenSettings && !IsReservedMenuKey(key))
                input.MarkToggleKey();
            else
                input.HandleKeyDown(key);
        }

        internal void MarkToggleKey() {
            if (IsOpen)
                input.MarkToggleKey();
        }

        internal void Process() {
            if (!IsOpen)
                return;

            SuppressGameControls();
            input.Update();
            if (input.Mode == SettingsInputMode.Mouse)
                Hud.ShowCursorThisFrame();
            else
                ClampSelection();

            SettingsLayout layout = SettingsLayout.Create();
            if (bindingCapture != BindingCaptureKind.None) {
                HandleBindingCapture(layout);
                UpdateSave();
                Draw(layout);
                DrawBindingCapture(layout, SettingsTheme.Accent);
                return;
            }

            if (resetConfirmation) {
                HandleResetConfirmation(layout);
                UpdateSave();
                Draw(layout);
                DrawResetConfirmation(layout, SettingsTheme.Accent);
                return;
            }

            HandlePointer(layout);
            if (!IsOpen)
                return;
            HandleSemanticInput();
            if (!IsOpen)
                return;
            UpdateSave();
            Draw(layout);
        }

        private void BuildPages() {
            pages.Clear();

            pages.Add(new SettingsPage("Playback", "Playback behavior and interruption rules.",
                new SectionSettingsItem("Game State", "When CRS is allowed to keep playing outside normal gameplay."),
                Toggle("Play in Pause Menu",
                    "Continue custom radio while GTA's pause menu is open. Off preserves GTA-like pause behaviour.",
                    () => Config.PlayInPauseMenu,
                    value => { Config.PlayInPauseMenu = value; AudioPauseCoordinator.RefreshSettings(); }, ApplicationSettingsDefaults.PlayInPauseMenu),
                Toggle("Play While Game Is in Background",
                    "Continue custom radio when GTA loses focus or is minimized. Off prevents unexpected audio while alt-tabbed.",
                    () => Config.PlayWhileInBackground,
                    value => { Config.PlayWhileInBackground = value; AudioPauseCoordinator.RefreshSettings(); }, ApplicationSettingsDefaults.PlayWhileInBackground),
                Toggle("Allow Skipping Radio Tracks",
                    "Enable skipping tracks in the radio. When turned off the prompt is hidden and the action is disabled.",
                    () => Config.AllowSkippingTracks,
                    value => Config.AllowSkippingTracks = value, ApplicationSettingsDefaults.AllowSkippingTracks),
                new SectionSettingsItem("Radio Wheel", "Presentation behavior while the custom station wheel is open."),
                Toggle("Wheel Slow Motion",
                    "Slow the game while the custom radio wheel is open.",
                    () => Config.EnableWheelSlowmotion,
                    value => Config.EnableWheelSlowmotion = value, ApplicationSettingsDefaults.EnableWheelSlowMotion)));

            pages.Add(new SettingsPage("Audio", "Volume and audio presentation for custom stations.",
                new SectionSettingsItem("Volume", "Overall custom-radio output level."),
                Slider("Custom Radio Volume",
                    "Master volume multiplier for Custom Radio Stations.",
                    () => SoundFile.SoundEngine.SoundVolume,
                    value => SoundFile.SoundEngine.SoundVolume = value,
                    0f, 1f, 0.05f, ApplicationSettingsDefaults.MasterVolume,
                    value => Math.Round(value * 100f).ToString(CultureInfo.InvariantCulture) + "%")));

            pages.Add(new SettingsPage("Radio", "Wheel presentation, metadata and text rendering.",
                new SectionSettingsItem("Wheel", "How the custom station wheel behaves and presents station information."),
                Toggle("Custom Wheel as Default",
                    "Prefer the custom wheel when opening the radio selector. This does not force a custom station to play.",
                    () => Config.CustomWheelAsDefault,
                    value => Config.CustomWheelAsDefault = value, ApplicationSettingsDefaults.CustomWheelAsDefault),
                Toggle("Show Help Text",
                    "Show CRS control hints while the custom radio wheel is open.",
                    () => Config.DisplayHelpText,
                    value => Config.DisplayHelpText = value, ApplicationSettingsDefaults.DisplayHelpText),
                Slider("Station Activation Delay",
                    "Delay before a highlighted station actually starts. Metadata can update immediately while rapidly moving around the wheel.",
                    () => Config.WheelActionDelay,
                    value => Config.WheelActionDelay = (int)Math.Round(value),
                    0f, 1200f, 50f, ApplicationSettingsDefaults.WheelActionDelayMs,
                    value => Math.Round(value).ToString(CultureInfo.InvariantCulture) + " ms"),
                new SectionSettingsItem("Text", "Text rendering used for track and station metadata."),
                new ChoiceSettingsItem("Unicode Text",
                    "Auto keeps GTA's native font where possible and uses the bitmap fallback only for unsupported text.",
                    new[] { "AUTO", "NATIVE ONLY", "BITMAP" },
                    () => UnicodeModeToIndex(Config.UnicodeMode),
                    index => {
                        Config.UnicodeMode = IndexToUnicodeMode(index);
                        UnicodeTextRenderer.NotifyConfigurationChanged();
                    }, UnicodeModeToIndex(ApplicationSettingsDefaults.UnicodeTextMode))));

            pages.Add(new SettingsPage("Controls", "Input sensitivity and settings-menu shortcuts.",
                new SectionSettingsItem("Radio Wheel", "Analog selection tuning for the custom radio wheel."),
                Slider("Radio Wheel Deadzone",
                    "Analog-stick deadzone used by the custom radio wheel.",
                    () => Config.GP_RadialDeadzone,
                    value => Config.GP_RadialDeadzone = value,
                    0f, 0.95f, 0.05f, ApplicationSettingsDefaults.RadialDeadzone,
                    value => value.ToString("0.00", CultureInfo.InvariantCulture)),
                Slider("Selection Hysteresis",
                    "Extra angular margin before the wheel changes station near a segment boundary.",
                    () => Config.GP_RadialHysteresisDegrees,
                    value => Config.GP_RadialHysteresisDegrees = value,
                    0f, 30f, 1f, ApplicationSettingsDefaults.RadialHysteresisDegrees,
                    value => Math.Round(value).ToString(CultureInfo.InvariantCulture) + " deg"),
                new SectionSettingsItem("Settings Menu", "Navigation timing while using a controller."),
                Slider("Menu Hold Delay",
                    "How long a controller direction must be held before menu navigation begins repeating.",
                    () => Config.SettingsMenuHoldDelayMs,
                    value => Config.SettingsMenuHoldDelayMs = (int)Math.Round(value),
                    100f, 1000f, 50f, ApplicationSettingsDefaults.MenuHoldDelayMs,
                    value => Math.Round(value).ToString(CultureInfo.InvariantCulture) + " ms"),
                Slider("Menu Repeat Rate",
                    "Delay between repeated controller navigation steps after the initial hold delay.",
                    () => Config.SettingsMenuRepeatRateMs,
                    value => Config.SettingsMenuRepeatRateMs = (int)Math.Round(value),
                    50f, 500f, 25f, ApplicationSettingsDefaults.MenuRepeatRateMs,
                    value => Math.Round(value).ToString(CultureInfo.InvariantCulture) + " ms"),
                new SectionSettingsItem("Open Settings", "Shortcuts used to open this menu."),
                new BindingSettingsItem("Open Settings (Keyboard)",
                    "Select this item, then press a keyboard key. Escape cancels capture.",
                    () => Config.KB_OpenSettings.ToString(),
                    () => BeginBindingCapture(BindingCaptureKind.KeyboardOpenSettings, "Open Settings (Keyboard)"),
                    () => Config.KB_OpenSettings = ApplicationSettingsDefaults.KeyboardOpenSettings,
                    () => Config.KB_OpenSettings == ApplicationSettingsDefaults.KeyboardOpenSettings, ApplicationSettingsDefaults.KeyboardOpenSettings.ToString()),
                new BindingSettingsItem("Open Settings (Controller)",
                    "Select this item, then press a controller button. B / Circle cancels. The default is View / Back / Select.",
                    () => ControllerDisplayName(Config.GP_OpenSettings),
                    () => BeginBindingCapture(BindingCaptureKind.ControllerOpenSettings, "Open Settings (Controller)"),
                    () => Config.GP_OpenSettings = ApplicationSettingsDefaults.GamepadOpenSettings,
                    () => Config.GP_OpenSettings == ApplicationSettingsDefaults.GamepadOpenSettings, ControllerDisplayName(ApplicationSettingsDefaults.GamepadOpenSettings))));

            pages.Add(new SettingsPage("Quality of Life", "Convenience options that leave GTA station selection alone.",
                new SectionSettingsItem("Menu", "Small conveniences that make CRS easier to use."),
                Toggle("Remember Settings Page",
                    "Reopen CRS Settings on the page you used last. Turn this off to always open on Playback.",
                    () => Config.RememberSettingsPage,
                    value => {
                        Config.RememberSettingsPage = value;
                        if (value)
                            Config.LastSettingsPage = CurrentPage.Title;
                    }, ApplicationSettingsDefaults.RememberSettingsPage),
                new SectionSettingsItem("Built-in Behavior", "Core CRS improvements that are always active."),
                new InfoSettingsItem("Instant Preview Metadata", "CRS projects the correct currently-playing track immediately when highlighting an inactive broadcast station.", () => "BUILT-IN"),
                new InfoSettingsItem("Analyzed Track Bounds", "Audio analysis and manual start/end bounds are applied to the logical playback timeline.", () => "BUILT-IN")));

            pages.Add(new SettingsPage("Advanced", "Reload and maintenance tools for CRS configuration.",
                new SectionSettingsItem("Maintenance", "Reload CRS data after editing configuration files outside the game."),
                new ActionSettingsItem("Reload Stations", "Reload station definitions and wheel configuration from disk.", () => RunAction("Stations reloaded.", reloadStations)),
                new ActionSettingsItem("Reload Settings", "Reload settings.json from disk. Menu changes still waiting for auto-save are discarded so external edits are not overwritten.", ReloadSettingsFromDisk),
                new SectionSettingsItem("Defaults", "Restore the global CRS settings file to its built-in defaults."),
                new ActionSettingsItem("Reset Settings to Defaults", "Restore all global CRS settings to their built-in defaults. Station definitions and music files are not changed.", BeginResetConfirmation, true)));
        }

        private ToggleSettingsItem Toggle(string title, string description, Func<bool> getter, Action<bool> setter, bool defaultValue) {
            return new ToggleSettingsItem(title, description, getter, value => { setter(value); MarkDirty(); }, defaultValue);
        }

        private SliderSettingsItem Slider(string title, string description, Func<float> getter, Action<float> setter,
            float minimum, float maximum, float step, float defaultValue, Func<float, string> formatter) {
            return new SliderSettingsItem(title, description, getter, value => { setter(value); MarkDirty(); },
                minimum, maximum, step, defaultValue, formatter);
        }

        private void HandleSemanticInput() {
            if ((input.ToggleMenu && DateTime.UtcNow >= ignoreToggleUntil) || input.Back) {
                Close();
                return;
            }

            if (input.DirectCategory >= 0 && input.DirectCategory < pages.Count)
                SelectPage(input.DirectCategory);
            else if (input.CategoryMove != 0)
                ChangePage(input.CategoryMove);
            if (input.SelectionBoundary != 0)
                MoveSelectionToBoundary(input.SelectionBoundary);
            else if (input.VerticalMove != 0)
                MoveSelection(input.VerticalMove);

            SettingsItem item = CurrentPage.SelectedItem;
            if (item == null)
                return;

            if (input.HorizontalMove != 0 && item.CanAdjust) {
                item.Adjust(input.HorizontalMove);
                MarkDirty();
            }
            if (input.Activate)
                ActivateItem(item);
            if (input.Reset && item.CanReset && !item.IsDefault) {
                item.Reset();
                MarkDirty();
                ShowStatus("Restored default: " + item.Title);
            }
        }

        private void HandlePointer(SettingsLayout layout) {
            if (input.Mode != SettingsInputMode.Mouse) {
                draggedSlider = null;
                draggingScrollbar = false;
                return;
            }

            if (input.MouseReleased) {
                draggedSlider = null;
                draggingScrollbar = false;
            }

            float mouseVirtualX = input.MouseX * UIScreen.ScaledWidth;
            float mouseVirtualY = input.MouseY * SettingsLayout.VirtualHeight;

            // Keep pointer capture while dragging even if the cursor leaves the panel.
            // Both slider and scrollbar setters clamp their normalized values, so this
            // gives normal desktop-style drag-to-the-edge behavior instead of freezing.
            if (draggedSlider != null && input.MousePressed) {
                SetSliderFromPointer(draggedSlider, layout, mouseVirtualX);
                return;
            }
            if (draggingScrollbar && input.MousePressed) {
                SettingsPage dragPage = CurrentPage;
                if (dragPage.Items.Count > SettingsLayout.VisibleRows) {
                    float trackLeft;
                    float trackTop;
                    float trackWidth;
                    float trackHeight;
                    float thumbTop;
                    float thumbHeight;
                    GetScrollbarGeometry(dragPage, layout, out trackLeft, out trackTop, out trackWidth,
                        out trackHeight, out thumbTop, out thumbHeight);
                    SetScrollbarFromPointer(dragPage, trackTop, trackHeight, thumbHeight, mouseVirtualY);
                }
                return;
            }

            bool insideMenu = mouseVirtualX >= layout.Left && mouseVirtualX <= layout.Left + layout.Width &&
                mouseVirtualY >= layout.Top && mouseVirtualY <= layout.Top + layout.Height;
            if (!insideMenu)
                return;

            float closeLeft;
            float closeTop;
            float closeSize;
            GetCloseButtonBounds(layout, out closeLeft, out closeTop, out closeSize);
            if (input.MouseClicked && mouseVirtualX >= closeLeft && mouseVirtualX <= closeLeft + closeSize &&
                mouseVirtualY >= closeTop && mouseVirtualY <= closeTop + closeSize) {
                Close();
                return;
            }

            SettingsItem selectedItem = CurrentPage.SelectedItem;
            if (input.MouseClicked && selectedItem != null && selectedItem.CanReset && !selectedItem.IsDefault) {
                float resetLeft;
                float resetTop;
                float resetWidth;
                float resetHeight;
                GetFooterResetBounds(layout, out resetLeft, out resetTop, out resetWidth, out resetHeight);
                if (mouseVirtualX >= resetLeft && mouseVirtualX <= resetLeft + resetWidth &&
                    mouseVirtualY >= resetTop && mouseVirtualY <= resetTop + resetHeight) {
                    selectedItem.Reset();
                    MarkDirty();
                    ShowStatus("Restored default: " + selectedItem.Title);
                    return;
                }
            }

            if (mouseVirtualY >= layout.CategoryTop && mouseVirtualY < layout.FooterTop &&
                mouseVirtualX < layout.ContentLeft) {
                int category = (int)((mouseVirtualY - layout.CategoryTop) / SettingsLayout.RowHeight);
                if (category >= 0 && category < pages.Count && input.MouseClicked) {
                    SelectPage(category);
                    draggedSlider = null;
                    draggingScrollbar = false;
                }
                return;
            }

            SettingsPage page = CurrentPage;

            // Mouse-wheel scrolling should work anywhere over the settings pane rather than
            // only when the pointer happens to be directly over a row.
            if (mouseVirtualX >= layout.ContentLeft && mouseVirtualY >= layout.BodyTop &&
                mouseVirtualY < layout.FooterTop && input.MouseWheel != 0) {
                draggedSlider = null;
                draggingScrollbar = false;
                MoveSelection(input.MouseWheel);
                return;
            }

            if (HandleScrollbarPointer(page, layout, mouseVirtualX, mouseVirtualY))
                return;

            if (mouseVirtualX < layout.ContentLeft || mouseVirtualY < layout.ItemsTop ||
                mouseVirtualY >= layout.ItemsTop + layout.ItemsHeight)
                return;

            int visibleIndex = (int)((mouseVirtualY - layout.ItemsTop) / SettingsLayout.RowHeight);
            int itemIndex = page.ScrollOffset + visibleIndex;
            if (itemIndex >= 0 && itemIndex < page.Items.Count)
                page.SelectedIndex = itemIndex;

            if (itemIndex < 0 || itemIndex >= page.Items.Count)
                return;

            SettingsItem item = page.Items[itemIndex];
            SliderSettingsItem slider = item as SliderSettingsItem;
            if (slider != null && input.MouseClicked) {
                float sliderStart;
                float sliderEnd;
                GetSliderBounds(layout, out sliderStart, out sliderEnd);
                if (mouseVirtualX >= sliderStart - 10f && mouseVirtualX <= sliderEnd + 10f) {
                    draggedSlider = slider;
                    SetSliderFromPointer(slider, layout, mouseVirtualX);
                    return;
                }
            }

            ChoiceSettingsItem choice = item as ChoiceSettingsItem;
            if (choice != null && input.MouseClicked && choice.CanAdjust) {
                float choiceLeft;
                float choiceRight;
                GetChoiceBounds(layout, out choiceLeft, out choiceRight);
                if (mouseVirtualX >= choiceLeft && mouseVirtualX <= choiceRight) {
                    float midpoint = (choiceLeft + choiceRight) / 2f;
                    choice.Adjust(mouseVirtualX < midpoint ? -1 : 1);
                    MarkDirty();
                    return;
                }
            }

            if (!input.MouseClicked)
                return;

            ActivateItem(item);
        }

        private bool HandleScrollbarPointer(SettingsPage page, SettingsLayout layout, float mouseX, float mouseY) {
            if (page.Items.Count <= SettingsLayout.VisibleRows) {
                draggingScrollbar = false;
                return false;
            }

            float trackLeft;
            float trackTop;
            float trackWidth;
            float trackHeight;
            float thumbTop;
            float thumbHeight;
            GetScrollbarGeometry(page, layout, out trackLeft, out trackTop, out trackWidth, out trackHeight,
                out thumbTop, out thumbHeight);

            bool overTrack = mouseX >= trackLeft - 7f && mouseX <= trackLeft + trackWidth + 7f &&
                mouseY >= trackTop && mouseY <= trackTop + trackHeight;

            if (input.MouseClicked && overTrack) {
                draggedSlider = null;
                draggingScrollbar = true;
                scrollbarSelectionOffset = Math.Max(0, Math.Min(SettingsLayout.VisibleRows - 1,
                    page.SelectedIndex - page.ScrollOffset));
                if (mouseY >= thumbTop && mouseY <= thumbTop + thumbHeight)
                    scrollbarGrabOffset = mouseY - thumbTop;
                else
                    scrollbarGrabOffset = thumbHeight / 2f;
                SetScrollbarFromPointer(page, trackTop, trackHeight, thumbHeight, mouseY);
                return true;
            }

            if (draggingScrollbar && input.MousePressed) {
                SetScrollbarFromPointer(page, trackTop, trackHeight, thumbHeight, mouseY);
                return true;
            }

            return overTrack;
        }

        private void SetScrollbarFromPointer(SettingsPage page, float trackTop, float trackHeight,
            float thumbHeight, float mouseY) {
            float travel = Math.Max(1f, trackHeight - thumbHeight);
            float thumbTop = mouseY - scrollbarGrabOffset;
            float normalized = Math.Max(0f, Math.Min(1f, (thumbTop - trackTop) / travel));
            int maxOffset = Math.Max(0, page.Items.Count - SettingsLayout.VisibleRows);
            page.ScrollOffset = (int)Math.Round(normalized * maxOffset);
            page.SelectedIndex = Math.Max(page.ScrollOffset, Math.Min(page.Items.Count - 1,
                page.ScrollOffset + scrollbarSelectionOffset));
        }

        private void SetSliderFromPointer(SliderSettingsItem slider, SettingsLayout layout, float mouseVirtualX) {
            float sliderStart;
            float sliderEnd;
            GetSliderBounds(layout, out sliderStart, out sliderEnd);
            if (sliderEnd <= sliderStart)
                return;

            slider.SetNormalized((mouseVirtualX - sliderStart) / (sliderEnd - sliderStart));
            MarkDirty();
        }

        private static void GetSliderBounds(SettingsLayout layout, out float start, out float end) {
            start = layout.ContentLeft + (layout.ContentWidth * 0.62f);
            end = layout.Left + layout.Width - 110f;
            if (end < start + 80f)
                end = start + 80f;
        }

        private static void GetChoiceBounds(SettingsLayout layout, out float left, out float right) {
            right = layout.Left + layout.Width - 20f;
            left = Math.Max(layout.ContentLeft + (layout.ContentWidth * 0.58f), right - 230f);
        }

        private static void GetCloseButtonBounds(SettingsLayout layout, out float left, out float top, out float size) {
            size = 28f;
            left = layout.Left + layout.Width - 18f - size;
            top = layout.Top + 14f;
        }

        private static void GetFooterResetBounds(SettingsLayout layout, out float left, out float top,
            out float width, out float height) {
            width = 142f;
            height = 28f;
            left = layout.Left + layout.Width - 22f - width;
            top = layout.FooterTop + 62f;
        }

        private static void GetBindingModalBounds(SettingsLayout layout, out float left, out float top,
            out float width, out float height) {
            width = 560f;
            height = 186f;
            left = layout.Left + ((layout.Width - width) / 2f);
            top = layout.Top + ((layout.Height - height) / 2f);
        }

        private static void GetBindingCancelBounds(SettingsLayout layout, out float left, out float top,
            out float width, out float height) {
            float modalLeft;
            float modalTop;
            float modalWidth;
            float modalHeight;
            GetBindingModalBounds(layout, out modalLeft, out modalTop, out modalWidth, out modalHeight);
            width = 102f;
            height = 30f;
            left = modalLeft + modalWidth - 22f - width;
            top = modalTop + modalHeight - 45f;
        }

        private static void GetResetConfirmationButtonBounds(SettingsLayout layout, bool yes,
            out float left, out float top, out float width, out float height) {
            float modalLeft;
            float modalTop;
            float modalWidth;
            float modalHeight;
            GetBindingModalBounds(layout, out modalLeft, out modalTop, out modalWidth, out modalHeight);
            width = 112f;
            height = 32f;
            top = modalTop + modalHeight - 49f;
            left = yes ? modalLeft + modalWidth - 22f - width : modalLeft + modalWidth - 34f - (width * 2f);
        }

        private static void GetScrollbarGeometry(SettingsPage page, SettingsLayout layout, out float trackLeft,
            out float trackTop, out float trackWidth, out float trackHeight, out float thumbTop, out float thumbHeight) {
            trackLeft = layout.Left + layout.Width - 8f;
            trackTop = layout.ItemsTop + 6f;
            trackWidth = 3f;
            trackHeight = layout.ItemsHeight - 12f;
            thumbHeight = Math.Max(28f, trackHeight * SettingsLayout.VisibleRows / page.Items.Count);
            float maxOffset = Math.Max(1f, page.Items.Count - SettingsLayout.VisibleRows);
            float progress = Math.Max(0f, Math.Min(1f, page.ScrollOffset / maxOffset));
            thumbTop = trackTop + ((trackHeight - thumbHeight) * progress);
        }

        private void ActivateItem(SettingsItem item) {
            if (item == null || !item.Enabled)
                return;

            string before = item.ValueText;
            item.Activate();
            if (!item.IsAction && !string.Equals(before, item.ValueText, StringComparison.Ordinal))
                MarkDirty();
        }

        private void BeginResetConfirmation() {
            resetConfirmation = true;
            resetConfirmationYes = false;
            draggedSlider = null;
            draggingScrollbar = false;
        }

        private void HandleResetConfirmation(SettingsLayout layout) {
            if (input.Back || input.ToggleMenu) {
                resetConfirmation = false;
                resetConfirmationYes = false;
                return;
            }

            if (input.HorizontalMove != 0)
                resetConfirmationYes = input.HorizontalMove > 0;

            if (input.Mode == SettingsInputMode.Mouse && input.MouseClicked) {
                float noLeft;
                float noTop;
                float noWidth;
                float noHeight;
                float yesLeft;
                float yesTop;
                float yesWidth;
                float yesHeight;
                GetResetConfirmationButtonBounds(layout, false, out noLeft, out noTop, out noWidth, out noHeight);
                GetResetConfirmationButtonBounds(layout, true, out yesLeft, out yesTop, out yesWidth, out yesHeight);
                if (IsMouseOverRect(layout, noLeft, noTop, noWidth, noHeight)) {
                    resetConfirmation = false;
                    resetConfirmationYes = false;
                    return;
                }
                if (IsMouseOverRect(layout, yesLeft, yesTop, yesWidth, yesHeight)) {
                    resetConfirmationYes = true;
                    ResetAllSettings();
                    return;
                }
            }

            if (!input.Activate)
                return;
            if (resetConfirmationYes)
                ResetAllSettings();
            else {
                resetConfirmation = false;
                resetConfirmationYes = false;
            }
        }

        private void ResetAllSettings() {
            string pageTitle = CurrentPage.Title;
            try {
                dirty = false;
                saveAt = DateTime.MinValue;
                saveFailed = false;
                savedIndicatorUntil = DateTime.MinValue;
                bool saved = Config.TryResetToDefaults();
                AudioPauseCoordinator.RefreshSettings();
                BuildPages();
                selectedPage = FindPageIndex(pageTitle);
                ClampSelection();
                if (saved) {
                    savedIndicatorUntil = DateTime.UtcNow.AddSeconds(1.25);
                    ShowStatus("Settings restored to defaults.");
                } else {
                    dirty = true;
                    saveFailed = true;
                    saveAt = DateTime.MaxValue;
                    ShowStatus("Defaults applied, but settings.json could not be saved.", true);
                }
            } catch (Exception ex) {
                ShowStatus("Reset failed. Check CustomRadioStations.log.", true);
                Logger.Log("ERROR: Settings menu reset failed: " + ex);
            } finally {
                resetConfirmation = false;
                resetConfirmationYes = false;
            }
        }

        private void BeginBindingCapture(BindingCaptureKind kind, string title) {
            bindingCapture = kind;
            bindingCaptureTitle = title ?? string.Empty;
            draggedSlider = null;
        }

        private void HandleBindingCapture(SettingsLayout layout) {
            if (input.Back) {
                CancelBindingCapture();
                return;
            }

            if (input.Mode == SettingsInputMode.Mouse && input.MouseClicked) {
                float cancelLeft;
                float cancelTop;
                float cancelWidth;
                float cancelHeight;
                GetBindingCancelBounds(layout, out cancelLeft, out cancelTop, out cancelWidth, out cancelHeight);
                if (IsMouseOverRect(layout, cancelLeft, cancelTop, cancelWidth, cancelHeight)) {
                    CancelBindingCapture();
                    return;
                }
            }

            if (bindingCapture != BindingCaptureKind.ControllerOpenSettings)
                return;

            Control control;
            if (input.TryCaptureControllerControl(ControllerBindingCandidates, out control)) {
                Config.GP_OpenSettings = control;
                CompleteBindingCapture("Controller settings button: " + ControllerDisplayName(control));
            }
        }

        private void CompleteBindingCapture(string message) {
            bindingCapture = BindingCaptureKind.None;
            bindingCaptureTitle = string.Empty;
            ignoreToggleUntil = DateTime.UtcNow.AddMilliseconds(250);
            MarkDirty();
            ShowStatus(message);
        }

        private void CancelBindingCapture() {
            bindingCapture = BindingCaptureKind.None;
            bindingCaptureTitle = string.Empty;
            ignoreToggleUntil = DateTime.UtcNow.AddMilliseconds(150);
            ShowStatus("Binding unchanged.");
        }

        private static bool IsModifierOnlyKey(Keys key) {
            return key == Keys.ShiftKey || key == Keys.ControlKey || key == Keys.Menu ||
                key == Keys.LShiftKey || key == Keys.RShiftKey ||
                key == Keys.LControlKey || key == Keys.RControlKey ||
                key == Keys.LMenu || key == Keys.RMenu;
        }

        private static bool IsReservedMenuKey(Keys key) {
            switch (key) {
            case Keys.Up:
            case Keys.Down:
            case Keys.Left:
            case Keys.Right:
            case Keys.PageUp:
            case Keys.PageDown:
            case Keys.Home:
            case Keys.End:
            case Keys.D1:
            case Keys.D2:
            case Keys.D3:
            case Keys.D4:
            case Keys.D5:
            case Keys.D6:
            case Keys.NumPad1:
            case Keys.NumPad2:
            case Keys.NumPad3:
            case Keys.NumPad4:
            case Keys.NumPad5:
            case Keys.NumPad6:
            case Keys.Q:
            case Keys.E:
            case Keys.Enter:
            case Keys.Space:
            case Keys.Back:
            case Keys.Delete:
            case Keys.Escape:
                return true;
            default:
                return false;
            }
        }

        private static string ControllerDisplayName(Control control) {
            switch (control) {
            case Control.ScriptSelect:
            case Control.FrontendSelect:
                return "View / Back / Select";
            case Control.FrontendAccept:
                return "A / Cross";
            case Control.FrontendX:
                return "X / Square";
            case Control.FrontendY:
                return "Y / Triangle";
            case Control.FrontendLb:
            case Control.ScriptLB:
                return "LB / L1";
            case Control.FrontendRb:
            case Control.ScriptRB:
                return "RB / R1";
            case Control.FrontendLt:
            case Control.ScriptLT:
                return "LT / L2";
            case Control.FrontendRt:
            case Control.ScriptRT:
                return "RT / R2";
            case Control.FrontendLs:
            case Control.ScriptLS:
                return "Left Stick";
            case Control.FrontendRs:
            case Control.ScriptRS:
                return "Right Stick";
            case Control.ScriptPadUp:
                return "D-Pad Up";
            case Control.ScriptPadDown:
                return "D-Pad Down";
            case Control.ScriptPadLeft:
                return "D-Pad Left";
            case Control.ScriptPadRight:
                return "D-Pad Right";
            default:
                return control.ToString();
            }
        }

        private void Draw(SettingsLayout layout) {
            Color accent = SettingsTheme.Accent;
            DrawPanel(layout, accent);
            DrawHeader(layout, accent);
            DrawCategories(layout, accent);
            DrawPageHeader(layout, accent);
            DrawItems(layout, accent);
            DrawFooter(layout, accent);
        }

        private static void DrawPanel(SettingsLayout layout, Color accent) {
            // Dim the live scene enough to make the menu readable without turning this into
            // a full pause-menu replacement. The centred panel still remains visually light.
            DrawRect(layout, 0f, 0f, UIScreen.ScaledWidth, SettingsLayout.VirtualHeight, SettingsTheme.Backdrop);
            DrawRect(layout, layout.Left + 5f, layout.Top + 6f, layout.Width, layout.Height,
                Color.FromArgb(105, 0, 0, 0));
            DrawRect(layout, layout.Left, layout.Top, layout.Width, layout.Height, SettingsTheme.Panel);
            DrawRect(layout, layout.Left, layout.Top, layout.Width, 1f, SettingsTheme.Divider);
            DrawRect(layout, layout.Left, layout.Top + layout.Height - 1f, layout.Width, 1f, SettingsTheme.Divider);
            DrawRect(layout, layout.Left, layout.Top, 1f, layout.Height, SettingsTheme.Divider);
            DrawRect(layout, layout.Left + layout.Width - 1f, layout.Top, 1f, layout.Height, SettingsTheme.Divider);
            DrawRect(layout, layout.Left, layout.Top, layout.Width, SettingsLayout.HeaderHeight, SettingsTheme.Header);
            DrawRect(layout, layout.Left, layout.FooterTop, layout.Width, layout.FooterHeight, SettingsTheme.Footer);
            DrawRect(layout, layout.Left, layout.BodyTop, layout.CategoryWidth, layout.BodyHeight, SettingsTheme.Rail);
            DrawRect(layout, layout.Left, layout.BodyTop, layout.CategoryWidth, SettingsLayout.PageHeaderHeight,
                SettingsTheme.PageHeader);
            DrawRect(layout, layout.ContentLeft, layout.BodyTop, layout.ContentWidth, SettingsLayout.PageHeaderHeight,
                SettingsTheme.PageHeader);
            DrawRect(layout, layout.Left, layout.Top + SettingsLayout.HeaderHeight - 3f, layout.Width, 3f, accent);
            DrawRect(layout, layout.ContentLeft, layout.BodyTop, 1f, layout.BodyHeight, SettingsTheme.Divider);
            DrawRect(layout, layout.Left, layout.CategoryTop - 1f, layout.CategoryWidth, 1f, SettingsTheme.Divider);
            DrawRect(layout, layout.Left, layout.FooterTop, layout.Width, 1f, SettingsTheme.Divider);
        }

        private void DrawHeader(SettingsLayout layout, Color accent) {
            DrawText(layout, "CUSTOM RADIO STATIONS", layout.Left + 22f, layout.Top + 12f, 0.44f,
                SettingsTheme.PrimaryText, UIHelper.TextJustification.Left);

            if (input.Mode != SettingsInputMode.Mouse) {
                DrawText(layout, "SETTINGS", layout.Left + layout.Width - 22f, layout.Top + 15f, 0.32f,
                    SettingsTheme.Alpha(accent, 235), UIHelper.TextJustification.Right);
                return;
            }

            float closeLeft;
            float closeTop;
            float closeSize;
            GetCloseButtonBounds(layout, out closeLeft, out closeTop, out closeSize);
            bool closeHover = IsMouseOverRect(layout, closeLeft, closeTop, closeSize, closeSize);
            DrawText(layout, "SETTINGS", closeLeft - 12f, layout.Top + 15f, 0.32f,
                SettingsTheme.Alpha(accent, 235), UIHelper.TextJustification.Right);
            DrawRect(layout, closeLeft, closeTop, closeSize, closeSize,
                closeHover ? SettingsTheme.ValueSurfaceHover : SettingsTheme.ValueSurface);
            DrawText(layout, "X", closeLeft + (closeSize / 2f), closeTop + 4f, 0.27f,
                closeHover ? Color.White : SettingsTheme.SecondaryText, UIHelper.TextJustification.Center);
        }

        private void DrawCategories(SettingsLayout layout, Color accent) {
            DrawText(layout, "CATEGORIES", layout.Left + 16f, layout.BodyTop + 17f, 0.255f,
                SettingsTheme.MutedText, UIHelper.TextJustification.Left);

            for (int index = 0; index < pages.Count; index++) {
                float y = layout.CategoryTop + (index * SettingsLayout.RowHeight);
                bool selected = index == selectedPage;
                bool hovered = IsMouseOverCategory(layout, index);
                if (selected) {
                    DrawRect(layout, layout.Left + 4f, y + 3f, layout.CategoryWidth - 8f,
                        SettingsLayout.RowHeight - 6f, SettingsTheme.Alpha(accent, 48));
                    DrawRect(layout, layout.Left + 4f, y + 7f, 4f,
                        SettingsLayout.RowHeight - 14f, accent);
                } else if (hovered) {
                    DrawRect(layout, layout.Left + 4f, y + 3f, layout.CategoryWidth - 8f,
                        SettingsLayout.RowHeight - 6f, SettingsTheme.HoverRow);
                }

                string categoryNumber = (index + 1).ToString("00", CultureInfo.InvariantCulture);
                DrawText(layout, categoryNumber, layout.Left + 16f, y + 12f, 0.25f,
                    selected ? SettingsTheme.Alpha(accent, 235) : SettingsTheme.MutedText,
                    UIHelper.TextJustification.Left);
                DrawText(layout, pages[index].Title.ToUpperInvariant(), layout.Left + 47f, y + 11f, 0.30f,
                    selected ? SettingsTheme.PrimaryText : SettingsTheme.SecondaryText,
                    UIHelper.TextJustification.Left);

                int modifiedCount = CountModifiedSettings(pages[index]);
                if (modifiedCount > 0) {
                    const float badgeWidth = 25f;
                    const float badgeHeight = 20f;
                    float badgeLeft = layout.ContentLeft - 13f - badgeWidth;
                    float badgeTop = y + ((SettingsLayout.RowHeight - badgeHeight) / 2f);
                    DrawRect(layout, badgeLeft, badgeTop, badgeWidth, badgeHeight, SettingsTheme.Alpha(accent, 52));
                    DrawText(layout, modifiedCount.ToString(CultureInfo.InvariantCulture), badgeLeft + (badgeWidth / 2f),
                        badgeTop + 3f, 0.225f, SettingsTheme.Alpha(accent, 245), UIHelper.TextJustification.Center);
                }
            }

            float inputTop = layout.FooterTop - 61f;
            DrawText(layout, "INPUT", layout.Left + 16f, inputTop, 0.225f,
                SettingsTheme.MutedText, UIHelper.TextJustification.Left);
            string inputName = input.Mode.ToString().ToUpperInvariant();
            DrawRect(layout, layout.Left + 16f, inputTop + 22f, layout.CategoryWidth - 32f, 27f,
                SettingsTheme.Alpha(accent, 34));
            DrawText(layout, inputName, layout.Left + 28f, inputTop + 27f, 0.245f,
                SettingsTheme.Alpha(accent, 235), UIHelper.TextJustification.Left);
        }

        private void DrawPageHeader(SettingsLayout layout, Color accent) {
            DrawText(layout, CurrentPage.Title.ToUpperInvariant(), layout.ContentLeft + 20f,
                layout.BodyTop + 7f, 0.31f, SettingsTheme.Alpha(accent, 245), UIHelper.TextJustification.Left);
            DrawText(layout, CurrentPage.Subtitle, layout.ContentLeft + 20f,
                layout.BodyTop + 29f, 0.235f, SettingsTheme.MutedText, UIHelper.TextJustification.Left);
            int modifiedCount = CountModifiedSettings(CurrentPage);
            string counter = "PAGE " + (selectedPage + 1).ToString(CultureInfo.InvariantCulture) + " / " +
                pages.Count.ToString(CultureInfo.InvariantCulture);
            if (modifiedCount > 0)
                counter += "   |   " + modifiedCount.ToString(CultureInfo.InvariantCulture) + " MODIFIED";
            DrawText(layout, counter, layout.Left + layout.Width - 20f, layout.BodyTop + 10f,
                0.255f, modifiedCount > 0 ? SettingsTheme.Alpha(accent, 225) : SettingsTheme.MutedText,
                UIHelper.TextJustification.Right);
            string saveIndicator;
            Color saveIndicatorColor;
            if (saveFailed) {
                saveIndicator = "SAVE FAILED";
                saveIndicatorColor = SettingsTheme.Danger;
            } else if (dirty) {
                saveIndicator = "SAVING...";
                saveIndicatorColor = SettingsTheme.Alpha(accent, 235);
            } else if (DateTime.UtcNow < savedIndicatorUntil) {
                saveIndicator = "SAVED";
                saveIndicatorColor = SettingsTheme.Alpha(accent, 235);
            } else {
                saveIndicator = "AUTO-SAVE ON";
                saveIndicatorColor = SettingsTheme.MutedText;
            }
            DrawText(layout, saveIndicator, layout.Left + layout.Width - 20f,
                layout.BodyTop + 31f, 0.215f, saveIndicatorColor, UIHelper.TextJustification.Right);
            DrawRect(layout, layout.ContentLeft, layout.ItemsTop - 1f, layout.ContentWidth, 1f, SettingsTheme.Divider);
        }

        private void DrawItems(SettingsLayout layout, Color accent) {
            SettingsPage page = CurrentPage;
            EnsureVisible(page);
            int count = Math.Min(SettingsLayout.VisibleRows, Math.Max(0, page.Items.Count - page.ScrollOffset));
            for (int visible = 0; visible < SettingsLayout.VisibleRows; visible++) {
                float y = layout.ItemsTop + (visible * SettingsLayout.RowHeight);
                DrawRect(layout, layout.ContentLeft + 16f, y + SettingsLayout.RowHeight - 1f,
                    layout.ContentWidth - 32f, 1f, Color.FromArgb(34, 255, 255, 255));

                if (visible >= count)
                    continue;

                int itemIndex = page.ScrollOffset + visible;
                SettingsItem item = page.Items[itemIndex];
                bool selected = itemIndex == page.SelectedIndex;
                if (selected) {
                    DrawRect(layout, layout.ContentLeft + 5f, y + 3f, layout.ContentWidth - 10f,
                        SettingsLayout.RowHeight - 6f, item.Selectable ? SettingsTheme.SelectedRow : SettingsTheme.HoverRow);
                    if (item.Selectable)
                        DrawRect(layout, layout.ContentLeft + 5f, y + 7f, 4f,
                            SettingsLayout.RowHeight - 14f, accent);
                }

                Color textColor = item.Enabled && item.Selectable ? SettingsTheme.PrimaryText : SettingsTheme.SecondaryText;
                bool modified = item.CanReset && !item.IsDefault;
                float titleX = layout.ContentLeft + (modified ? 28f : 20f);
                if (modified)
                    DrawRect(layout, layout.ContentLeft + 17f, y + 20f, 5f, 5f, SettingsTheme.Alpha(accent, 245));
                SectionSettingsItem section = item as SectionSettingsItem;
                if (section != null) {
                    DrawRect(layout, layout.ContentLeft + 19f, y + 21f, 18f, 2f, SettingsTheme.Alpha(accent, 205));
                    DrawText(layout, section.Title.ToUpperInvariant(), layout.ContentLeft + 47f, y + 13f, 0.235f,
                        SettingsTheme.Alpha(accent, 225), UIHelper.TextJustification.Left);
                    continue;
                }

                ActionSettingsItem actionItem = item as ActionSettingsItem;
                Color titleColor = actionItem != null && actionItem.IsDestructive
                    ? SettingsTheme.Danger
                    : textColor;
                DrawText(layout, item.Title, titleX, y + 11f, 0.33f,
                    titleColor, UIHelper.TextJustification.Left);

                SliderSettingsItem slider = item as SliderSettingsItem;
                if (slider != null) {
                    DrawSlider(layout, slider, y, accent, textColor);
                    continue;
                }

                ToggleSettingsItem toggle = item as ToggleSettingsItem;
                if (toggle != null) {
                    DrawToggle(layout, toggle, y, accent, item.Enabled);
                    continue;
                }

                ChoiceSettingsItem choice = item as ChoiceSettingsItem;
                if (choice != null) {
                    DrawChoice(layout, choice, y, accent, item.Enabled);
                    continue;
                }

                BindingSettingsItem binding = item as BindingSettingsItem;
                if (binding != null) {
                    DrawValueButton(layout, item.ValueText, y, accent, 250f, true);
                    continue;
                }

                ActionSettingsItem action = item as ActionSettingsItem;
                if (action != null) {
                    DrawValueButton(layout, action.IsDestructive ? "RESET" : "RUN", y, accent, 106f, true,
                        action.IsDestructive);
                    continue;
                }

                InfoSettingsItem info = item as InfoSettingsItem;
                if (info != null) {
                    DrawInfoBadge(layout, info.ValueText, y, accent);
                    continue;
                }

                DrawText(layout, item.ValueText, layout.Left + layout.Width - 20f, y + 11f, 0.30f,
                    item.Enabled ? SettingsTheme.SecondaryText : SettingsTheme.MutedText,
                    UIHelper.TextJustification.Right);
            }

            if (page.Items.Count > SettingsLayout.VisibleRows) {
                float trackLeft;
                float trackTop;
                float trackWidth;
                float trackHeight;
                float thumbTop;
                float thumbHeight;
                GetScrollbarGeometry(page, layout, out trackLeft, out trackTop, out trackWidth, out trackHeight,
                    out thumbTop, out thumbHeight);
                bool scrollbarHover = input.Mode == SettingsInputMode.Mouse &&
                    IsMouseOverRect(layout, trackLeft - 8f, trackTop, trackWidth + 16f, trackHeight);
                float visibleTrackWidth = scrollbarHover || draggingScrollbar ? 5f : trackWidth;
                float visibleTrackLeft = trackLeft - ((visibleTrackWidth - trackWidth) / 2f);
                DrawRect(layout, visibleTrackLeft, trackTop, visibleTrackWidth, trackHeight, SettingsTheme.ScrollTrack);
                DrawRect(layout, visibleTrackLeft - 1f, thumbTop, visibleTrackWidth + 2f, thumbHeight,
                    SettingsTheme.Alpha(accent, draggingScrollbar ? 255 : (scrollbarHover ? 245 : 220)));
            }
        }

        private void DrawValueButton(SettingsLayout layout, string value, float y, Color accent,
            float width, bool showChevron, bool destructive = false) {
            float right = layout.Left + layout.Width - 20f;
            float left = right - width;
            float top = y + 7f;
            float height = SettingsLayout.RowHeight - 14f;
            bool hover = input.Mode == SettingsInputMode.Mouse && IsMouseOverRect(layout, left, top, width, height);
            Color surface = destructive
                ? (hover ? SettingsTheme.DangerSurfaceHover : SettingsTheme.DangerSurface)
                : (hover ? SettingsTheme.ValueSurfaceHover : SettingsTheme.ValueSurface);
            Color chevronColor = destructive ? SettingsTheme.Danger : SettingsTheme.Alpha(accent, 245);
            DrawRect(layout, left, top, width, height, surface);
            DrawText(layout, value, left + 12f, y + 11f, 0.27f, SettingsTheme.PrimaryText,
                UIHelper.TextJustification.Left);
            if (showChevron)
                DrawText(layout, ">", right - 12f, y + 11f, 0.29f, chevronColor,
                    UIHelper.TextJustification.Right);
        }

        private static void DrawInfoBadge(SettingsLayout layout, string value, float y, Color accent) {
            const float width = 72f;
            const float height = 24f;
            float right = layout.Left + layout.Width - 20f;
            float left = right - width;
            float top = y + ((SettingsLayout.RowHeight - height) / 2f);
            DrawRect(layout, left, top, width, height, SettingsTheme.Alpha(accent, 52));
            DrawText(layout, value, left + (width / 2f), top + 4f, 0.245f, SettingsTheme.Alpha(accent, 245),
                UIHelper.TextJustification.Center);
        }

        private void DrawChoice(SettingsLayout layout, ChoiceSettingsItem choice, float y,
            Color accent, bool enabled) {
            float left;
            float right;
            GetChoiceBounds(layout, out left, out right);
            float top = y + 7f;
            float height = SettingsLayout.RowHeight - 14f;
            float arrowWidth = 34f;
            bool hoverValue = input.Mode == SettingsInputMode.Mouse && IsMouseOverRect(layout, left, top, right - left, height);
            Color surface = enabled
                ? (hoverValue ? SettingsTheme.ValueSurfaceHover : SettingsTheme.ValueSurface)
                : Color.FromArgb(75, 42, 42, 49);
            DrawRect(layout, left, top, right - left, height, surface);
            DrawRect(layout, left + arrowWidth, top + 4f, 1f, height - 8f, SettingsTheme.Divider);
            DrawRect(layout, right - arrowWidth, top + 4f, 1f, height - 8f, SettingsTheme.Divider);
            DrawText(layout, "<", left + (arrowWidth / 2f), y + 11f, 0.29f,
                enabled ? SettingsTheme.Alpha(accent, 245) : SettingsTheme.MutedText, UIHelper.TextJustification.Center);
            DrawText(layout, choice.ValueText, (left + right) / 2f, y + 11f, 0.28f,
                enabled ? SettingsTheme.PrimaryText : SettingsTheme.MutedText, UIHelper.TextJustification.Center);
            DrawText(layout, ">", right - (arrowWidth / 2f), y + 11f, 0.29f,
                enabled ? SettingsTheme.Alpha(accent, 245) : SettingsTheme.MutedText, UIHelper.TextJustification.Center);
        }

        private void DrawSlider(SettingsLayout layout, SliderSettingsItem slider, float y,
            Color accent, Color textColor) {
            float sliderStart;
            float sliderEnd;
            GetSliderBounds(layout, out sliderStart, out sliderEnd);
            float sliderWidth = sliderEnd - sliderStart;
            float barY = y + (SettingsLayout.RowHeight / 2f);
            bool hover = input.Mode == SettingsInputMode.Mouse &&
                IsMouseOverRect(layout, sliderStart - 8f, barY - 12f, sliderWidth + 16f, 24f);
            DrawRect(layout, sliderStart, barY - 2f, sliderWidth, 4f,
                Color.FromArgb(hover ? 170 : 110, 185, 185, 193));
            float filledWidth = sliderWidth * slider.NormalizedValue;
            if (filledWidth > 0f)
                DrawRect(layout, sliderStart, barY - 2f, filledWidth, 4f, accent);
            float handleX = sliderStart + filledWidth;
            DrawRect(layout, handleX - 5f, barY - 8f, 10f, 16f, hover ? Color.White : SettingsTheme.PrimaryText);
            DrawRect(layout, handleX - 2f, barY - 5f, 4f, 10f, accent);
            DrawText(layout, slider.ValueText, layout.Left + layout.Width - 20f, y + 11f, 0.29f,
                textColor, UIHelper.TextJustification.Right);
        }

        private void DrawToggle(SettingsLayout layout, ToggleSettingsItem toggle, float y,
            Color accent, bool enabled) {
            const float width = 62f;
            const float height = 24f;
            float left = layout.Left + layout.Width - 20f - width;
            float top = y + ((SettingsLayout.RowHeight - height) / 2f);
            bool hover = input.Mode == SettingsInputMode.Mouse && IsMouseOverRect(layout, left, top, width, height);
            Color background = toggle.IsOn
                ? SettingsTheme.Alpha(accent, enabled ? (hover ? 255 : 235) : 100)
                : Color.FromArgb(enabled ? (hover ? 205 : 160) : 90, 54, 54, 61);
            DrawRect(layout, left, top, width, height, background);
            DrawText(layout, toggle.ValueText, left + (width / 2f), top + 4f, 0.25f,
                toggle.IsOn ? Color.White : SettingsTheme.SecondaryText,
                UIHelper.TextJustification.Center);
        }

        private void DrawBindingCapture(SettingsLayout layout, Color accent) {
            DrawRect(layout, layout.Left, layout.Top, layout.Width, layout.Height, Color.FromArgb(145, 0, 0, 0));

            float left;
            float top;
            float modalWidth;
            float modalHeight;
            GetBindingModalBounds(layout, out left, out top, out modalWidth, out modalHeight);
            DrawRect(layout, left + 4f, top + 5f, modalWidth, modalHeight, Color.FromArgb(100, 0, 0, 0));
            DrawRect(layout, left, top, modalWidth, modalHeight, SettingsTheme.Header);
            DrawRect(layout, left, top, modalWidth, 3f, accent);

            DrawText(layout, "REBIND CONTROL", left + 22f, top + 19f, 0.34f,
                SettingsTheme.PrimaryText, UIHelper.TextJustification.Left);
            DrawText(layout, bindingCaptureTitle, left + 22f, top + 57f, 0.30f,
                SettingsTheme.Alpha(accent, 245), UIHelper.TextJustification.Left);
            string currentBinding = bindingCapture == BindingCaptureKind.KeyboardOpenSettings
                ? Config.KB_OpenSettings.ToString()
                : ControllerDisplayName(Config.GP_OpenSettings);
            DrawText(layout, "Current: " + currentBinding, left + 22f, top + 84f, 0.245f,
                SettingsTheme.MutedText, UIHelper.TextJustification.Left);

            string prompt = bindingCapture == BindingCaptureKind.KeyboardOpenSettings
                ? "Press a keyboard key..."
                : "Press a controller button...";
            string cancel = bindingCapture == BindingCaptureKind.KeyboardOpenSettings
                ? "Esc  Cancel"
                : GTAFunction.InputString(Control.FrontendCancel) + "  Cancel";
            DrawText(layout, prompt, left + 22f, top + 116f, 0.30f,
                SettingsTheme.SecondaryText, UIHelper.TextJustification.Left);
            if (input.Mode == SettingsInputMode.Mouse) {
                float cancelLeft;
                float cancelTop;
                float cancelWidth;
                float cancelHeight;
                GetBindingCancelBounds(layout, out cancelLeft, out cancelTop, out cancelWidth, out cancelHeight);
                bool hover = IsMouseOverRect(layout, cancelLeft, cancelTop, cancelWidth, cancelHeight);
                DrawRect(layout, cancelLeft, cancelTop, cancelWidth, cancelHeight,
                    hover ? SettingsTheme.ValueSurfaceHover : SettingsTheme.ValueSurface);
                DrawText(layout, "CANCEL", cancelLeft + (cancelWidth / 2f), cancelTop + 5f, 0.25f,
                    hover ? Color.White : SettingsTheme.SecondaryText, UIHelper.TextJustification.Center);
            } else {
                DrawText(layout, cancel, left + modalWidth - 22f, top + 148f, 0.25f,
                    SettingsTheme.MutedText, UIHelper.TextJustification.Right);
            }
        }

        private void DrawResetConfirmation(SettingsLayout layout, Color accent) {
            DrawRect(layout, layout.Left, layout.Top, layout.Width, layout.Height, Color.FromArgb(155, 0, 0, 0));

            float left;
            float top;
            float modalWidth;
            float modalHeight;
            GetBindingModalBounds(layout, out left, out top, out modalWidth, out modalHeight);
            DrawRect(layout, left + 4f, top + 5f, modalWidth, modalHeight, Color.FromArgb(100, 0, 0, 0));
            DrawRect(layout, left, top, modalWidth, modalHeight, SettingsTheme.Header);
            DrawRect(layout, left, top, modalWidth, 3f, accent);

            DrawText(layout, "RESET ALL SETTINGS?", left + 22f, top + 19f, 0.34f,
                SettingsTheme.Danger, UIHelper.TextJustification.Left);
            DrawWrappedText(layout,
                "Restore all Custom Radio Stations settings to their built-in defaults? Station definitions and music files are not changed.",
                left + 22f, top + 58f, modalWidth - 44f, 0.27f, SettingsTheme.SecondaryText);

            float noLeft;
            float noTop;
            float noWidth;
            float noHeight;
            float yesLeft;
            float yesTop;
            float yesWidth;
            float yesHeight;
            GetResetConfirmationButtonBounds(layout, false, out noLeft, out noTop, out noWidth, out noHeight);
            GetResetConfirmationButtonBounds(layout, true, out yesLeft, out yesTop, out yesWidth, out yesHeight);

            bool noHover = input.Mode == SettingsInputMode.Mouse && IsMouseOverRect(layout, noLeft, noTop, noWidth, noHeight);
            bool yesHover = input.Mode == SettingsInputMode.Mouse && IsMouseOverRect(layout, yesLeft, yesTop, yesWidth, yesHeight);
            bool noSelected = input.Mode == SettingsInputMode.Mouse ? noHover : !resetConfirmationYes;
            bool yesSelected = input.Mode == SettingsInputMode.Mouse ? yesHover : resetConfirmationYes;

            DrawRect(layout, noLeft, noTop, noWidth, noHeight,
                noSelected ? SettingsTheme.ValueSurfaceHover : SettingsTheme.ValueSurface);
            DrawText(layout, "NO", noLeft + (noWidth / 2f), noTop + 6f, 0.255f,
                noSelected ? Color.White : SettingsTheme.SecondaryText, UIHelper.TextJustification.Center);
            DrawRect(layout, yesLeft, yesTop, yesWidth, yesHeight,
                yesSelected ? SettingsTheme.DangerSurfaceHover : SettingsTheme.DangerSurface);
            DrawText(layout, "YES", yesLeft + (yesWidth / 2f), yesTop + 6f, 0.255f,
                yesSelected ? Color.White : SettingsTheme.SecondaryText, UIHelper.TextJustification.Center);
        }

        private void DrawFooter(SettingsLayout layout, Color accent) {
            SettingsItem item = CurrentPage.SelectedItem;
            bool showingStatus = !string.IsNullOrEmpty(statusMessage) && DateTime.UtcNow < statusUntil;
            string description = showingStatus ? statusMessage : (item == null ? string.Empty : item.Description);
            Color statusColor = statusIsError ? SettingsTheme.Danger : SettingsTheme.Alpha(accent, 245);
            Color descriptionColor = showingStatus
                ? statusColor
                : Color.FromArgb(235, 225, 225, 230);

            if (showingStatus)
                DrawRect(layout, layout.Left + 15f, layout.FooterTop + 12f, 3f, 42f, statusColor);
            DrawWrappedText(layout, description, layout.Left + 22f, layout.FooterTop + 11f,
                layout.Width - 44f, 0.29f, descriptionColor);
            float controlsDividerY = layout.FooterTop + (layout.UseCompactFooter
                ? Math.Min(101f, layout.FooterHeight - 42f)
                : 111f);
            DrawRect(layout, layout.Left + 20f, controlsDividerY,
                layout.Width - 40f, 1f, SettingsTheme.Divider);

            bool canReset = item != null && item.CanReset && !item.IsDefault;
            if (item != null && item.CanReset && !string.IsNullOrEmpty(item.DefaultValueText)) {
                string defaultText = "Default: " + item.DefaultValueText;
                if (!item.IsDefault)
                    defaultText += "   |   MODIFIED";
                DrawText(layout, defaultText, layout.Left + 22f, layout.FooterTop + 70f, 0.235f,
                    item.IsDefault ? SettingsTheme.MutedText : SettingsTheme.Alpha(accent, 235),
                    UIHelper.TextJustification.Left);
            }

            if (input.Mode == SettingsInputMode.Mouse && canReset) {
                float resetLeft;
                float resetTop;
                float resetWidth;
                float resetHeight;
                GetFooterResetBounds(layout, out resetLeft, out resetTop, out resetWidth, out resetHeight);
                bool hover = IsMouseOverRect(layout, resetLeft, resetTop, resetWidth, resetHeight);
                DrawRect(layout, resetLeft, resetTop, resetWidth, resetHeight,
                    hover ? SettingsTheme.ValueSurfaceHover : SettingsTheme.ValueSurface);
                DrawText(layout, "RESTORE DEFAULT", resetLeft + (resetWidth / 2f), resetTop + 5f, 0.23f,
                    hover ? Color.White : SettingsTheme.SecondaryText, UIHelper.TextJustification.Center);
            }

            string leftControls;
            string rightControls;
            switch (input.Mode) {
            case SettingsInputMode.Mouse:
                leftControls = "Click Select   Wheel Scroll   Drag sliders / scrollbar";
                rightControls = (canReset ? "Restore Default   " : string.Empty) + "Right Click / Esc Back";
                break;
            case SettingsInputMode.Keyboard:
                leftControls = "Arrows Navigate / Change   1-6 Category";
                rightControls = "Home/End Jump   Enter Select" +
                    (canReset ? "   Backspace Reset" : string.Empty) + "   Esc Back";
                break;
            default:
                leftControls = GTAFunction.InputString(Control.FrontendUp) + " " +
                    GTAFunction.InputString(Control.FrontendDown) + " Navigate   " +
                    GTAFunction.InputString(Control.FrontendLeft) + " " + GTAFunction.InputString(Control.FrontendRight) + " Change   " +
                    GTAFunction.InputString(Control.FrontendLb) + " " + GTAFunction.InputString(Control.FrontendRb) + " Category";
                rightControls = GTAFunction.InputString(Control.FrontendAccept) + " Select" +
                    (canReset ? "   " + GTAFunction.InputString(Control.FrontendX) + " Reset" : string.Empty) +
                    "   " + GTAFunction.InputString(Control.FrontendCancel) + " Back";
                break;
            }
            if (layout.UseCompactFooter) {
                float firstControlsY = controlsDividerY + 7f;
                float secondControlsY = controlsDividerY + 26f;
                DrawText(layout, leftControls, layout.Left + 22f, firstControlsY, 0.225f,
                    SettingsTheme.MutedText, UIHelper.TextJustification.Left);
                DrawText(layout, rightControls, layout.Left + layout.Width - 22f, secondControlsY, 0.225f,
                    SettingsTheme.MutedText, UIHelper.TextJustification.Right);
            } else {
                float controlsY = layout.FooterTop + 120f;
                DrawText(layout, leftControls, layout.Left + 22f, controlsY, 0.255f,
                    SettingsTheme.MutedText, UIHelper.TextJustification.Left);
                DrawText(layout, rightControls, layout.Left + layout.Width - 22f, controlsY, 0.255f,
                    SettingsTheme.MutedText, UIHelper.TextJustification.Right);
            }
        }

        private bool IsMouseOverRect(SettingsLayout layout, float left, float top, float width, float height) {
            if (input.Mode != SettingsInputMode.Mouse)
                return false;
            float x = input.MouseX * UIScreen.ScaledWidth;
            float y = input.MouseY * SettingsLayout.VirtualHeight;
            return x >= left && x <= left + width && y >= top && y <= top + height;
        }

        private bool IsMouseOverCategory(SettingsLayout layout, int index) {
            if (input.Mode != SettingsInputMode.Mouse)
                return false;
            float x = input.MouseX * UIScreen.ScaledWidth;
            float y = input.MouseY * SettingsLayout.VirtualHeight;
            float rowTop = layout.CategoryTop + (index * SettingsLayout.RowHeight);
            return x >= layout.Left && x < layout.ContentLeft &&
                y >= rowTop && y < rowTop + SettingsLayout.RowHeight;
        }

        private void MoveSelection(int direction) {
            SettingsPage page = CurrentPage;
            if (page.Items.Count == 0)
                return;

            int step = direction >= 0 ? 1 : -1;
            int candidate = page.SelectedIndex + step;
            while (candidate >= 0 && candidate < page.Items.Count) {
                if (page.Items[candidate].Selectable) {
                    page.SelectedIndex = candidate;
                    EnsureVisible(page);
                    return;
                }
                candidate += step;
            }
        }

        private void MoveSelectionToBoundary(int direction) {
            SettingsPage page = CurrentPage;
            if (page.Items.Count == 0)
                return;

            if (direction < 0) {
                for (int index = 0; index < page.Items.Count; index++) {
                    if (!page.Items[index].Selectable)
                        continue;
                    page.SelectedIndex = index;
                    EnsureVisible(page);
                    return;
                }
            } else {
                for (int index = page.Items.Count - 1; index >= 0; index--) {
                    if (!page.Items[index].Selectable)
                        continue;
                    page.SelectedIndex = index;
                    EnsureVisible(page);
                    return;
                }
            }
        }

        private void ChangePage(int direction) {
            if (pages.Count == 0)
                return;
            SelectPage((selectedPage + (direction >= 0 ? 1 : -1) + pages.Count) % pages.Count);
        }

        private void SelectPage(int index) {
            if (pages.Count == 0)
                return;
            selectedPage = Math.Max(0, Math.Min(pages.Count - 1, index));
            ClampSelection();
            if (Config.RememberSettingsPage) {
                Config.LastSettingsPage = CurrentPage.Title;
                MarkDirty();
            }
        }

        private int FindPageIndex(string title) {
            if (string.IsNullOrWhiteSpace(title))
                return 0;
            for (int index = 0; index < pages.Count; index++) {
                if (string.Equals(pages[index].Title, title, StringComparison.OrdinalIgnoreCase))
                    return index;
            }
            return 0;
        }

        private static int CountModifiedSettings(SettingsPage page) {
            if (page == null)
                return 0;
            int count = 0;
            foreach (SettingsItem item in page.Items) {
                if (item != null && item.CanReset && !item.IsDefault)
                    count++;
            }
            return count;
        }

        private SettingsPage CurrentPage => pages[Math.Max(0, Math.Min(pages.Count - 1, selectedPage))];

        private void ClampSelection() {
            if (pages.Count == 0)
                return;
            selectedPage = Math.Max(0, Math.Min(pages.Count - 1, selectedPage));
            SettingsPage page = CurrentPage;
            if (page.Items.Count == 0) {
                page.SelectedIndex = 0;
                page.ScrollOffset = 0;
                return;
            }
            page.SelectedIndex = Math.Max(0, Math.Min(page.Items.Count - 1, page.SelectedIndex));
            if (input.Mode != SettingsInputMode.Mouse && !page.Items[page.SelectedIndex].Selectable)
                page.SelectedIndex = FindNearestSelectable(page, page.SelectedIndex);
            EnsureVisible(page);
        }

        private static int FindNearestSelectable(SettingsPage page, int startIndex) {
            if (page == null || page.Items.Count == 0)
                return 0;

            for (int distance = 0; distance < page.Items.Count; distance++) {
                int forward = startIndex + distance;
                if (forward >= 0 && forward < page.Items.Count && page.Items[forward].Selectable)
                    return forward;
                int backward = startIndex - distance;
                if (backward >= 0 && backward < page.Items.Count && page.Items[backward].Selectable)
                    return backward;
            }
            return Math.Max(0, Math.Min(page.Items.Count - 1, startIndex));
        }

        private static void EnsureVisible(SettingsPage page) {
            if (page.SelectedIndex < page.ScrollOffset)
                page.ScrollOffset = page.SelectedIndex;
            else if (page.SelectedIndex >= page.ScrollOffset + SettingsLayout.VisibleRows)
                page.ScrollOffset = page.SelectedIndex - SettingsLayout.VisibleRows + 1;
            int maxOffset = Math.Max(0, page.Items.Count - SettingsLayout.VisibleRows);
            page.ScrollOffset = Math.Max(0, Math.Min(maxOffset, page.ScrollOffset));
        }

        private void MarkDirty() {
            dirty = true;
            saveFailed = false;
            saveAt = DateTime.UtcNow.AddMilliseconds(350);
        }

        private void UpdateSave() {
            if (dirty && DateTime.UtcNow >= saveAt)
                FlushSave();
        }

        private void FlushSave() {
            if (!dirty)
                return;

            if (Config.TrySave()) {
                dirty = false;
                saveFailed = false;
                saveAt = DateTime.MinValue;
                savedIndicatorUntil = DateTime.UtcNow.AddSeconds(1.25);
                return;
            }

            // Do not retry every frame after an I/O failure. Keep the dirty state so
            // closing the menu or changing another option gets another save attempt.
            saveFailed = true;
            saveAt = DateTime.MaxValue;
            ShowStatus("Settings could not be saved. Check CustomRadioStations.log.", true);
        }

        private void ReloadSettingsFromDisk() {
            // This action must not FlushSave first: doing so would overwrite settings.json
            // and defeat the purpose of reloading external edits. Changes that already
            // passed the auto-save debounce naturally remain because they are on disk.
            dirty = false;
            saveAt = DateTime.MinValue;
            saveFailed = false;
            savedIndicatorUntil = DateTime.MinValue;
            try {
                reloadSettings?.Invoke();
                BuildPages();
                selectedPage = Config.RememberSettingsPage ? FindPageIndex(Config.LastSettingsPage) : 0;
                ClampSelection();
                ShowStatus("Settings reloaded from disk.");
            } catch (Exception ex) {
                ShowStatus("Reload failed. Check CustomRadioStations.log.", true);
                Logger.Log("ERROR: Settings menu reload failed: " + ex);
            }
        }

        private void RunAction(string successMessage, Action action) {
            try {
                action?.Invoke();
                ShowStatus(successMessage);
            } catch (Exception ex) {
                ShowStatus("Action failed. Check CustomRadioStations.log.", true);
                Logger.Log("ERROR: Settings menu action failed: " + ex);
            }
        }

        private void ShowStatus(string message, bool isError = false) {
            statusMessage = message ?? string.Empty;
            statusIsError = isError;
            statusUntil = DateTime.UtcNow.AddSeconds(2.5);
        }

        private static int UnicodeModeToIndex(UnicodeTextMode mode) {
            switch (mode) {
            case UnicodeTextMode.NativeOnly:
                return 1;
            case UnicodeTextMode.BitmapFallback:
                return 2;
            default:
                return 0;
            }
        }

        private static UnicodeTextMode IndexToUnicodeMode(int index) {
            if (index == 1)
                return UnicodeTextMode.NativeOnly;
            if (index == 2)
                return UnicodeTextMode.BitmapFallback;
            return UnicodeTextMode.Auto;
        }

        private static void SuppressGameControls() {
            try {
                Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);
                Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 1);
                Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 2);
            } catch { }
        }

        private static void DrawRect(SettingsLayout layout, float x, float y, float width, float height, Color color) {
            UIHelper.DrawRectangle(layout.ToX(x + (width / 2f)), SettingsLayout.ToY(y + (height / 2f)),
                layout.ToWidth(width), SettingsLayout.ToHeight(height), color.R, color.G, color.B, color.A);
        }

        private static void DrawText(SettingsLayout layout, string text, float x, float y, float size, Color color,
            UIHelper.TextJustification justification) {
            UIHelper.DrawCustomText(text ?? string.Empty, size, GtaFont.ChaletLondon,
                color.R, color.G, color.B, color.A,
                layout.ToX(x), SettingsLayout.ToY(y),
                0, 0, 0, 0, 0, justification);
        }

        private static void DrawWrappedText(SettingsLayout layout, string text, float x, float y, float width,
            float size, Color color) {
            float startWrap = layout.ToX(x);
            float endWrap = layout.ToX(x + width);
            UIHelper.DrawCustomText(text ?? string.Empty, size, GtaFont.ChaletLondon,
                color.R, color.G, color.B, color.A,
                startWrap, SettingsLayout.ToY(y),
                0, 0, 0, 0, 0, UIHelper.TextJustification.Left,
                true, startWrap, endWrap);
        }
    }
}
