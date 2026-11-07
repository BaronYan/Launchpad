using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Launchpad
{
    // ---------------------------------------------------------------------------
    // 判断当前进程是不是以 MSIX 包的形式运行（从 Microsoft Store 安装）。
    //
    // 为什么要判断：
    //   · 打包后 %APPDATA% / HKCU 写入会被系统虚拟化，写 Run 键无法真正开机启动，
    //     必须改用清单里的 startupTask（由“设置 → 应用 → 启动”管理）；
    //   · 打包后清单是 asInvoker，不会也不允许申请管理员权限。
    //
    // 实现：kernel32!GetCurrentPackageFullName，未打包时返回
    //   APPMODEL_ERROR_NO_PACKAGE (15700)。
    // ---------------------------------------------------------------------------
    internal static class Packaging
    {
        private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetCurrentPackageFullName(ref int length, StringBuilder name);

        private static bool? _isPackaged;

        public static bool IsPackaged
        {
            get
            {
                if (!_isPackaged.HasValue) _isPackaged = Detect();
                return _isPackaged.Value;
            }
        }

        private static bool Detect()
        {
            try
            {
                int length = 0;
                int result = GetCurrentPackageFullName(ref length, null);
                return result != APPMODEL_ERROR_NO_PACKAGE;
            }
            catch
            {
                return false;
            }
        }
    }
}
