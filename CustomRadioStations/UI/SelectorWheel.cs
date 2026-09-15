using GTA;
using GTA.Math;
using GTA.Native;

using GTAVFunctions;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

using Control = GTA.Control;
using CustomSprite = GTA.UI.CustomSprite;
using Font = GTA.UI.Font;
using UIScreen = GTA.UI.Screen;

namespace SelectorWheel {
    public delegate void CategoryChangeEvent(Wheel sender, WheelCategory selectedCategory, WheelCategoryItem selectedItem, bool wheelJustOpened);
    public delegate void ItemChangeEvent(Wheel sender, WheelCategory selectedCategory, WheelCategoryItem selectedItem, bool wheelJustOpened, GoTo direction);
    public delegate void WheelOpenEvent(Wheel sender, WheelCategory selectedCategory, WheelCategoryItem selectedItem);
    public delegate void WheelCloseEvent(Wheel sender, WheelCategory selectedCategory, WheelCategoryItem selectedItem);
    public delegate void WheelItemTrigger(Wheel sender, WheelCategory selectedCategory, WheelCategoryItem selectedItem);

    public enum GoTo {
        Prev,
        Next,
        Same
    }

    public class Wheel {
        public string WheelName { get; set; }
        private bool _visible;
        public int CurrentCatIndex = 0;
        public List<WheelCategory> Categories = new List<WheelCategory>();

        private Vector2 _origin = new Vector2(0.5f, 0.4f);
        private Vector2 inputCoord = Vector2.Zero;
        public float Radius = 250;
        private const float keyboardDeadzone = 0.03f;

        private bool UseTextures;
        private bool HaveTexturesBeenCached;
        private string TexturePath;
        private int TextureRefreshRate;
        private int xTextureOffset = 0;
        private int yTextureOffset = 0;

        private string TextureCatBgPath = ""; // Background path
        private Color TextureCatBgColor;
        private double TextureCatBgSizeMultiple;
        private string TextureCatHlPath = ""; // Highlight path
        private Color TextureCatBgHighlightColor;
        private double TextureCatBgHighlightSizeMultiple;

        public Func<Color> HighlightColorProvider { private get; set; }
        public Action<Wheel, WheelCategory, WheelCategoryItem, float> SelectedItemSupplementRenderer { private get; set; }

        private Size _textureSize;
        public Size TextureSize {
            get {
                return _textureSize;
            }
            set {
                // CustomSprite sizes are expressed in screen pixels. The old DrawTexture
                // renderer needed a fixed 16:9 width correction, but retaining it here
                // turns square station artwork into a wide rectangle (and gets worse on
                // ultrawide displays).
                _textureSize = value;
            }
        }

        private static bool transitionIn;
        private static bool transitionOut;

        private static float timeScale = 1f;

        /// <summary>
        /// https://pastebin.com/kVPwMemE
        /// </summary>
        public static string TimecycleModifier = "hud_def_desat_Neutral";
        public static float TimecycleModifierStrength = 1.0f;
        private static float timecycleCurrentStrength = 0f;

        private const string AUDIO_SOUNDSET = "HUD_FRONTEND_DEFAULT_SOUNDSET";
        private const string AUDIO_SELECTSOUND = "HIGHLIGHT_NAV_UP_DOWN";

        private const string QuickMutedAudioScene = "FADE_OUT_WORLD_250MS_SCENE";
        private const string MutedMuffledAudioScene = "DEATH_SCENE";

        /// <summary>
        /// Called when user hovers over a new category.
        /// </summary>
        public event CategoryChangeEvent OnCategoryChange;

        /// <summary>
        /// Called when user switches to a new item.
        /// </summary>
        public event ItemChangeEvent OnItemChange;

        /// <summary>
        /// Called when user opens the wheel.
        /// </summary>
        public event WheelOpenEvent OnWheelOpen;

        /// <summary>
        /// Called when user closes the wheel.
        /// </summary>
        public event WheelCloseEvent OnWheelClose;

        /// <summary>
        /// Called when the TriggerSelectedItem() method is called.
        /// An external class has to call it itself.
        /// </summary>
        public event WheelItemTrigger OnItemTrigger;

        /// <summary>
        /// Show/Hide the selection wheel.
        /// </summary>
        public bool Visible {
            get {
                return _visible;
            }
            set {
                //start and end screen effects, etc. before toggling.

                if (_visible == false && value == true) //When the wheel is just opened.
                {
                    Function.Call(Hash.SET_TIMECYCLE_MODIFIER, TimecycleModifier);
                    Function.Call(Hash.SET_TIMECYCLE_MODIFIER_STRENGTH, timecycleCurrentStrength);
                    transitionIn = true;
                    transitionOut = false;

                    CalculateCategoryPlacement();
                    UIHelper.UpdateAspectRatio();

                    CategoryChange(SelectedCategory, SelectedCategory.SelectedItem, true);
                    ItemChange(SelectedCategory, SelectedCategory.SelectedItem, true, GoTo.Same);
                    WheelOpen(SelectedCategory, SelectedCategory.SelectedItem);
                } else if (_visible == true && value == false) //When the wheel is just closed.
                  {
                    transitionIn = false;
                    transitionOut = true;

                    WheelClose(SelectedCategory, SelectedCategory.SelectedItem);
                    foreach (var cat in Categories) {
                        if (cat.CategoryTexture != null) {
                            cat.CategoryTexture.StopDraw();
                        }
                        if (cat.BackgroundTexture != null) {
                            cat.BackgroundTexture.StopDraw();
                        }
                        if (cat.HighlightTexture != null) {
                            cat.HighlightTexture.StopDraw();
                        }
                        if (cat.SelectedItem.ItemTexture != null) {
                            cat.SelectedItem.ItemTexture.StopDraw();
                        }
                    }
                }

                _visible = value;
            }
        }

        /// <summary>
        /// Instantiates a simple Selection Wheel that does not use textures. Just displays the category and item names.
        /// </summary>
        /// <param name="name">Name of the wheel. I was planning to display it while the wheel is shown but I didn't implement it yet.</param>
        /// <param name="wheelRadius">Length from the origin.</param>
        public Wheel(string name, float wheelRadius = 250) {
            WheelName = name;
            Radius = wheelRadius;
        }

