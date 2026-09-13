using GTA.UI;

using SelectorWheel;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using DrawingFont = System.Drawing.Font;
using GtaFont = GTA.UI.Font;

namespace CustomRadioStations {
    /// <summary>
    /// Renders text which GTA's active font library cannot display into transparent
    /// PNG textures, then draws those textures through SHVDN's CustomSprite wrapper.
    /// Native-safe text never enters this renderer in Auto mode.
    /// </summary>
    internal static class UnicodeTextRenderer {
        // Bump when bitmap-generation semantics change so ScriptHookV never reuses a stale texture path.
        private const string RendererVersion = "7";
        private const int MaxManagedEntries = 128;
        private const int MaxFailedKeys = 256;
        private const int MaxDiskEntries = 512;
        private const int MaxDiskAgeDays = 30;
        // Prevent pathological metadata from allocating enormous 32-bpp bitmaps before
        // the DirectX dimension guard is reached. 16M pixels is 64 MiB of raw ARGB data.
        private const long MaxBitmapPixels = 16L * 1024L * 1024L;
        private const float VirtualHeight = 720f;

        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<RenderKey, CacheEntry> Cache = new Dictionary<RenderKey, CacheEntry>();
        private static readonly HashSet<RenderKey> FailedKeys = new HashSet<RenderKey>();
        private static readonly Queue<RenderKey> FailedKeyOrder = new Queue<RenderKey>();
        private static readonly string[] BundledFontCandidates = {
            "NotoSansCJKjp-Regular.ttf",
            "NotoSansCJKjp-VF.ttf",
            "NotoSansJP-Regular.ttf",
            "NotoSansJP-VF.ttf",
            "NotoSansCJKjp-Regular.otf",
            "NotoSansJP-Regular.otf"
        };
        private static readonly string[] SystemFontCandidates = {
            "Noto Sans CJK JP",
            "Noto Sans JP",
            "Yu Gothic UI",
            "Yu Gothic",
            "Meiryo UI",
            "Meiryo",
            "Microsoft YaHei UI",
            "Microsoft JhengHei UI",
            "Malgun Gothic",
            "MS Gothic",
            "MS PGothic",
            "Segoe UI",
            "Arial"
        };

        private static PrivateFontCollection privateFonts;
        private static FontFamily fontFamily;
        private static string fontIdentity;
        private static bool initializationAttempted;
        private static bool initializationFailed;
        private static long accessCounter;

        internal static bool TryDraw(string text, float fontSize, GtaFont gtaFont, Color color, Color shadowColor,
            float xPosition, float yPosition, UnicodeTextAlignment alignment) {
            UnicodeTextLayout ignored;
            return TryDrawCore(text, fontSize, gtaFont, color, shadowColor, xPosition, yPosition, alignment, 0f, true, out ignored);
        }

        internal static bool TryDrawWrapped(string text, float fontSize, GtaFont gtaFont, Color color, Color shadowColor,
            float xPosition, float yPosition, UnicodeTextAlignment alignment, float maxWidthVirtual, out UnicodeTextLayout layout) {
            return TryDrawCore(text, fontSize, gtaFont, color, shadowColor, xPosition, yPosition, alignment,
                NormalizeWrapWidth(maxWidthVirtual), true, out layout);
        }

        internal static bool TryGetLayout(string text, float fontSize, GtaFont gtaFont, Color color, Color shadowColor,
            UnicodeTextAlignment alignment, float maxWidthVirtual, out UnicodeTextLayout layout) {
            return TryDrawCore(text, fontSize, gtaFont, color, shadowColor, 0f, 0f, alignment,
                NormalizeWrapWidth(maxWidthVirtual), false, out layout);
        }

        private static bool TryDrawCore(string text, float fontSize, GtaFont gtaFont, Color color, Color shadowColor,
            float xPosition, float yPosition, UnicodeTextAlignment alignment, float maxWidthVirtual, bool draw,
            out UnicodeTextLayout layout) {
            layout = default(UnicodeTextLayout);
            if (string.IsNullOrEmpty(text) || !ShouldUseBitmap(text))
                return false;

            lock (SyncRoot) {
                RenderKey key = default(RenderKey);
                bool hasKey = false;
                try {
                    if (!EnsureInitialized())
                        return false;

                    int outputHeight = Math.Max(1, Screen.Resolution.Height);
                    float renderScale = WheelDisplayMetrics.GetTextRenderScale(outputHeight);
                    key = new RenderKey(text, fontSize, gtaFont, color, shadowColor, alignment, renderScale, fontIdentity, maxWidthVirtual);
                    hasKey = true;
                    if (FailedKeys.Contains(key))
                        return false;

                    CacheEntry entry;
                    if (!Cache.TryGetValue(key, out entry)) {
                        entry = CreateCacheEntry(key);
                        Cache.Add(key, entry);
                        TrimManagedCache(key);
                    }

                    entry.LastAccess = ++accessCounter;
                    layout = new UnicodeTextLayout(entry.LineCount, entry.LayoutWidth, entry.LayoutHeight);
                    if (draw)
                        DrawEntry(entry, xPosition, yPosition, alignment);
                    return true;
                } catch (Exception ex) {
                    if (hasKey)
                        RememberFailedKey(key);
                    Logger.Log("WARNING: Unicode text rendering failed; using GTA native text instead. " + ex.Message);
                    return false;
                }
            }
        }

