using System;
using System.Windows.Forms;
using Microsoft.Win32;

// ---------------------------------------------------------------------------
// Win11 托盘图标“提升”工具。
// 系统把新图标默认放进 ^ 溢出区，对应设置存在：
//   HKCU\Control Panel\NotifyIconSettings\<哈希>\IsPromoted
// 这里找到本程序对应的项并置 1（等价于用户手动把图标拖出来）。
// 注意：资源管理器需要重启一次才会重新读取该设置。
// ---------------------------------------------------------------------------

namespace Launchpad
{
    /// <summary>
    /// Windows 11 会把新托盘图标折叠进 ^。这里把本程序的图标写进 IsPromoted=1，
    /// 让系统把它放到可见的托盘区域（和用户手动拖出来是同一个设置）。
    /// </summary>
    internal static class TrayPromoter
    {
        private const string SettingsKey = @"Control Panel\NotifyIconSettings";

        public static bool Promote()
        {
            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(SettingsKey, true))
                {
                    if (root == null) return false;

                    bool found = false;

                    foreach (string name in root.GetSubKeyNames())
                    {
                        using (RegistryKey key = root.OpenSubKey(name, true))
                        {
                            if (key == null) continue;

                            string path = key.GetValue("ExecutablePath") as string;
                            if (string.IsNullOrEmpty(path)) continue;
                            if (!string.Equals(path, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) continue;

                            found = true;

                            object value = key.GetValue("IsPromoted");
                            if (value == null || Convert.ToInt32(value) != 1)
                            {
                                key.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                            }
                        }
                    }

                    return found;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