        /// <summary>
        /// Instantiates a Selection Wheel that uses textures for categories or items, if they exist.
        /// </summary>
        /// <param name="name">Name of the wheel. I was planning to display it while the wheel is shown but I didn't implement it yet.</param>
        /// <param name="texturePath">Path where category and item .png files are kept. Ex: @"scripts\SelectorWheelExample\"</param>
        /// <param name="xtextureOffset">Simple X offset, usually set to 0.</param>
        /// <param name="ytextureOffset">Simple Y offset, usually set to 0.</param>
        /// <param name="textureSize">Size of images (in pixels).</param>
        /// <param name="textureRefreshRate">How long (in ms) each texture will be displayed for.</param>
        /// <param name="wheelRadius">Length from the origin.</param>
        public Wheel(string name, string texturePath, int xtextureOffset, int ytextureOffset, Size textureSize, int textureRefreshRate = 50, float wheelRadius = 250) {
            WheelName = name;
            UseTextures = true;
            TexturePath = texturePath;
            xTextureOffset = xtextureOffset;
            yTextureOffset = ytextureOffset;
            TextureSize = textureSize;
            TextureRefreshRate = textureRefreshRate;
            Radius = wheelRadius;
        }

        /// <summary>
        /// Must be placed in your Tick method.
        /// </summary>
        public void ProcessSelectorWheel() {
            if (!Visible)
                return;

            DisableControls();
            ControlCategorySelection();
            ControlItemSelection();
        }

        public static void ResetTransitions() {
            bool hadActiveTransition = transitionIn || transitionOut ||
                Math.Abs(timeScale - 1f) > 0.0001f || timecycleCurrentStrength > 0.0001f;

            transitionIn = false;
            transitionOut = false;
            timeScale = 1f;
            timecycleCurrentStrength = 0f;

            if (!hadActiveTransition)
                return;

            // Reload can discard every Wheel instance while a transition is active. Reset
            // the GTA-side state here as well so stale static transition flags cannot leave
            // slow-motion, timecycle, or muted audio scenes behind. Each operation is
            // best-effort because this method is also used during script abort.
            try { Game.TimeScale = 1f; } catch { }
            try { Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER); } catch { }
            try {
                if (Function.Call<bool>(Hash.IS_AUDIO_SCENE_ACTIVE, MutedMuffledAudioScene))
                    Function.Call(Hash.STOP_AUDIO_SCENE, MutedMuffledAudioScene);
                if (Function.Call<bool>(Hash.IS_AUDIO_SCENE_ACTIVE, QuickMutedAudioScene))
                    Function.Call(Hash.STOP_AUDIO_SCENE, QuickMutedAudioScene);
            } catch { }
        }

