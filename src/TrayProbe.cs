using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Launchpad
{
    // ---------------------------------------------------------------------------
    // 托盘能力检测 + 自救。
    //
    // 背景：Shell_NotifyIcon 是往资源管理器的托盘窗口发消息，受 UIPI 限制。
    // 如果本进程的完整性级别低于资源管理器（例如被低权限的宿主程序/聊天窗口拉起），
    // Shell_NotifyIcon 会返回 FALSE、GetLastError()=5（拒绝访问），
    // 表现就是“程序在跑，但托盘里怎么都没有图标”，WinForms 的 NotifyIcon 也不会报错。
    //
    // 处理办法：先探测；探测失败就让 explorer.exe 以普通权限重新拉起自己，然后退出。
    // ---------------------------------------------------------------------------
    internal static class TrayProbe
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public int uTimeout;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int needed);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        private const int NIM_ADD = 0;
        private const int NIM_DELETE = 2;
        private const int NIF_MESSAGE = 1;
        private const int NIF_ICON = 2;
        private const int NIF_TIP = 4;

        /// <summary>
        /// 把“本进程”和“任务栏进程”的完整性级别写进日志，用来判断托盘被拒是不是权限差。
        /// </summary>
        public static void LogIntegrity()
        {
            try
            {
                string mine = IntegrityOf(GetCurrentProcess());

                string shell = "?";
                IntPtr tray = FindWindow("Shell_TrayWnd", null);
                if (tray != IntPtr.Zero)
                {
                    uint pid;
                    GetWindowThreadProcessId(tray, out pid);

                    IntPtr process = OpenProcess(0x1000, false, pid);
                    if (process != IntPtr.Zero)
                    {
                        try { shell = IntegrityOf(process); }
                        finally { CloseHandle(process); }
                    }
                }

                Program.LogText("完整性级别：本进程=" + mine + " 资源管理器=" + shell);
            }
            catch
            {
            }
        }

        private static string IntegrityOf(IntPtr process)
        {
            IntPtr token;
            if (!OpenProcessToken(process, 0x0008, out token)) return "?";

            try
            {
                int needed;
                GetTokenInformation(token, 25, IntPtr.Zero, 0, out needed);

                IntPtr buffer = Marshal.AllocHGlobal(needed);
                try
                {
                    if (!GetTokenInformation(token, 25, buffer, needed, out needed)) return "?";

                    IntPtr sid = Marshal.ReadIntPtr(buffer);
                    int count = Marshal.ReadByte(sid, 1);
                    int last = Marshal.ReadInt32(sid, 8 + (count - 1) * 4);

                    if (last == 0x1000) return "Low";
                    if (last == 0x2000) return "Medium";
                    if (last == 0x3000) return "High";
                    if (last == 0x4000) return "System";
                    return "0x" + last.ToString("X");
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseHandle(token);
            }
        }

        /// <summary>
        /// 试加一个临时托盘图标再删掉。返回 true 表示当前进程有权限往托盘里放图标。
        /// 失败时把错误码写进 error.log，方便排查。
        /// </summary>
        public static bool Available()
        {
            Form host = null;

            try
            {
                // 只需要一个能接收托盘回调消息的窗口句柄，不显示出来
                host = new Form();
                host.ShowInTaskbar = false;
                host.WindowState = FormWindowState.Minimized;

                var data = new NOTIFYICONDATA();
                data.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA));
                data.hWnd = host.Handle;
                data.uID = 0x7A11;
                data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
                data.uCallbackMessage = 0x0400 + 1;
                data.hIcon = SystemIcons.Application.Handle;
                data.szTip = "Launchpad probe";

                bool ok = Shell_NotifyIcon(NIM_ADD, ref data);
                int error = Marshal.GetLastWin32Error();

                if (ok)
                {
                    Shell_NotifyIcon(NIM_DELETE, ref data);
                }
                else
                {
                    Program.Log(new Exception(
                        "托盘图标不可用：Shell_NotifyIcon(NIM_ADD) 返回 FALSE，Win32 错误码 = " + error
                        + "（5 = 拒绝访问，通常是进程完整性级别低于资源管理器）"));
                }

                return ok;
            }
            catch (Exception ex)
            {
                Program.Log(ex);
                return false;
            }
            finally
            {
                if (host != null) host.Dispose();
            }
        }

    }
}