        private static float NormalizeWrapWidth(float maxWidthVirtual) {
            if (!IsFinite(maxWidthVirtual) || maxWidthVirtual <= 0f)
                return 0f;

            // ScriptHookV cannot release individual custom textures during a session.
            // Bucket wrapped widths to four virtual pixels so dragging a window through
            // many aspect ratios cannot create a texture for every fractional width. Round
            // down so the resulting line never exceeds the caller's requested wrap box.
            if (maxWidthVirtual < 4f)
                return maxWidthVirtual;
            return (float)(Math.Floor(maxWidthVirtual / 4f) * 4f);
        }

        internal static void NotifyConfigurationChanged() {
            lock (SyncRoot) {
                DisableAllSprites();
                Cache.Clear();
                FailedKeys.Clear();
                FailedKeyOrder.Clear();
                DisposeFontCollection();
                initializationAttempted = false;
                initializationFailed = false;
                fontIdentity = null;
                accessCounter = 0;
            }
        }

        internal static void Shutdown() {
            NotifyConfigurationChanged();
        }

        private static bool ShouldUseBitmap(string text) {
            switch (Config.UnicodeMode) {
            case UnicodeTextMode.NativeOnly:
                return false;
            case UnicodeTextMode.BitmapFallback:
                return true;
            default:
                return UnicodeTextSupport.RequiresFallback(text);
            }
        }

        private static bool EnsureInitialized() {
            if (initializationAttempted)
                return !initializationFailed;

            initializationAttempted = true;
            try {
                Directory.CreateDirectory(AppPaths.UnicodeTextCacheDirectory);
                PruneDiskCache();

                if (TryLoadConfiguredOrBundledFont())
                    return true;
                if (TryLoadSystemFont())
                    return true;

                initializationFailed = true;
                Logger.Log("WARNING: Unicode text fallback could not find a usable font. " +
                    "Add a TrueType/OpenType font under '" + AppPaths.UnicodeFontsDirectory +
                    "' or set graphics.unicodeFont. GTA native text will be used instead.");
                return false;
            } catch (Exception ex) {
                initializationFailed = true;
                Logger.Log("WARNING: Unicode text fallback initialization failed; GTA native text will be used instead. " + ex.Message);
                return false;
            }
        }

        private static bool TryLoadConfiguredOrBundledFont() {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(Config.UnicodeFont))
                candidates.Add(ResolveConfiguredFontPath(Config.UnicodeFont));

            foreach (string fileName in BundledFontCandidates)
                candidates.Add(Path.Combine(AppPaths.UnicodeFontsDirectory, fileName));

            foreach (string candidate in candidates.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase)) {
                if (!File.Exists(candidate))
                    continue;

                PrivateFontCollection collection = null;
                FontFamily selectedFamily = null;
                try {
                    collection = new PrivateFontCollection();
                    string fullPath = Path.GetFullPath(candidate);
                    collection.AddFontFile(fullPath);

                    FontFamily[] families = collection.Families;
                    foreach (FontFamily family in families) {
                        string validationError = null;
                        if (selectedFamily == null && TryValidateFontFamily(family, out validationError)) {
                            selectedFamily = family;
                            continue;
                        }

                        if (selectedFamily == null) {
                            Logger.Log("WARNING: Unicode font family '" + family.Name + "' from '" + fullPath +
                                "' is not usable by the GDI+ outline renderer: " + validationError);
                        }
                        family.Dispose();
                    }

                    if (selectedFamily == null)
                        continue;

                    var info = new FileInfo(fullPath);
                    string identity = "file|" + info.FullName + "|" + info.Length.ToString(CultureInfo.InvariantCulture) + "|" +
                        info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + selectedFamily.Name;

                    DisposeFontCollection();
                    privateFonts = collection;
                    collection = null;
                    fontFamily = selectedFamily;
                    selectedFamily = null;
                    fontIdentity = identity;
                    Logger.Log("Unicode text fallback loaded private font: " + info.FullName + " (" + fontFamily.Name + ")");
                    return true;
                } catch (Exception ex) {
                    Logger.Log("WARNING: Could not load Unicode font '" + candidate + "': " + ex.Message);
                } finally {
                    if (selectedFamily != null)
                        selectedFamily.Dispose();
                    if (collection != null)
                        collection.Dispose();
                }
            }

