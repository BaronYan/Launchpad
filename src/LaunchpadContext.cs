using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

// ---------------------------------------------------------------------------
// 常驻进程的中枢（ApplicationContext）。
// 职责：
//   - 注册/重新注册全局热键（Ctrl+Alt+Space 可配置）；
//   - 维护托盘图标（必须等消息循环跑起来之后再显示，否则 Shell_NotifyIcon 会静默失败）；
//   - 按需创建“配置窗口”（不占任务栏，托盘左键打开）；
//   - 管理全屏 Launchpad 面板（热键打开，同一时间只允许一个）；
//   - 后台线程扫描开始菜单，加载完成后再更新面板。
// 线程模型：所有 UI 操作都在 UI 线程；后台线程只做扫描，结果通过 _marshal.BeginInvoke 回传。
// ---------------------------------------------------------------------------

namespace Launchpad
{
    /// <summary>常驻进程：热键、托盘、按需弹出的控制窗口、Launchpad 面板。</summary>
    internal sealed class LaunchpadContext : ApplicationContext
    {
        private const int HotkeyId = 1;

        private readonly HotkeyWindow _hotkeyWindow;
        private readonly NotifyIcon _tray;
        private readonly Control _marshal;
        private readonly AppSettings _settings;

        private LauncherWindow _window;
        private List<LaunchItem> _items = new List<LaunchItem>();
        private LaunchpadForm _form;
        private bool _hotkeyOk;
        private readonly bool _trayOk;

        public LaunchpadContext(string[] args, bool showConfig, bool trayOk)
        {
            _marshal = new Control();
            IntPtr handle = _marshal.Handle;

            _settings = AppSettings.Load();
            _trayOk = trayOk;

            _hotkeyWindow = new HotkeyWindow();
            _hotkeyWindow.HotkeyPressed += delegate { Toggle(); };
            _hotkeyWindow.ActivateRequested += delegate { ShowConfig(); };
            _hotkeyWindow.ExitRequested += delegate { Exit(); };

            RegisterHotkey();

            _tray = new NotifyIcon();
            _tray.Icon = TrayIcon.Create();
            _tray.Text = "Launchpad";
            _tray.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) ShowConfig();
            };
            _tray.ContextMenuStrip = BuildMenu();

            // 关键：托盘图标必须等消息循环真正跑起来之后再显示，
            // 在构造函数里直接 Visible=true 时消息泵还没启动，Shell_NotifyIcon 可能静默失败。
            _marshal.BeginInvoke((MethodInvoker)delegate
            {
                _tray.Visible = true;
                Program.LogText("托盘已请求显示，Visible=" + _tray.Visible);

                if (TrayPromoter.Promote())
                {
                    // 通知资源管理器重新读取托盘图标设置，否则要等下一次重启才生效
                    UIntPtr ignored;
                    Native.SendMessageTimeout(Native.HWND_BROADCAST, Native.WM_SETTINGCHANGE,
                        UIntPtr.Zero, "TraySettings", Native.SMTO_ABORTIFHUNG, 1000, out ignored);

                    _tray.Visible = false;
                    _tray.Visible = true;
                Program.LogText("托盘已请求显示，Visible=" + _tray.Visible);
                }
            });


            Shortcut.EnsureDesktop();

            LoadItemsAsync();