        public static void ControlTransitions(bool useSlowmotion) {
            if (transitionIn) {
                if (!Function.Call<bool>(Hash.IS_AUDIO_SCENE_ACTIVE, MutedMuffledAudioScene)) {
                    Function.Call(Hash.START_AUDIO_SCENE, QuickMutedAudioScene);
                    Function.Call(Hash.SET_AUDIO_SCENE_VARIABLE, QuickMutedAudioScene, "apply", 0.8f);
                    Function.Call(Hash.START_AUDIO_SCENE, MutedMuffledAudioScene);
                }

                float amount = Game.LastFrameTime * 8f;

                if (useSlowmotion) {
                    float tempTScale = DecreaseNum(timeScale, amount, 0.05f);
                    Game.TimeScale = tempTScale;
                    timeScale = tempTScale;
                } else {
                    timeScale = 1;
                    Game.TimeScale = 1;
                }

                float tempStrength = IncreaseNum(timecycleCurrentStrength, amount, TimecycleModifierStrength);
                Function.Call(Hash.SET_TIMECYCLE_MODIFIER_STRENGTH, tempStrength);
                timecycleCurrentStrength = tempStrength;
            }
            if (transitionOut) {
                if (Function.Call<bool>(Hash.IS_AUDIO_SCENE_ACTIVE, MutedMuffledAudioScene)) {
                    Function.Call(Hash.STOP_AUDIO_SCENE, MutedMuffledAudioScene);
                    Function.Call(Hash.STOP_AUDIO_SCENE, QuickMutedAudioScene);
                }

                float amount = Game.LastFrameTime * 8f;

                if (useSlowmotion) {
                    float tempTScale = IncreaseNum(timeScale, amount, 1f);
                    Game.TimeScale = tempTScale;
                    timeScale = tempTScale;
                } else {
                    timeScale = 1;
                    Game.TimeScale = 1;
                }

                float tempStrength = DecreaseNum(timecycleCurrentStrength, Game.LastFrameTime * 2f, 0f);
                Function.Call(Hash.SET_TIMECYCLE_MODIFIER_STRENGTH, tempStrength);
                timecycleCurrentStrength = tempStrength;
                if (timecycleCurrentStrength <= 0f && timeScale >= 1f) {
                    Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER);
                    transitionOut = false;
                }
            }

        }

        /// <summary>
        /// Call this after adding categories to position them around the wheel origin.
        /// </summary>
        public void CalculateCategoryPlacement() {
            // 0 degrees is middle-right and angles increase clockwise; 270 degrees points up.

            int radioOffIndex = Categories.FindIndex(category => category.IsRadioOff);
            if (radioOffIndex >= 0) {
                float angleOffset = 360f / Categories.Count;
                CalculateFromStartAngle(90f - (radioOffIndex * angleOffset), Categories.Count);
            } else {
                CalculateFromStartAngle(270f, Categories.Count);
            }

            if (!HaveTexturesBeenCached) {
                foreach (var cat in Categories) {
                    bool hasTexture = cat.CategoryTexture != null ||
                        cat.ItemList.Any(item => item.ItemTexture != null);
                    if (cat.CategoryTexture == null && File.Exists(Path.Combine(TexturePath, UIHelper.MakeValidFileName(cat.Name) + ".png"))) {
                        cat.CategoryTexture = new Texture(Path.Combine(TexturePath, UIHelper.MakeValidFileName(cat.Name) + ".png"), Categories.IndexOf(cat));
                        hasTexture = true;
                    }
                    foreach (var item in cat.ItemList) {
                        if (File.Exists(Path.Combine(TexturePath, UIHelper.MakeValidFileName(item.Name) + ".png"))) {
                            item.ItemTexture = new Texture(Path.Combine(TexturePath, UIHelper.MakeValidFileName(item.Name) + ".png"), Categories.IndexOf(cat));
                            hasTexture = true;
                        }
                    }

                    if (hasTexture && !string.IsNullOrWhiteSpace(TextureCatBgPath)) {
                        cat.BackgroundTexture = new Texture(TextureCatBgPath, Categories.IndexOf(cat) + Categories.Count);
                    }
                    if (!string.IsNullOrWhiteSpace(TextureCatHlPath)) {
                        cat.HighlightTexture = new Texture(TextureCatHlPath, Categories.IndexOf(cat) + (Categories.Count * 2));
                    }

                    // Load textures into cache.
                    if (cat.CategoryTexture != null) {
                        cat.CategoryTexture.LoadTexture();
                    }
                    if (cat.BackgroundTexture != null) {
                        cat.BackgroundTexture.LoadTexture();
                    }
                    if (cat.HighlightTexture != null) {
                        cat.HighlightTexture.LoadTexture();
                    }
                    foreach (var item in cat.ItemList) {
                        if (item.ItemTexture != null) {
                            item.ItemTexture.LoadTexture();
                        }
                    }
                }

                HaveTexturesBeenCached = true;
            }
        }

        private void CalculateFromStartAngle(float startAngle, int numCategories) {
            if (numCategories < 1)
                return;
            float angleOffset = 360 / numCategories;
            for (int i = 0; i < numCategories; i++) {
                Categories[i].position2D = PointOnCircleInPercentage(Radius, startAngle, OriginInPixels);
                startAngle += angleOffset;
            }
        }

        /// <summary>
        /// In screen percentage.
        /// X: 0.5f = 50% from the left.
        /// Y: 0.5f = 50% from the top.
        /// Set this before calling CalculateCategoryPlacement() or it won't apply.
        /// </summary>
        public Vector2 Origin {
            get {
                return _origin;
            }
            set {
                _origin = value;
            }
        }

        public Vector2 OriginInPixels {
            get {
                return new Vector2(UIHelper.XPercentageToPixel(_origin.X), UIHelper.YPercentageToPixel(_origin.Y));
            }
        }


        private float AddYPixelDistanceToPercent(float percent, int pixelDist) {
            return UIHelper.YPixelToPercentage
                (
                    (int)UIHelper.YPercentageToPixel(percent) + pixelDist
                );
        }

        /// <summary>
        /// Taken from https://stackoverflow.com/a/839904
        /// </summary>
        /// <param name="radius"></param>
        /// <param name="angleInDegrees"></param>
        /// <param name="origin"></param>
        /// <returns></returns>
        private static Vector2 PointOnCircleInPercentage(float radius, float angleInDegrees, Vector2 origin) {
            // Convert from degrees to radians via multiplication by PI/180
            double radians = angleInDegrees * Math.PI / 180F;
            float x = (float)(radius * Math.Cos(radians)) + origin.X;
            float y = (float)(radius * Math.Sin(radians)) + origin.Y;

            return new Vector2(UIHelper.XPixelToPercentage((int)x), UIHelper.YPixelToPercentage((int)y));
        }

        private static Size SizeMultiply(Size size, double factor) {
            return new Size((int)(size.Width * factor), (int)(size.Height * factor));
        }


        private static float IncreaseNum(float num, float increment, float max) {
            return num + increment > max ? max : num + increment;
        }

        private static float DecreaseNum(float num, float decrement, float min) {
            return num - decrement < min ? min : num - decrement;
        }

        /// <summary>
        /// Default is an empty string.
        /// Setting a proper path will show the targetted .png image behind each category icon.
        /// Call before <see cref="CalculateCategoryPlacement"/>
        /// </summary>
        /// <param name="pathBg"></param>
        public void SetCategoryBackgroundIcons(string pathBg, Color bgColor, double bgSizeMultiple, string pathHl, Color highlightColor, double hlSizeMultiple) {
            bool exists = File.Exists(pathBg);

            TextureCatBgPath = exists ? pathBg : "";
            TextureCatBgColor = bgColor;
            TextureCatBgSizeMultiple = bgSizeMultiple;

            exists = File.Exists(pathHl);
            TextureCatHlPath = exists ? pathHl : "";
            TextureCatBgHighlightColor = highlightColor;
            TextureCatBgHighlightSizeMultiple = hlSizeMultiple;
        }

        /// <summary>
        /// Add category to this wheel.
        /// </summary>
        /// <param name="category"></param>
        public void AddCategory(WheelCategory category) {
            Categories.Add(category);
        }

        public void ClearAllCategories() {
            Categories.Clear();
        }

        public void RemoveCategory(WheelCategory cat) {
            Categories.Remove(cat);
        }

        public bool IsCategorySelected(WheelCategory cat) {
            if (Categories.Contains(cat)) {
                return Categories.IndexOf(cat) == CurrentCatIndex;
            }
            return false;
        }

        public WheelCategory SelectedCategory {
            get {
                return Categories[CurrentCatIndex];
            }
            set {

                if (Categories.Exists(x => x.Equals(value))) {
                    CurrentCatIndex = Categories.FindIndex(x => x.Equals(value));
                    inputCoord = Categories[CurrentCatIndex].position2D;
                }
            }
        }

        public Font FontCategory = Font.ChaletComprimeCologne;
        public Font FontSelectedItem = Font.ChaletComprimeCologne;
        public Font FontCategoryItemCount = Font.ChaletComprimeCologne;
        public Font FontDescription = Font.ChaletLondon;
        private void ControlCategorySelection() {
            Color selectedHighlightColor = TextureCatBgHighlightColor;
            if (HighlightColorProvider != null) {
                try { selectedHighlightColor = HighlightColorProvider(); } catch { }
            }

            foreach (var cat in Categories) {
                bool isSelectedCategory = SelectedCategory == cat;

                bool catTextureExists = cat.CategoryTexture != null; //File.Exists(TexturePath + UIHelper.MakeValidFileName(cat.Name) + ".png");
                bool itemTextureExists = cat.SelectedItem.ItemTexture != null; //File.Exists(TexturePath + UIHelper.MakeValidFileName(cat.SelectedItem.Name) + ".png");
                bool bgTextureExists = cat.BackgroundTexture != null;
                bool hlTextureExists = cat.HighlightTexture != null;
                bool anyTextureExists = catTextureExists || itemTextureExists;

                if (UseTextures && anyTextureExists) {
                    // CustomSprite is composited in call order, so the neutral disc must
                    // be submitted before the artwork or it washes the icon grey.
                    if (bgTextureExists) {
                        cat.BackgroundTexture.Draw(2, TextureRefreshRate,
                            new Point((int)(cat.position2D.X * UIHelper.ScaledWidth) + xTextureOffset, (int)(cat.position2D.Y * UIScreen.Height) + yTextureOffset),
                            new PointF(0.5f, 0.5f),
                            SizeMultiply(TextureSize, TextureCatBgSizeMultiple),
                            0f, isSelectedCategory ? TextureCatBgColor : Color.FromArgb(120, TextureCatBgColor.R, TextureCatBgColor.G, TextureCatBgColor.B), UIHelper.AspectRatio);

                    }

                    Texture temp = catTextureExists ? cat.CategoryTexture : cat.SelectedItem.ItemTexture;
                    temp.Draw(3, TextureRefreshRate,
                        new Point((int)(cat.position2D.X * UIHelper.ScaledWidth) + xTextureOffset, (int)(cat.position2D.Y * UIScreen.Height) + yTextureOffset),
                        new PointF(0.5f, 0.5f),
                        isSelectedCategory && !bgTextureExists ? SizeMultiply(TextureSize, 1.25) : TextureSize,
                        0f, isSelectedCategory ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(120, 255, 255, 255), UIHelper.AspectRatio);
                } else {
                    Color col = isSelectedCategory ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(120, 255, 255, 255);
                    UIHelper.DrawCustomText(cat.Name, 0.8f, FontCategory, col.R, col.G, col.B, col.A, cat.position2D.X, cat.position2D.Y, 50, 0, 0, 0, 255, UIHelper.TextJustification.Center);
                }

                if (isSelectedCategory && hlTextureExists) {
                    cat.HighlightTexture.Draw(1, TextureRefreshRate,
                        new Point((int)(cat.position2D.X * UIHelper.ScaledWidth) + xTextureOffset, (int)(cat.position2D.Y * UIScreen.Height) + yTextureOffset),
                        new PointF(0.5f, 0.5f),
                        SizeMultiply(TextureSize, TextureCatBgHighlightSizeMultiple),
                        0f, selectedHighlightColor, UIHelper.AspectRatio);
                }

            }

            float selectedItemBottom = DrawSelectedItemName(SelectedCategory.SelectedItem.Name);
            if (SelectedItemSupplementRenderer != null) {
                try { SelectedItemSupplementRenderer(this, SelectedCategory, SelectedCategory.SelectedItem, selectedItemBottom); } catch { }
            }
            if (SelectedCategory.ItemCount() > 1) {
                UIHelper.DrawCustomText((SelectedCategory.CurrentItemIndex + 1).ToString() + " / " + SelectedCategory.ItemCount().ToString(), 0.55f, FontCategoryItemCount, 255, 255, 255, 255, _origin.X, AddYPixelDistanceToPercent(_origin.Y, -50), 50, 0, 0, 0, 255, UIHelper.TextJustification.Center);
            }

            string description = !string.IsNullOrWhiteSpace(SelectedCategory.SelectedItem.Description)
                ? SelectedCategory.SelectedItem.Description
                : SelectedCategory.Description;
            DrawDescription(description);

            CategorySelectionControls();
        }

        private float DrawSelectedItemName(string text) {
            const float fontSize = 0.45f;
            float startY = AddYPixelDistanceToPercent(_origin.Y, -50);
            if (string.IsNullOrEmpty(text))
                return startY;

            float x = _origin.X;
            float lineHeight = UIHelper.MeasureFontHeightNoConvert(fontSize, FontSelectedItem);
            if (lineHeight <= 0f || float.IsNaN(lineHeight) || float.IsInfinity(lineHeight))
                lineHeight = 0.03f;

            string[] lines = NormalizeItemTextLines(text);
            Color textColor = Color.FromArgb(255, 255, 255, 255);
            Color shadowColor = Color.FromArgb(255, 0, 0, 0);
            for (int index = 0; index < lines.Length; index++) {
                string line = lines[index];
                float y = startY + (index * lineHeight);
                if (!string.IsNullOrEmpty(line) && !CustomRadioStations.UnicodeTextRenderer.TryDraw(
                    line, fontSize, FontSelectedItem, textColor, shadowColor, x, y,
                    CustomRadioStations.UnicodeTextAlignment.Center)) {
                    UIHelper.DrawCustomText(line, fontSize, FontSelectedItem,
                        255, 255, 255, 255, x, y,
                        50, 0, 0, 0, 255, UIHelper.TextJustification.Center);
                }
            }

            int occupiedLines = lines.Length;
            while (occupiedLines > 1 && string.IsNullOrEmpty(lines[occupiedLines - 1]))
                occupiedLines--;
            return startY + (occupiedLines * lineHeight);
        }

        private static string[] NormalizeItemTextLines(string text) {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.None);
        }

        private void DrawDescription(string description) {
            if (string.IsNullOrWhiteSpace(description))
                return;

            const float fontSize = 0.35f;
            const float centerX = 0.5f;
            const float bottomEdge = 0.94f;

            // Cap the text block in virtual pixels as well as screen percentage. On a
            // 32:9 display this keeps the copy near the visual centre instead of
            // producing a several-thousand-pixel-wide line.
            float descriptionWidth = Math.Min(0.60f, UIHelper.XPixelToPercentage(1100));
            float startWrap = centerX - (descriptionWidth / 2f);
            float endWrap = centerX + (descriptionWidth / 2f);
            float fontHeight;
            int lineCount;
            CustomRadioStations.UnicodeTextLayout unicodeLayout;
            float unicodeWrapWidth = (endWrap - startWrap) * UIScreen.ScaledWidth;
            bool measureUnicodeLayout = CustomRadioStations.Config.UnicodeMode == CustomRadioStations.UnicodeTextMode.BitmapFallback ||
                (CustomRadioStations.Config.UnicodeMode == CustomRadioStations.UnicodeTextMode.Auto &&
                    CustomRadioStations.UnicodeTextSupport.RequiresFallback(description));
            if (measureUnicodeLayout && CustomRadioStations.UnicodeTextRenderer.TryGetLayout(
                description, fontSize, FontDescription, Color.White, Color.Transparent,
                CustomRadioStations.UnicodeTextAlignment.Center, unicodeWrapWidth, out unicodeLayout)) {
                fontHeight = unicodeLayout.LineHeightVirtual / CustomRadioStations.WheelDisplayMetrics.VirtualHeight;
                lineCount = Math.Max(1, unicodeLayout.LineCount);
            } else {
                fontHeight = UIHelper.MeasureFontHeightNoConvert(fontSize, FontDescription);
                lineCount = Math.Max(1, UIHelper.GetStringLineCount(
                    description, fontSize, FontDescription, startWrap, endWrap, centerX, bottomEdge));
            }
            float paddingY = UIHelper.YPixelToPercentage(10);
            float topPadding = UIHelper.YPixelToPercentage(10);
            float textY = Math.Max(0.72f, bottomEdge - (lineCount * fontHeight) - paddingY);

            UIHelper.DrawCustomText(description, fontSize, FontDescription,
                255, 255, 255, 255, centerX, textY,
                0, 0, 0, 0, 0,
                UIHelper.TextJustification.Center, true, startWrap, endWrap,
                true, 0, 0, 0, 180,
                UIHelper.XPixelToPercentage(10), paddingY + topPadding, 23.5f,
                -(topPadding / 2f));
        }

        private void CategorySelectionControls() {
            float horizontal = WheelLeftRightValue();
            float vertical = WheelUpDownValue();
            bool usingGamepad = Game.LastInputMethod == InputMethod.GamePad;
            float deadzone = usingGamepad
                ? CustomRadioStations.Config.GP_RadialDeadzone
                : keyboardDeadzone;
            float? activeInputAngle = null;

            if (new Vector2(horizontal, vertical).Length() > deadzone) {
                activeInputAngle = InputToAngle(horizontal, vertical);
                inputCoord = PointOnCircleInPercentage(Radius, activeInputAngle.Value, OriginInPixels);
            }

            WheelCategory closestCategory = ClosestCategoryToInputCoord();
            int inputIndex = closestCategory != null ? Categories.IndexOf(closestCategory) : CurrentCatIndex;

            if (usingGamepad && activeInputAngle.HasValue && inputIndex != CurrentCatIndex) {
                float currentCenterAngle = CategoryAngle(CurrentCatIndex);
                if (!CustomRadioStations.RadialSelectionHysteresis.ShouldSwitch(
                    currentCenterAngle, activeInputAngle.Value, Categories.Count,
                    CustomRadioStations.Config.GP_RadialHysteresisDegrees)) {
                    inputIndex = CurrentCatIndex;
                }
            }

            if (inputIndex != CurrentCatIndex) {
                // Stop the current category highlight texture before switching.
                if (!string.IsNullOrWhiteSpace(TextureCatHlPath)) {
                    WheelCategory temp = Categories[CurrentCatIndex];
                    if (temp.HighlightTexture != null)
                        temp.HighlightTexture.StopDraw();
                }
                CurrentCatIndex = inputIndex;
                CategoryChange(SelectedCategory, SelectedCategory.SelectedItem, false);
                ItemChange(SelectedCategory, SelectedCategory.SelectedItem, false, GoTo.Same);
                Audio.PlaySoundFrontendAndForget(AUDIO_SELECTSOUND, AUDIO_SOUNDSET);
            }
        }

        private float InputToAngle(float horizontal, float vertical) {
            double angle = Math.Atan2(vertical, horizontal);
            if (angle < 0) {
                angle += Math.PI * 2;
            }
            return (float)(angle * (180 / Math.PI));
        }

        private float CategoryAngle(int index) {
            if (index < 0 || index >= Categories.Count)
                return 0f;

            Vector2 origin = OriginInPixels;
            Vector2 category = Categories[index].position2D;
            float horizontal = UIHelper.XPercentageToPixel(category.X) - origin.X;
            float vertical = UIHelper.YPercentageToPixel(category.Y) - origin.Y;
            return InputToAngle(horizontal, vertical);
        }

        private static double GetDistance(Vector2 point1, Vector2 point2) {
            // Pythagorean theorem: c = sqrt(a² + b²).
            double a = (double)(point2.X - point1.X);
            double b = (double)(point2.Y - point1.Y);

            return Math.Sqrt(a * a + b * b);
        }

        private WheelCategory ClosestCategoryToInputCoord() {
            return Categories.OrderBy(c => GetDistance(c.position2D, inputCoord)).First();
        }

        public void TriggerSelectedItemEvent() {
            if (SelectedCategory == null || Categories.Count < 1 || SelectedCategory.SelectedItem == null)
                return;

            ItemTrigger(SelectedCategory, SelectedCategory.SelectedItem);
        }

        private void ControlItemSelection() {
            if (Control_GoToNextItemInCategory_Pressed()) {
                if (SelectedCategory.SelectedItem.ItemTexture != null && SelectedCategory.ItemCount() > 1) {
                    SelectedCategory.SelectedItem.ItemTexture.StopDraw();
                }
                SelectedCategory.GoToNextItem();
                ItemChange(SelectedCategory, SelectedCategory.SelectedItem, false, GoTo.Next);
                Audio.PlaySoundFrontendAndForget(AUDIO_SELECTSOUND, AUDIO_SOUNDSET);
            } else if (Control_GoToPreviousItemInCategory_Pressed()) {
                if (SelectedCategory.SelectedItem.ItemTexture != null && SelectedCategory.ItemCount() > 1) {
                    SelectedCategory.SelectedItem.ItemTexture.StopDraw();
                }
                SelectedCategory.GoToPreviousItem();
                ItemChange(SelectedCategory, SelectedCategory.SelectedItem, false, GoTo.Prev);
                Audio.PlaySoundFrontendAndForget(AUDIO_SELECTSOUND, AUDIO_SOUNDSET);
            }
        }

        private readonly List<Control> ControlsToEnable = new List<Control> {
            Control.MoveUpDown,
            Control.MoveLeftRight,
            Control.Sprint,
            // X/Y are reserved for track rating while the custom wheel is open.
            // Do not re-enable their gameplay aliases (Jump/Enter/VehicleExit), or the
            // same physical button press can leak through and make the player exit.
            Control.VehicleAccelerate,
            Control.VehicleBrake,
            Control.VehicleMoveLeftRight,
            Control.VehicleFlyYawLeft,
            Control.FlyLeftRight,
            Control.FlyUpDown,
            Control.VehicleFlyYawRight,
            Control.VehicleHandbrake,
            Control.WeaponWheelLeftRight,
            Control.WeaponWheelUpDown
        };

        protected void DisableControls() {
            // Group 2 is the frontend/wheel control set; group 0 is normal player input.
            // Disable both and restore only the wheel's movement/driving whitelist. This
            // suppresses every gameplay alias of the physical X/Y buttons rather than
            // only Jump/Enter/VehicleExit, while disabled Frontend X/Y remain readable
            // by the rating overlay through IS_DISABLED_CONTROL_* natives.
            ControlInput.DisableAllThisFrame();
            ControlInput.DisableAllPlayerThisFrame();

            foreach (Control control in ControlsToEnable) {
                ControlInput.EnableThisFrame(control);
                ControlInput.EnablePlayerThisFrame(control);
            }
        }

        /// <summary>
        /// Right: positive 1
        /// Left: negative 1
        /// </summary>
        /// <returns>normalized value of left/right mouse/stick movement.</returns>
        private float WheelLeftRightValue() {
            return ControlInput.GetValueNormalized(Control.WeaponWheelLeftRight);
        }

        /// <summary>
        /// Down: positive 1
        /// Up: negative 1
        /// </summary>
        /// <returns>normalized value of up/down mouse/stick movement.</returns>
        private float WheelUpDownValue() {
            return ControlInput.GetValueNormalized(Control.WeaponWheelUpDown);
        }

        private bool Control_GoToNextItemInCategory_Pressed() {
            return ControlInput.IsJustPressed(Game.LastInputMethod == InputMethod.MouseAndKeyboard ?
                Control.WeaponWheelPrev : Control.VehicleAccelerate);
        }

        private bool Control_GoToPreviousItemInCategory_Pressed() {
            return ControlInput.IsJustPressed(Game.LastInputMethod == InputMethod.MouseAndKeyboard ?
                Control.WeaponWheelNext : Control.VehicleBrake);
        }

        protected virtual void CategoryChange(WheelCategory selectedCategory, WheelCategoryItem selecteditem, bool wheelJustOpened) {
            OnCategoryChange?.Invoke(this, selectedCategory, selecteditem, wheelJustOpened);
        }

        protected virtual void ItemChange(WheelCategory selectedCategory, WheelCategoryItem selecteditem, bool wheelJustOpened, GoTo direction) {
            OnItemChange?.Invoke(this, selectedCategory, selecteditem, wheelJustOpened, direction);
        }

        protected virtual void WheelOpen(WheelCategory selectedCategory, WheelCategoryItem selecteditem) {
            OnWheelOpen?.Invoke(this, selectedCategory, selecteditem);
        }

        protected virtual void WheelClose(WheelCategory selectedCategory, WheelCategoryItem selecteditem) {
            OnWheelClose?.Invoke(this, selectedCategory, selecteditem);
        }

        protected virtual void ItemTrigger(WheelCategory selectedCategory, WheelCategoryItem selecteditem) {
            OnItemTrigger?.Invoke(this, selectedCategory, selecteditem);
        }

        public void UnsubscribeAllEvents() {
            OnCategoryChange = null;
            OnItemChange = null;
            OnWheelOpen = null;
            OnWheelClose = null;
        }
    }

    public class WheelCategory {
        public string Name;
        public int CurrentItemIndex = 0;
        protected List<WheelCategoryItem> Items = new List<WheelCategoryItem>();
        public Vector2 position2D = Vector2.Zero;
        public Texture CategoryTexture;
        public Texture BackgroundTexture;
        public Texture HighlightTexture;
        public string Description { get; set; }
        public bool IsRadioOff { get; set; }

        /// <summary>
        /// Instantiates a new category for use in a selection wheel.
        /// </summary>
        /// <param name="name">Name of the category. If a matching .png image is found, the image will be displayed instead of any item image.</param>
        public WheelCategory(string name) {
            Name = name;
        }

        /// <summary>
        /// Instantiates a new category for use in a selection wheel.
        /// </summary>
        /// <param name="name">Name of the category. If a matching .png image is found, the image will be displayed instead of any item image.</param>
        /// <param name="description">Category description. Only shown if there is no selected item description.</param>
        public WheelCategory(string name, string description) {
            Name = name;
            Description = description;
        }

        /// <summary>
        /// Add item to this category.
        /// </summary>
        /// <param name="item">Item to add to this category</param>
        public void AddItem(WheelCategoryItem item) {
            Items.Add(item);
        }

        public void ClearAllItems() {
            Items.Clear();
        }

        public void RemoveItem(WheelCategoryItem item) {
            Items.Remove(item);
        }

        public int ItemCount() {
            return Items.Count;
        }

        public List<WheelCategoryItem> ItemList {
            get {
                return Items;
            }
        }

        public bool IsItemSelected(WheelCategoryItem item) {
            if (Items.Contains(item)) {
                return Items.IndexOf(item) == CurrentItemIndex;
            }
            return false;
        }

        public WheelCategoryItem SelectedItem {
            get {
                return Items.ElementAt(CurrentItemIndex);
            }
        }

        public void GoToNextItem() {
            if (CurrentItemIndex < Items.Count - 1) {
                CurrentItemIndex++;
            } else {
                CurrentItemIndex = 0;
            }
        }

        public void GoToPreviousItem() {
            if (CurrentItemIndex > 0) {
                CurrentItemIndex--;
            } else {
                CurrentItemIndex = Items.Count - 1;
            }
        }
    }

    public class WheelCategoryItem {
        public string Name;
        public Texture ItemTexture;
        public string Description { get; set; }

        /// <summary>
        /// Instantiate a new item to be later added to a WheelCategory.
        /// </summary>
        /// <param name="name">Name of the item. If a matching .png image is found, the image will be displayed assuming no image for this item's category has been found.</param>
        public WheelCategoryItem(string name) {
            Name = name;
        }

        /// <summary>
        /// Instantiate a new item to be later added to a WheelCategory.
        /// </summary>
        /// <param name="name">Name of the item. If a matching .png image is found, the image will be displayed assuming no image for this item's category has been found.</param>
        /// <param name="description">A description that will be displayed at the bottom-center of the screen.</param>
        public WheelCategoryItem(string name, string description) {
            Name = name;
            Description = description;
        }
    }

    public class Texture {
        public string Path { get; set; }
        public int Index { get; set; }
        public int DrawLevel { get; set; }

        private CustomSprite _sprite;
        private bool _validationAttempted;
        private bool _failed;

        public Texture(string path, int index) {
            // SHVDN Enhanced's CustomSprite passes this string directly to the native
            // DirectX texture loader. Relative PNG paths can be interpreted as texture
            // directories there (and acquire a trailing slash), so resolve them while
            // still in managed code. Station icons were already absolute; bundled wheel
            // assets such as selection-ring.png exposed this difference.
            try {
                Path = System.IO.Path.GetFullPath(path);
            } catch { Path = path; }
            Index = index;
        }

        public void Draw(int level, int time, Point pos, Size size) {
            Draw(level, time, pos, new PointF(0.5f, 0.5f), size, 0f, Color.White, UIHelper.AspectRatio);
        }

        public void Draw(int level, int time, Point pos, Size size, float rotation, Color color) {
            Draw(level, time, pos, new PointF(0.5f, 0.5f), size, rotation, color, UIHelper.AspectRatio);
        }

        public void Draw(int level, int time, Point pos, PointF center, Size size, float rotation, Color color) {
            Draw(level, time, pos, center, size, rotation, color, UIHelper.AspectRatio);
        }

        public void Draw(int level, int time, Point pos, PointF center, Size size, float rotation, Color color, float aspectRatio) {
            if (!CanUseTexture())
                return;

            // SHVDN3 replaces GTA.UI.DrawTexture with GTA.UI.CustomSprite.
            // The live wheel rendering uses a 0.5/0.5 center, which maps directly to Centered=true.
            bool centered = Math.Abs(center.X - 0.5f) < 0.0001f && Math.Abs(center.Y - 0.5f) < 0.0001f;
            PointF position = new PointF(pos.X, pos.Y);

            if (!centered) {
                // Preserve the old DrawTexture anchor semantics for any future caller that
                // supplies a non-central anchor. CustomSprite uses either top-left or center.
                position.X -= size.Width * center.X;
                position.Y -= size.Height * center.Y;
            }

            try {
                if (_sprite == null) {
                    _sprite = new CustomSprite(Path, new SizeF(size.Width, size.Height), position, color, rotation, centered);
                } else {
                    _sprite.Position = position;
                    _sprite.Size = new SizeF(size.Width, size.Height);
                    _sprite.Color = color;
                    _sprite.Rotation = rotation;
                    _sprite.Centered = centered;
                    _sprite.Enabled = true;
                }

                // A 720-high virtual canvas scales with vertical resolution and keeps
                // the same physical proportions at 4:3, 21:9, and 32:9.
                _sprite.ScaledDraw();
            } catch (Exception ex) {
                FailTexture("draw", ex);
            }
        }

        public void LoadTexture() {
            if (_sprite != null || !CanUseTexture())
                return;

            try {
                _sprite = new CustomSprite(Path, SizeF.Empty, PointF.Empty, Color.White, 0f, false);
                _sprite.Enabled = false;
            } catch (Exception ex) {
                FailTexture("creation", ex);
            }
        }

        public void StopDraw() {
            if (_sprite == null)
                return;
            try {
                _sprite.Enabled = false;
            } catch (Exception ex) { FailTexture("disable", ex); }
        }

        private bool CanUseTexture() {
            if (_failed)
                return false;
            if (_validationAttempted)
                return true;
            _validationAttempted = true;

            try {
                string validationError;
                if (!CustomRadioStations.TextureFileValidator.TryValidatePng(Path, out validationError))
                    throw new InvalidDataException(validationError);

                return true;
            } catch (Exception ex) {
                FailTexture("validation", ex);
                return false;
            }
        }

        private void FailTexture(string operation, Exception exception) {
            _failed = true;
            try {
                if (_sprite != null)
                    _sprite.Enabled = false;
            } catch { }
            _sprite = null;

            try {
                CustomRadioStations.Logger.Log("WARNING: Disabled texture '" + Path +
                    "' after DirectX " + operation + " failed: " + exception.Message);
            } catch { }
        }
    }

    public static class UIHelper {
        public enum TextJustification {
            Center = 0,
            Left,
            Right //requires SET_TEXT_WRAP
        }

        public static void DrawCustomText(string Message, float FontSize, Font FontType,
            int Red, int Green, int Blue, int Alpha, float XPos, float YPos,
            int dropShawdowPixelDistance, int dRed, int dGreen, int dBlue, int dAlpha,
            TextJustification justifyType = TextJustification.Left, bool ForceTextWrap = false, float startWrap = 0f, float endWrap = 1f,
            bool withRectangle = false, int R = 0, int G = 0, int B = 0, int A = 255,
            float rectWidthOffset = 0f, float rectHeightOffset = 0f, float rectYPosDivisor = 23.5f,
            float rectYOffset = 0f) {
            CustomRadioStations.UnicodeTextAlignment unicodeAlignment = justifyType == TextJustification.Center
                ? CustomRadioStations.UnicodeTextAlignment.Center
                : (justifyType == TextJustification.Right
                    ? CustomRadioStations.UnicodeTextAlignment.Right
                    : CustomRadioStations.UnicodeTextAlignment.Left);
            Color textColor = Color.FromArgb(Alpha, Red, Green, Blue);
            Color shadowColor = dropShawdowPixelDistance > 0
                ? Color.FromArgb(dAlpha, dRed, dGreen, dBlue)
                : Color.FromArgb(0, dRed, dGreen, dBlue);
            float unicodeWrapWidth = ForceTextWrap
                ? Math.Max(0f, (endWrap - startWrap) * UIScreen.ScaledWidth)
                : 0f;
            CustomRadioStations.UnicodeTextLayout unicodeLayout;
            bool unicodeDrawn = CustomRadioStations.UnicodeTextRenderer.TryDrawWrapped(
                Message, FontSize, FontType, textColor, shadowColor, XPos, YPos, unicodeAlignment, unicodeWrapWidth, out unicodeLayout);

            if (!unicodeDrawn) {
                DrawNativeText(Message, FontSize, FontType, Red, Green, Blue, Alpha, XPos, YPos,
                    dropShawdowPixelDistance, dRed, dGreen, dBlue, dAlpha, justifyType, ForceTextWrap, startWrap, endWrap);
            }

            if (unicodeDrawn && !withRectangle)
                return;

            if (withRectangle) {
                switch (FontType) {
                case Font.ChaletLondon:
                    rectYPosDivisor = 15f;
                    break;
                case Font.HouseScript:
                    rectYPosDivisor = 23f;
                    break;
                case Font.RockstarTag:
                    rectYPosDivisor = 25f;
                    break;
                case Font.ChaletComprimeCologne:
                    rectYPosDivisor = 30f;
                    break;
                case Font.Pricedown:
                    rectYPosDivisor = 35f;
                    break;
                }

                float fontHeight = unicodeDrawn
                    ? unicodeLayout.LineHeightVirtual / CustomRadioStations.WheelDisplayMetrics.VirtualHeight
                    : MeasureFontHeightNoConvert(FontSize, FontType);
                float rectangleWidth = (endWrap - startWrap) + rectWidthOffset;
                float baseYPos = YPos + (FontSize / rectYPosDivisor);
                int numLines = unicodeDrawn
                    ? Math.Max(1, unicodeLayout.LineCount)
                    : GetStringLineCount(Message, FontSize, FontType, startWrap, endWrap, XPos, YPos);
                for (int i = 0; i < numLines; i++) {
                    float adjustedYPos = i == 0 ? baseYPos - rectHeightOffset / 2
                        : (i == numLines - 1 ? baseYPos + rectHeightOffset / 2
                        : baseYPos);

                    float adjustedRectangleHeight = i == 0 || i == numLines - 1 ? fontHeight + rectHeightOffset
                        : fontHeight;

                    float adjustedXPos = justifyType == TextJustification.Left ? XPos + ((endWrap - startWrap) / 2)
                        : (justifyType == TextJustification.Right ? endWrap - ((endWrap - startWrap) / 2)
                        : XPos);

                    DrawRectangle(adjustedXPos, adjustedYPos + (i * fontHeight) + rectYOffset, rectangleWidth, adjustedRectangleHeight, R, G, B, A);
                }
            }
        }

        public static void DrawNativeText(string Message, float FontSize, Font FontType,
            int Red, int Green, int Blue, int Alpha, float XPos, float YPos,
            int dropShawdowPixelDistance, int dRed, int dGreen, int dBlue, int dAlpha,
            TextJustification justifyType = TextJustification.Left, bool ForceTextWrap = false,
            float startWrap = 0f, float endWrap = 1f) {
            Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "jamyfafi"); //Required, don't change this! AKA BEGIN_TEXT_COMMAND_DISPLAY_TEXT
            Function.Call(Hash.SET_TEXT_SCALE, FontSize, FontSize); //1st param: 1.0f
            Function.Call(Hash.SET_TEXT_FONT, (int)FontType);
            Function.Call(Hash.SET_TEXT_COLOUR, Red, Green, Blue, Alpha);
            Function.Call((Hash)0x465C84BC39F1C351, dropShawdowPixelDistance, dRed, dGreen, dBlue, dAlpha); // SET_TEXT_DROPSHADOW
            Function.Call(Hash.SET_TEXT_OUTLINE);
            Function.Call(Hash.SET_TEXT_JUSTIFICATION, (int)justifyType);
            if (justifyType == TextJustification.Right || ForceTextWrap)
                Function.Call(Hash.SET_TEXT_WRAP, startWrap, endWrap);

            AddLongString(Message);
            Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, XPos, YPos); //AKA END_TEXT_COMMAND_DISPLAY_TEXT
        }

        public static void DrawRectangle(float BgXpos, float BgYpos, float BgWidth, float BgHeight, int bgR, int bgG, int bgB, int bgA) {
            Function.Call(Hash.DRAW_RECT, BgXpos, BgYpos, BgWidth, BgHeight, bgR, bgG, bgB, bgA);
        }

        public static void AddLongString(string str) {
            const int strLen = 99;
            for (int i = 0; i < str.Length; i += strLen) {
                string substr = str.Substring(i, Math.Min(strLen, str.Length - i));
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, substr); //ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME
            }
        }

        public static float MeasureStringWidth(string str, Font font, float fontsize) {
            const float height = 1080f;
            float ratio = (float)UIScreen.Resolution.Width / UIScreen.Resolution.Height;
            float width = height * ratio;
            return MeasureStringWidthNoConvert(str, font, fontsize) * width;
        }

        private static float MeasureStringWidthNoConvert(string str, Font font, float fontsize) {
            Function.Call((Hash)0x54CE8AC98E120CAB, "jamyfafi"); //_BEGIN_TEXT_COMMAND_WIDTH
            AddLongString(str);
            Function.Call(Hash.SET_TEXT_FONT, (int)font);
            Function.Call(Hash.SET_TEXT_SCALE, fontsize, fontsize);
            return Function.Call<float>((Hash)0x85F061DA64ED2F67, true); //_END_TEXT_COMMAND_GET_WIDTH
        }

        public static float MeasureFontHeight(float fontSize, Font font) {
            return Function.Call<float>((Hash)0xDB88A37483346780, fontSize, (int)font) * UIScreen.Resolution.Height; //1080f
        }

        public static float MeasureFontHeightNoConvert(float fontSize, Font font) {
            return Function.Call<float>((Hash)0xDB88A37483346780, fontSize, (int)font);
        }

        public static int GetStringLineCount(string text, float FontSize, Font FontType, float startWrap, float endWrap, float x, float y) {
            Function.Call((Hash)0x521FB041D93DD0E4, "jamyfafi"); //_BEGIN_TEXT_COMMAND_LINE_COUNT
            Function.Call(Hash.SET_TEXT_SCALE, FontSize, FontSize); //1st param: 1.0f
            Function.Call(Hash.SET_TEXT_FONT, (int)FontType);
            Function.Call(Hash.SET_TEXT_WRAP, startWrap, endWrap);
            AddLongString(text);
            return Function.Call<int>((Hash)0x9040DFB09BE75706, x, y); //_END_TEXT_COMMAND_GET_LINE_COUNT
        }

        public static float XPixelToPercentage(int pixel) {
            const float height = 1080f;
            float ratio = (float)UIScreen.Resolution.Width / UIScreen.Resolution.Height;
            float width = height * ratio;

            return pixel / width;
        }

        public static float YPixelToPercentage(int pixel) {
            const float height = 1080f;
            float ratio = (float)UIScreen.Resolution.Width / UIScreen.Resolution.Height;
            float width = height * ratio;

            return pixel / height;
        }

        public static float XPercentageToPixel(float percent) {
            const float height = 1080f;
            float ratio = (float)UIScreen.Resolution.Width / UIScreen.Resolution.Height;
            float width = height * ratio;

            return percent * width;
        }

        public static float YPercentageToPixel(float percent) {
            const float height = 1080f;
            float ratio = (float)UIScreen.Resolution.Width / UIScreen.Resolution.Height;
            float width = height * ratio;

            return percent * height;
        }

        public static string MakeValidFileName(string original, char replacementChar = '_') {
            var invalidChars = new HashSet<char>(System.IO.Path.GetInvalidFileNameChars());
            return new string(original.Select(c => invalidChars.Contains(c) ? replacementChar : c).ToArray());
        }

        public static float AspectRatio { get; private set; } = UIScreen.PhysicalAspectRatio;

        public static float ScaledWidth {
            get {
                Size resolution = UIScreen.Resolution;
                return CustomRadioStations.WheelDisplayMetrics.GetVirtualWidth(resolution.Width, resolution.Height);
            }
        }

        public static float UpdateAspectRatio() {
            AspectRatio = UIScreen.PhysicalAspectRatio;
            return AspectRatio;
        }

        public static PointF PointFromCenter(float x, float y, float normalizedAngle) {
            // Credits to MaxShadow for this method
            float angle2 = (normalizedAngle * (float)Math.PI * 2) - ((float)Math.PI / 2);
            float x2 = (UIScreen.Width / 2) + (float)Math.Cos(angle2) * x;
            float y2 = (UIScreen.Height / 2) + (float)Math.Sin(angle2) * y * (AspectRatio / (16f / 9f));
            return new PointF(x2, y2);
        }
    }
}
