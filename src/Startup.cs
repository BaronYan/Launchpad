using System;
using System.Windows.Forms;
using Microsoft.Win32;

// ---------------------------------------------------------------------------
// 开机启动：写 HKCU\...\Run，值为 "exe 路径" --silent。
// 只做读写，不判断是否已存在（重复写同样的值无害）。
// ---------------------------------------------------------------------------

namespace Launchpad
{
    internal static class Startup
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Launchpad";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    if (key == null) return false;
                    return key.GetValue(ValueName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null) return;

                    if (enabled)
                    {
                        key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\" --silent");
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }
            }
            catch
            {
            }
        }
    }
}
