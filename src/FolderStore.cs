using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

// ---------------------------------------------------------------------------
// 文件夹与顶层顺序的持久化。
//   FolderGroup      ：一个文件夹（名字、成员、2×2 缩略图）；
//   FolderStore      ：读写 folders.txt（F|id|名字 / A|id|快捷方式路径）；
//   OrderStore       ：读写 order.txt（顶层图标顺序，A|路径 或 F|id）。
// 纯文本格式，方便用户自己备份/编辑；损坏时忽略坏行。
// ---------------------------------------------------------------------------

namespace Launchpad
{
    /// <summary>用户把图标拖到一起形成的“文件夹”。</summary>
    internal sealed class FolderGroup
    {
        public int Id;
        public string Name;
        public readonly List<LaunchItem> Items = new List<LaunchItem>();
        public Bitmap Thumbnail;

        public void BuildThumbnail()
        {
            var bitmap = new Bitmap(64, 64);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (var shape = RoundedRect(new Rectangle(1, 1, 62, 62), 14))
                using (var background = new SolidBrush(Color.FromArgb(86, 255, 255, 255)))
                {
                    g.FillPath(background, shape);
                }
                using (var shape = RoundedRect(new Rectangle(1, 1, 62, 62), 14))
                using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255)))
                {
                    g.DrawPath(pen, shape);
                }

                int count = Math.Min(4, Items.Count);
                int columns = count <= 1 ? 1 : 2;
                int rows = (count + columns - 1) / columns;
                const int size = 26;
                const int gap = 3;

                int totalWidth = columns * size + (columns - 1) * gap;
                int totalHeight = rows * size + (rows - 1) * gap;
                int startX = (64 - totalWidth) / 2;
                int startY = (64 - totalHeight) / 2;

                for (int i = 0; i < count; i++)
                {
                    int column = i % columns;
                    int row = i / columns;
                    var target = new Rectangle(
                        startX + column * (size + gap),
                        startY + row * (size + gap),
                        size, size);

                    if (Items[i].Icon != null) g.DrawImage(Items[i].Icon, target);
                }
            }

            Thumbnail = bitmap;
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int diameter = radius * 2;
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>顶层图标的顺序（应用 / 文件夹），一行一个 key。</summary>
    internal static class OrderStore
    {
        private static string FilePath
        {
            get { return Path.Combine(FolderStore.RootPath, "order.txt"); }
        }

        public static bool Exists
        {
            get
            {
                try { return File.Exists(FilePath); } catch { return false; }
            }
        }

        public static List<string> Load()
        {
            var result = new List<string>();
            try
            {
                if (!File.Exists(FilePath)) return result;

                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string key = line.Trim();
                    if (key.StartsWith("A|") || key.StartsWith("F|")) result.Add(key);
                }
            }
            catch
            {
            }
            return result;
        }

        public static void Save(List<string> order)
        {
            try
            {
                File.WriteAllLines(FilePath, order);
            }
            catch
            {
            }
        }
    }

    internal static class FolderStore
    {
        private static readonly string Root = ResolveRoot();

        internal static string RootPath
        {
            get { return Root; }
        }

        private static string FilePath
        {
            get { return Path.Combine(Root, "folders.txt"); }
        }

        private static string ResolveRoot()
        {
            // 首选 %LOCALAPPDATA%\Launchpad：
            //   · MSIX 打包后会被重定向到包容器（Packages\<包名>\LocalCache），
            //     卸载应用时一起清理，符合商店应用的预期；
            //   · 不走漫游，避免在多台机器之间同步界面布局这类本机状态。
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
            {
                string root = Path.Combine(localAppData, "Launchpad");
                if (TryUse(root))
                {
                    MigrateFromRoaming(root);
                    return root;
                }
            }

            // 兼容旧版本：%APPDATA%\Launchpad
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appData) && TryUse(Path.Combine(appData, "Launchpad")))
            {
                return Path.Combine(appData, "Launchpad");
            }

            // 最后退回 exe 同目录的 data（便携模式 / 受限环境）
            string fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            try
            {
                Directory.CreateDirectory(fallback);
            }
            catch
            {
            }
            return fallback;
        }

        /// <summary>目录存在不代表能写（受限账户、MSIX 容器等），真的写一下再决定。</summary>
        private static bool TryUse(string root)
        {
            try
            {
                Directory.CreateDirectory(root);

                string probe = Path.Combine(root, ".write-test");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 旧版本的配置在 %APPDATA%\Launchpad。首次切到 %LOCALAPPDATA% 时复制过去，
        /// 避免用户升级后“文件夹和排序全丢了”。只在新目录还没有 folders.txt 时执行一次。
        /// </summary>
        private static void MigrateFromRoaming(string newRoot)
        {
            try
            {
                if (File.Exists(Path.Combine(newRoot, "folders.txt"))) return;

                string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(roaming)) return;

                string oldRoot = Path.Combine(roaming, "Launchpad");
                if (!Directory.Exists(oldRoot)) return;

                foreach (string name in new[] { "folders.txt", "order.txt", "settings.txt" })
                {
                    string source = Path.Combine(oldRoot, name);
                    if (!File.Exists(source)) continue;

                    try
                    {
                        File.Copy(source, Path.Combine(newRoot, name), true);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        public static List<FolderGroup> Load(List<LaunchItem> apps)
        {
            var result = new List<FolderGroup>();

            try
            {
                if (!File.Exists(FilePath)) return result;

                var byPath = new Dictionary<string, LaunchItem>(StringComparer.OrdinalIgnoreCase);
                foreach (LaunchItem app in apps) byPath[app.Path] = app;

                FolderGroup current = null;

                foreach (string line in File.ReadAllLines(FilePath))
                {
                    if (line.StartsWith("F|"))
                    {
                        string[] parts = line.Split(new[] { '|' }, 3);
                        if (parts.Length < 3) continue;

                        int id;
                        if (!int.TryParse(parts[1], out id)) continue;

                        current = new FolderGroup();
                        current.Id = id;
                        current.Name = parts[2];
                        result.Add(current);
                    }
                    else if (line.StartsWith("A|") && current != null)
                    {
                        string[] parts = line.Split(new[] { '|' }, 3);
                        if (parts.Length < 3) continue;

                        LaunchItem app;
                        if (byPath.TryGetValue(parts[2], out app)) current.Items.Add(app);
                    }
                }
            }
            catch
            {
            }

            result.RemoveAll(delegate(FolderGroup folder) { return folder.Items.Count == 0; });
            foreach (FolderGroup folder in result) folder.BuildThumbnail();

            return result;
        }

        public static void Save(List<FolderGroup> folders)
        {
            try
            {
                var lines = new List<string>();
                foreach (FolderGroup folder in folders)
                {
                    lines.Add("F|" + folder.Id + "|" + folder.Name);
                    foreach (LaunchItem app in folder.Items)
                    {
                        lines.Add("A|" + folder.Id + "|" + app.Path);
                    }
                }
                File.WriteAllLines(FilePath, lines);
            }
            catch
            {
            }
        }
    }
}




