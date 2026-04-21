// File: src/Smart_Stay_Awake/Imaging/IconWriter.cs
// Purpose: Phase 1 – get a 32x32 Icon from a Bitmap using GetHicon.
// Notes:
//   * Icon.FromHandle does not own the HICON lifetime; we duplicate via new Icon(icon, size) to detach.
//   * Caller owns the returned Icon (dispose when done). We also destroy the original HICON.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Smart_Stay_Awake.Imaging
{
    internal static class IconWriter
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static Icon BitmapToIcon32(Bitmap bmpSquare)
        {
            Trace.WriteLine("IconWriter: Entered BitmapToIcon32 ...");
            if (bmpSquare is null) throw new ArgumentNullException(nameof(bmpSquare));

            // Ensure 32x32
            Bitmap use = bmpSquare.Width == 32 ? bmpSquare : ImageSquareReplicator.ResizeSquareExact(bmpSquare, 32);

            IntPtr hIcon = use.GetHicon();
            try
            {
                using var tmp = Icon.FromHandle(hIcon);
                // Clone into a true managed Icon we own
                var managed = new Icon(tmp, 32, 32);
                Trace.WriteLine("IconWriter: Exiting BitmapToIcon32 (success).");
                return managed;
            }
            finally
            {
                // Release native HICON
                try { DestroyIcon(hIcon); } catch { /* ignore */ }
            }
        }
    }
}
