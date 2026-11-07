using System;
using System.Drawing;
using System.Windows.Forms;

// ---------------------------------------------------------------------------
// 首次关闭配置窗口时的询问框：退出程序 or 收起到托盘，可勾选“记住”。
// 选择结果由 LaunchpadContext 写进 AppSettings.CloseBehavior。
// ---------------------------------------------------------------------------

namespace Launchpad
{
    /// <summary>第一次关闭控制窗口时询问：退出，还是最小化到托盘。</summary>
    internal sealed class CloseDialog : Form
    {
        public bool ExitApplication { get; private set; }
        public bool Remember { get; private set; }

        private readonly CheckBox _remember;

        public CloseDialog()
        {
            Text = "关闭 Launchpad";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(392, 156);
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(248, 249, 252);
            Font = new Font("Microsoft YaHei UI", 9f);

            var message = new Label();
            message.Text = "要退出程序，还是收起到系统托盘继续在后台运行？";
            message.AutoSize = false;
            message.Bounds = new Rectangle(20, 20, 352, 40);
            message.ForeColor = Color.FromArgb(50, 54, 66);
            Controls.Add(message);

            _remember = new CheckBox();
            _remember.Text = "记住我的选择（之后可在控制窗口里修改）";
            _remember.AutoSize = true;
            _remember.Checked = true;
            _remember.Location = new Point(22, 66);
            _remember.ForeColor = Color.FromArgb(90, 94, 106);
            Controls.Add(_remember);

            var exit = new Button();
            exit.Text = "退出程序";
            exit.Bounds = new Rectangle(120, 106, 120, 32);
            exit.Click += delegate
            {
                ExitApplication = true;
                Remember = _remember.Checked;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(exit);

            var tray = new Button();
            tray.Text = "收起到托盘";
            tray.Bounds = new Rectangle(250, 106, 120, 32);
            tray.Click += delegate
            {
                ExitApplication = false;
                Remember = _remember.Checked;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(tray);

            AcceptButton = tray;
        }
    }
}


