using System;
using System.Windows.Forms;

// ---------------------------------------------------------------------------
// 隐藏消息窗口：
//   1. 接收 WM_HOTKEY（全局热键）→ 抛 HotkeyPressed；
//   2. 作为单实例握手的锚点：标题固定为 LaunchpadHandshake，
//      收到 WM_APP+1 时抛 ActivateRequested（新实例请求唤起）。
// ---------------------------------------------------------------------------

namespace Launchpad
{
    /// <summary>隐藏窗口：接收全局热键，同时作为单实例握手的锚点。</summary>
    internal sealed class HotkeyWindow : NativeWindow
    {
        public event EventHandler HotkeyPressed;
        public event EventHandler ActivateRequested;
        public event EventHandler ExitRequested;

        public HotkeyWindow()
        {
            var parameters = new CreateParams();
            parameters.Caption = Program.HandshakeTitle;
            CreateHandle(parameters);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                EventHandler handler = HotkeyPressed;
                if (handler != null) handler(this, EventArgs.Empty);
            }
            else if (m.Msg == 0x8001)
            {
                // 新实例请求“唤起”：把配置窗口显示出来
                EventHandler handler = ActivateRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            }
            else if (m.Msg == 0x8002)
            {
                // 新实例请求“交接”：老实例清理托盘图标和热键后退出，
                // 让新实例以自己（通常是普通权限）的身份重新注册。
                EventHandler handler = ExitRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            }

            base.WndProc(ref m);
        }
    }
}

