// File: src/Smart_Stay_Awake/Imaging/ImageSquareReplicator.cs
// Purpose: Make a bitmap square by *edge replication* (no solid padding).
// Strategy:
//   * Target side = max(width, height)
//   * Create a new square bitmap; draw original centered.
//   * Replicate the left/right columns outward to fill horizontal padding (if width < height).
//   * Replicate the top/bottom rows outward to fill vertical padding (if height < width).
//   * If final dimension is off by 1 (odd/even concerns), we add one more replicated col/row.
//   * Evenize to a multiple of 2 px
// Performance: Uses GetPixel/SetPixel for clarity (images are small for tray/window). We can optimize later.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Smart_Stay_Awake.Imaging
{
    internal static class ImageSquareReplicator
    {
        /// <summary>
        /// Make the image square by edge replication. If pads are odd, the extra pixel goes to right/bottom.
        /// Finally ensure the result is an even-numbered square (replicate +1 right col and +1 bottom row if needed).
        /// </summary>
        public static Bitmap MakeSquareByEdgeReplication(Bitmap src)
        {
            Trace.WriteLine("ImageSquareReplicator: Entered MakeSquareByEdgeReplication ...");
            if (src is null) throw new ArgumentNullException(nameof(src));

            int w = src.Width, h = src.Height;
            Trace.WriteLine($"ImageSquareReplicator: Source size = {w}x{h}");

            if (w == h)
            {
                // If odd, evenize; else just copy.
                if ((w & 1) == 1)
                {
                    Trace.WriteLine("ImageSquareReplicator: Already square but odd; evenizing by +1 col and +1 row (replicate edges).");
                    return EvenizeSquare(new Bitmap(src));
                }
                Trace.WriteLine("ImageSquareReplicator: Already square (even); returning clone.");
                return new Bitmap(src);
            }

            int target = Math.Max(w, h);
            int padXtotal = target - w; // may be 0
            int padYtotal = target - h; // may be 0

            // Compute side pads, put odd extra on right/bottom
            int leftPad = padXtotal > 0 ? padXtotal / 2 : 0;
            int rightPad = padXtotal > 0 ? (padXtotal - leftPad) : 0;
            int topPad = padYtotal > 0 ? padYtotal / 2 : 0;
            int bottomPad = padYtotal > 0 ? (padYtotal - topPad) : 0;

            Trace.WriteLine($"ImageSquareReplicator: target={target}, leftPad={leftPad}, rightPad={rightPad}, topPad={topPad}, bottomPad={bottomPad}");

            var dst = new Bitmap(target, target);
            using (var g = Graphics.FromImage(dst))
            {
                g.Clear(Color.Transparent);
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.NearestNeighbor; // we want exact copy; replication happens manually
                g.SmoothingMode = SmoothingMode.None;

                // Draw original centered
                g.DrawImage(src, leftPad, topPad, w, h);
            }

            // Replicate horizontally
            if (padXtotal > 0)
            {
                int leftSrcX = leftPad;         // left-most column of original
                int rightSrcX = leftPad + w - 1; // right-most column of original

                // Fill left pad from left-most original column
                for (int x = leftPad - 1; x >= 0; x--)
                {
                    for (int y = 0; y < target; y++)
                        dst.SetPixel(x, y, dst.GetPixel(leftSrcX, y));
                }
                // Fill right pad from right-most original column
                for (int x = leftPad + w; x <= target - 1; x++)
                {
                    for (int y = 0; y < target; y++)
                        dst.SetPixel(x, y, dst.GetPixel(rightSrcX, y));
                }
            }

            // Replicate vertically
            if (padYtotal > 0)
            {
                int topSrcY = topPad;            // top-most row of original
                int bottomSrcY = topPad + h - 1; // bottom-most row of original

                // Fill top pad from top-most original row
                for (int y = topPad - 1; y >= 0; y--)
                {
                    for (int x = 0; x < target; x++)
                        dst.SetPixel(x, y, dst.GetPixel(x, topSrcY));
                }
                // Fill bottom pad from bottom-most original row
                for (int y = topPad + h; y <= target - 1; y++)
                {
                    for (int x = 0; x < target; x++)
                        dst.SetPixel(x, y, dst.GetPixel(x, bottomSrcY));
                }
            }

            // Final evenization if needed
            if ((target & 1) == 1)
            {
                Trace.WriteLine("ImageSquareReplicator: Result side is odd; evenizing by +1 right col and +1 bottom row.");
                var even = EvenizeSquare(dst);
                dst.Dispose();
                Trace.WriteLine($"ImageSquareReplicator: Final evenized size = {even.Width}x{even.Height}");
                Trace.WriteLine("ImageSquareReplicator: Exiting MakeSquareByEdgeReplication (success).");
                return even;
            }

            Trace.WriteLine($"ImageSquareReplicator: Final size = {dst.Width}x{dst.Height} (already even).");
            Trace.WriteLine("ImageSquareReplicator: Exiting MakeSquareByEdgeReplication (success).");
            return dst;
        }

        private static Bitmap EvenizeSquare(Bitmap square)
        {
            int s = square.Width; // equals Height
            var even = new Bitmap(s + 1, s + 1);
            using (var g = Graphics.FromImage(even))
            {
                g.DrawImage(square, 0, 0, s, s);
            }
            // replicate right-most column into the new col s
            for (int y = 0; y < s; y++)
                even.SetPixel(s, y, square.GetPixel(s - 1, y));
            // replicate bottom-most row into the new row s
            for (int x = 0; x < s + 1; x++)
                even.SetPixel(x, s, even.GetPixel(x, s - 1));
            return even;
        }

        /// <summary>
        /// Resize a square image down to targetSize (no upscaling). If src is <= target, returns a copy.
        /// </summary>
        public static Bitmap ResizeSquareMax(Bitmap srcSquare, int targetSize)
        {
            Trace.WriteLine($"ImageSquareReplicator: Entered ResizeSquareMax to {targetSize} ...");
            if (srcSquare is null) throw new ArgumentNullException(nameof(srcSquare));
            if (srcSquare.Width != srcSquare.Height)
                throw new ArgumentException("ResizeSquareMax expects a square image.", nameof(srcSquare));

            int s = srcSquare.Width;
            if (s <= targetSize)
            {
                Trace.WriteLine($"ImageSquareReplicator: No resize needed (side={s} <= cap={targetSize}). Returning clone.");
                return new Bitmap(srcSquare);
            }

            int t = Math.Max(8, Math.Min(4096, targetSize));
            var dst = new Bitmap(t, t);
            using var g = Graphics.FromImage(dst);
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(srcSquare, 0, 0, t, t);
            Trace.WriteLine($"ImageSquareReplicator: Resized from {s} to {t}.");
            Trace.WriteLine("ImageSquareReplicator: Exiting ResizeSquareMax (success).");
            return dst;
        }

        /// <summary>
        /// Resize a square bitmap to an exact size (e.g., 16, 32, 256). Needed for ICO frames, returns a copy.
        /// </summary>
        public static Bitmap ResizeSquareExact(Bitmap srcSquare, int targetSize)
        {
            Trace.WriteLine($"ImageSquareReplicator: Entered ResizeSquareExact to {targetSize} ...");
            if (srcSquare is null) throw new ArgumentNullException(nameof(srcSquare));
            if (srcSquare.Width != srcSquare.Height)
                throw new ArgumentException("ResizeSquareExact expects a square image.", nameof(srcSquare));

            int s = Math.Max(8, Math.Min(4096, targetSize));
            var dst = new Bitmap(s, s);
            using var g = Graphics.FromImage(dst);
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(srcSquare, 0, 0, s, s);
            Trace.WriteLine("ImageSquareReplicator: Exiting ResizeSquareExact (success).");
            return dst;
        }

        // Optional shim so older calls still compile:
        // public static Bitmap ResizeSquare(Bitmap srcSquare, int targetSize) => ResizeSquareExact(srcSquare, targetSize);

    }
}
