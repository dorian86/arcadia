using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Arcadia.Services;

/// <summary>Extracts the high resolution (256 px) shell icon of a file and saves it as PNG. Call from an STA thread.</summary>
public static class IconExtractor
{
    public static bool TryExtract(string sourcePath, string destinationPng, int size = 256)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return false;
        sourcePath = sourcePath.Replace('/', '\\');
        if (!(File.Exists(sourcePath) || Directory.Exists(sourcePath))) return false;
        IntPtr hBitmap = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(sourcePath, IntPtr.Zero, ref iid, out var factory);
            try
            {
                var hr = factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out hBitmap);
                if (hr != 0 || hBitmap == IntPtr.Zero) return false;
            }
            finally { Marshal.ReleaseComObject(factory); }

            var source = FromHBitmap(hBitmap);
            if (source == null) return false;

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            var tmp = destinationPng + ".tmp";
            using (var fs = File.Create(tmp)) encoder.Save(fs);
            File.Move(tmp, destinationPng, true);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Icon extraction failed for {sourcePath}: {ex.Message}");
            return false;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
        }
    }

    private static BitmapSource? FromHBitmap(IntPtr hBitmap)
    {
        var size = Marshal.SizeOf<DIBSECTION>();
        if (GetObject(hBitmap, size, out var dib) == size && dib.dsBm.bmBits != IntPtr.Zero && dib.dsBm.bmBitsPixel == 32)
        {
            int w = dib.dsBm.bmWidth, h = dib.dsBm.bmHeight, stride = w * 4;
            var pixels = new byte[stride * h];
            Marshal.Copy(dib.dsBm.bmBits, pixels, 0, pixels.Length);
            if (dib.dsBmih.biHeight > 0)
            {
                // bottom-up DIB: flip rows
                var flipped = new byte[pixels.Length];
                for (var y = 0; y < h; y++)
                    Buffer.BlockCopy(pixels, y * stride, flipped, (h - 1 - y) * stride, stride);
                pixels = flipped;
            }
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
            bmp.Freeze();
            return bmp;
        }

        var fallback = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        fallback.Freeze();
        return fallback;
    }

    private const int SIIGBF_BIGGERSIZEOK = 0x1;
    private const int SIIGBF_ICONONLY = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx; public int cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DIBSECTION
    {
        public BITMAP dsBm;
        public BITMAPINFOHEADER dsBmih;
        public uint dsBitfield0, dsBitfield1, dsBitfield2;
        public IntPtr dshSection;
        public uint dsOffset;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int c, out DIBSECTION pv);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
}
