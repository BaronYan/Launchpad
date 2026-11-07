using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

// ---------------------------------------------------------------------------
// 配置窗口（不显示在任务栏，只由托盘左键唤起）。
// 包含：快捷键录制、背景类型/图片/颜色/透明度、Ctrl+滚轮开关、
//       自定义应用增删、开机启动、关闭行为选择。
// 所有改动立即写入 AppSettings，并通过事件通知 LaunchpadContext 应用。
// ---------------------------------------------------------------------------

namespace Launchpad
{
    /// <summary>常驻的控制面板：快捷键、背景、缩放、自定义应用等设置。</summary>
    internal sealed class LauncherWindow : Form
    {
        private readonly AppSettings _settings;

        private readonly HotkeyBox _hotkey;
        private readonly Label _subtitle;
        private readonly ComboBox _mode;
        private readonly TextBox _imagePath;
        private readonly Button _browse;
        private readonly Button _pickColor;
        private readonly Panel _colorSwatch;
        private readonly TrackBar _opacity;
        private readonly Label _opacityLabel;
        private readonly CheckBox _zoom;
        private readonly ListBox _apps;
        private readonly Label _status;
        private readonly ComboBox _closeBehavior;

        public event EventHandler CloseRequested;
        public event EventHandler RestartElevatedRequested;
        public event EventHandler HotkeyChanged;
        public event EventHandler SettingsChanged;
        public event EventHandler AppsChanged;

        public LauncherWindow(AppSettings settings, bool trayOk)
        {
            _settings = settings;

            Text = "Launchpad";
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(620, trayOk ? 636 : 700);
            BackColor = Color.FromArgb(248, 249, 252);
            Font = new Font("Microsoft YaHei UI", 9f);

            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
            }

            var title = new Label();
            title.Text = "Launchpad";
            title.Font = new Font("Microsoft YaHei UI", 18f, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(32, 34, 44);
            title.AutoSize = true;
            title.Location = new Point(24, 14);
            Controls.Add(title);

            _subtitle = new Label();
            _subtitle.Text = "快捷键：" + _settings.HotkeyText;
            _subtitle.ForeColor = Color.FromArgb(120, 124, 136);
            _subtitle.AutoSize = true;
            _subtitle.Location = new Point(26, 52);
            Controls.Add(_subtitle);

            // ---- 快捷键 ----
            var hotkeyGroup = new GroupBox();
            hotkeyGroup.Text = "快捷键";
            hotkeyGroup.Bounds = new Rectangle(20, 78, 580, 72);
            Controls.Add(hotkeyGroup);

            _hotkey = new HotkeyBox();
            _hotkey.Bounds = new Rectangle(16, 28, 220, 26);
            _hotkey.SetHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey);
            _hotkey.HotkeyRecorded += delegate
            {
                _settings.HotkeyModifiers = _hotkey.Modifiers;
                _settings.HotkeyKey = _hotkey.Key;
                _settings.Save();
                _subtitle.Text = "快捷键：" + _settings.HotkeyText;

                if (HotkeyChanged != null) HotkeyChanged(this, EventArgs.Empty);
            };
            hotkeyGroup.Controls.Add(_hotkey);

            var hotkeyHint = new Label();
            hotkeyHint.Text = "点一下输入框，然后按下新的组合键（需带 Ctrl / Alt / Shift）";
            hotkeyHint.AutoSize = true;
            hotkeyHint.ForeColor = Color.FromArgb(140, 144, 156);
            hotkeyHint.Location = new Point(250, 33);
            hotkeyGroup.Controls.Add(hotkeyHint);

            // ---- 背景 ----
            var backgroundGroup = new GroupBox();
            backgroundGroup.Text = "顶层背景";
            backgroundGroup.Bounds = new Rectangle(20, 158, 580, 196);
            Controls.Add(backgroundGroup);

            var modeLabel = new Label();
            modeLabel.Text = "类型";
            modeLabel.AutoSize = true;
            modeLabel.Location = new Point(16, 30);
            backgroundGroup.Controls.Add(modeLabel);

            _mode = new ComboBox();
            _mode.DropDownStyle = ComboBoxStyle.DropDownList;
            _mode.Bounds = new Rectangle(60, 26, 170, 24);
            _mode.Items.Add("纯色");
            _mode.Items.Add("自定义图片");
            _mode.SelectedIndex = _settings.BackgroundMode == "image" ? 1 : 0;
            _mode.SelectedIndexChanged += delegate
            {
                _settings.BackgroundMode = _mode.SelectedIndex == 1 ? "image" : "color";
                _settings.Save();
                RefreshMode();
                RaiseSettingsChanged();
            };
            backgroundGroup.Controls.Add(_mode);

