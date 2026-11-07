using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

// ---------------------------------------------------------------------------
// 配置模型 + 读写（%APPDATA%\Launchpad\settings.txt）。
// 字段：背景模式/图片/颜色/透明度、图标缩放、热键、关闭行为、自定义应用列表。
// 读取时容错：文件不存在或字段非法一律回落到默认值，保证程序永远能启动。
// ---------------------------------------------------------------------------

namespace Launchpad
{
    /// <summary>控制面板里可调的配置，存放到 %APPDATA%\Launchpad\settings.txt。</summary>
    internal sealed class AppSettings
    {
        public string BackgroundMode = "color";       // color | image
        public string BackgroundImage = "";
        public Color BackgroundColor = Color.FromArgb(24, 28, 40);
        public int BackgroundOpacity = 80;             // 0-100
        public bool CtrlWheelZoom = true;
        public int IconScalePercent = 100;             // 70-180
        public uint HotkeyModifiers = Native.MOD_CONTROL | Native.MOD_ALT;
        public uint HotkeyKey = Native.VK_SPACE;
        public string CloseBehavior = "ask";          // ask | exit | tray

        public readonly List<string> ExtraApps = new List<string>();

        public static string FilePath
        {
            get { return Path.Combine(FolderStore.RootPath, "settings.txt"); }
        }

        public float IconScale
        {
            get { return Math.Max(0.7f, Math.Min(1.8f, IconScalePercent / 100f)); }
        }

        public string HotkeyText
        {
            get { return FormatHotkey(HotkeyModifiers, HotkeyKey); }
        }

        public static string FormatHotkey(uint modifiers, uint key)
        {
            var parts = new List<string>();
            if ((modifiers & Native.MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((modifiers & Native.MOD_ALT) != 0) parts.Add("Alt");
            if ((modifiers & Native.MOD_SHIFT) != 0) parts.Add("Shift");

            string keyName = ((Keys)key).ToString();
            if (key == (uint)Keys.Escape) keyName = "Esc";
            else if (key == (uint)Keys.Return) keyName = "Enter";
            else if (key == (uint)Keys.Back) keyName = "Backspace";
            else if (key == (uint)Keys.Tab) keyName = "Tab";
            parts.Add(keyName);
            return string.Join(" + ", parts.ToArray());
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();

            try
            {
                if (!File.Exists(FilePath)) return settings;

                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string name = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();

                    switch (name)
                    {
                        case "background_mode": settings.BackgroundMode = (value == "image") ? "image" : "color"; break;
                        case "background_image": settings.BackgroundImage = value; break;
                        case "background_color": settings.BackgroundColor = ParseColor(value); break;
                        case "background_opacity": settings.BackgroundOpacity = ClampInt(value, 0, 100, 80); break;
                        case "ctrl_wheel_zoom": settings.CtrlWheelZoom = value == "1"; break;
                        case "icon_scale": settings.IconScalePercent = ClampInt(value, 70, 180, 100); break;
                        case "hotkey_modifiers": settings.HotkeyModifiers = ParseUInt(value, settings.HotkeyModifiers); break;
                        case "hotkey_key": settings.HotkeyKey = ParseUInt(value, settings.HotkeyKey); break;
                        case "close_behavior": settings.CloseBehavior = value; break;
                        case "extra_app":
                            if (value.Length > 0 && !settings.ExtraApps.Contains(value)) settings.ExtraApps.Add(value);
                            break;
                    }
                }
            }
            catch
            {
            }

            return settings;
        }

        public void Save()
        {
            try
            {
                var lines = new List<string>();
                lines.Add("# Launchpad 配置");
                lines.Add("background_mode=" + BackgroundMode);
                lines.Add("background_image=" + BackgroundImage);
                lines.Add("background_color=" + ToHex(BackgroundColor));
                lines.Add("background_opacity=" + BackgroundOpacity);
                lines.Add("ctrl_wheel_zoom=" + (CtrlWheelZoom ? "1" : "0"));
                lines.Add("icon_scale=" + IconScalePercent);
                lines.Add("hotkey_modifiers=" + HotkeyModifiers);
                lines.Add("hotkey_key=" + HotkeyKey);
                lines.Add("close_behavior=" + CloseBehavior);

                foreach (string app in ExtraApps)
                {
                    lines.Add("extra_app=" + app);
                }

                File.WriteAllLines(FilePath, lines);
            }
            catch
            {
            }
        }

        private static string ToHex(Color color)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        private static Color ParseColor(string text)
        {
            try
            {
                string value = text.TrimStart('#');
                if (value.Length == 6)
                {
                    int rgb = int.Parse(value, NumberStyles.HexNumber);
                    return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
                }
            }
            catch
            {
            }
            return Color.FromArgb(24, 28, 40);
        }

        private static int ClampInt(string text, int min, int max, int fallback)
        {
            int value;
            if (!int.TryParse(text, out value)) return fallback;
            return Math.Max(min, Math.Min(max, value));
        }

        private static uint ParseUInt(string text, uint fallback)
        {
            uint value;
            if (!uint.TryParse(text, out value)) return fallback;
            return value;
        }
    }
}



