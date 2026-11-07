using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;

// ---------------------------------------------------------------------------
// 应用清单：扫描开始菜单（公共 + 用户），过滤“卸载/帮助/更新”类快捷方式，
// 识别 Windows 自带工具（用于自动归入「系统工具」文件夹），
// 并把用户在配置里手动添加的 exe / lnk / url 合并进来。
// ---------------------------------------------------------------------------

namespace Launchpad
{
    internal sealed class LaunchItem
    {
        public string Path { get; private set; }
        public string Name { get; private set; }
        public Bitmap Icon { get; private set; }
        public bool IsSystem { get; set; }

        public LaunchItem(string path, string name, Bitmap icon)
        {
            Path = path;
            Name = name;
            Icon = icon;
        }
    }

    /// <summary>扫描开始菜单，得到“所有已安装软件”。</summary>
    internal static class AppCatalog
    {
        private static readonly Regex Junk = new Regex(
            "卸载|uninstall|帮助|help|readme|自述|说明|文档|documentation|网站|website|官网|update|更新|license|许可|support|客服|修复|repair|release ?notes",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // 这些目录下的都是 Windows 自带工具
        private static readonly string[] SystemFolders =
        {
            "Windows 系统工具", "Windows Administrative Tools", "Windows 附件",
            "Windows Accessories", "System Tools", "Accessibility", "Windows 轻松使用",
            "Administrative Tools", "Accessories", "Windows PowerShell", "Windows 管理工具"
        };

        // 少量放在开始菜单根目录、靠名字识别的系统工具
        private static readonly HashSet<string> SystemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Administrative Tools", "Command Prompt", "Control Panel", "Run", "Task Manager",
            "Event Viewer", "Registry Editor", "Computer Management", "services",
            "System Configuration", "Resource Monitor", "Performance Monitor", "Disk Cleanup",
            "Character Map", "Magnify", "Narrator", "On-Screen Keyboard", "VoiceAccess",
            "LiveCaptions", "Steps Recorder", "Memory Diagnostics Tool", "Component Services",
            "iSCSI Initiator", "Print Management", "Security Configuration Management",
            "System Information", "Task Scheduler", "Windows PowerShell",
            "Windows PowerShell (x86)", "Windows PowerShell ISE", "Windows PowerShell ISE (x86)",
            "Windows Media Player Legacy", "ODBC Data Sources (32-bit)", "ODBC Data Sources (64-bit)",
            "File Explorer", "Windows PowerShell ISE 5.1", "Windows PowerShell ISE 5.1 (x86)",
            "Remote Desktop Connection", "Windows Defender Firewall with Advanced Security",
            "RecoveryDrive"
        };

        public static List<LaunchItem> Load(AppSettings settings)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var items = new List<LaunchItem>();

            foreach (string programs in Roots())
            {
                if (!Directory.Exists(programs)) continue;

                string[] files;
                try
                {
                    files = Directory.GetFiles(programs, "*.lnk", SearchOption.AllDirectories);
                }
                catch
                {
                    continue;
                }

                foreach (string file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrEmpty(name) || Junk.IsMatch(name)) continue;
                    if (!seen.Add(name)) continue;

                    var item = new LaunchItem(file, name, ShellIcons.Get(file));
                    item.IsSystem = IsSystemItem(file, name);
                    items.Add(item);
                }
            }

            if (settings != null)
            {
                foreach (string path in settings.ExtraApps)
                {
                    try
                    {
                        if (!File.Exists(path)) continue;

                        string name = Path.GetFileNameWithoutExtension(path);
                        if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;

                        var extra = new LaunchItem(path, name, ShellIcons.Get(path));
                        extra.IsSystem = false;
                        items.Add(extra);
                    }
                    catch
                    {
                    }
                }
            }

            items.Sort(delegate(LaunchItem a, LaunchItem b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });

            return items;
        }

        private static bool IsSystemItem(string path, string name)
        {
            foreach (string folder in SystemFolders)
            {
                if (path.IndexOf(folder, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            return SystemNames.Contains(name);
        }

        private static IEnumerable<string> Roots()
        {
            string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
            string user = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);

            if (!string.IsNullOrEmpty(common)) yield return Path.Combine(common, "Programs");
            if (!string.IsNullOrEmpty(user)) yield return Path.Combine(user, "Programs");
        }
    }
}