            // 从“交接”启动（双击时已有实例在跑）：把配置窗口显示出来，给用户一个明确反馈
            if (showConfig)
            {
                _marshal.BeginInvoke((MethodInvoker)ShowConfig);
            }
        }


        private void RegisterHotkey()
        {
            try
            {
                Native.UnregisterHotKey(_hotkeyWindow.Handle, HotkeyId);
            }
            catch
            {
            }

            _hotkeyOk = Native.RegisterHotKey(_hotkeyWindow.Handle, HotkeyId,
                _settings.HotkeyModifiers, _settings.HotkeyKey);

            Program.LogText("热键注册=" + (_hotkeyOk ? "ok" : "fail") + " " + _settings.HotkeyText);

            if (_window != null && !_window.IsDisposed) _window.SetHotkeyStatus(_hotkeyOk);

            if (!_hotkeyOk)
            {
                try
                {
                    _marshal.BeginInvoke((MethodInvoker)ShowHotkeyConflict);
                }
                catch
                {
                }
            }
        }

        private void ShowHotkeyConflict()
        {
            try
            {
                ShowConfig();
                _window.SetHotkeyStatus(false);

                if (_tray != null)
                {
                    _tray.ShowBalloonTip(4000, "Launchpad",
                        "快捷键 " + _settings.HotkeyText + " 注册失败，可能已被其他程序占用。",
                        ToolTipIcon.Warning);
                }

                MessageBox.Show(_window,
                    "快捷键 “" + _settings.HotkeyText + "” 注册失败，可能已被其他程序占用。\r\n\r\n"
                    + "请在控制窗口里点快捷键输入框，换一个组合键。",
                    "Launchpad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch
            {
            }
        }

        private void LoadItemsAsync()
        {
            var thread = new Thread(delegate()
            {
                List<LaunchItem> items;
                try
                {
                    items = AppCatalog.Load(_settings);
                }
                catch
                {
                    items = new List<LaunchItem>();
                }

                try
                {
                    _marshal.BeginInvoke((MethodInvoker)delegate
                    {
                        _items = items;
                        if (_form != null && !_form.IsDisposed) _form.SetItems(_items);
                    });
                }
                catch
                {
                }
            });

            thread.IsBackground = true;
            thread.Start();
        }

        private void Toggle()
        {
            if (_form != null && !_form.IsDisposed)
            {
                _form.Close();
                return;
            }
            ShowLaunchpad();
        }

        private void ShowLaunchpad()
        {
            Program.LogText("打开面板：items=" + _items.Count);
            if (_form != null && !_form.IsDisposed)
            {
                _form.Activate();
                return;
            }

            _form = new LaunchpadForm(_items, _hotkeyOk, _settings);
            _form.FormClosed += delegate { _form = null; };
            _form.RefreshRequested += delegate
            {
                LoadItemsAsync();
                ShowLaunchpad();
            };
            _form.SettingsRequested += delegate { ShowConfig(); };
            _form.ExitRequested += delegate { Exit(); };
            _form.ShowOverlay();
        }

        private void ShowConfig()
        {
            Program.LogText("打开设置窗口");
            if (_window == null || _window.IsDisposed)
            {
                _window = new LauncherWindow(_settings, _trayOk);
                _window.CloseRequested += delegate { HandleClose(); };
                _window.HotkeyChanged += delegate { RegisterHotkey(); };
                _window.SettingsChanged += delegate { RefreshLauncher(); };
                _window.AppsChanged += delegate
                {
                    LoadItemsAsync();
                    RefreshLauncher();
                };
                _window.SetHotkeyStatus(_hotkeyOk);
                _window.RestartElevatedRequested += delegate { RestartElevated(); };
            }

            if (_window.WindowState == FormWindowState.Minimized)
                _window.WindowState = FormWindowState.Normal;

            _window.Show();
            _window.Activate();
        }

        /// <summary>用户主动点“以管理员身份重启”：先拉起高权限实例，再退出当前实例。</summary>
        private void RestartElevated()
        {
            try
            {
                var info = new ProcessStartInfo();
                info.FileName = Application.ExecutablePath;
                info.Arguments = "--elevated";
                info.Verb = "runas";
                info.UseShellExecute = true;
                Process.Start(info);
                Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show(_window, "提权被取消或失败：" + ex.Message, "Launchpad",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void HandleClose()
        {
            string behavior = _settings.CloseBehavior;

            if (behavior == "ask")
            {
                using (var dialog = new CloseDialog())
                {
                    dialog.ShowDialog(_window);

                    behavior = dialog.ExitApplication ? "exit" : "tray";

                    if (dialog.Remember)
                    {
                        _settings.CloseBehavior = behavior;
                        _settings.Save();
                    }
                }
            }

            if (behavior == "exit")
            {
                Exit();
                return;
            }

            _window.Hide();
        }

        private void RefreshLauncher()
        {
            if (_form != null && !_form.IsDisposed) _form.Close();
        }

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Font = new Font("Microsoft YaHei UI", 9f);

            menu.Items.Add("打开 Launchpad", null, delegate { ShowLaunchpad(); });
            menu.Items.Add("显示控制窗口", null, delegate { ShowConfig(); });
            menu.Items.Add("刷新列表", null, delegate { LoadItemsAsync(); });
            menu.Items.Add(new ToolStripSeparator());

            var startup = new ToolStripMenuItem("开机启动");
            startup.CheckOnClick = true;
            startup.Checked = Startup.IsEnabled();
            startup.Click += delegate { Startup.SetEnabled(startup.Checked); };
            menu.Items.Add(startup);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Exit(); });

            return menu;
        }

        private void Exit()
        {
            try
            {
                Native.UnregisterHotKey(_hotkeyWindow.Handle, HotkeyId);
            }
            catch
            {
            }

            if (_window != null && !_window.IsDisposed)
            {
                _window.Hide();
                _window.Dispose();
            }

            _tray.Visible = false;
            _tray.Dispose();
            ExitThread();
        }
    }
}