            return false;
        }

        private static bool TryLoadSystemFont() {
            foreach (string familyName in SystemFontCandidates) {
                FontFamily candidateFamily = null;
                try {
                    using (var testFont = new DrawingFont(familyName, 12f, FontStyle.Regular, GraphicsUnit.Pixel)) {
                        if (!string.Equals(testFont.FontFamily.Name, familyName, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    candidateFamily = new FontFamily(familyName);
                    string validationError;
                    if (!TryValidateFontFamily(candidateFamily, out validationError)) {
                        candidateFamily.Dispose();
                        candidateFamily = null;
                        Logger.Log("WARNING: Installed Unicode font family '" + familyName +
                            "' is not usable by the GDI+ outline renderer: " + validationError);
                        continue;
                    }

                    DisposeFontCollection();
                    fontFamily = candidateFamily;
                    candidateFamily = null;
                    fontIdentity = "system|" + fontFamily.Name;
                    Logger.Log("Unicode text fallback is using installed Windows font: " + fontFamily.Name +
                        ". A bundled font under '" + AppPaths.UnicodeFontsDirectory + "' is preferred for consistent coverage.");
                    return true;
                } catch {
                    if (candidateFamily != null)
                        candidateFamily.Dispose();
                }
            }

            return false;
        }

        private static bool TryValidateFontFamily(FontFamily family, out string error) {
            error = null;
            if (family == null) {
                error = "No font family was loaded.";
                return false;
            }

            try {
                FontStyle style = GetAvailableFontStyle(family);
                int emHeight = family.GetEmHeight(style);
                int lineSpacing = family.GetLineSpacing(style);
                if (emHeight <= 0 || lineSpacing <= 0) {
                    error = "The font reported invalid em/line-spacing metrics.";
                    return false;
                }

                // PrivateFontCollection may accept an OpenType file which GDI+ cannot
                // later convert to a GraphicsPath (for example CFF-only outlines). Prove
                // that the exact operation used by the renderer works before accepting it.
                using (var probeFont = new DrawingFont(family, 16f, style, GraphicsUnit.Pixel))
                using (StringFormat format = CreateStringFormat())
                using (var path = new GraphicsPath()) {
                    // The normal renderer intentionally allows fallback, but the probe must
                    // prove this family itself is outline-capable rather than succeeding only
                    // because Windows silently substituted another installed font.
                    format.FormatFlags |= StringFormatFlags.NoFontFallback;
                    path.AddString("Ag", family, (int)style, probeFont.Size, PointF.Empty, format);
                    if (path.PointCount == 0) {
                        error = "The font produced no vector glyph outline.";
                        return false;
                    }

                    RectangleF bounds = path.GetBounds();
                    if (!IsFinite(bounds.Left) || !IsFinite(bounds.Top) || !IsFinite(bounds.Right) ||
                        !IsFinite(bounds.Bottom) || bounds.Width <= 0f || bounds.Height <= 0f) {
                        error = "The font produced invalid glyph bounds.";
                        return false;
                    }
                }

                return true;
            } catch (Exception ex) {
                error = ex.Message;
                return false;
            }
        }

        private static string ResolveConfiguredFontPath(string configuredPath) {
            string trimmed = configuredPath.Trim();
            if (Path.IsPathRooted(trimmed))
                return trimmed;

            string underRoot = Path.Combine(AppPaths.RootDirectory, trimmed);
            if (File.Exists(underRoot))
                return underRoot;
            return Path.Combine(AppPaths.UnicodeFontsDirectory, trimmed);
        }

        private static CacheEntry CreateCacheEntry(RenderKey key) {
            RenderMetrics metrics = CalculateRenderMetrics(key.FontSize, key.GtaFont, key.RenderScale);
            string fileName = UnicodeTextSupport.ComputeStableHash(BuildLogicalKey(key, metrics)) + ".png";
            string path = Path.GetFullPath(Path.Combine(AppPaths.UnicodeTextCacheDirectory, fileName));
            BitmapDimensions dimensions;

            if (!TryReadBitmapDimensions(path, out dimensions)) {
                dimensions = RenderBitmap(path, key.Text, Color.FromArgb(key.TextArgb), Color.FromArgb(key.ShadowArgb),
                    key.Alignment, metrics, key.MaxWidthVirtual > 0f ? key.MaxWidthVirtual * key.RenderScale : 0f);
                TrimDiskCache(path);
            }

            float width = dimensions.Width / key.RenderScale;
            float height = dimensions.Height / key.RenderScale;
            float leftInset = dimensions.LeftInset / key.RenderScale;
            float topInset = dimensions.TopInset / key.RenderScale;
            float layoutWidth = dimensions.LayoutWidth / key.RenderScale;
            float layoutHeight = dimensions.LayoutHeight / key.RenderScale;
            var sprite = new CustomSprite(path, new SizeF(width, height), PointF.Empty, Color.White, 0f, false) {
                Enabled = false
            };

            return new CacheEntry(sprite, leftInset, topInset, layoutWidth, layoutHeight, dimensions.LineCount, ++accessCounter);
        }

        private static RenderMetrics CalculateRenderMetrics(float fontSize, GtaFont gtaFont, float renderScale) {
            float targetLineHeightVirtual;
            try {
                targetLineHeightVirtual = UIHelper.MeasureFontHeightNoConvert(fontSize, gtaFont) * VirtualHeight;
            } catch {
                targetLineHeightVirtual = Math.Max(12f, fontSize * 42f);
            }
            if (targetLineHeightVirtual <= 0f || float.IsNaN(targetLineHeightVirtual) || float.IsInfinity(targetLineHeightVirtual))
                targetLineHeightVirtual = Math.Max(12f, fontSize * 42f);

            FontStyle style = GetAvailableFontStyle();
            float emHeight = fontFamily.GetEmHeight(style);
            float lineSpacing = fontFamily.GetLineSpacing(style);
            if (emHeight <= 0f || lineSpacing <= 0f)
                throw new InvalidOperationException("The Unicode fallback font reported invalid metrics.");

            float targetLineHeightPixels = Math.Max(1f, targetLineHeightVirtual * renderScale);
            float fontPixelSize = Math.Max(1f, targetLineHeightPixels * emHeight / lineSpacing);
            if (!IsFinite(targetLineHeightPixels) || !IsFinite(fontPixelSize))
                throw new InvalidOperationException("The Unicode fallback font produced invalid scaled metrics.");
            float outlinePixels = Math.Max(renderScale, fontPixelSize * 0.055f);
            int paddingPixels = Math.Max(2, (int)Math.Ceiling(outlinePixels * 2.5f));
            return new RenderMetrics(style, targetLineHeightPixels, fontPixelSize, outlinePixels, paddingPixels);
        }

        private static FontStyle GetAvailableFontStyle() {
            return GetAvailableFontStyle(fontFamily);
        }

        private static FontStyle GetAvailableFontStyle(FontFamily family) {
            FontStyle[] candidates = { FontStyle.Regular, FontStyle.Bold, FontStyle.Italic, FontStyle.Bold | FontStyle.Italic };
            foreach (FontStyle candidate in candidates) {
                if (family != null && family.IsStyleAvailable(candidate))
                    return candidate;
            }
            throw new InvalidOperationException("The Unicode fallback font has no GDI+ compatible style.");
        }

        private static BitmapDimensions RenderBitmap(string finalPath, string text, Color color, Color shadowColor,
            UnicodeTextAlignment alignment, RenderMetrics metrics, float maxWidthPixels) {
            string[] sourceLines = NormalizeLines(text);

            using (var font = new DrawingFont(fontFamily, metrics.FontPixelSize, metrics.Style, GraphicsUnit.Pixel))
            using (var measureBitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb))
            using (Graphics measureGraphics = Graphics.FromImage(measureBitmap))
            using (StringFormat format = CreateStringFormat()) {
                ConfigureGraphics(measureGraphics);

                string[] lines = WrapLines(sourceLines, measureGraphics, font, format, maxWidthPixels);
                LineMeasurement[] measuredLines = new LineMeasurement[lines.Length];
                float widestAdvance = 1f;
                for (int index = 0; index < lines.Length; index++) {
                    measuredLines[index] = MeasureLine(measureGraphics, font, lines[index], metrics, format);
                    widestAdvance = Math.Max(widestAdvance, measuredLines[index].AdvanceWidth);
                }

                float minInkX = 0f;
                float maxInkX = widestAdvance;
                float minInkY = 0f;
                float maxInkY = metrics.TargetLineHeightPixels * lines.Length;
                for (int index = 0; index < measuredLines.Length; index++) {
                    LineMeasurement line = measuredLines[index];
                    if (!line.HasInk)
                        continue;

                    float layoutX = GetLineLayoutX(alignment, widestAdvance, line.AdvanceWidth);
                    float layoutY = index * metrics.TargetLineHeightPixels;
                    minInkX = Math.Min(minInkX, layoutX + line.InkBounds.Left);
                    maxInkX = Math.Max(maxInkX, layoutX + line.InkBounds.Right);
                    minInkY = Math.Min(minInkY, layoutY + line.InkBounds.Top);
                    maxInkY = Math.Max(maxInkY, layoutY + line.InkBounds.Bottom);
                }

                float shadowOffset = shadowColor.A > 0
                    ? Math.Max(1f, metrics.OutlinePixels * 0.75f)
                    : 0f;
                int effectPadding = Math.Max(metrics.PaddingPixels,
                    (int)Math.Ceiling(metrics.OutlinePixels + shadowOffset + 1f));
                int leftInset = effectPadding + (int)Math.Ceiling(Math.Max(0f, -minInkX));
                int rightInset = effectPadding + (int)Math.Ceiling(Math.Max(0f, maxInkX - widestAdvance));
                int topInset = effectPadding + (int)Math.Ceiling(Math.Max(0f, -minInkY));
                float layoutHeight = metrics.TargetLineHeightPixels * lines.Length;
                int bottomInset = effectPadding + (int)Math.Ceiling(Math.Max(0f, maxInkY - layoutHeight));

                int bitmapWidth = Math.Max(1, (int)Math.Ceiling(widestAdvance) + leftInset + rightInset);
                int bitmapHeight = Math.Max(1, (int)Math.Ceiling(layoutHeight) + topInset + bottomInset);
                if (bitmapWidth > 16384 || bitmapHeight > 16384)
                    throw new InvalidOperationException("Rendered Unicode text texture exceeds the 16384px DirectX safety limit.");
                if ((long)bitmapWidth * bitmapHeight > MaxBitmapPixels)
                    throw new InvalidOperationException("Rendered Unicode text texture exceeds the bitmap memory safety limit.");

                using (var bitmap = new Bitmap(bitmapWidth, bitmapHeight, PixelFormat.Format32bppArgb))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                using (var shadowBrush = new SolidBrush(shadowColor))
                using (var outlinePen = new Pen(Color.FromArgb(color.A, 0, 0, 0), metrics.OutlinePixels * 2f))
                using (var textBrush = new SolidBrush(color)) {
                    ConfigureGraphics(graphics);
                    graphics.Clear(Color.Transparent);
                    outlinePen.LineJoin = LineJoin.Round;

                    for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++) {
                        string line = lines[lineIndex];
                        if (line.Length == 0)
                            continue;

                        LineMeasurement measured = measuredLines[lineIndex];
                        float x = leftInset + GetLineLayoutX(alignment, widestAdvance, measured.AdvanceWidth);
                        float y = topInset + (lineIndex * metrics.TargetLineHeightPixels);

                        using (GraphicsPath path = CreateTextPath(line, x, y, metrics, format)) {
                            if (path.PointCount == 0)
                                continue;

                            if (shadowColor.A > 0) {
                                using (var shadowPath = (GraphicsPath)path.Clone())
                                using (var shadowMatrix = new Matrix()) {
                                    shadowMatrix.Translate(shadowOffset, shadowOffset);
                                    shadowPath.Transform(shadowMatrix);
                                    graphics.FillPath(shadowBrush, shadowPath);
                                }
                            }

                            graphics.DrawPath(outlinePen, path);
                            graphics.FillPath(textBrush, path);
                        }
                    }

                    string tempPath = finalPath + ".tmp-" + Guid.NewGuid().ToString("N");
                    try {
                        bitmap.Save(tempPath, ImageFormat.Png);
                        if (File.Exists(finalPath))
                            File.Delete(finalPath);
                        File.Move(tempPath, finalPath);
                    } finally {
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }
                }

                WriteMetricsSidecar(finalPath, leftInset, topInset, widestAdvance, layoutHeight, lines.Length);
                return new BitmapDimensions(bitmapWidth, bitmapHeight, leftInset, topInset, widestAdvance, layoutHeight, lines.Length);
            }
        }

        private static LineMeasurement MeasureLine(Graphics graphics, DrawingFont font, string text,
            RenderMetrics metrics, StringFormat format) {
            if (string.IsNullOrEmpty(text))
                return new LineMeasurement(0f, RectangleF.Empty, false);

            SizeF advance = graphics.MeasureString(text, font, PointF.Empty, format);
            if (!IsFinite(advance.Width) || advance.Width < 0f)
                throw new InvalidOperationException("The Unicode fallback font produced an invalid text advance width.");

            using (GraphicsPath path = CreateTextPath(text, 0f, 0f, metrics, format)) {
                if (path.PointCount == 0)
                    return new LineMeasurement(advance.Width, RectangleF.Empty, false);

                RectangleF bounds = path.GetBounds();
                if (!IsFinite(bounds.Left) || !IsFinite(bounds.Top) || !IsFinite(bounds.Right) || !IsFinite(bounds.Bottom))
                    throw new InvalidOperationException("The Unicode fallback font produced invalid glyph bounds.");
                return new LineMeasurement(advance.Width, bounds, true);
            }
        }

        private static GraphicsPath CreateTextPath(string text, float x, float y, RenderMetrics metrics, StringFormat format) {
            var path = new GraphicsPath();
            path.AddString(text, fontFamily, (int)metrics.Style, metrics.FontPixelSize,
                new PointF(x, y), format);
            return path;
        }

        private static float GetLineLayoutX(UnicodeTextAlignment alignment, float widestAdvance, float lineAdvance) {
            switch (alignment) {
            case UnicodeTextAlignment.Left:
                return 0f;
            case UnicodeTextAlignment.Right:
                return widestAdvance - lineAdvance;
            default:
                return (widestAdvance - lineAdvance) / 2f;
            }
        }

        private static void DrawEntry(CacheEntry entry, float xPosition, float yPosition, UnicodeTextAlignment alignment) {
            // Pair Screen.ScaledWidth with CustomSprite.ScaledDraw(). Both use the same
            // 720-high GTA UI canvas, so normalized X/Y positions remain stable at 4:3,
            // 16:9, 21:9, 32:9 and when GTA applies an aspect-ratio override.
            float anchorX = xPosition * Screen.ScaledWidth;
            float anchorY = yPosition * VirtualHeight;
            float drawX;
            switch (alignment) {
            case UnicodeTextAlignment.Left:
                drawX = anchorX - entry.LeftInset;
                break;
            case UnicodeTextAlignment.Right:
                drawX = anchorX - entry.LeftInset - entry.LayoutWidth;
                break;
            default:
                drawX = anchorX - entry.LeftInset - (entry.LayoutWidth / 2f);
                break;
            }

            float drawY = anchorY - entry.TopInset;
            entry.Sprite.Position = new PointF(drawX, drawY);
            entry.Sprite.Enabled = true;
            entry.Sprite.ScaledDraw();
        }

        private static string BuildLogicalKey(RenderKey key, RenderMetrics metrics) {
            // This string exists only on a managed-cache miss. The every-frame path uses
            // RenderKey directly so holding the wheel open does not allocate a composite
            // cache-key string on each tick. Aspect ratio does not affect the bitmap itself.
            // Include the GTA-measured line height/style so a game/font-mapping update cannot
            // silently reuse a disk bitmap generated with different native font metrics.
            return RendererVersion + "|" + key.FontIdentity + "|" + key.RenderScale.ToString("R", CultureInfo.InvariantCulture) + "|" +
                ((int)key.GtaFont).ToString(CultureInfo.InvariantCulture) + "|" + key.FontSize.ToString("R", CultureInfo.InvariantCulture) + "|" +
                metrics.TargetLineHeightPixels.ToString("R", CultureInfo.InvariantCulture) + "|" + ((int)metrics.Style).ToString(CultureInfo.InvariantCulture) + "|" +
                key.TextArgb.ToString(CultureInfo.InvariantCulture) + "|" + key.ShadowArgb.ToString(CultureInfo.InvariantCulture) + "|" +
                ((int)key.Alignment).ToString(CultureInfo.InvariantCulture) + "|" +
                key.MaxWidthVirtual.ToString("R", CultureInfo.InvariantCulture) + "|" + key.Text;
        }

        private static bool TryReadBitmapDimensions(string path, out BitmapDimensions dimensions) {
            dimensions = default(BitmapDimensions);
            if (!File.Exists(path))
                return false;

            try {
                // RendererVersion is part of the filename hash, so a cached image was created
                // by the same layout algorithm. Reconstruct only the invariant layout box here;
                // asymmetric glyph overhang insets are persisted in a tiny sidecar.
                string metricsPath = path + ".metrics";
                if (!File.Exists(metricsPath))
                    return false;
                if (new FileInfo(metricsPath).Length > 1024)
                    throw new InvalidDataException("Cached Unicode text metrics are unexpectedly large.");

                string[] values = File.ReadAllText(metricsPath).Split('|');
                if (values.Length != 5)
                    throw new InvalidDataException("Cached Unicode text metrics are invalid.");

                int leftInset = int.Parse(values[0], CultureInfo.InvariantCulture);
                int topInset = int.Parse(values[1], CultureInfo.InvariantCulture);
                float layoutWidth = float.Parse(values[2], CultureInfo.InvariantCulture);
                float layoutHeight = float.Parse(values[3], CultureInfo.InvariantCulture);
                int lineCount = int.Parse(values[4], CultureInfo.InvariantCulture);
                if (leftInset < 0 || topInset < 0 || !IsFinite(layoutWidth) || !IsFinite(layoutHeight) ||
                    layoutWidth < 0f || layoutHeight <= 0f || lineCount <= 0 || lineCount > 4096)
                    throw new InvalidDataException("Cached Unicode text metrics are out of range.");

                int imageWidth;
                int imageHeight;
                ReadPngDimensions(path, out imageWidth, out imageHeight);
                if (imageWidth <= 0 || imageHeight <= 0 || imageWidth > 16384 || imageHeight > 16384 ||
                    (long)imageWidth * imageHeight > MaxBitmapPixels)
                    throw new InvalidDataException("Cached Unicode text texture has invalid or unsafe dimensions.");
                ValidatePngImageData(path, imageWidth, imageHeight);
                if (leftInset > imageWidth || topInset > imageHeight || layoutWidth > imageWidth || layoutHeight > imageHeight ||
                    leftInset + layoutWidth > imageWidth || topInset + layoutHeight > imageHeight)
                    throw new InvalidDataException("Cached Unicode text metrics do not fit the cached texture.");

                dimensions = new BitmapDimensions(imageWidth, imageHeight, leftInset, topInset, layoutWidth, layoutHeight, lineCount);
                return true;
            } catch {
                try { File.Delete(path); } catch { }
                try { File.Delete(path + ".metrics"); } catch { }
                return false;
            }
        }

        private static void ReadPngDimensions(string path, out int width, out int height) {
            byte[] header = new byte[24];
            using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                if (stream.Read(header, 0, header.Length) != header.Length)
                    throw new InvalidDataException("Cached Unicode text texture has a truncated PNG header.");
            }

            if (header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E || header[3] != 0x47 ||
                header[4] != 0x0D || header[5] != 0x0A || header[6] != 0x1A || header[7] != 0x0A ||
                ReadBigEndianInt32(header, 8) != 13 ||
                header[12] != 0x49 || header[13] != 0x48 || header[14] != 0x44 || header[15] != 0x52) {
                throw new InvalidDataException("Cached Unicode text texture is not a valid PNG/IHDR stream.");
            }

            width = ReadBigEndianInt32(header, 16);
            height = ReadBigEndianInt32(header, 20);
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset) {
            return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
        }

        private static void ValidatePngImageData(string path, int expectedWidth, int expectedHeight) {
            // Validate the complete image only after the cheap IHDR/pixel-count checks above.
            // This catches truncated/corrupt cache files without allowing a hostile header to
            // make GDI+ attempt to decode an enormous bitmap first.
            using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (Image image = Image.FromStream(stream, false, true)) {
                if (image.Width != expectedWidth || image.Height != expectedHeight)
                    throw new InvalidDataException("Cached Unicode text texture dimensions changed while validating the PNG.");
            }
        }

        private static void WriteMetricsSidecar(string imagePath, int leftInset, int topInset, float layoutWidth, float layoutHeight, int lineCount) {
            string finalPath = imagePath + ".metrics";
            string tempPath = finalPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try {
                string contents = leftInset.ToString(CultureInfo.InvariantCulture) + "|" +
                    topInset.ToString(CultureInfo.InvariantCulture) + "|" +
                    layoutWidth.ToString("R", CultureInfo.InvariantCulture) + "|" +
                    layoutHeight.ToString("R", CultureInfo.InvariantCulture) + "|" +
                    lineCount.ToString(CultureInfo.InvariantCulture);
                File.WriteAllText(tempPath, contents);
                if (File.Exists(finalPath))
                    File.Delete(finalPath);
                File.Move(tempPath, finalPath);
            } finally {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private static string[] WrapLines(string[] sourceLines, Graphics graphics, DrawingFont font,
            StringFormat format, float maxWidthPixels) {
            if (maxWidthPixels <= 0f || !IsFinite(maxWidthPixels))
                return sourceLines;

            var result = new List<string>();
            foreach (string sourceLine in sourceLines) {
                if (string.IsNullOrWhiteSpace(sourceLine)) {
                    result.Add(string.Empty);
                    continue;
                }

                SizeF wholeLine = graphics.MeasureString(sourceLine, font, PointF.Empty, format);
                if (IsFinite(wholeLine.Width) && wholeLine.Width <= maxWidthPixels) {
                    result.Add(sourceLine);
                    continue;
                }

                List<string> elements = UnicodeTextSupport.SplitTextElements(sourceLine);
                int start = 0;
                while (start < elements.Count) {
                    while (start < elements.Count && IsWhitespaceElement(elements[start]))
                        start++;
                    if (start >= elements.Count)
                        break;

                    int lastFit = start;
                    int lastBreak = -1;
                    var builder = new StringBuilder();
                    for (int index = start; index < elements.Count; index++) {
                        builder.Append(elements[index]);
                        SizeF candidate = graphics.MeasureString(builder.ToString(), font, PointF.Empty, format);
                        if (!IsFinite(candidate.Width))
                            throw new InvalidOperationException("The Unicode fallback font produced an invalid wrapped text width.");

                        if (candidate.Width <= maxWidthPixels || index == start) {
                            lastFit = index + 1;
                            if (IsWhitespaceElement(elements[index]))
                                lastBreak = index + 1;
                            continue;
                        }
                        break;
                    }

                    int breakAt = lastFit >= elements.Count
                        ? elements.Count
                        : (lastBreak > start ? lastBreak : lastFit);
                    if (breakAt <= start)
                        breakAt = Math.Min(start + 1, elements.Count);
                    breakAt = UnicodeTextSupport.AdjustWrapBreak(elements, start, breakAt);

                    string line = JoinTextElements(elements, start, breakAt).TrimEnd();
                    result.Add(line);
                    start = breakAt;
                }
            }

            if (result.Count == 0)
                result.Add(string.Empty);
            return result.ToArray();
        }

        private static string JoinTextElements(List<string> elements, int start, int end) {
            var builder = new StringBuilder();
            for (int index = start; index < end; index++)
                builder.Append(elements[index]);
            return builder.ToString();
        }

        private static bool IsWhitespaceElement(string element) {
            return !string.IsNullOrEmpty(element) && char.IsWhiteSpace(element, 0);
        }

        private static string[] NormalizeLines(string text) {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.None);
        }

        private static StringFormat CreateStringFormat() {
            var format = new StringFormat(StringFormat.GenericTypographic) {
                Trimming = StringTrimming.None,
                // FitBlackBox preserves real glyph overhang instead of nudging it back into
                // the nominal layout box; the renderer measures that ink and pads for it.
                // Intentionally do NOT set NoFontFallback: GDI+ can select an alternate
                // installed family when the requested CJK font lacks a particular glyph.
                FormatFlags = StringFormatFlags.FitBlackBox | StringFormatFlags.NoClip |
                    StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces
            };
            return format;
        }


        private static bool IsFinite(float value) {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void ConfigureGraphics(Graphics graphics) {
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        }

        private static void RememberFailedKey(RenderKey key) {
            if (!FailedKeys.Add(key))
                return;

            FailedKeyOrder.Enqueue(key);
            while (FailedKeyOrder.Count > MaxFailedKeys) {
                RenderKey expired = FailedKeyOrder.Dequeue();
                FailedKeys.Remove(expired);
            }
        }

        private static void TrimManagedCache(RenderKey keepKey) {
            while (Cache.Count > MaxManagedEntries) {
                KeyValuePair<RenderKey, CacheEntry> oldest = Cache
                    .Where(pair => !pair.Key.Equals(keepKey))
                    .OrderBy(pair => pair.Value.LastAccess)
                    .FirstOrDefault();
                if (oldest.Value == null)
                    break;

                try { oldest.Value.Sprite.Enabled = false; } catch { }
                Cache.Remove(oldest.Key);
            }
        }

        private static void PruneDiskCache() {
            try {
                var directory = new DirectoryInfo(AppPaths.UnicodeTextCacheDirectory);
                if (!directory.Exists)
                    return;

                foreach (FileInfo tempFile in directory.GetFiles("*.tmp-*")) {
                    try { tempFile.Delete(); } catch { }
                }

                foreach (FileInfo metricsFile in directory.GetFiles("*.png.metrics")) {
                    string imagePath = metricsFile.FullName.Substring(0, metricsFile.FullName.Length - ".metrics".Length);
                    if (!File.Exists(imagePath)) {
                        try { metricsFile.Delete(); } catch { }
                    }
                }

                DateTime cutoff = DateTime.UtcNow.AddDays(-MaxDiskAgeDays);
                foreach (FileInfo file in directory.GetFiles("*.png").Where(file => file.LastWriteTimeUtc < cutoff)) {
                    try { file.Delete(); File.Delete(file.FullName + ".metrics"); } catch { }
                }

                FileInfo[] remaining = directory.GetFiles("*.png")
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .ToArray();
                for (int index = MaxDiskEntries; index < remaining.Length; index++) {
                    try { remaining[index].Delete(); File.Delete(remaining[index].FullName + ".metrics"); } catch { }
                }
            } catch (Exception ex) {
                Logger.Log("WARNING: Could not prune Unicode text cache: " + ex.Message);
            }
        }

        private static void TrimDiskCache(string keepPath) {
            try {
                var directory = new DirectoryInfo(AppPaths.UnicodeTextCacheDirectory);
                if (!directory.Exists)
                    return;

                FileInfo[] files = directory.GetFiles("*.png")
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .ToArray();
                for (int index = MaxDiskEntries; index < files.Length; index++) {
                    if (string.Equals(files[index].FullName, keepPath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    try { files[index].Delete(); File.Delete(files[index].FullName + ".metrics"); } catch { }
                }
            } catch { }
        }

        private static void DisableAllSprites() {
            foreach (CacheEntry entry in Cache.Values) {
                try { entry.Sprite.Enabled = false; } catch { }
            }
        }

        private static void DisposeFontCollection() {
            if (fontFamily != null) {
                try { fontFamily.Dispose(); } catch { }
                fontFamily = null;
            }
            if (privateFonts != null) {
                try { privateFonts.Dispose(); } catch { }
                privateFonts = null;
            }
        }

        private struct RenderKey : IEquatable<RenderKey> {
            internal RenderKey(string text, float fontSize, GtaFont gtaFont, Color color, Color shadowColor,
                UnicodeTextAlignment alignment, float renderScale, string fontIdentity, float maxWidthVirtual) {
                Text = text ?? string.Empty;
                FontSize = fontSize;
                GtaFont = gtaFont;
                TextArgb = color.ToArgb();
                ShadowArgb = shadowColor.A == 0 ? 0 : shadowColor.ToArgb();
                Alignment = alignment;
                RenderScale = renderScale;
                FontIdentity = fontIdentity ?? string.Empty;
                MaxWidthVirtual = maxWidthVirtual;
            }

            internal string Text;
            internal float FontSize;
            internal GtaFont GtaFont;
            internal int TextArgb;
            internal int ShadowArgb;
            internal UnicodeTextAlignment Alignment;
            internal float RenderScale;
            internal string FontIdentity;
            internal float MaxWidthVirtual;

            public bool Equals(RenderKey other) {
                return FontSize.Equals(other.FontSize) && GtaFont == other.GtaFont &&
                    TextArgb == other.TextArgb && ShadowArgb == other.ShadowArgb &&
                    Alignment == other.Alignment && RenderScale.Equals(other.RenderScale) &&
                    MaxWidthVirtual.Equals(other.MaxWidthVirtual) &&
                    string.Equals(FontIdentity, other.FontIdentity, StringComparison.Ordinal) &&
                    string.Equals(Text, other.Text, StringComparison.Ordinal);
            }

            public override bool Equals(object obj) {
                return obj is RenderKey && Equals((RenderKey)obj);
            }

            public override int GetHashCode() {
                unchecked {
                    int hash = 17;
                    hash = (hash * 31) + FontSize.GetHashCode();
                    hash = (hash * 31) + (int)GtaFont;
                    hash = (hash * 31) + TextArgb;
                    hash = (hash * 31) + ShadowArgb;
                    hash = (hash * 31) + (int)Alignment;
                    hash = (hash * 31) + RenderScale.GetHashCode();
                    hash = (hash * 31) + MaxWidthVirtual.GetHashCode();
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(FontIdentity ?? string.Empty);
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(Text ?? string.Empty);
                    return hash;
                }
            }
        }

        private sealed class CacheEntry {
            internal CacheEntry(CustomSprite sprite, float leftInset, float topInset, float layoutWidth,
                float layoutHeight, int lineCount, long lastAccess) {
                Sprite = sprite;
                LeftInset = leftInset;
                TopInset = topInset;
                LayoutWidth = layoutWidth;
                LayoutHeight = layoutHeight;
                LineCount = lineCount;
                LastAccess = lastAccess;
            }

            internal CustomSprite Sprite { get; }
            internal float LeftInset { get; }
            internal float TopInset { get; }
            internal float LayoutWidth { get; }
            internal float LayoutHeight { get; }
            internal int LineCount { get; }
            internal long LastAccess { get; set; }
        }


        private struct RenderMetrics {
            internal RenderMetrics(FontStyle style, float targetLineHeightPixels, float fontPixelSize,
                float outlinePixels, int paddingPixels) {
                Style = style;
                TargetLineHeightPixels = targetLineHeightPixels;
                FontPixelSize = fontPixelSize;
                OutlinePixels = outlinePixels;
                PaddingPixels = paddingPixels;
            }

            internal FontStyle Style;
            internal float TargetLineHeightPixels;
            internal float FontPixelSize;
            internal float OutlinePixels;
            internal int PaddingPixels;
        }

        private struct LineMeasurement {
            internal LineMeasurement(float advanceWidth, RectangleF inkBounds, bool hasInk) {
                AdvanceWidth = advanceWidth;
                InkBounds = inkBounds;
                HasInk = hasInk;
            }

            internal float AdvanceWidth;
            internal RectangleF InkBounds;
            internal bool HasInk;
        }

        private struct BitmapDimensions {
            internal BitmapDimensions(int width, int height, int leftInset, int topInset, float layoutWidth,
                float layoutHeight, int lineCount) {
                Width = width;
                Height = height;
                LeftInset = leftInset;
                TopInset = topInset;
                LayoutWidth = layoutWidth;
                LayoutHeight = layoutHeight;
                LineCount = lineCount;
            }

            internal int Width;
            internal int Height;
            internal int LeftInset;
            internal int TopInset;
            internal float LayoutWidth;
            internal float LayoutHeight;
            internal int LineCount;
        }
    }

    internal struct UnicodeTextLayout {
        internal UnicodeTextLayout(int lineCount, float layoutWidthVirtual, float layoutHeightVirtual) {
            LineCount = lineCount;
            LayoutWidthVirtual = layoutWidthVirtual;
            LayoutHeightVirtual = layoutHeightVirtual;
        }

        internal int LineCount;
        internal float LayoutWidthVirtual;
        internal float LayoutHeightVirtual;
        internal float LineHeightVirtual => LineCount > 0 ? LayoutHeightVirtual / LineCount : 0f;
    }
}