            _imagePath = new TextBox();
            _imagePath.Bounds = new Rectangle(60, 62, 380, 24);
            _imagePath.Text = _settings.BackgroundImage;
            _imagePath.TextChanged += delegate
            {
                _settings.BackgroundImage = _imagePath.Text.Trim();
                _settings.Save();
                RaiseSettingsChanged();
            };
            backgroundGroup.Controls.Add(_imagePath);

            _browse = new Button();
            _browse.Text = "浏览…";
            _browse.Bounds = new Rectangle(450, 60, 96, 28);
            _browse.Click += delegate
            {
                using (var dialog = new OpenFileDialog())
                {
                    dialog.Filter = "图片|*.jpg;*.jpeg;*.png;*.bmp;*.gif|所有文件|*.*";
                    dialog.Title = "选择背景图片";
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;

                    _imagePath.Text = dialog.FileName;
                }
            };
            backgroundGroup.Controls.Add(_browse);

            _pickColor = new Button();
            _pickColor.Text = "选择颜色…";
            _pickColor.Bounds = new Rectangle(60, 98, 110, 28);
            _pickColor.Click += delegate
            {
                using (var dialog = new ColorDialog())
                {
                    dialog.Color = _settings.BackgroundColor;
                    dialog.FullOpen = true;
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;

                    _settings.BackgroundColor = dialog.Color;
                    _settings.Save();
                    UpdateSwatch();
                    RaiseSettingsChanged();
                }
            };
            backgroundGroup.Controls.Add(_pickColor);

            _colorSwatch = new Panel();
            _colorSwatch.Bounds = new Rectangle(180, 98, 44, 28);
            _colorSwatch.BorderStyle = BorderStyle.FixedSingle;
            backgroundGroup.Controls.Add(_colorSwatch);

            _opacity = new TrackBar();
            _opacity.Minimum = 0;
            _opacity.Maximum = 100;
            _opacity.TickFrequency = 10;
            _opacity.Bounds = new Rectangle(60, 136, 320, 34);
            _opacity.Value = _settings.BackgroundOpacity;
            _opacity.ValueChanged += delegate
            {
                _settings.BackgroundOpacity = _opacity.Value;
                _settings.Save();
                _opacityLabel.Text = "透明度 " + _opacity.Value + "%";
                RaiseSettingsChanged();
            };
            backgroundGroup.Controls.Add(_opacity);

            _opacityLabel = new Label();
            _opacityLabel.Text = "透明度 " + _settings.BackgroundOpacity + "%";
            _opacityLabel.AutoSize = true;
            _opacityLabel.Location = new Point(390, 142);
            backgroundGroup.Controls.Add(_opacityLabel);

            // ---- 缩放开关 ----
            _zoom = new CheckBox();
            _zoom.Text = "支持 Ctrl + 鼠标滚轮 放大 / 缩小图标";
            _zoom.AutoSize = true;
            _zoom.Checked = _settings.CtrlWheelZoom;
            _zoom.Location = new Point(26, 366);
            _zoom.CheckedChanged += delegate
            {
                _settings.CtrlWheelZoom = _zoom.Checked;
                _settings.Save();
                RaiseSettingsChanged();
            };
            Controls.Add(_zoom);

            // ---- 自定义应用 ----
            var appsGroup = new GroupBox();
            appsGroup.Text = "自定义应用（手动添加 exe / 快捷方式）";
            appsGroup.Bounds = new Rectangle(20, 396, 580, 168);
            Controls.Add(appsGroup);

            _apps = new ListBox();
            _apps.Bounds = new Rectangle(16, 26, 380, 122);
            appsGroup.Controls.Add(_apps);

            var add = new Button();
            add.Text = "添加…";
            add.Bounds = new Rectangle(410, 26, 110, 30);
            add.Click += delegate
            {
                using (var dialog = new OpenFileDialog())
                {
                    dialog.Filter = "程序 / 快捷方式|*.exe;*.lnk;*.url|所有文件|*.*";
                    dialog.Title = "选择要添加的应用";
                    dialog.Multiselect = true;
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;

                    bool changed = false;
                    foreach (string file in dialog.FileNames)
                    {
                        if (!File.Exists(file)) continue;
                        if (_settings.ExtraApps.Contains(file)) continue;

                        _settings.ExtraApps.Add(file);
                        changed = true;
                    }

                    if (!changed) return;

                    _settings.Save();
                    ReloadApps();
                    if (AppsChanged != null) AppsChanged(this, EventArgs.Empty);
                }
            };
            appsGroup.Controls.Add(add);

