# 把 Launchpad 正规化并上架 Microsoft Store

这份文档对应仓库里新增的工程化文件：

| 文件 | 作用 |
| --- | --- |
| `Launchpad.sln` | Visual Studio 解决方案 |
| `Launchpad.csproj` | 正式工程（net48 + WinForms，SDK 风格），用 `src\app.manifest`（asInvoker） |
| `src\app.manifest` | 正式版清单：不申请管理员权限（Store 要求） |
| `app.manifest` | 个人版清单：`requireAdministrator`，仍由 `build.ps1`（csc）使用 |
| `package\Package.appxmanifest` | MSIX 包清单模板（含 startupTask 开机启动声明） |
| `package\Assets\*` | 商店图标（44/71/150/310/310×150/50），由 `tools\make-store-assets.ps1` 生成 |
| `package\build-msix.ps1` | 调 `makeappx.exe` 打 MSIX 包 |
| `tools\sign.ps1` / `tools\make-selfsigned.ps1` | 代码签名脚本 |

---

## 0. 先做的两件事

1. **Visual Studio 安装**：勾选
   - 「.NET 桌面开发」
   - 「Windows SDK」← **必须有**，`makeappx.exe`、`signtool.exe` 都在里面

   如果已经装完 VS 但找不到 `makeappx.exe`：打开 Visual Studio Installer → 修改 →
   「单个组件」→ 搜索 `Windows SDK`（例如 *Windows 11 SDK (10.0.26100)*）→ 勾选 → 修改。
2. **Partner Center 产品**：`应用和游戏` → `新产品` → 选「应用」→ 选 **MSIX 或 PWA 应用** → 预留名称

预留完成后，进入产品的 **产品标识** 页面，记下三个值：

```
Identity Name        = 例如 12345Publisher.Launchpad
Publisher            = CN=1A2B3C4D-...（发布者 ID）
PublisherDisplayName = 你的开发者名称
```

---

## 1. 用 Visual Studio 打开、编译

双击 `Launchpad.sln` → 配置切到 `Release` / `Any CPU` → 生成。

产物：`bin\Release\net48\Launchpad.exe`

> 工程用的是 `src\app.manifest`（asInvoker）。这是 **Store 版本** 的配置。
> 本地想要托盘图标、又碰上资源管理器提权运行的情况，请继续用 `build.ps1` 编出来的
> 管理员版（`app.manifest`），两者可以并存，互不影响。

---

## 2. 打 MSIX 包（在 Visual Studio 里一键完成）

**身份信息只在一个地方配置**：`package\identity.json`

```json
{
  "IdentityName": "BaronYan.OJLaunch",
  "Publisher": "CN=B09612D5-A53A-4635-9D9B-09F00873131F",
  "PublisherDisplayName": "BaronYan",
  "DisplayName": "OJLaunch",
  "Version": "1.0.0.0"
}
```

- `IdentityName` / `Publisher` 必须和 Partner Center 的「产品标识」页**逐字一致**
- `DisplayName` 必须是**已保留的名称**（否则上传报"使用了你未保留的显示名称"）
- `Version` 四段式，必须比上一版大

改完这个文件，在 Visual Studio 里把配置切到 **Release** → 点「生成」：

```
Launchpad.csproj 里的 AfterBuild 目标会自动调用 package\build-msix.ps1
→ 输出 package\out\<DisplayName>_<Version>_x64.msix
```

> Debug 配置不打包（只编译 exe，省时间）；没装 Windows SDK 时只警告、不让编译失败。

也可以继续用命令行（等价）：

```powershell
.\package\build-msix.ps1
```

## 2.1 命令行手动指定身份（可选）

```powershell
.\package\build-msix.ps1 `
    -Identity "12345Publisher.Launchpad" `
    -Publisher "CN=1A2B3C4D-1111-2222-3333-444455556666" `
    -PublisherName "你的开发者名称" `
    -Version "1.0.0.0"
```

产物：`package\out\Launchpad_1.0.0.0_x64.msix`

本地安装测试（需要签名 + 开发者模式）：

```powershell
# 用自签名证书签一下（本机测试用，Store 会用自己的证书重签）
.\tools\make-selfsigned.ps1
.\tools\sign.ps1 -Exe .\package\out\Launchpad_1.0.0.0_x64.msix -Pfx .\launchpad-selfsigned.pfx -Password launchpad

Add-AppxPackage -Path .\package\out\Launchpad_1.0.0.0_x64.msix
```

