using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;

namespace CustomRadioStations {
    public static class AudioAnalysisIdentity {
        private const int SampleSize = 64 * 1024;

        public static string CreateAnalysisKey(ResolvedMediaSource source) {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            return CreateAnalysisKey(source.FilePath, source.StartMs, source.EndMs);
        }

        public static string CreateAnalysisKey(string filePath, uint? startMs, uint? endMs) {
            string fingerprint = CreateFileFingerprint(filePath);
            return CreateAnalysisKeyFromFingerprint(fingerprint, startMs, endMs);
        }

        public static string CreateAnalysisKeyFromFingerprint(string fingerprint, uint? startMs, uint? endMs) {
            if (string.IsNullOrWhiteSpace(fingerprint))
                throw new ArgumentException("A file fingerprint is required.", nameof(fingerprint));
            if (!startMs.HasValue && !endMs.HasValue)
                return fingerprint;
            return fingerprint + "|segment:" + (startMs.HasValue ? startMs.Value.ToString(CultureInfo.InvariantCulture) : "0") + "-" +
                (endMs.HasValue ? endMs.Value.ToString(CultureInfo.InvariantCulture) : "eof");
        }

        public static string CreateFileFingerprint(string filePath) {
            string fullPath = Path.GetFullPath(filePath);
            using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 sha256 = SHA256.Create()) {
                long length = stream.Length;
                byte[] lengthBytes = BitConverter.GetBytes(length);
                sha256.TransformBlock(lengthBytes, 0, lengthBytes.Length, lengthBytes, 0);

                if (length <= SampleSize * 3L) {
                    HashRange(stream, sha256, 0L, length);
                } else {
                    HashRange(stream, sha256, 0L, SampleSize);
                    HashRange(stream, sha256, Math.Max(0L, (length / 2L) - (SampleSize / 2L)), SampleSize);
                    HashRange(stream, sha256, length - SampleSize, SampleSize);
                }

                sha256.TransformFinalBlock(new byte[0], 0, 0);
                return "sample-v1:" + length.ToString("x", CultureInfo.InvariantCulture) + ":" + ToHex(sha256.Hash);
            }
        }

        private static void HashRange(Stream stream, HashAlgorithm hash, long offset, long count) {
            stream.Position = offset;
            byte[] offsetBytes = BitConverter.GetBytes(offset);
            hash.TransformBlock(offsetBytes, 0, offsetBytes.Length, offsetBytes, 0);

            byte[] buffer = new byte[SampleSize];
            long remaining = count;
            while (remaining > 0L) {
                int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read <= 0)
                    break;
                hash.TransformBlock(buffer, 0, read, buffer, 0);
                remaining -= read;
            }
        }

        private static string ToHex(byte[] bytes) {
            var result = new char[bytes.Length * 2];
            const string digits = "0123456789abcdef";
            for (int index = 0; index < bytes.Length; index++) {
                result[index * 2] = digits[bytes[index] >> 4];
                result[(index * 2) + 1] = digits[bytes[index] & 0x0f];
            }
            return new string(result);
        }
    }
}
