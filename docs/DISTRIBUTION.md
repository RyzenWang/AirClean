# 分发与更新

AirClean 有两条分发线，都不需要本项目持有代码签名证书：

- **Windows**：Microsoft Store 的 MSIX 包。Store 负责签名与更新。
- **macOS / Linux**：免安装压缩包，解压即用。

发布工作流从版本 tag 触发。

## 发布产物

| 平台 | 产物 | 用途 |
| --- | --- | --- |
| Windows x64 | `AirClean-<version>-win-x64.msix` | 上架 Microsoft Store；也可本地测试安装 |
| Windows x64 | `AirClean-<version>-win-x64-portable.zip` | 免安装运行（没有 Store 账号时） |
| macOS arm64 / x64 | `AirClean-<version>-osx-<arch>-portable.zip` | 解压即用 |
| Linux x64 | `AirClean-<version>-linux-x64.tar.gz` | 解压即用，含桌面入口与图标 |

不再产出 `Setup.exe`、`.pkg`、`.nupkg` 或 `releases.*.json`，因此也没有"未签名安装包被扣留"这一步。

所有平台都用 NativeAOT 提前编译。Windows 需要 MSVC 生成工具；Linux 需要 clang 与
zlib1g-dev，并在 Ubuntu 22.04 上构建以保证 glibc 下限不高于 2.35；macOS 使用 Xcode 自带 clang。

## 本地打包

**便携包（三平台通用）**

```bash
dotnet publish src/AirClean.App/AirClean.App.csproj -c Release -r win-x64 \
  -p:PublishAot=true -p:StripSymbols=true -p:Version=0.6.0 -o publish/win-x64
```

然后把 `publish/win-x64` 整个压缩成 zip；macOS / Linux 同理，只换 `-r`。

**MSIX（Windows）**

```powershell
./packaging/msix/Build-Msix.ps1 `
  -PublishDir publish/win-x64 `
  -Version 0.6.0 `
  -Output releases/win/AirClean-0.6.0-win-x64.msix
```

脚本会用 `System.Drawing` 从 `src/AirClean.App/Assets/airclean.png` 生成 44/150/50/310×150
四种图标，按 `packaging/msix/AppxManifest.xml` 模板写出清单（版本补成四段），再调用 Windows
SDK 的 `makeappx.exe` 打包。本机实测：9 个文件、约 17.6 MB。

## 发布一个新版本

```bash
git tag v0.6.0
git push origin v0.6.0
```

工作流会构建三个平台、上传产物并创建 GitHub Release；Release 说明取自 `CHANGELOG.md`
对应段落。tag 里带 `-preview` 会标成预发布。

不加 tag 也可以验证打包：手动运行 **Release**（`workflow_dispatch`），它只构建并上传
artifact，不创建 Release。

## Microsoft Store

清单里的两项必须与 Partner Center 完全一致，否则会装成另一个应用：

| 清单字段 | 现在用的值 | 说明 |
| --- | --- | --- |
| `Identity/@Name` | `RyzenWang.AirClean` | 在 Partner Center 预留的名称 |
| `Identity/@Publisher` | `CN=AirClean contributors` | 必须是 Partner Center 显示的 Publisher 全名 |

`Build-Msix.ps1` 的 `-Identity` / `-Publisher` 可以覆盖这两项。提交时上传 MSIX 即可，
**签名由 Store 完成**，仓库里不需要任何证书或密钥。

打包环境与运行时的三点约束，代码里已经处理：

- **更新走 Store**：`PlatformServices.IsStoreManaged` 用 `GetCurrentPackageFullName` 判断
  是否在包内运行；是的话 `UpdateService.IsInstalled` 直接为 false，不去 GitHub 找
  Velopack 更新（那种包已经不再发布）。
- **不提权**：MSIX 不适合启动提权子进程，包内运行时 `AdminHelperClient` 不会被创建，
  管理员加速与系统级清理分类（Windows Update 缓存、系统日志）因此不可用，大文件扫描
  回退到目录遍历。
- **数据目录**：设置、日志、单实例锁仍在用户的 `%APPDATA%\AirClean`。

本地测试安装（可选）：给 MSIX 自签一个测试证书并信任它，或用开发者模式侧载；这只是
为了在你自己的机器上验证，**不要**用于分发。

## 应用内更新

| 构建形态 | 更新方式 |
| --- | --- |
| Microsoft Store | Store 自动更新 |
| 便携包（Windows / macOS / Linux） | 不自更新，重新下载新版本覆盖 |

`Program.Main` 仍会在 UI 之前执行 `VelopackApp.Build().Run()`，`UpdateService` 也保留着，
但既然不再发布安装器，它只在"未来重新引入安装版"时才有意义；便携版与源码运行时
`IsInstalled` 为 false，不产生任何网络请求。

## 工作流清单

| 文件 | 触发 | 作用 |
| --- | --- | --- |
| `.github/workflows/ci.yml` | push 到 main / PR | 三平台 build + test |
| `.github/workflows/release.yml` | `v*` tag，或手动 | MSIX + 便携包，创建 Release |

## Linux 桌面入口

Linux 的 tar.gz 里除可执行文件与原生库，还带两样东西：

- `AirClean.desktop`：桌面入口（`Categories=Utility;System;`，`StartupWMClass=AirClean`）；
- `airclean.png`：256px 图标，和入口里的 `Icon=airclean` 对应。

解压后这样装（不需要 root）：

```bash
mkdir -p ~/.local/bin ~/.local/share/applications ~/.local/share/icons/hicolor/256x256/apps
cp AirClean ~/.local/bin/
cp airclean.png ~/.local/share/icons/hicolor/256x256/apps/
cp AirClean.desktop ~/.local/share/applications/
update-desktop-database ~/.local/share/applications 2>/dev/null || true
```

入口里写的是 `Exec=AirClean`，所以 `~/.local/bin` 需要在 `PATH` 里；如果放在别处，
改 `.desktop` 的 `Exec=` 为绝对路径即可。

## 当前限制

- MSIX 已在 Windows 上本地打包并解包核对（清单字段、图标、AOT 主程序），但**还没有真正
  提交过 Store**：审核、Store 签名、包身份匹配都要在 Partner Center 里走一次才算验证。
- 本地安装 MSIX 需要自签测试证书或开发者模式，仓库不提供证书。
- 便携版不自更新，macOS 因此失去了此前 `.pkg` 安装版具备的自更新能力。
- Linux 只提供便携 tar.gz，deb 与 AppImage 还没做。
- 没有 arm64 Windows 与 linux-arm64 产物。