            var remove = new Button();
            remove.Text = "删除";
            remove.Bounds = new Rectangle(410, 64, 110, 30);
            remove.Click += delegate
            {
                if (_apps.SelectedIndex < 0) return;

                string path = _settings.ExtraApps[_apps.SelectedIndex];
                _settings.ExtraApps.Remove(path);
                _settings.Save();
                ReloadApps();
                if (AppsChanged != null) AppsChanged(this, EventArgs.Empty);
            };
            appsGroup.Controls.Add(remove);

            var startup = new CheckBox();
            startup.Text = "开机启动";
            startup.AutoSize = true;
            startup.Checked = Startup.IsEnabled();
            startup.Location = new Point(26, 574);
            startup.CheckedChanged += delegate { Startup.SetEnabled(startup.Checked); };

            // 以 MSIX 包运行时：写 HKCU\...\Run 会被虚拟化，开机启动必须由系统管理，
            // 所以这里把开关变成只读提示，指向“设置 → 应用 → 启动”。
            if (Packaging.IsPackaged)
            {
                startup.Text = "开机启动（请在 设置 → 应用 → 启动 中开启）";
                startup.Enabled = false;
            }

            Controls.Add(startup);

            var closeLabel = new Label();
            closeLabel.Text = "关闭窗口时";
            closeLabel.AutoSize = true;
            closeLabel.ForeColor = Color.FromArgb(70, 74, 86);
            closeLabel.Location = new Point(210, 577);
            Controls.Add(closeLabel);

            _closeBehavior = new ComboBox();
            _closeBehavior.DropDownStyle = ComboBoxStyle.DropDownList;
            _closeBehavior.Bounds = new Rectangle(290, 573, 160, 24);
            _closeBehavior.Items.Add("每次询问");
            _closeBehavior.Items.Add("退出程序");
            _closeBehavior.Items.Add("收起到托盘");
            _closeBehavior.SelectedIndex = _settings.CloseBehavior == "exit" ? 1 : (_settings.CloseBehavior == "tray" ? 2 : 0);
            _closeBehavior.SelectedIndexChanged += delegate
            {
                _settings.CloseBehavior = _closeBehavior.SelectedIndex == 1 ? "exit"
                    : (_closeBehavior.SelectedIndex == 2 ? "tray" : "ask");
                _settings.Save();
            };
            Controls.Add(_closeBehavior);

            _status = new Label();
            _status.AutoSize = true;
            _status.ForeColor = Color.FromArgb(150, 154, 166);
            _status.Location = new Point(26, 606);
            Controls.Add(_status);

            // 托盘权限不足时，在窗口底部给出“以管理员身份重启”的入口
            if (!trayOk)
            {
                var banner = new Panel();
                banner.Bounds = new Rectangle(20, 634, 580, 52);
                banner.BackColor = Color.FromArgb(255, 244, 230);
                Controls.Add(banner);

                var message = new Label();
                message.Text = "托盘图标不可用：当前进程权限低于资源管理器。";
                message.AutoSize = true;
                message.ForeColor = Color.FromArgb(150, 80, 20);
                message.Location = new Point(12, 17);
                banner.Controls.Add(message);

                var restart = new Button();
                restart.Text = "以管理员身份重启";
                restart.Bounds = new Rectangle(400, 10, 160, 32);
                restart.Click += delegate
                {
                    if (RestartElevatedRequested != null) RestartElevatedRequested(this, EventArgs.Empty);
                };
                banner.Controls.Add(restart);
            }

            ReloadApps();
            RefreshMode();
            UpdateSwatch();
        }

        public void SetHotkeyStatus(bool ok)
        {
            _status.Text = ok ? "" : "快捷键注册失败（可能被其他软件占用），换个组合试试。";
            _status.ForeColor = ok ? Color.FromArgb(150, 154, 166) : Color.FromArgb(200, 80, 60);
        }

        private void ReloadApps()
        {
            _apps.BeginUpdate();
            _apps.Items.Clear();
            foreach (string path in _settings.ExtraApps)
            {
                _apps.Items.Add(Path.GetFileName(path));
            }
            _apps.EndUpdate();
        }

        private void RefreshMode()
        {
            bool image = _mode.SelectedIndex == 1;

            _imagePath.Visible = image;
            _browse.Visible = image;
            _pickColor.Visible = !image;
            _colorSwatch.Visible = !image;
        }

        private void UpdateSwatch()
        {
            _colorSwatch.BackColor = _settings.BackgroundColor;
        }

        private void RaiseSettingsChanged()
        {
            if (SettingsChanged != null) SettingsChanged(this, EventArgs.Empty);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && CloseRequested != null)
            {
                e.Cancel = true;
                CloseRequested(this, EventArgs.Empty);
                return;
            }
            base.OnFormClosing(e);
        }
    }
}