> MSIX 也要签名才能安装；用 `signtool` 时目标文件换成 `.msix` 即可。

---

## 3. 本地认证自检（Windows App Certification Kit）

Windows SDK 里带 WACK：

```powershell
& "C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe" reset
& "C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe" test `
    -appxpackagepath .\package\out\Launchpad_1.0.0.0_x64.msix `
    -reportoutputpath .\package\out\wack.xml
```

常见不通过项：

- 缺少某个尺寸的图标资源
- 启动超时（我们的程序启动很快，一般没问题）
- 窗口在无交互环境下崩溃（要保证异常都被捕获、有日志）

---

## 4. 提交到 Partner Center

在产品页面依次填写：

| 页面 | 要准备的内容 |
| --- | --- |
| 包 | 上传 `Launchpad_1.0.0.0_x64.msix` |
| 属性 | 类别（个性化 / 工具）、是否支持无障碍、系统要求 |
| 商店列表 | 描述、功能列表、截图（≥1 张，1366×768 或 1920×1080）、商店图标（可选） |
| 年龄分级 | 填写问卷，一般得到 3+ |
| 定价和可用性 | 免费、所有市场 |
| 提交选项 | 隐私政策 URL（**必填**，可用 GitHub Pages 建一个静态页） |

提交后 **1–3 个工作日** 出审核结果。通过后 Store 会用微软的证书重签，用户下载安装不再有 SmartScreen 拦截。

---

## 5. 上架前必须处理的三件事

### 5.1 不能申请管理员权限

MSIX 应用以当前用户权限运行，清单里的 `requireAdministrator` 会被认证直接拒绝。
正式工程已经切到 `asInvoker`。

副作用：如果某台机器的资源管理器是 **高完整性**（提权运行），那么该机器上托盘图标仍会
被 UIPI 拒绝（`Shell_NotifyIcon` 返回 5）。这是系统限制，Store 版无法绕过。
绝大多数机器的资源管理器是「中」完整性，正常可用。

### 5.2 开机启动要用 StartupTask（代码已适配）

`package\Package.appxmanifest` 里已经声明了：

```xml
<desktop:Extension Category="windows.startupTask" Executable="Launchpad.exe" EntryPoint="Windows.FullTrustApplication">
  <desktop:StartupTask TaskId="LaunchpadStartup" Enabled="false" DisplayName="Launchpad" />
</desktop:Extension>
```

打包后，`Startup.cs` 里写 `HKCU\...\Run` 的做法会被 MSIX 虚拟化，**不能真正开机启动**。
需要改成调用 `Windows.ApplicationModel.StartupTask.GetAsync("LaunchpadStartup")` 再 `RequestEnableAsync()`。

> **代码已适配**：`src\Packaging.cs` 用 `GetCurrentPackageFullName` 判断是否以 MSIX 包运行；
> 打包运行时，设置窗口里的「开机启动」变成只读提示，引导用户去
> 「设置 → 应用 → 启动」里开关，不再写会被虚拟化的 `HKCU\...\Run`。

### 5.3 配置目录（代码已适配）

`FolderStore.ResolveRoot()` 现在优先用 `%LOCALAPPDATA%\Launchpad`：

- 打包运行时会落到包容器里（`Packages\<包名>\LocalCache\...`），卸载时一起清理，符合商店应用预期
- 首次切换到新目录时会把旧版 `%APPDATA%\Launchpad` 里的 `folders.txt / order.txt / settings.txt`
  复制过去，升级不丢配置
- 两个目录都不可写时，退回 exe 同目录的 `data\`（便携模式）

---

## 6. 版本更新

每次发布新版本：

1. 改 `Launchpad.csproj` 的 `<Version>`（和 `FileVersion`）
2. `build-msix.ps1 -Version "1.1.0.0"`
3. Partner Center → 包 → 上传新包 → 提交
4. MSIX 的 `Version` 必须比上一版大，且格式固定为 `x.y.z.0`

---

## 7. 两个构建产物的区别

| 构建 | 命令 | 清单 | 用途 |
| --- | --- | --- | --- |
| 个人版 | `.\build.ps1` | `app.manifest`（管理员） | 自己用，托盘稳定 |
| Store 版 | VS 打开 sln / `dotnet build` | `src\app.manifest`（asInvoker） | 打 MSIX 上架 |


