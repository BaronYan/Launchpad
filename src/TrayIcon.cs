using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// ---------------------------------------------------------------------------
// 托盘图标绘制：16×16 透明底，画一个橙色小鲸鱼轮廓。
// 用 GetHicon 生成 HICON 后 Clone 出来，原始句柄立即销毁，避免 GDI 句柄泄漏。
// ---------------------------------------------------------------------------

namespace Launchpad
{
    internal static class TrayIcon
    {
        /// <summary>托盘图标直接用 exe 内嵌的图标（橙色鲸鱼）。</summary>
        public static Icon Create()
        {
            return DrawFallback();
        }

        private static Icon DrawFallback()
        {
            using (var bitmap = new Bitmap(16, 16))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.None;
                g.Clear(Color.Transparent);

                using (var brush = new SolidBrush(Color.FromArgb(255, 140, 40)))
                {
                    g.FillEllipse(brush, 1, 5, 12, 8);
                    g.FillPolygon(brush, new[] { new Point(2, 8), new Point(0, 3), new Point(6, 6) });
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon temporary = Icon.FromHandle(handle))
                    {
                        return (Icon)temporary.Clone();
                    }
                }
                finally
                {
                    Native.DestroyIcon(handle);
                }
            }
        }
    }
}

