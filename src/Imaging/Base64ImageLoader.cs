// File: src/Smart_Stay_Awake/Imaging/Base64ImageLoader.cs
// Purpose: Decode an embedded base64 image (PNG/JPG/etc.) into a Bitmap safely.
// Heavy trace + friendly errors; returns decoupled Bitmap.

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;

namespace Smart_Stay_Awake.Imaging
{
    internal static class Base64ImageLoader
    {
        /// <summary>
        /// Returns true if the embedded base64 string looks present (non-empty after trim).
        /// </summary>
        public static bool HasEmbeddedImage()
        {
            bool present = !string.IsNullOrWhiteSpace(EmbeddedImage.EYE_IMAGE_BASE64);
            Trace.WriteLine($"Base64ImageLoader: HasEmbeddedImage = {present}");
            return present;
        }

        /// <summary>
        /// Decode the embedded base64 string into a decoupled Bitmap (caller owns).
        /// Throws InvalidOperationException on failures with a friendly message.
        /// </summary>
        public static Bitmap LoadEmbeddedBitmap()
        {
            Trace.WriteLine("Base64ImageLoader: Entered LoadEmbeddedBitmap ...");
            try
            {
                string raw = EmbeddedImage.EYE_IMAGE_BASE64?.Trim() ?? string.Empty;

                // The if/throw will be replaced by something permitting skipping to the next fallack.
                if (raw.Length == 0)
                    throw new InvalidOperationException("Embedded base64 image is empty.");

                byte[] bytes = Convert.FromBase64String(raw);
                Trace.WriteLine($"Base64ImageLoader: Decoded {bytes.Length} bytes from embedded base64.");

                using var ms = new MemoryStream(bytes, writable: false);
                using var img = Image.FromStream(ms, useEmbeddedColorManagement: true, validateImageData: true);
                var bmp = new Bitmap(img); // decouple from stream
                Trace.WriteLine($"Base64ImageLoader: Loaded embedded image {bmp.Width}x{bmp.Height}.");
                Trace.WriteLine("Base64ImageLoader: Exiting LoadEmbeddedBitmap (success).");
                return bmp;
            }
            catch (FormatException ex)
            {
                Trace.WriteLine("Base64ImageLoader: Invalid base64 format: " + ex.Message);
                throw new InvalidOperationException("Embedded image base64 is invalid.", ex);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Base64ImageLoader: Unexpected error: " + ex);
                throw new InvalidOperationException("Failed to load embedded base64 image: " + ex.Message, ex);
            }
        }
    }
}
