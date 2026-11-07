using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;

// ---------------------------------------------------------------------------
// 全屏 Launchpad 面板（无边框、置顶、覆盖整个主屏）。
// 功能：
//   - 背景：纯色或自定义图片 + 透明度，画在一张离屏位图上；
//   - 图标网格：按窗口尺寸自动算行列，支持分页（滚轮/方向键）、Ctrl+滚轮缩放；
//   - 文件夹：拖动图标互相合并、弹层展示、双击标题改名、拖出弹层移出；
//   - 搜索：顶层 + 文件夹内全部参与，不区分大小写的包含匹配；
//   - 右键菜单：设置、刷新、开机启动、退出。
// 绘制采用自绘（OnPaint），控件只有搜索框和改名输入框两个。
// ---------------------------------------------------------------------------

namespace Launchpad
{
    internal sealed class Entry
    {
        public LaunchItem App;
        public FolderGroup Folder;

        public bool IsFolder { get { return Folder != null; } }
        public string Name { get { return IsFolder ? Folder.Name : App.Name; } }
        public Bitmap Icon { get { return IsFolder ? Folder.Thumbnail : App.Icon; } }
    }

    /// <summary>全屏 Launchpad：模糊壁纸 + 搜索 + 图标网格 + 文件夹（拖拽合并 / 重命名）。</summary>
    internal sealed class LaunchpadForm : Form
    {
        private const int BaseCellW = 160;
        private const int BaseCellH = 150;
        private const int BaseIconPx = 64;
        private const int GridTop = 150;
        private const int GridTopFolder = 170;
        private const int GridBottom = 90;

        private readonly TextBox _search;
        private readonly Panel _searchHost;
        private readonly Label _placeholder;
        private readonly Timer _fade;
        private readonly Timer _closeTimer;
        private readonly List<Rectangle> _cells = new List<Rectangle>();
        private readonly List<ContextMenuStrip> _menus = new List<ContextMenuStrip>();
        private readonly bool _hotkeyOk;
        private readonly AppSettings _settings;

        private List<LaunchItem> _apps;
        private List<FolderGroup> _folders;
        private List<string> _order;
        private List<Entry> _entries;
        private List<Entry> _view;

        private Bitmap _backdrop;
        private FolderGroup _openFolder;
        private int _page;
        private int _hover = -1;
        private int _hoverName = -1;
        private int _folderHover = -1;
        private readonly List<Rectangle> _folderCells = new List<Rectangle>();
        private Rectangle _pencilRect;
        private bool _menuOpen;
        private bool _activated;
        private int _nextFolderId = 1;

        private Entry _dragEntry;
        private FolderGroup _dragSourceFolder;
        private int _dragIndex = -1;
        private Point _dragStart;
        private Point _dragPoint;
        private bool _dragging;

        private TextBox _renameBox;
        private FolderGroup _renaming;
        private bool _endingRename;
        private bool _renameCancelled;
        private int _renameCommittedAt;
        private bool _suppressClickAfterRename;

        private Rectangle _backRect;

        public event EventHandler RefreshRequested;
        public event EventHandler SettingsRequested;
        public event EventHandler ExitRequested;

        private int CellW { get { return Math.Max(110, (int)(BaseCellW * IconScaleFactor)); } }
        private int CellH { get { return Math.Max(100, (int)(BaseCellH * IconScaleFactor)); } }
        private int IconPx { get { return Math.Max(32, (int)(BaseIconPx * IconScaleFactor)); } }
        private float IconScaleFactor { get { return _settings == null ? 1f : _settings.IconScale; } }

        public LaunchpadForm(List<LaunchItem> items, bool hotkeyOk, AppSettings settings)
        {
            _apps = items ?? new List<LaunchItem>();
            _hotkeyOk = hotkeyOk;
            _settings = settings ?? new AppSettings();

            Text = "Launchpad";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(10, 10, 14);
            Font = new Font("Microsoft YaHei UI", 9.5f);
            KeyPreview = true;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            Bounds = Screen.PrimaryScreen.Bounds;
            Opacity = 0;

            _searchHost = new Panel();
            _searchHost.Size = new Size(460, 46);
            _searchHost.BackColor = Color.FromArgb(40, 40, 46);
            _searchHost.Left = (ClientSize.Width - _searchHost.Width) / 2;
            _searchHost.Top = 54;
            _searchHost.Region = new Region(RoundedPath(new Rectangle(0, 0, _searchHost.Width, _searchHost.Height), 23));
            _searchHost.Paint += OnSearchHostPaint;

            _search = new TextBox();
            _search.BorderStyle = BorderStyle.None;
            _search.Font = new Font("Microsoft YaHei UI", 12f);
            _search.ForeColor = Color.White;
            _search.BackColor = Color.FromArgb(40, 40, 46);
            _search.Location = new Point(20, 12);
            _search.Width = _searchHost.Width - 40;
            _search.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            _search.TextChanged += delegate
            {
                _placeholder.Visible = _search.Text.Length == 0;
                ApplyFilter();
            };

            _placeholder = new Label();
            _placeholder.AutoSize = true;
            _placeholder.Text = "搜索软件";
            _placeholder.Font = new Font("Microsoft YaHei UI", 12f);
            _placeholder.ForeColor = Color.FromArgb(150, 255, 255, 255);
            _placeholder.BackColor = Color.FromArgb(40, 40, 46);
            _placeholder.Location = new Point(22, 13);
            _placeholder.Cursor = Cursors.IBeam;
            _placeholder.Click += delegate { _search.Focus(); };

            _searchHost.Controls.Add(_search);
            _searchHost.Controls.Add(_placeholder);
            _placeholder.BringToFront();

            Controls.Add(_searchHost);

            _fade = new Timer();
            _fade.Interval = 15;
            _fade.Tick += OnFadeTick;

            _closeTimer = new Timer();
            _closeTimer.Interval = 220;
            _closeTimer.Tick += OnCloseTick;

            Activated += delegate { _activated = true; };
            Deactivate += delegate
            {
                if (!_activated) return;
                _closeTimer.Stop();
                _closeTimer.Start();
            };

            RebuildAll();
        }

