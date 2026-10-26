using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace LumiDisplay;

/// <summary>
/// Writes the <see cref="Logo"/> mark as a multi-resolution .ico file.
/// Run with: LumiDisplay.exe --export-icon lumidisplay.ico
///
/// Sizes up to 64px are stored as 32-bit DIBs (best compatibility with GDI / NotifyIcon);
/// 128px and 256px are stored PNG-compressed, as Windows Vista+ expects.
/// </summary>
internal static class IconExporter
{
    private static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    public static void Export(string path)
    {
        var images = new List<(int size, byte[] data)>();
        foreach (int size in Sizes)
        {
            using var bmp = Logo.ToBitmap(size);
            images.Add((size, size >= 128 ? EncodePng(bmp) : EncodeDib(bmp)));
        }

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);

        // ICONDIR
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)images.Count);

        // ICONDIRENTRY list
        int offset = 6 + 16 * images.Count;
        foreach (var (size, data) in images)
        {
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);       // palette colors
            w.Write((byte)0);       // reserved
            w.Write((ushort)1);     // planes
            w.Write((ushort)32);    // bits per pixel
            w.Write(data.Length);
            w.Write(offset);
            offset += data.Length;
        }

        foreach (var (_, data) in images)
            w.Write(data);
    }

    private static byte[] EncodePng(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static byte[] EncodeDib(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        int maskStride = ((w + 31) / 32) * 4;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // BITMAPINFOHEADER (height is doubled to include the AND mask)
        bw.Write(40);
        bw.Write(w);
        bw.Write(h * 2);
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write(0);                                // BI_RGB
        bw.Write(w * h * 4 + maskStride * h);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);

        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[w * 4];
            var alpha = new bool[w * h];

            // XOR (color) bitmap, bottom-up BGRA
            for (int y = h - 1; y >= 0; y--)
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                bw.Write(row);
                for (int x = 0; x < w; x++)
                    alpha[y * w + x] = row[x * 4 + 3] != 0;
            }

            // AND mask, bottom-up: 1 = transparent (for legacy renderers that ignore alpha)
            var maskRow = new byte[maskStride];
            for (int y = h - 1; y >= 0; y--)
            {
                Array.Clear(maskRow);
                for (int x = 0; x < w; x++)
                {
                    if (!alpha[y * w + x])
                        maskRow[x / 8] |= (byte)(0x80 >> (x % 8));
                }
                bw.Write(maskRow);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        return ms.ToArray();
    }
}
