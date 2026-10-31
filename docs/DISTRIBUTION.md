# 分发、签名与自动更新

AirClean 用 [Velopack](https://velopack.io/) 生成 Windows 安装器、macOS `.pkg` 与自更新包。
发布工作流从版本 tag 触发。

## 发布产物

以 `v0.1.0` 为例：

| 平台 | 产物 |
| --- | --- |
| Windows x64 | `AirClean-win-Setup.exe`、`AirClean-win-Portable.zip`、`AirClean-0.1.0-full.nupkg`、`releases.win.json` |
| macOS arm64 | `AirClean-osx-arm64-Setup.pkg`、便携包、`.nupkg`、`releases.osx-arm64.json` |
| macOS x64 | `AirClean-osx-x64-Setup.pkg`、便携包、`.nupkg`、`releases.osx-x64.json` |
| Linux x64 | `AirClean-<version>-linux-x64.tar.gz` |

`releases.*.json` 是应用内更新用的清单。便携版与源码运行不会自更新；装 Setup.exe 或 `.pkg` 才会。

## 本地打包

```bash
dotnet publish src/AirClean.App/AirClean.App.csproj -c Release -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:Version=0.1.0 \
  -o publish/win-x64

dotnet tool install vpk --version 1.2.161 --tool-path .vpk

./.vpk/vpk pack \
  --packId AirClean \
  --packVersion 0.1.0 \
  --packDir publish/win-x64 \
  --mainExe AirClean.exe \
  --packTitle AirClean \
  --packAuthors "AirClean contributors" \
  --icon src/AirClean.App/Assets/airclean.ico \
  --runtime win-x64 \
  --channel win \
  --outputDir releases/win
```

macOS 上把 `--mainExe` 改成 `AirClean`、`--icon` 用 `src/AirClean.App/Assets/airclean.icns`，
`--runtime` 用 `osx-arm64` / `osx-x64`，`--packId` 用 `AirClean-osx-<arch>`，
`--channel` 用 `osx-<arch>`，这样一次 release 里多个平台的完整包不会重名。

## 发布一个新版本

```bash
git tag v0.1.0
git push origin v0.1.0
```

工作流会构建四个平台、上传产物，并创建 GitHub Release。tag 里带 `-preview` 会标成预发布，
预发布不会被应用内更新选中。

不加 tag 也可以验证打包：在 Actions 里手动运行 **Release**（`workflow_dispatch`），
它只构建并上传 artifact，不创建 Release。

## Windows 代码签名

配置下面两个仓库 secret 后会自动签名，否则产出未签名安装包（SmartScreen 会警告）：

| Secret | 含义 |
| --- | --- |
| `WINDOWS_CERTIFICATE_BASE64` | `.pfx` 的 base64 |
| `WINDOWS_CERTIFICATE_PASSWORD` | pfx 密码 |

只配一个会当作未配置处理，并在运行日志里给出警告。

现代 OV 证书通常无法导出成可移植 pfx，生产环境建议改用 Azure Trusted Signing 或 HSM，
并把对应的 `--signParams` 传进去。

## macOS 签名与公证

需要 Apple Developer Program 会员，以及 Developer ID Application 与 Developer ID Installer
两张证书。

| Secret | 含义 |
| --- | --- |
| `MACOS_CERTIFICATE_BASE64` | Developer ID Application `.p12` 的 base64 |
| `MACOS_INSTALLER_CERTIFICATE_BASE64` | Developer ID Installer `.p12` 的 base64 |
| `MACOS_CERTIFICATE_PASSWORD` | 两个 `.p12` 的密码 |
| `MACOS_APP_SIGN_IDENTITY` | 形如 `Developer ID Application: Your Name` |
| `MACOS_INSTALLER_SIGN_IDENTITY` | 形如 `Developer ID Installer: Your Name` |
| `APPLE_ID` | Apple 账号邮箱 |
| `APPLE_APP_PASSWORD` | notarytool 用的 App 专用密码 |
| `APPLE_TEAM_ID` | Apple Developer Team ID |
| `MACOS_KEYCHAIN_PASSWORD` | CI 临时钥匙串的随机密码 |

工作流会创建临时钥匙串、导入两张证书、生成 notary 配置，然后把
`--signAppIdentity` / `--signInstallIdentity` / `--notaryProfile` / `--keychain` 传给 `vpk pack`。
未配置时仍会生成未签名 pkg，Gatekeeper 会拦截。

## 应用内更新

`Program.Main` 里 `VelopackApp.Build().Run()` 在所有 UI 之前执行。
`UpdateService` 只在安装版里工作：

1. 启动 2 秒后检查稳定版
2. 后台下载并显示进度
3. 状态栏出现 **重启以更新 X.Y.Z**
4. 点一下由 Velopack 应用更新并重启

`GithubSource` 只读稳定 release，预发布不会推送更新。从源码或便携包运行时
`UpdateManager.IsInstalled` 为 false，检查直接跳过、不产生任何网络请求。

端到端验证：

1. 用 Setup.exe 装 `v0.1.0`
2. 打 `v0.1.1` tag，等 Release 产出新的 `releases.*.json` 与 `.nupkg`
3. 打开已安装的 0.1.0，等状态栏更新按钮

## 工作流清单

| 文件 | 触发 | 作用 |
| --- | --- | --- |
| `.github/workflows/ci.yml` | push 到 main / PR | 三平台 build + test |
| `.github/workflows/release.yml` | `v*` tag，或手动 | 打包、可选签名、创建 Release |

## 当前限制

- Windows 与 macOS 的签名步骤都**没有被实际执行验证过**（本仓库还没有证书）。
  未签名路径已在本地用 `vpk pack` 验证：能产出 Setup.exe / Portable.zip / nupkg / releases.json。
- Linux 只提供便携 tar.gz，deb 与 AppImage 还没做。
- 没有 arm64 Windows 与 linux-arm64 产物。