        public void ShowOverlay()
        {
            _backdrop = CreateBackdrop(Bounds);
            Show();
            Activate();
        }

        public void SetItems(List<LaunchItem> items)
        {
            _apps = items ?? new List<LaunchItem>();
            RebuildAll();
        }

        // ---------------- 数据 ----------------

        private void RebuildAll()
        {
            _openFolder = null;
            _page = 0;
            EndRename(false);

            _folders = FolderStore.Load(_apps);
            _nextFolderId = 1;
            foreach (FolderGroup folder in _folders)
            {
                if (folder.Id >= _nextFolderId) _nextFolderId = folder.Id + 1;
            }

            bool hasOrder = OrderStore.Exists;
            _order = OrderStore.Load();

            if (!hasOrder) EnsureSystemFolder();
            else EnsureOrderComplete();

            BuildEntries();
        }

        /// <summary>第一次运行时，把 Windows 自带工具收进「系统工具」文件夹。</summary>
        private void EnsureSystemFolder()
        {
            var system = new List<LaunchItem>();
            foreach (LaunchItem app in _apps)
            {
                if (app.IsSystem) system.Add(app);
            }

            _order.Clear();

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (system.Count >= 3)
            {
                var folder = new FolderGroup();
                folder.Id = _nextFolderId++;
                folder.Name = "系统工具";
                foreach (LaunchItem app in system)
                {
                    folder.Items.Add(app);
                    used.Add(app.Path);
                }
                folder.BuildThumbnail();
                _folders.Add(folder);
            }

            foreach (LaunchItem app in _apps)
            {
                if (!used.Contains(app.Path)) _order.Add("A|" + app.Path);
            }
            foreach (FolderGroup folder in _folders)
            {
                _order.Add("F|" + folder.Id);
            }

            FolderStore.Save(_folders);
            OrderStore.Save(_order);
        }

        private void EnsureOrderComplete()
        {
            var inFolder = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FolderGroup folder in _folders)
            {
                foreach (LaunchItem app in folder.Items) inFolder.Add(app.Path);
            }

            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cleaned = new List<string>();

            foreach (string key in _order)
            {
                if (key.StartsWith("A|"))
                {
                    string path = key.Substring(2);
                    if (inFolder.Contains(path)) continue;
                    bool exists = false;
                    foreach (LaunchItem app in _apps)
                    {
                        if (string.Equals(app.Path, path, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
                    }
                    if (!exists || !known.Add(key)) continue;
                    cleaned.Add(key);
                }
                else if (key.StartsWith("F|"))
                {
                    int id;
                    if (!int.TryParse(key.Substring(2), out id)) continue;
                    if (!FolderExists(id) || !known.Add(key)) continue;
                    cleaned.Add(key);
                }
            }

            foreach (LaunchItem app in _apps)
            {
                if (inFolder.Contains(app.Path)) continue;
                string key = "A|" + app.Path;
                if (known.Add(key)) cleaned.Add(key);
            }
            foreach (FolderGroup folder in _folders)
            {
                string key = "F|" + folder.Id;
                if (known.Add(key)) cleaned.Add(key);
            }

            _order = cleaned;
            OrderStore.Save(_order);
        }

        private bool FolderExists(int id)
        {
            foreach (FolderGroup folder in _folders)
            {
                if (folder.Id == id) return true;
            }
            return false;
        }

        private void BuildEntries()
        {
            var inFolder = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FolderGroup folder in _folders)
            {
                foreach (LaunchItem app in folder.Items) inFolder.Add(app.Path);
            }

            var appByPath = new Dictionary<string, LaunchItem>(StringComparer.OrdinalIgnoreCase);
            foreach (LaunchItem app in _apps) appByPath[app.Path] = app;

            var folderById = new Dictionary<int, FolderGroup>();
            foreach (FolderGroup folder in _folders) folderById[folder.Id] = folder;

            var entries = new List<Entry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string key in _order)
            {
                if (key.StartsWith("A|"))
                {
                    string path = key.Substring(2);
                    LaunchItem app;
                    if (!appByPath.TryGetValue(path, out app)) continue;
                    if (inFolder.Contains(path)) continue;
                    if (!seen.Add(key)) continue;
                    entries.Add(new Entry { App = app });
                }
                else if (key.StartsWith("F|"))
                {
                    int id;
                    if (!int.TryParse(key.Substring(2), out id)) continue;
                    FolderGroup folder;
                    if (!folderById.TryGetValue(id, out folder)) continue;
                    if (!seen.Add(key)) continue;
                    entries.Add(new Entry { Folder = folder });
                }
            }

            _entries = entries;
            ApplyFilter();
        }

        private void SaveAll()
        {
            FolderStore.Save(_folders);
            OrderStore.Save(_order);
        }

        private void RemoveFromOrder(string key)
        {
            _order.RemoveAll(delegate(string item) { return string.Equals(item, key, StringComparison.OrdinalIgnoreCase); });
        }

