using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

// ---------------------------------------------------------------------------
// 图标提取（64×64，保留透明通道）。
// .lnk 会先用 WScript.Shell 解析出目标程序再取图标；
// 优先用 IShellItemImageFactory（清晰），失败退回 SHGetFileInfo；
// 全部按路径缓存，避免每次开面板都去查磁盘。
// ---------------------------------------------------------------------------

namespace Launchpad
{
    /// <summary>取高清软件图标（默认 64px），保留透明通道；.lnk 会先解析目标程序。</summary>
    internal static class ShellIcons
    {
        private const int IconSize = 64;

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
        }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            void GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object item);

        private static readonly Dictionary<string, Bitmap> Cache =
            new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        private static readonly object Gate = new object();
        private static Bitmap _fallback;

        public static Bitmap Get(string path)
        {
            lock (Gate)
            {
                Bitmap cached;
                if (Cache.TryGetValue(path, out cached)) return cached;

                Bitmap icon = Load(path) ?? Fallback;
                Cache[path] = icon;
                return icon;
            }
        }

        private static Bitmap Load(string path)
        {
            if (string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string target;
                string iconLocation;
                ResolveShortcut(path, out target, out iconLocation);

                // 快捷方式里记录的图标（系统工具常用 dll,索引 的形式）优先。
                Bitmap fromLocation = LoadFromIconLocation(iconLocation);
                if (fromLocation != null) return fromLocation;

                if (!string.IsNullOrEmpty(target) && File.Exists(target))
                {
                    Bitmap fromTarget = LoadShellImage(target);
                    if (fromTarget != null) return fromTarget;
                }
            }

            Bitmap icon = LoadShellImage(path);
            if (icon != null) return icon;

            return LoadSmallIcon(path);
        }

        private static Bitmap LoadFromIconLocation(string location)
        {
            if (string.IsNullOrEmpty(location)) return null;

            string file = location.Trim();
            int index = 0;
            int comma = file.LastIndexOf(',');
            if (comma > 0)
            {
                string indexText = file.Substring(comma + 1).Trim();
                int parsed;
                if (!int.TryParse(indexText, out parsed)) return null;
                index = parsed;
                file = file.Substring(0, comma).Trim();
            }

            file = Environment.ExpandEnvironmentVariables(file);
            if (!File.Exists(file)) return null;

            var icons = new IntPtr[1];
            var ids = new uint[1];
            uint count = Native.PrivateExtractIcons(file, index, IconSize, IconSize, icons, ids, 1, 0);
            if (count == 0 || icons[0] == IntPtr.Zero) return null;

            try
            {
                using (Icon icon = Icon.FromHandle(icons[0]))
                {
                    var bitmap = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        g.Clear(Color.Transparent);
                        g.DrawIcon(icon, new Rectangle(0, 0, IconSize, IconSize));
                    }
                    return bitmap;
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                Native.DestroyIcon(icons[0]);
            }
        }

        private static Bitmap LoadShellImage(string path)
        {
            try
            {
                Guid iid = typeof(IShellItemImageFactory).GUID;
                object item;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out item);

                var factory = (IShellItemImageFactory)item;
                IntPtr hbitmap;
                // SIIGBF_BIGGERSIZEOK(1) | SIIGBF_ICONONLY(4)
                factory.GetImage(new SIZE { cx = IconSize, cy = IconSize }, 1 | 4, out hbitmap);

                try
                {
                    return FromHBitmap(hbitmap);
                }
                finally
                {
                    Native.DeleteObject(hbitmap);
                }
            }
            catch
            {
                return null;
            }
        }

        private static Bitmap FromHBitmap(IntPtr hbitmap)
        {
            var info = new Native.BITMAP();
            if (Native.GetObject(hbitmap, Marshal.SizeOf(typeof(Native.BITMAP)), ref info) == 0) return null;

            int width = info.bmWidth;
            int height = info.bmHeight;
            if (width <= 0 || height <= 0) return null;

            var header = new Native.BITMAPINFO();
            header.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
            header.bmiHeader.biWidth = width;
            header.bmiHeader.biHeight = -height;
            header.bmiHeader.biPlanes = 1;
            header.bmiHeader.biBitCount = 32;
            header.bmiHeader.biCompression = 0;

            byte[] bits = new byte[width * height * 4];
            IntPtr dc = Native.CreateCompatibleDC(IntPtr.Zero);
            try
            {
                Native.GetDIBits(dc, hbitmap, 0, (uint)height, bits, ref header, 0);
            }
            finally
            {
                Native.DeleteDC(dc);
            }

            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(bits, 0, data.Scan0, bits.Length);
            bitmap.UnlockBits(data);
            return bitmap;
        }

        private static Bitmap LoadSmallIcon(string path)
        {
            var info = new Native.SHFILEINFO();
            IntPtr result = Native.SHGetFileInfo(path, 0, ref info,
                (uint)Marshal.SizeOf(typeof(Native.SHFILEINFO)),
                Native.SHGFI_ICON | Native.SHGFI_LARGEICON);

            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;

            try
            {
                using (Icon icon = (Icon)Icon.FromHandle(info.hIcon).Clone())
                {
                    var bitmap = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.Clear(Color.Transparent);
                        g.DrawIcon(icon, new Rectangle(0, 0, IconSize, IconSize));
                    }
                    return bitmap;
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                Native.DestroyIcon(info.hIcon);
            }
        }

        private static void ResolveShortcut(string lnkPath, out string target, out string iconLocation)
        {
            target = null;
            iconLocation = null;

            object shell = null;
            try
            {
                Type type = Type.GetTypeFromProgID("WScript.Shell");
                if (type == null) return;

                shell = Activator.CreateInstance(type);
                object shortcut = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                    null, shell, new object[] { lnkPath });

                object targetValue = shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty,
                    null, shortcut, null);
                object iconValue = shortcut.GetType().InvokeMember("IconLocation", BindingFlags.GetProperty,
                    null, shortcut, null);

                target = targetValue as string;
                iconLocation = iconValue as string;
            }
            catch
            {
            }
            finally
            {
                if (shell != null)
                {
                    try
                    {
                        Marshal.ReleaseComObject(shell);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static Bitmap Fallback
        {
            get
            {
                if (_fallback != null) return _fallback;

                var bitmap = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var brush = new SolidBrush(Color.FromArgb(120, 160, 220)))
                    {
                        g.FillEllipse(brush, 6, 6, IconSize - 12, IconSize - 12);
                    }
                }
                _fallback = bitmap;
                return _fallback;
            }
        }
    }
}
