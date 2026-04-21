// File: src/Smart_Stay_Awake/Imaging/IcoBuilder.cs
// Purpose: Build a multi-size .ICO in memory from a single *square* bitmap by producing PNG frames.
// Sizes default: 16,20,24,32,40,48,64,128,256 (configurable).
// Implementation:
//   * For each requested size, resize the square bitmap, encode as PNG (byte[]).
//   * Write ICO header + ICONDIRENTRY table + PNG blobs.
//   * Return (Icon icon, MemoryStream stream) — keep stream alive while using the Icon.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;

namespace Smart_Stay_Awake.Imaging
{
    internal static class IcoBuilder
    {
        /// <summary>
        /// Build a multi-size icon (all-PNG frames) from a square bitmap.
        /// Returns the Icon and its backing MemoryStream (caller should retain and dispose both).
        /// </summary>
        public static (Icon icon, MemoryStream backingStream) BuildMultiSizePngIco(Bitmap square, IReadOnlyList<int> sizes)
        {
            Trace.WriteLine("IcoBuilder: Entered BuildMultiSizePngIco ...");
            if (square == null) throw new ArgumentNullException(nameof(square));
            if (square.Width != square.Height) throw new ArgumentException("Input must be square.", nameof(square));
            if (sizes == null || sizes.Count == 0) throw new ArgumentException("No sizes provided.", nameof(sizes));

            // 1) Create PNG frames (size → bytes)
            var frames = new List<(int Size, byte[] Bytes)>();
            foreach (int s in sizes)
            {
                using var resized = ImageSquareReplicator.ResizeSquareExact(square, s);
                using var ms = new MemoryStream();
                resized.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                var bytes = ms.ToArray();
                frames.Add((s, bytes));
                Trace.WriteLine($"IcoBuilder: Prepared {s}x{s} PNG frame: {bytes.Length} bytes.");
            }

            // 2) Write ICO structure into a single stream.
            // ICO format:
            //   ICONDIR {
            //     WORD idReserved = 0;
            //     WORD idType = 1; // 1 = ICON
            //     WORD idCount = N; // number of images
            //   }
            //   ICONDIRENTRY[N] {
            //     BYTE  bWidth;        // 0 means 256
            //     BYTE  bHeight;       // 0 means 256
            //     BYTE  bColorCount;   // 0 if >= 8bpp
            //     BYTE  bReserved;     // 0
            //     WORD  wPlanes;       // 0 for PNG frames
            //     WORD  wBitCount;     // 0 for PNG frames
            //     DWORD dwBytesInRes;  // length of PNG data
            //     DWORD dwImageOffset; // offset from start of file to PNG data
            //   }
            //   Then PNG blobs appended

            var outStream = new MemoryStream();
            using (var writer = new BinaryWriter(outStream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                // Header
                writer.Write((ushort)0); // Reserved
                writer.Write((ushort)1); // Type=ICON
                writer.Write((ushort)frames.Count);

                // Reserve space for directory entries (we’ll fill offsets later)
                long dirStart = outStream.Position;
                long entrySize = 16; // ICONDIRENTRY is 16 bytes
                outStream.Position = dirStart + frames.Count * entrySize;

                // Write images and collect their offsets/lengths
                var entries = new List<(int Size, uint BytesInRes, uint Offset)>(frames.Count);
                foreach (var frame in frames)
                {
                    uint offset = (uint)outStream.Position;
                    writer.Write(frame.Bytes);
                    uint bytesInRes = (uint)frame.Bytes.Length;
                    entries.Add((frame.Size, bytesInRes, offset));
                    Trace.WriteLine($"IcoBuilder: Wrote {frame.Size}x{frame.Size} at +{offset}, {bytesInRes} bytes.");
                }

                // Go back and write directory entries
                outStream.Position = dirStart;
                foreach (var e in entries)
                {
                    byte dim = (byte)(e.Size == 256 ? 0 : e.Size); // 0 encodes 256
                    writer.Write(dim);                // bWidth
                    writer.Write(dim);                // bHeight
                    writer.Write((byte)0);            // bColorCount (0 for >= 8bpp)
                    writer.Write((byte)0);            // bReserved
                    writer.Write((ushort)0);          // wPlanes (0 for PNG)
                    writer.Write((ushort)0);          // wBitCount (0 for PNG)
                    writer.Write((uint)e.BytesInRes); // dwBytesInRes
                    writer.Write((uint)e.Offset);     // dwImageOffset
                }

                writer.Flush();
            }

            outStream.Position = 0;
            // Create an Icon from the stream. Keep the stream alive while the icon is in use.
            Icon icon = new Icon(outStream);
            Trace.WriteLine("IcoBuilder: Exiting BuildMultiSizePngIco (success).");
            return (icon, outStream);
        }
    }
}
