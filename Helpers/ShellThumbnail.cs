using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace HashDash.Helpers
{
    /// <summary>
    /// Windows Shell thumbnails for local/network files — the same images Explorer shows,
    /// so videos, HEIC (with the HEIF extension), PDFs etc. work without extra decoders.
    /// Falls back to the file-type icon when no thumbnail handler exists.
    /// </summary>
    public static class ShellThumbnail
    {
        public static BitmapSource? Get(string path, int size)
        {
            IShellItemImageFactory? factory = null;
            IntPtr hBitmap = IntPtr.Zero;
            try
            {
                SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out factory);
                if (factory.GetImage(new NativeSize { Width = size, Height = size }, BiggerSizeOk, out hBitmap) != 0)
                    return null;

                var source = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                if (factory != null) Marshal.ReleaseComObject(factory);
            }
        }

        private const int BiggerSizeOk = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            public int Width;
            public int Height;
        }

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(NativeSize size, int flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
