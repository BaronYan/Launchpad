using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// ---------------------------------------------------------------------------
// 桌面快捷方式创建（通过 WScript.Shell COM + 反射，避免依赖 dynamic）。
// 首次运行时自动建一个，方便用户固定到任务栏/开始屏幕。
// ---------------------------------------------------------------------------

namespace Launchpad
{
    /// <summary>创建桌面快捷方式（Windows 不允许程序自己把图标钉到任务栏，只能给用户一个可右键固定的入口）。</summary>
    internal static class Shortcut
    {
        public static string DesktopLink
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "Launchpad.lnk");
            }
        }

        public static bool EnsureDesktop()
        {
            try
            {
                if (File.Exists(DesktopLink)) return false;
                return Create(DesktopLink);
            }
            catch
            {
                return false;
            }
        }

        public static bool Create(string shortcutPath)
        {
            object shell = null;
            try
            {
                string target = Application.ExecutablePath;
                Type type = Type.GetTypeFromProgID("WScript.Shell");
                if (type == null) return false;

                shell = Activator.CreateInstance(type);
                object shortcut = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                    null, shell, new object[] { shortcutPath });

                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { target });
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut,
                    new object[] { Path.GetDirectoryName(target) });
                shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut,
                    new object[] { target + ",0" });
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);

                return true;
            }
            catch
            {
                return false;
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
    }
}