        // ---------------- 生命周期 ----------------

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _search.Focus();
            _fade.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _fade.Stop();
            _closeTimer.Stop();

            foreach (ContextMenuStrip menu in _menus)
            {
                try { menu.Dispose(); } catch { }
            }
            _menus.Clear();

            if (_backdrop != null)
            {
                _backdrop.Dispose();
                _backdrop = null;
            }
            base.OnFormClosed(e);
        }

        private void OnFadeTick(object sender, EventArgs e)
        {
            Opacity += 0.14;
            if (Opacity >= 1)
            {
                Opacity = 1;
                _fade.Stop();
            }
        }

        private void OnCloseTick(object sender, EventArgs e)
        {
            _closeTimer.Stop();
            if (IsDisposed || Disposing || _menuOpen || _renaming != null) return;
            if (!ContainsFocus) Close();
        }

        private void OnSearchHostPaint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Color.FromArgb(38, 255, 255, 255)))
            using (GraphicsPath path = RoundedPath(new Rectangle(0, 0, _searchHost.Width - 1, _searchHost.Height - 1), 23))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }

        // ---------------- 绘制 ----------------

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                PaintCore(e);
            }
            catch (Exception ex)
            {
                Program.Log(ex);
            }
        }

        private void PaintCore(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            if (_backdrop != null) g.DrawImage(_backdrop, ClientRectangle);



            int cols, rows, startX, top;
            ComputeLayout(out cols, out rows, out startX, out top);

            int pageSize = cols * rows;
            int start = _page * pageSize;
            int end = Math.Min(_view.Count, start + pageSize);

            _cells.Clear();
            for (int i = start; i < end; i++)
            {
                int index = i - start;
                int col = index % cols;
                int row = index / cols;

                var cell = new Rectangle(startX + col * CellW, top + row * CellH, CellW, CellH);
                _cells.Add(cell);
                DrawItem(g, _view[i], cell, index == _hover, index == _hoverName);
            }

            if (_view.Count == 0)
            {
                using (var font = new Font("Microsoft YaHei UI", 13f))
                using (var brush = new SolidBrush(Color.FromArgb(210, 255, 255, 255)))
                {
                    string text = _apps.Count == 0 ? "正在加载软件…" : "没有匹配的软件";
                    SizeF size = g.MeasureString(text, font);
                    g.DrawString(text, font, brush, (ClientSize.Width - size.Width) / 2, ClientSize.Height / 2 - 40);
                }
            }
            else
            {
                DrawDots(g, pageSize);
            }

            DrawHint(g);

            if (_openFolder != null) DrawFolderPopup(g);

            if (_dragging) DrawDragFeedback(g);
        }

        private void DrawFolderHeader(Graphics g)
        {
            using (var font = new Font("Microsoft YaHei UI", 10f))
            {
                _backRect = new Rectangle(20, 96, 96, 34);
                TextRenderer.DrawText(g, "‹ 返回", font, _backRect,
                    Color.FromArgb(230, 255, 255, 255),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            }

            using (var font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, _openFolder.Name, font, new Rectangle(0, 96, ClientSize.Width, 38),
                    Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        private void ComputeLayout(out int cols, out int rows, out int startX, out int top)
        {
            cols = Math.Max(4, Math.Min(8, (ClientSize.Width - 120) / CellW));
            rows = Math.Max(2, (ClientSize.Height - GridTop - GridBottom) / CellH);
            startX = (ClientSize.Width - cols * CellW) / 2;
            top = GridTop;
        }

        private int PageSize
        {
            get
            {
                int cols, rows, startX, top;
                ComputeLayout(out cols, out rows, out startX, out top);
                return cols * rows;
            }
        }

        private Rectangle CellRect(int viewIndex)
        {
            int cols, rows, startX, top;
            ComputeLayout(out cols, out rows, out startX, out top);
            int pageSize = cols * rows;
            int local = viewIndex % pageSize;
            return new Rectangle(startX + (local % cols) * CellW, top + (local / cols) * CellH, CellW, CellH);
        }

        private void DrawItem(Graphics g, Entry entry, Rectangle cell, bool hot, bool nameHot)
        {
            if (hot)
            {
                Rectangle highlight = cell;
                highlight.Inflate(-8, -8);
                using (GraphicsPath path = RoundedPath(highlight, 12))
                using (var brush = new SolidBrush(Color.FromArgb(36, 255, 255, 255)))
                {
                    g.FillPath(brush, path);
                }
            }

            int iconX = cell.X + (cell.Width - IconPx) / 2;
            int iconY = cell.Y + 18;

            if (entry.Icon != null)
            {
                g.DrawImage(entry.Icon, new Rectangle(iconX, iconY, IconPx, IconPx));
            }

            var label = new Rectangle(cell.X + 6, iconY + IconPx + 8, cell.Width - 12, 42);
            var shadow = label;
            shadow.Offset(1, 1);
            TextRenderer.DrawText(g, entry.Name, Font, shadow, Color.FromArgb(170, 0, 0, 0),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, entry.Name, Font, label, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (entry.IsFolder && nameHot)
            {
                Size textSize = TextRenderer.MeasureText(entry.Name, Font);
                int textWidth = Math.Min(textSize.Width, cell.Width - 12);
                int textRight = cell.X + cell.Width / 2 + textWidth / 2;
                int pencilX = Math.Min(textRight + 6, cell.Right - 24);
                int pencilY = label.Y + 2;
                _pencilRect = new Rectangle(pencilX - 2, pencilY - 2, 16, 18);
                DrawPencil(g, pencilX, pencilY);
            }
        }

        private static void DrawPencil(Graphics g, int x, int y)
        {
            using (var pen = new Pen(Color.FromArgb(230, 255, 255, 255), 2f))
            {
                g.DrawLine(pen, x + 1, y + 10, x + 9, y + 2);
                g.DrawLine(pen, x + 8, y + 1, x + 11, y + 4);
                g.DrawLine(pen, x + 1, y + 10, x, y + 12);
                g.DrawLine(pen, x, y + 12, x + 2, y + 11);
            }
        }

        private void DrawDragFeedback(Graphics g)
        {
            int target = HitTest(_dragPoint);
            if (target >= 0 && target < _cells.Count)
            {
                using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 2f))
                using (GraphicsPath path = RoundedPath(_cells[target], 14))
                {
                    g.DrawPath(pen, path);
                }
            }

            if (_dragEntry != null && _dragEntry.Icon != null)
            {
                var attributes = new ImageAttributes();
                var matrix = new ColorMatrix();
                matrix.Matrix33 = 0.8f;
                attributes.SetColorMatrix(matrix);

                g.DrawImage(_dragEntry.Icon,
                    new Rectangle(_dragPoint.X - 32, _dragPoint.Y - 32, 64, 64),
                    0, 0, _dragEntry.Icon.Width, _dragEntry.Icon.Height,
                    GraphicsUnit.Pixel, attributes);

                attributes.Dispose();
            }
        }

        private void DrawDots(Graphics g, int pageSize)
        {
            int pages = Math.Max(1, (int)Math.Ceiling((double)_view.Count / Math.Max(1, pageSize)));
            if (pages <= 1) return;

            const int spacing = 18;
            int y = ClientSize.Height - 46;
            int x = (ClientSize.Width - pages * spacing) / 2;

            for (int i = 0; i < pages; i++)
            {
                Color color = i == _page ? Color.White : Color.FromArgb(90, 255, 255, 255);
                using (var brush = new SolidBrush(color))
                {
                    g.FillEllipse(brush, x + i * spacing, y, 7, 7);
                }
            }
        }

        private void DrawHint(Graphics g)
        {
            string text = _openFolder != null
                ? "Esc 收起文件夹   ·   点弹层外面也能收起   ·   把图标拖出弹层可移出文件夹"
                : (_hotkeyOk
                    ? "Ctrl+Alt+Space 打开 / 关闭   ·   Esc 关闭   ·   拖动图标合并成文件夹   ·   悬停名称可重命名"
                    : "热键 Ctrl+Alt+Space 被占用：点任务栏窗口打开");

            using (var font = new Font("Microsoft YaHei UI", 8.5f))
            using (var brush = new SolidBrush(Color.FromArgb(115, 255, 255, 255)))
            {
                g.DrawString(text, font, brush, 26, ClientSize.Height - 38);
            }
        }

        // ---------------- 改名 ----------------

        private void BeginRename(FolderGroup folder, int viewIndex)
        {
            if (folder == null) return;

            Rectangle cell = CellRect(viewIndex);
            int labelTop = cell.Y + 18 + IconPx + 6;
            BeginRenameAt(folder, new Rectangle(cell.X + 12, labelTop, cell.Width - 24, 26));
        }

        private void BeginRenameAt(FolderGroup folder, Rectangle box)
        {
            if (folder == null) return;

            EndRename(false);

            if (_renameBox == null)
            {
                _renameBox = new TextBox();
                _renameBox.BorderStyle = BorderStyle.FixedSingle;
                _renameBox.Font = new Font("Microsoft YaHei UI", 9.5f);
                _renameBox.TextAlign = HorizontalAlignment.Center;
                _renameBox.BackColor = Color.FromArgb(46, 46, 54);
                _renameBox.ForeColor = Color.White;
                _renameBox.Visible = false;
                _renameBox.KeyDown += OnRenameKeyDown;
                _renameBox.LostFocus += delegate { EndRename(true); };
                Controls.Add(_renameBox);
            }

            _renaming = folder;
            _renameCancelled = false;
            _renameBox.Bounds = box;
            _renameBox.Text = folder.Name;
            _renameBox.Visible = true;
            _renameBox.BringToFront();
            _renameBox.Focus();
            _renameBox.SelectAll();
        }

        private void OnRenameKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                EndRename(true);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                _renameCancelled = true;
                EndRename(false);
            }
        }

        private void EndRename(bool commit)
        {
            if (_renameBox == null || _renaming == null || _endingRename) return;

            _endingRename = true;
            try
            {
                if (commit && !_renameCancelled)
                {
                    string name = _renameBox.Text.Trim();
                    if (name.Length > 0 && name != _renaming.Name)
                    {
                        _renaming.Name = name;
                        FolderStore.Save(_folders);
                        Invalidate();
                    }

                    // 这次提交是点击空白触发的：吞掉紧接着的这次点击，不关闭面板
                    _renameCommittedAt = Environment.TickCount;
                }

                _renaming = null;
                _renameBox.Visible = false;

                if (!IsDisposed)
                {
                    Activate();
                    Focus();
                }
            }
            finally
            {
                _endingRename = false;
            }
        }

        // ---------------- 交互 ----------------

        private int HitTest(Point point)
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                if (_cells[i].Contains(point)) return i;
            }
            return -1;
        }

        private Entry EntryAt(int cellIndex)
        {
            int index = _page * PageSize + cellIndex;
            return index >= 0 && index < _view.Count ? _view[index] : null;
        }

        private bool IsInsideGrid(Point point)
        {
            int cols, rows, startX, top;
            ComputeLayout(out cols, out rows, out startX, out top);
            return new Rectangle(startX, top, cols * CellW, rows * CellH).Contains(point);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button != MouseButtons.Left) return;

            // 搜索结果只是用来启动的，拖动会把文件夹内外搞乱
            if (_search.Text.Trim().Length > 0) return;

            // 正在改名时，点输入框以外的任何地方都直接提交，不需要回车
            if (_renaming != null)
            {
                if (_renameBox != null && _renameBox.Visible && _renameBox.Bounds.Contains(e.Location))
                {
                    return;
                }

                _suppressClickAfterRename = true;
                EndRename(true);
                return;
            }

            if (_openFolder != null)
            {
                int folderIndex = FolderHitTest(e.Location);
                if (folderIndex < 0 || folderIndex >= _openFolder.Items.Count) return;

                _dragIndex = -1;
                _dragSourceFolder = _openFolder;
                _dragEntry = new Entry { App = _openFolder.Items[folderIndex] };
                _dragStart = e.Location;
                _dragPoint = e.Location;
                _dragging = false;
                return;
            }

            int index = HitTest(e.Location);
            if (index < 0) return;

            _dragSourceFolder = null;
            _dragIndex = index;
            _dragEntry = EntryAt(index);
            _dragStart = e.Location;
            _dragPoint = e.Location;
            _dragging = false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_openFolder != null && _dragEntry == null)
            {
                int folderIndex = FolderHitTest(e.Location);
                if (folderIndex != _folderHover)
                {
                    _folderHover = folderIndex;
                    Cursor = folderIndex >= 0 ? Cursors.Hand : Cursors.Default;
                    Invalidate();
                }
                return;
            }

            if (_dragEntry != null && (e.Button & MouseButtons.Left) != 0)
            {
                if (!_dragging && DistanceSquared(e.Location, _dragStart) > 100)
                {
                    _dragging = true;
                    _hover = -1;
                    _hoverName = -1;
                    Cursor = Cursors.Default;
                    Invalidate();
                }

                if (_dragging)
                {
                    _dragPoint = e.Location;

                    if (_dragSourceFolder != null && _openFolder != null && !FolderPanelRect().Contains(e.Location))
                    {
                        _openFolder = null;
                        _folderHover = -1;
                    }

                    Invalidate();
                    return;
                }
            }

            int index = HitTest(e.Location);
            bool nameHot = false;

            if (index >= 0 && index < _cells.Count)
            {
                Rectangle cell = _cells[index];
                Rectangle label = new Rectangle(cell.X + 6, cell.Y + 18 + IconPx + 8, cell.Width - 12, 42);
                Entry entry = EntryAt(index);
                nameHot = entry != null && entry.IsFolder && label.Contains(e.Location);
            }

            int newNameHot = nameHot ? index : -1;

            if (index != _hover || newNameHot != _hoverName)
            {
                int old = _hover;
                _hover = index;
                _hoverName = newNameHot;

                if (old >= 0 && old < _cells.Count) Invalidate(_cells[old]);
                if (_hover >= 0 && _hover < _cells.Count) Invalidate(_cells[_hover]);

                Cursor = (_hover >= 0 || _hoverName >= 0) ? Cursors.Hand : Cursors.Default;
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            if (e.Button != MouseButtons.Left || _openFolder == null) return;

            Rectangle title = FolderTitleRect();
            if (!title.Contains(e.Location)) return;

            BeginRenameAt(_openFolder, new Rectangle(title.X + 40, title.Y + 4, title.Width - 80, 30));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hover >= 0 && _hover < _cells.Count) Invalidate(_cells[_hover]);
            _hover = -1;
            _hoverName = -1;
            Cursor = Cursors.Default;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button == MouseButtons.Right)
            {
                ShowContextMenu(e.Location);
                return;
            }

            if (e.Button != MouseButtons.Left) return;

            if (!_dragging && _dragEntry != null && DistanceSquared(e.Location, _dragStart) > 400)
            {
                _dragging = true;
                _hover = -1;
                _hoverName = -1;
                _folderHover = -1;
            }

            if (_dragging)
            {
                Entry source = _dragEntry;
                FolderGroup origin = _dragSourceFolder;

                _suppressClickAfterRename = false;
                _dragging = false;
                _dragEntry = null;
                _dragIndex = -1;
                _dragSourceFolder = null;

                try
                {
                    HandleDrop(source, origin, e.Location);
                }
                catch (Exception ex)
                {
                    Program.Log(ex);
                }

                Invalidate();
                return;
            }

            _dragEntry = null;
            _dragIndex = -1;
            _dragSourceFolder = null;

            try
            {
                HandleClick(e.Location);
            }
            catch (Exception ex)
            {
                Program.Log(ex);
                CloseDeferred();
            }
        }

        private static int DistanceSquared(Point a, Point b)
        {
            int dx = a.X - b.X;
            int dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        private void HandleClick(Point location)
        {
            if (_suppressClickAfterRename)
            {
                _suppressClickAfterRename = false;
                return;
            }

            if (Environment.TickCount - _renameCommittedAt < 350) return;

            if (_openFolder != null)
            {
                int folderIndex = FolderHitTest(location);
                if (folderIndex >= 0 && folderIndex < _openFolder.Items.Count)
                {
                    Launch(_openFolder.Items[folderIndex]);
                    return;
                }

                if (!FolderPanelRect().Contains(location)) CloseFolder();
                return;
            }

            if (_hoverName >= 0 && _pencilRect.Contains(location))
            {
                Entry entry = EntryAt(_hoverName);
                if (entry != null && entry.IsFolder)
                {
                    int index = _page * PageSize + _hoverName;
                    BeginRename(entry.Folder, index);
                }
                return;
            }

            int hit = HitTest(location);
            if (hit >= 0)
            {
                Entry entry = EntryAt(hit);
                if (entry == null) return;

                if (entry.IsFolder)
                {
                    _openFolder = entry.Folder;
                    _folderHover = -1;
                    _folderCells.Clear();
                    Invalidate();
                }
                else if (entry.App != null)
                {
                    Launch(entry.App);
                }
                return;
            }

            if (_searchHost.Bounds.Contains(location)) return;

            // 点空白处：关闭整个面板
            CloseDeferred();
        }

        private void HandleDrop(Entry source, FolderGroup origin, Point point)
        {
            if (source == null || source.App == null) return;

            // 1) 还在本文件夹里：只调整文件夹内部顺序
            if (origin != null && _openFolder == origin)
            {
                int folderIndex = FolderHitTest(point);
                if (folderIndex < 0) return;

                int from = origin.Items.IndexOf(source.App);
                if (from < 0) return;

                int to = folderIndex;
                origin.Items.RemoveAt(from);
                if (from < to) to--;
                to = Math.Max(0, Math.Min(origin.Items.Count, to));
                origin.Items.Insert(to, source.App);
                origin.BuildThumbnail();

                SaveAll();
                Invalidate();
                return;
            }

            // 2) 顶层拖动（可能从文件夹里拖出来）
            if (origin != null) DetachFromFolder(origin, source.App);

            int target = HitTest(point);
            Entry destination = target >= 0 ? EntryAt(target) : null;

            if (destination != null && destination.App != null && destination.App != source.App)
            {
                CreateFolderAt(destination.App, source.App);
                return;
            }

            if (destination != null && destination.IsFolder)
            {
                AddToFolder(destination.Folder, source.App);
                return;
            }

            int index = IsInsideGrid(point) ? NearestInsertIndex(point) : _entries.Count;
            InsertAt(index, source.App);
        }

        private void DetachFromFolder(FolderGroup folder, LaunchItem app)
        {
            folder.Items.Remove(app);

            if (folder.Items.Count == 0)
            {
                _folders.Remove(folder);
                _openFolder = null;
            }
            else
            {
                folder.BuildThumbnail();
            }
        }

        private List<string> KeysFromEntries()
        {
            var keys = new List<string>();
            foreach (Entry entry in _entries)
            {
                if (entry.IsFolder) keys.Add("F|" + entry.Folder.Id);
                else if (entry.App != null) keys.Add("A|" + entry.App.Path);
            }
            return keys;
        }

        private void CreateFolderAt(LaunchItem target, LaunchItem source)
        {
            int id = _nextFolderId++;
            var folder = new FolderGroup();
            folder.Id = id;
            folder.Name = "文件夹 " + id;
            folder.Items.Add(target);
            folder.Items.Add(source);
            folder.BuildThumbnail();
            _folders.Add(folder);

            List<string> keys = KeysFromEntries();
            keys.Remove("A|" + source.Path);

            int index = keys.IndexOf("A|" + target.Path);
            if (index >= 0) keys.RemoveAt(index);
            else index = keys.Count;

            keys.Insert(Math.Min(index, keys.Count), "F|" + folder.Id);

            _order = keys;
            SaveAll();
            BuildEntries();

            int entryIndex = _entries.FindIndex(delegate(Entry entry) { return entry.IsFolder && entry.Folder == folder; });
            if (entryIndex >= 0)
            {
                _search.Text = string.Empty;
                _page = entryIndex / Math.Max(1, PageSize);
                ApplyFilter();
                _page = entryIndex / Math.Max(1, PageSize);
                Invalidate();
                BeginRename(folder, entryIndex);
            }
        }

        private void AddToFolder(FolderGroup folder, LaunchItem app)
        {
            if (ContainsApp(folder, app)) return;

            folder.Items.Add(app);
            folder.BuildThumbnail();

            List<string> keys = KeysFromEntries();
            keys.Remove("A|" + app.Path);
            _order = keys;

            SaveAll();
            BuildEntries();
        }

        private void InsertAt(int index, LaunchItem app)
        {
            List<string> keys = KeysFromEntries();
            keys.Remove("A|" + app.Path);

            index = Math.Max(0, Math.Min(index, keys.Count));
            keys.Insert(index, "A|" + app.Path);

            _order = keys;
            SaveAll();
            BuildEntries();
        }

        private int NearestInsertIndex(Point point)
        {
            int best = _entries.Count;
            double bestDistance = double.MaxValue;

            for (int i = 0; i < _cells.Count; i++)
            {
                Rectangle cell = _cells[i];
                double dx = point.X - (cell.X + cell.Width / 2.0);
                double dy = point.Y - (cell.Y + cell.Height / 2.0);
                double distance = dx * dx + dy * dy;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = _page * PageSize + i;

                    if (point.X > cell.X + cell.Width / 2.0) best++;
                }
            }

            return Math.Max(0, Math.Min(best, _entries.Count));
        }

        private static bool ContainsApp(FolderGroup folder, LaunchItem app)
        {
            foreach (LaunchItem existing in folder.Items)
            {
                if (string.Equals(existing.Path, app.Path, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // ---------------- 文件夹弹层 ----------------

        private int FolderColumns()
        {
            int count = _openFolder == null ? 1 : Math.Max(1, _openFolder.Items.Count);
            return Math.Max(3, Math.Min(6, (int)Math.Ceiling(Math.Sqrt(count))));
        }

        private Rectangle FolderPanelRect()
        {
            int count = _openFolder == null ? 0 : _openFolder.Items.Count;
            int cols = FolderColumns();
            int rows = Math.Max(1, (int)Math.Ceiling((double)count / cols));

            int width = Math.Min(ClientSize.Width - 80, cols * 150 + 72);
            int height = Math.Min(ClientSize.Height - 80, 70 + rows * 140 + 36);

            return new Rectangle((ClientSize.Width - width) / 2, (ClientSize.Height - height) / 2, width, height);
        }

        private Rectangle FolderTitleRect()
        {
            Rectangle panel = FolderPanelRect();
            return new Rectangle(panel.X + 24, panel.Y + 16, panel.Width - 48, 38);
        }

        private int FolderHitTest(Point point)
        {
            for (int i = 0; i < _folderCells.Count; i++)
            {
                if (_folderCells[i].Contains(point)) return i;
            }
            return -1;
        }

        private void DrawFolderPopup(Graphics g)
        {
            if (_openFolder == null) return;

            using (var dim = new SolidBrush(Color.FromArgb(130, 0, 0, 0)))
            {
                g.FillRectangle(dim, ClientRectangle);
            }

            Rectangle panel = FolderPanelRect();

            using (GraphicsPath path = RoundedPath(panel, 30))
            using (var brush = new SolidBrush(Color.FromArgb(240, 30, 32, 40)))
            using (var pen = new Pen(Color.FromArgb(46, 255, 255, 255)))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }

            using (var titleFont = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, _openFolder.Name, titleFont,
                    FolderTitleRect(), Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            _folderCells.Clear();

            int columns = FolderColumns();
            for (int i = 0; i < _openFolder.Items.Count; i++)
            {
                int col = i % columns;
                int row = i / columns;

                var cell = new Rectangle(panel.X + 36 + col * 150, panel.Y + 70 + row * 140, 150, 140);
                if (cell.Bottom > panel.Bottom - 16) break;

                _folderCells.Add(cell);
                DrawItem(g, new Entry { App = _openFolder.Items[i] }, cell, i == _folderHover, false);
            }
        }


        private void CloseFolder()
        {
            EndRename(false);
            _openFolder = null;
            _folderHover = -1;
            Invalidate();
        }

        private void DissolveFolder()
        {
            if (_openFolder == null) return;

            FolderGroup folder = _openFolder;

            int index = _order.IndexOf("F|" + folder.Id);
            if (index >= 0) _order.RemoveAt(index);

            // 把成员放回它们各自原来的位置之后：直接追加到末尾，避免丢图标
            foreach (LaunchItem app in folder.Items)
            {
                string key = "A|" + app.Path;
                if (!_order.Contains(key)) _order.Add(key);
            }

            _folders.Remove(folder);
            SaveAll();

            _openFolder = null;
            _page = 0;
            BuildEntries();
        }

        private void ApplyFilter()
        {
            string query = _search.Text.Trim();

            if (query.Length > 0 && _openFolder != null)
            {
                _openFolder = null;
                _folderHover = -1;
            }

            if (query.Length == 0)
            {
                _view = _entries ?? new List<Entry>();
                _page = 0;
                _hover = -1;
                _hoverName = -1;
                Invalidate();
                return;
            }

            var results = new List<Entry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Entry entry in _entries)
            {
                if (!Matches(entry.Name, query)) continue;
                results.Add(entry);
                if (entry.App != null) seen.Add(entry.App.Path);
            }

            // 文件夹里的软件也参与搜索
            foreach (FolderGroup folder in _folders)
            {
                foreach (LaunchItem app in folder.Items)
                {
                    if (seen.Contains(app.Path)) continue;
                    if (!Matches(app.Name, query)) continue;

                    results.Add(new Entry { App = app });
                    seen.Add(app.Path);
                }
            }

            _view = results;
            _page = 0;
            _hover = -1;
            _hoverName = -1;
            Invalidate();
        }

        private static bool Matches(string name, string query)
        {
            return name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (_settings.CtrlWheelZoom && (ModifierKeys & Keys.Control) != 0)
            {
                int nextScale = _settings.IconScalePercent + (e.Delta > 0 ? 10 : -10);
                nextScale = Math.Max(70, Math.Min(180, nextScale));

                if (nextScale != _settings.IconScalePercent)
                {
                    _settings.IconScalePercent = nextScale;
                    _settings.Save();

                    _hover = -1;
                    _hoverName = -1;
                    Invalidate();
                }
                return;
            }

            int pages = Math.Max(1, (int)Math.Ceiling((double)_view.Count / Math.Max(1, PageSize)));
            int next = _page + (e.Delta > 0 ? -1 : 1);
            next = Math.Max(0, Math.Min(pages - 1, next));

            if (next != _page)
            {
                _page = next;
                _hover = -1;
                _hoverName = -1;
                Invalidate();
            }
            base.OnMouseWheel(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_renaming != null) return base.ProcessCmdKey(ref msg, keyData);

            if (keyData == Keys.Escape)
            {
                if (_openFolder != null)
                {
                    CloseFolder();
                }
                else
                {
                    CloseDeferred();
                }
                return true;
            }

            if (keyData == Keys.Enter)
            {
                if (_view.Count > 0 && !_view[0].IsFolder) Launch(_view[0].App);
                return true;
            }

            if (_search.Text.Length == 0 && (keyData == Keys.Right || keyData == Keys.Left))
            {
                ChangePage(keyData == Keys.Right ? 1 : -1);
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ChangePage(int delta)
        {
            int pages = Math.Max(1, (int)Math.Ceiling((double)_view.Count / Math.Max(1, PageSize)));
            int next = Math.Max(0, Math.Min(pages - 1, _page + delta));

            if (next != _page)
            {
                _page = next;
                _hover = -1;
                _hoverName = -1;
                Invalidate();
            }
        }

        private void CloseDeferred()
        {
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (!IsDisposed && !Disposing) Close();
                });
            }
            catch
            {
            }
        }

        private void Launch(LaunchItem item)
        {
            LaunchItem captured = item;

            // 延迟到消息循环下一轮再关闭 + 启动，避免在鼠标/键盘事件处理中销毁窗口导致崩溃
            BeginInvoke((MethodInvoker)delegate
            {
                if (IsDisposed || Disposing) return;

                Close();

                try
                {
                    var info = new ProcessStartInfo(captured.Path);
                    info.UseShellExecute = true;
                    Process.Start(info);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("无法启动 “" + captured.Name + "”\r\n\r\n" + ex.Message,
                        "Launchpad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            });
        }

        private void ShowContextMenu(Point location)
        {
            var menu = new ContextMenuStrip();
            menu.Font = new Font("Microsoft YaHei UI", 9f);

            if (_openFolder != null)
            {
                menu.Items.Add("解散文件夹", null, delegate { DissolveFolder(); });
                menu.Items.Add(new ToolStripSeparator());
            }

            menu.Items.Add("设置…", null, delegate
            {
                CloseDeferred();
                if (SettingsRequested != null) SettingsRequested(this, EventArgs.Empty);
            });

            menu.Items.Add("刷新列表", null, delegate
            {
                CloseDeferred();
                if (RefreshRequested != null) RefreshRequested(this, EventArgs.Empty);
            });

            var startup = new ToolStripMenuItem("开机启动");
            startup.CheckOnClick = true;
            startup.Checked = Startup.IsEnabled();
            startup.Click += delegate { Startup.SetEnabled(startup.Checked); };
            menu.Items.Add(startup);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate
            {
                CloseDeferred();
                if (ExitRequested != null) ExitRequested(this, EventArgs.Empty);
            });

            _menuOpen = true;
            _menus.Add(menu);
            menu.Closed += delegate
            {
                _menuOpen = false;
                if (!IsDisposed && !Disposing) Activate();
            };

            menu.Show(this, location);
        }

        // ---------------- 背景 ----------------

        private Bitmap CreateBackdrop(Rectangle bounds)
        {
            var canvas = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(canvas))
            {
                using (var brush = new LinearGradientBrush(
                    new Rectangle(0, 0, bounds.Width, bounds.Height),
                    Color.FromArgb(34, 40, 62), Color.FromArgb(12, 14, 22), 90f))
                {
                    g.FillRectangle(brush, new Rectangle(Point.Empty, bounds.Size));
                }

                float opacity = Math.Max(0, Math.Min(100, _settings.BackgroundOpacity)) / 100f;

                if (_settings.BackgroundMode == "image"
                    && !string.IsNullOrEmpty(_settings.BackgroundImage)
                    && File.Exists(_settings.BackgroundImage))
                {
                    try
                    {
                        using (var image = Image.FromFile(_settings.BackgroundImage))
                        {
                            var attributes = new ImageAttributes();
                            var matrix = new ColorMatrix();
                            matrix.Matrix33 = opacity;
                            attributes.SetColorMatrix(matrix);

                            Rectangle destination = CoverRect(image.Size, bounds.Size);
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.DrawImage(image, destination, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
                            attributes.Dispose();
                        }
                    }
                    catch
                    {
                    }
                }
                else
                {
                    int alpha = (int)(opacity * 255);
                    using (var brush = new SolidBrush(Color.FromArgb(alpha, _settings.BackgroundColor)))
                    {
                        g.FillRectangle(brush, 0, 0, bounds.Width, bounds.Height);
                    }
                }
            }

            return canvas;
        }

        private static Rectangle CoverRect(Size image, Size target)
        {
            float scale = Math.Max((float)target.Width / image.Width, (float)target.Height / image.Height);
            int width = (int)Math.Ceiling(image.Width * scale);
            int height = (int)Math.Ceiling(image.Height * scale);
            return new Rectangle((target.Width - width) / 2, (target.Height - height) / 2, width, height);
        }

        private static string GetWallpaperPath()
        {
            var buffer = new StringBuilder(520);
            return Native.SystemParametersInfo(Native.SPI_GETDESKWALLPAPER, (uint)buffer.Capacity, buffer, 0)
                ? buffer.ToString()
                : null;
        }

        private static void DrawCover(Graphics g, Image image, Rectangle target)
        {
            float scale = Math.Max((float)target.Width / image.Width, (float)target.Height / image.Height);
            int width = (int)Math.Ceiling(image.Width * scale);
            int height = (int)Math.Ceiling(image.Height * scale);
            int x = target.X + (target.Width - width) / 2;
            int y = target.Y + (target.Height - height) / 2;

            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(image, new Rectangle(x, y, width, height));
        }

        private static Bitmap Blur(Bitmap source)
        {
            int width = Math.Max(1, source.Width / 28);
            int height = Math.Max(1, source.Height / 28);

            using (var small = new Bitmap(source, width, height))
            using (var tiny = new Bitmap(small, Math.Max(1, width / 3), Math.Max(1, height / 3)))
            {
                var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(result))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(tiny, new Rectangle(0, 0, source.Width, source.Height));
                }
                return result;
            }
        }

        private static GraphicsPath RoundedPath(Rectangle rect, int radius)
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
}



















