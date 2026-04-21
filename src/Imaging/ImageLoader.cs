// File: src/Smart_Stay_Awake/Imaging/ImageLoader.cs
// Purpose: Load a Bitmap from a chosen source (CLI path now; fallback next).
// Notes:
//   * System.Drawing on .NET 10 is supported on Windows. We dispose streams promptly.
//   * We return a *new Bitmap* to decouple from any underlying file handles (avoids file locks).

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;

namespace Smart_Stay_Awake.Imaging
{
    internal static class ImageLoader
    {
        /// <summary>
        /// Attempts to load a Bitmap from an absolute path. Throws on failure with a friendly message.
        /// Returns a *decoupled* Bitmap (not tied to file stream).
        /// Caller owns Bitmap disposal.
        /// </summary>
        public static Bitmap LoadBitmapFromPath(string fullPath)
        {
            Trace.WriteLine("ImageLoader: Entered LoadBitmapFromPath ...");
            if (string.IsNullOrWhiteSpace(fullPath))
                throw new ArgumentException("fullPath is null/empty", nameof(fullPath));

            try
            {
                // Using Image.FromFile keeps the file locked. Instead, open a FileStream and .FromStream,
                // then clone to a new Bitmap so we can close the stream immediately.
                using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var temp = Image.FromStream(fs, useEmbeddedColorManagement: true, validateImageData: true);
                var bmp = new Bitmap(temp); // decouple from stream
                Trace.WriteLine($"ImageLoader: Loaded image: {fullPath}, size={bmp.Width}x{bmp.Height}");
                Trace.WriteLine("ImageLoader: Exiting LoadBitmapFromPath (success).");
                return bmp;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("ImageLoader: LoadBitmapFromPath error: " + ex);
                throw new InvalidOperationException("Failed to load image file: " + fullPath + "\n" + ex.Message, ex);
            }
        }

        // Neighbor search helper
        public static bool TryLoadNeighborIconBesideExe(out Bitmap? bmp, out string? chosenPath)
        {
            Trace.WriteLine("ImageLoader: Entered TryLoadNeighborIconBesideExe ...");
            bmp = null; chosenPath = null;

            try
            {
                string exeDir = AppContext.BaseDirectory;
                string baseName = "Smart_Stay_Awake_icon";
                foreach (var ext in AppConfig.ALLOWED_ICON_EXTENSIONS)
                {
                    string candidate = Path.Combine(exeDir, baseName + ext);
                    if (File.Exists(candidate))
                    {
                        Trace.WriteLine($"ImageLoader: Neighbor candidate exists: {candidate}");
                        bmp = LoadBitmapFromPath(candidate);
                        chosenPath = candidate;
                        Trace.WriteLine("ImageLoader: Exiting TryLoadNeighborIconBesideExe (success).");
                        return true;
                    }
                }
                Trace.WriteLine("ImageLoader: No neighbor icon found next to EXE.");
                return false;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("ImageLoader: TryLoadNeighborIconBesideExe error: " + ex);
                return false;
            }
        }

    }
}
