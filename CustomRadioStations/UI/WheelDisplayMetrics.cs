using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CustomRadioStations {
    /// <summary>
    /// Resolution-independent wheel geometry and station icon source selection.
    /// GTA's UI canvas is always 720 virtual pixels high; its width follows the
    /// viewport aspect ratio.
    /// </summary>
    internal static class WheelDisplayMetrics {
        internal const float VirtualHeight = 720f;

        internal static float GetVirtualWidth(int outputWidth, int outputHeight) {
            if (outputWidth <= 0 || outputHeight <= 0)
                return 1280f;
            return VirtualHeight * outputWidth / outputHeight;
        }

        internal static float GetTextRenderScale(int outputHeight) {
            if (outputHeight <= 0)
                return 1f;

            // ScriptHookV retains every texture path until scripts reload. If the
            // exact window height were used here, dragging a window through many
            // heights could create a native texture variant for every single pixel.
            // Round UP to 0.25x density buckets: this never undersamples the current
            // output and keeps common 1080p/1440p/4K scales exactly 1.5/2/3x.
            float physicalScale = Math.Max(1f, outputHeight / VirtualHeight);
            return (float)(Math.Ceiling(physicalScale * 4f) / 4f);
        }

        internal static int GetRequiredIconPixels(int virtualWidth, int virtualHeight, int outputHeight) {
            if (outputHeight <= 0)
                outputHeight = (int)VirtualHeight;
            int largestVirtualDimension = Math.Max(1, Math.Max(virtualWidth, virtualHeight));
            return Math.Max(1, (int)Math.Ceiling(largestVirtualDimension * outputHeight / VirtualHeight));
        }
    }

    internal static class StationIconVariantResolver {
        private static readonly int[] VariantSizes = { 128, 256, 512, 1024 };

        internal static bool HasAnyVariant(string configuredPath) {
            return GetAvailableVariants(configuredPath).Count > 0;
        }

        internal static string Resolve(string configuredPath, int requiredPixels) {
            List<IconVariant> variants = GetAvailableVariants(configuredPath);
            if (variants.Count == 0)
                return null;

            IconVariant selected = variants.FirstOrDefault(candidate => candidate.Pixels >= requiredPixels)
                ?? variants[variants.Count - 1];
            return selected.Path;
        }

        private static List<IconVariant> GetAvailableVariants(string configuredPath) {
            var result = new List<IconVariant>();
            if (string.IsNullOrWhiteSpace(configuredPath))
                return result;

            try {
                string fullPath = Path.GetFullPath(configuredPath);
                string directory = Path.GetDirectoryName(fullPath);
                string extension = Path.GetExtension(fullPath);
                string stem = Path.GetFileNameWithoutExtension(fullPath);
                if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(stem) || string.IsNullOrEmpty(extension))
                    return result;

                int explicitSize = VariantSizes.FirstOrDefault(size =>
                    stem.EndsWith("." + size, StringComparison.OrdinalIgnoreCase));
                string familyStem = explicitSize > 0
                    ? stem.Substring(0, stem.Length - (explicitSize.ToString().Length + 1))
                    : stem;

                AddIfPresent(result, Path.Combine(directory, familyStem + extension), 128);
                foreach (int size in VariantSizes)
                    AddIfPresent(result, Path.Combine(directory, familyStem + "." + size + extension), size);
            } catch {
                return new List<IconVariant>();
            }

            return result
                .GroupBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(candidate => candidate.Pixels).First())
                .OrderBy(candidate => candidate.Pixels)
                .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void AddIfPresent(List<IconVariant> result, string path, int pixels) {
            string validationError;
            if (TextureFileValidator.TryValidatePng(path, out validationError))
                result.Add(new IconVariant(path, pixels));
        }

        private sealed class IconVariant {
            internal IconVariant(string path, int pixels) {
                Path = path;
                Pixels = pixels;
            }

            internal string Path { get; }
            internal int Pixels { get; }
        }
    }

    internal static class TextureFileValidator {
        internal static bool TryValidatePng(string path, out string error) {
            error = null;
            try {
                if (!File.Exists(path)) {
                    error = "Texture file was not found.";
                    return false;
                }

                using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                    byte[] header = new byte[24];
                    if (stream.Read(header, 0, header.Length) != header.Length ||
                        header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4e || header[3] != 0x47 ||
                        header[4] != 0x0d || header[5] != 0x0a || header[6] != 0x1a || header[7] != 0x0a) {
                        error = "Texture is not a valid PNG file.";
                        return false;
                    }

                    int width = ReadBigEndianInt32(header, 16);
                    int height = ReadBigEndianInt32(header, 20);
                    if (width <= 0 || height <= 0 || width > 16384 || height > 16384) {
                        error = "PNG dimensions are invalid or exceed the 16384px DirectX safety limit.";
                        return false;
                    }
                }

                return true;
            } catch (Exception ex) {
                error = ex.Message;
                return false;
            }
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset) {
            return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
        }
    }
}