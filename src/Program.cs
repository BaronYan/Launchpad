using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// ---------------------------------------------------------------------------
// 程序入口。
//
// 职责：
//   1. 单实例：先在系统里找隐藏窗口 LaunchpadHandshake。
//      找到 → 发 WM_LAUNCHPAD_ACTIVATE 让它把设置窗口弹出来，本进程立刻退出；
//      找不到 → 正常启动，成为唯一实例。
//      （带 requireAdministrator 清单，两个实例权限一致，消息不会被 UIPI 拦）
//   2. 托盘能力自检：低权限进程无法向托盘注册图标，这里只记录日志，不阻塞启动。
//   3. 全局异常写 error.log。
//
// 重要顺序：WinForms 的三个全局初始化必须在任何窗口创建之前调用，
// 否则会抛 InvalidOperationException（表现为“双击没反应”）。
// ---------------------------------------------------------------------------

namespace Launchpad
{
    internal static class Program
    {
        // 隐藏窗口标题：窗口存在 = 已有实例
        internal const string HandshakeTitle = "LaunchpadHandshake";

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeout, out UIntPtr result);

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);

        private const uint WM_LAUNCHPAD_ACTIVATE = 0x8001;   // 唤起设置窗口
        private const uint SMTO_ABORTIFHUNG = 0x0002;
        private const uint SMTO_BLOCK = 0x0001;

        private static readonly string LogPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");

        [STAThread]
        private static void Main(string[] args)
        {
            // WinForms 全局初始化：必须在创建任何窗口/控件之前完成
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                Log(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Log(e.ExceptionObject as Exception);
            };

            // 1. 单实例：已有实例就唤起它，本进程退出
            if (TryActivateExisting()) return;

            // 2. 托盘能力自检（低权限会被拒绝，错误码 5）
            bool trayOk = TrayProbe.Available();
            LogText("启动：托盘=" + (trayOk ? "ok" : "fail"));

            if (!trayOk)
            {
                // 绝不能用模态框：会在消息循环启动前阻塞整个程序
                LogText("托盘不可用（权限不足），程序继续运行，托盘图标将不会出现");
                TrayProbe.LogIntegrity();
            }

            try
            {
                Application.Run(new LaunchpadContext(args, false, trayOk));
            }
            catch (Exception ex)
            {
                Log(ex);
                throw;
            }
        }

        /// <summary>
        /// 如果已有实例在运行：发消息让它把设置窗口显示出来，返回 true（调用方直接退出）。
        /// 消息发送失败时返回 false，让新实例继续启动（异常情况下的兜底）。
        /// </summary>
        private static bool TryActivateExisting()
        {
            IntPtr existing = FindWindow(null, HandshakeTitle);
            if (existing == IntPtr.Zero) return false;

            AllowSetForegroundWindow(-1);

            UIntPtr result;
            IntPtr ok = SendMessageTimeout(existing, WM_LAUNCHPAD_ACTIVATE, IntPtr.Zero, IntPtr.Zero,
                SMTO_ABORTIFHUNG | SMTO_BLOCK, 1500, out result);

            return ok != IntPtr.Zero;
        }

        internal static void Log(Exception exception)
        {
            if (exception == null) return;
            LogText(exception.ToString());
        }

        /// <summary>往 error.log 追加一行诊断信息（不抛异常）。</summary>
        internal static void LogText(string text)
        {
            try
            {
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + text + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
