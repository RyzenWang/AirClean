# AirClean 差距分析（对标 AirPDF）

> 基准：AirClean 0.2.0（README 标为 0.2 预览）· AirPDF 0.8.0
> 依据：两个仓库的源码、README、DESIGN.md、`.github/workflows`（2026-11-10 本地快照）
> 说明：粗估为单人节奏，用于排期而非承诺；标 ★ 的条目不在当前 README 路线图里。

## 摘要

主链路已经打通：扫描（规则缓存 / 大文件 / 重复文件 / 废纸篓）→ 预览勾选 →
确认 → 清理 → 完成页。CI 也已经是三平台构建 + 三平台 NativeAOT 门禁，比多数
0.x 项目扎实。

与 AirPDF 0.8 的差距不在"有没有功能"，而在四层配套：

1. **清理后的闭环**：撤销、最近清理、跳过报告、把"废纸篓里的空间"真正释放掉。
   这是清理软件信任的核心，当前只有"打开回收站"。
2. **桌面常驻**：托盘、定时提醒、单实例、崩溃日志。托盘与崩溃日志 AirPDF 有
   可参考的实现；单实例和提醒需要自己设计。
3. **设置与扫描范围**：现在只有主题、语言、管理员加速三个开关，而且每次扫描
   都全盘遍历大文件与重复文件，没有"快速扫描"。
4. **分发与合规**：签名验证工作流、Store/MSIX、CHANGELOG、社区文档、第三方声明，
   以及测试深度（66 对 284）。

另有 12 个实现级缺陷（§5），多数落在"信任"这条线上，建议先修。

---

## 1. P0：清理闭环

### 1.1 真正的撤销（路线图已列，但这里给出可落地方案）

| 现状 | 缺口 |
| --- | --- |
| `ITrashService` 只有 Probe / MoveToTrash / Empty / OpenTrash | 没有 Restore，接口层就缺 |
| Journal 只记 `Path` + `Size`（`Deleting/JournalJsonContext.cs`） | 没有恢复路径、没有批次状态 |
| Windows 用 `SHFileOperation` + `FOF_ALLOWUNDO`，完成页只给"打开回收站" | 没有解析 `$Recycle.Bin\<SID>\$I*` 的恢复信息 |
| macOS 直接用 `File.Move` 搬进 `~/.Trash`（`UnixTrashService`） | 没有走 `NSFileManager.trashItemAtURL`，拿不到 `resultingItemURL` |
| Linux 已写 `.trashinfo`（含原始路径） | 已有恢复数据，但没人读，也没有 UI |

落地顺序建议：

1. 扩展 `ITrashService`：`bool TryRestore(string trashPath, out string? error)`，
   或在 Core 里加 `IRestoreService`，避免把恢复逻辑塞进扫描器。
2. Journal v2：批次记录 `mode`、`items[{p, s, r}]`（`r` = 回收站内路径）、
   `canceled`、`restored` 状态；读取端按批次回放。
3. Windows：解析 `$I` 元数据（原始路径 + `$R` 文件名），或改用 `IFileOperation`
   并在删除后枚举回收站匹配；恢复 = 从 `$R` 移回原路径（跨卷时提示）。
4. macOS：改用 `NSFileManager`（P/Invoke ObjC 或 `trash` CLI）拿到
   `resultingItemURL`；外置卷还要处理 `/.Trashes/<uid>`。
5. Linux：读 `info/*.trashinfo` 的 `Path=`，恢复时避免覆盖同名文件。
6. 完成页在 `CanUndo` 时显示"撤销上一次清理"，失败/被清空时如实说明，不能给
   一个点了没反应的按钮。

估时：接口 + Journal + UI 约 1 天；Windows 2–3 天（含测试）；macOS、Linux 各 1 天。
测试要点：恢复后内容一致、目标已存在、回收站已被清空、跨卷、部分失败。

### 1.2 最近清理（★）

- 本地化已经写了 `L.RecentCleanups` / `L.NoRecentCleanups`
  （`App/Services/LocalizationService.cs:54-55`），但 XAML 与 ViewModel 都没有使用，
  属于死字符串。
- Journal 没有读取 API，欢迎页也就没有数据源。
- 要做：`JournalReader`（最近 N 批）、欢迎页"最近清理"列表、点击回看明细、
  可撤销；保留 30 天 / 200 批，超出滚动删除（DESIGN 4.6 已写）。
- 可直接借用 AirPDF 的 recent-files shelf 的信息结构（标题 + 时间 + 体积 + 主操作）。

估时：1–1.5 天。

### 1.3 跳过报告（★）

- 完成页只有三类计数和一句汇总（`MainWindowViewModel.RefreshCompletionTexts`），
  没有路径、没有原因分组、不能导出。
- `DeletionReport` 只有 `Skipped` 计数，没有 DESIGN 里写的 `FailedSamples`。
- 要做：按原因分组（占用 / 权限 / 扫描后变更 / 受保护 / 链接 / 不存在），
  每组可展开前 N 条，提供"导出 CSV"（只含路径与原因），并解释如何避免。
- 这是"删得少一点，说得清楚一点"这条底线的直接体现，优先级高于多数新功能。

估时：1–1.5 天（含导出与测试）。

### 1.4 完成后释放废纸篓（★）

- DESIGN 1.3 / 2.2 明确写了"清空废纸篓是独立动作"，但完成页只有
  "打开回收站"（`MainWindow.axaml:860-866`）。
- 用户按默认方式清理后，看到的提示是"还有 X 在废纸篓里，清空后才会释放"，
  却没有就地清空的入口；只能回欢迎页重新扫描、再勾废纸篓分类。
- 要做：完成页在 `TrashedBytes > 0` 时给"清空本次移入的文件"或
  "去清空废纸篓"，二次确认并标明不可恢复；或跳转到废纸篓分类并预选。
- 与撤销互斥：清空后撤销按钮必须消失。UI 文案要同时说清这一点。

估时：0.5–1 天。

### 1.5 删除失败保护（★ DESIGN 4.6 写了，代码没有）

- 当前逐条跳过，不统计失败率，不中止。
- 要做：
  - 单批失败超过 30% 自动中止并报告（避免在错误状态下持续删除）；
  - 磁盘满导致移入废纸篓失败时，提示"空间不足"，提供显式永久删除（二次确认）；
  - 删除后复查（存在性 / 大小）并计入报告，避免"以为删了"。

估时：1 天。

### 1.6 Journal 生命周期（★）

- 现在没有保留策略、没有 schema 迁移、没有 `canceled` 字段。
- 要做：按 DESIGN 的 30 天 / 200 批滚动；`JournalPayload.V` 升级时保留读取旧版
  的能力；把"取消的批次"也标出来，撤销时不能假装整批完整。

估时：0.5 天。

---

## 2. P1：扫描范围、规则与正确性

### 2.1 快速扫描 vs 全盘扫描（★ 这是体验上最大的单点缺口）

- 现状：每次扫描都 `IncludeLargeFiles: true` / `IncludeDuplicates: true`
  （`MainWindowViewModel.cs:475-484`），大文件是整盘遍历，重复文件在数据盘也是
  整盘遍历并哈希。README 自己承认"机械盘冷缓存下仍可能要几分钟"。
- DESIGN 2.3 的"默认快速扫描 = 已知缓存位置 + 废纸篓 + 日志；整盘扫描是显式选择"
  没有实现。
- 要做：
  - 欢迎页提供"快速扫描 / 全盘扫描"两个入口（或一个开关 + 说明）；
  - 全盘前给出预估（基于上一次扫描耗时 / 文件数）；
  - 结果页标注本次范围，避免用户以为"全盘只有 3 GB 可清理"；
  - 快速模式下大文件/重复文件可只扫用户内容目录（已有 `DefaultContentRoots`）。

估时：2–3 天（VM 参数已具备，主要是 UI、默认值与文案）。

### 2.2 排除项 / 忽略列表（★）

- 现在没有任何"别动这个目录"的入口；用户只能取消勾选，下次扫描又会回来。
- 要做：设置里维护路径列表，扫描与删除双重过滤；`PathGuard` 之外再加一层
  用户级排除；持久化到 `AppSettings`。
- 与"规则根目录"的区别要在文案里说清。

估时：1.5–2 天（含设置 UI 与测试）。

### 2.3 阈值与上限可配（★）

硬编码项（建议全部进设置页）：

| 项 | 当前值 | 位置 |
| --- | --- | --- |
| 大文件阈值 | 100 MB | `LargeFileScanner.DefaultThresholdBytes` |
| 大文件列表上限 | 200 | `LargeFileScanner.MaxItems` |
| 重复文件最小体积 | 1 MB | `DuplicateScanner.MinFileSize` |
| 重复文件组上限 | 200 | `DuplicateScanner.MaxGroups` |
| 哈希预算（系统盘） | 8 GB | `DefaultHashBudgetBytes` |
| 哈希预算（数据盘） | 2 GB | `MainWindowViewModel.cs:484` |
| 目录深度上限 | 24（大文件）/ 16（重复） | 两个扫描器 |
| 规则条目总数上限 | 200,000 | `Scanner.MaxItems`（全盘合计） |

估时：1 天（配合设置页）。

### 2.4 硬链接与 APFS 克隆（★ 正确性问题）

- 重复文件先按大小分桶、再比首 64 KB 的 SHA-256、最后整文件 SHA-256；
  **硬链接会被当成"两份"**，界面承诺"可释放 X"，删掉一份其实一个字节都没释放。
  这是信任问题，不是优化项。
- 要做：
  - Windows：`GetFileInformationByHandleEx(FileIdInfo)` 取 `VolumeSerialNumber +
    FileId`；同一文件 ID 只保留一份参与分组。
  - Unix：`stat` 的 `st_dev + st_ino`。
  - APFS clone / 稀疏文件：详情页加一行脚注，说明"删除副本可能不释放空间"。
- 测试：同一文件建两个硬链接，断言只出现一份、可释放体积为 0 或正确值。

估时：1–2 天。

### 2.5 只读建议项（★ DESIGN 4.8）

- Windows：WinSxS（DISM）、`hiberfil.sys`、系统还原点；
  macOS：Time Machine 本地快照；Linux：旧内核、`/var/log` 归档。
- 形态：操作卡，展示体积 + 官方命令 + "复制命令"，**不在应用内执行**。
- 这类"看得见但不能一键做"的卡片能显著提升可信度，且实现风险低。

估时：2 天。

### 2.6 截断要透明（★）

- 重复组超过 200 组会静默丢弃（`DuplicateScanner.cs:240-243`），
  `Limited` 只反映哈希预算，不反映组数截断。
- 大文件用 `MatchedBytes/MatchedCount` 保留了"实际找到多少"；重复文件的
  `Matched*` 只按前 200 组计算，被截断时没有等价的"另有 N 组"。
- 要做：组数截断也置 `Limited`；UI 文案区分"预算到上限"和"只显示前 200 组"。

估时：0.5 天。

---

## 3. P1/P2：桌面集成与可达性

| # | 事项 | 现状 | AirPDF 参考 | 估时 |
| --- | --- | --- | --- | --- |
| 3.1 | 托盘 + 关闭到托盘 + 托盘菜单 | 无 `TrayIconService`，关闭窗口即退出 | `Services/TrayIconService.cs`，README 有行为说明 | 2–3 天 |
| 3.2 | 单实例（★） | 无 Mutex；两个实例可并发扫描/清理同一磁盘 | 无（阅读器场景不同），需要自己设计 | 0.5 天 |
| 3.3 | 崩溃日志（★） | `Program.cs` 无任何异常处理；WinExe 崩溃无痕 | `CrashLog.cs`：AppDomain / TaskScheduler / Dispatcher / 非零退出，512 KB 上限 | 0.5 天 |
| 3.4 | 定时提醒 | 未开始（路线图） | 无 | 2 天 |
| 3.5 | 键盘体系 | 只有 `Ctrl+R`、`Esc`（`MainWindow.axaml:14-17`）；DESIGN 承诺 `Ctrl+,`、↑↓+Space、Tab 顺序 | 快捷键集中在一个 `OnWindowKeyDown` 大 switch（约 40 个分支），没有 `KeyBinding`；AirClean 用声明式绑定可以更干净 | 1–2 天 |
| 3.6 | 无障碍名称 | 0 处 `AutomationProperties` | 约 48 处 | 1–2 天 |
| 3.7 | 高对比度主题 | 无 | 手动 4 状态循环（System / Dark / Light / HighContrast）+ 独立 brush 字典；注意 Avalonia 11 没有原生 HC variant | 1–2 天 |
| 3.8 | 窗口位置记忆 | 只记大小/最大化，不记位置 | 有窗口状态持久化 | 0.5 天 |
| 3.9 | 命令行参数 | 无（只有内部 `--admin-helper`） | `CommandLineOptions.cs`：`--help`、`--version`、打开文件 | 0.5 天 |
| 3.10 | "关于"弹层 | 无；状态栏只有版本号 | 有版本/帮助提示 | 0.5 天 |

对清理器来说，3.1 / 3.2 / 3.3 的价值明显高于 3.9 / 3.10：托盘解决"定期清一下"
的使用场景，单实例防止并发清理，崩溃日志让用户反馈可排查。

### 3.11 平台补齐（★）

| 平台 | 缺口 | 说明 |
| --- | --- | --- |
| macOS | 完全磁盘访问（TCC）引导 | 扫描 `~/Library/Containers` 大量失败时，给一张卡片 + 深链到"系统设置 › 隐私与安全性 › 完全磁盘访问"，而不是静默少报（DESIGN 4.9） |
| macOS | 废纸篓规范 | 现在只处理 `~/.Trash`；外置卷在 `/.Trashes/<uid>`，且没有还原 URL（见 §5 #9） |
| Linux | `.desktop` 文件 | AirPDF 有 `packaging/linux/airpdf.desktop`；AirClean 没有，桌面环境里没有图标/启动器/文件管理器入口 |
| Linux | 托盘依赖 | StatusNotifier/AppIndicator 不可用时要在设置里说明，而不是让托盘按钮"消失" |
| 产物 | `win-arm64` / `linux-arm64` | 路线图未开始；AirPDF 也只在 Store 侧做 ARM64 MSIX，可参考其构建方式 |
| 全部 | 单实例 + 提醒的自动启动 | 若做定时提醒，需要登录自启（Windows 计划任务 / macOS LaunchAgent / Linux autostart），要单独设计开关 |

---

## 4. P1：设置页

现状：设置只是一个 `MenuFlyout`（主题 3 项、语言 2 项、管理员加速 1 项），
外加一行"更多设置将在后续版本提供"（`MainWindow.axaml:68-84`）。

需要补的设置项（按优先级）：

1. 扫描范围：快速 / 全盘；是否包含重复文件、大文件。
2. 排除项：路径列表（增删）。
3. 阈值：大文件 ≥ N MB、列表上限；重复文件 ≥ N MB、组上限、哈希预算。
4. 删除方式：默认废纸篓 / 每次询问；是否允许永久删除。
5. 更新：自动检查开关、是否接收 preview。
6. 管理员加速：保留现有开关，补充"什么时候会弹 UAC"的说明。
7. 日志：详细日志开关（默认关，含路径，仅本地）——DESIGN 4.11。
8. 语言 / 主题：从 flyout 迁入（flyout 可保留快捷入口）。

配套改动：

- `AppSettings` 需要扩展约 8–12 个字段，并真正使用 `SchemaVersion` 做迁移
  （现在这个字段只写不读，`AppSettings.cs:9`）。
- 设置页建议做成独立窗口或独立 `UserControl`，不要继续塞进 1093 行的
  `MainWindow.axaml`。

估时：3–5 天（含持久化测试与文案）。

---

## 5. 实现级缺陷（建议直接开 issue）

| # | 缺陷 | 位置 | 影响 |
| --- | --- | --- | --- |
| 1 | 扫描取消后回欢迎页，DESIGN 2.3 说"已发现结果保留可查看" | `MainWindowViewModel.cs:494-497` | 用户取消后丢失已扫到的结果 |
| 2 | `L.RecentCleanups` / `L.NoRecentCleanups` 无任何 UI 使用 | `LocalizationService.cs:54-55` | 死字符串，欢迎页缺"最近清理" |
| 3 | 重复文件组超过 200 静默截断，`Limited` 不反映 | `DuplicateScanner.cs:240-243` | 低估可清理体积且不说明 |
| 4 | Journal 无恢复路径、无保留期、无取消标记 | `JournalJsonContext.cs` / `DeletionService.cs:250` | 撤销无法实现，日志无限增长 |
| 5 | 无单实例保护 | `Program.cs` | 两个实例可能并发清理同一目录 |
| 6 | 无未处理异常日志 | `Program.cs` | 崩溃后无任何线索 |
| 7 | Windows 删除走 `SHFileOperation`，注释自认不支持 MAX_PATH 以上 | `WindowsTrashService.cs:8-9` | 长路径文件永远删不掉 |
| 8 | 受保护用户目录硬编码英文名，`Downloads` 不在列表 | `PathGuard.cs:163-182` | 目录重定向/本地化时防线有缺口 |
| 9 | macOS 只用 `~/.Trash`，无外置卷 `.Trashes`、无恢复 URL | `UnixTrashService.cs:21-24` | 外置卷清理结果不准确，撤销缺数据 |
| 10 | `AppSettings.SchemaVersion` 只写不读，无迁移 | `AppSettings.cs:9` | 设置结构升级时会静默丢配置 |
| 11 | 完成页没有"清空本次废纸篓"入口 | `MainWindow.axaml:860-866` | 用户以为清理完就释放了空间 |
| 12 | `TreatWarningsAsErrors` 只在 Core / Platform 设置，App 项目没有；`Directory.Build.props` 未统一 | `Directory.Build.props`、`AirClean.App.csproj` | App 层的警告长期无人处理 |

前 6 条建议在 0.3 一起修；第 7、8、9 条放在平台验证那一批；第 10、12 条随设置页
和 CI 改造顺手做；第 11 条已在 §1.4 覆盖。

---

## 6. 工程与分发

### 6.1 签名与公证（进行中）

已有：`release.yml` 按 secrets 条件签名；未配置时隐藏未签名安装包；
`DISTRIBUTION.md` 有 Secrets 说明。

缺（对照 AirPDF）：

- `signing-check.yml`（`workflow_dispatch`）：无证书时用 `New-TestCertificate.ps1`
  生成一次性自签证书，跑通 pack → 签名 → `Get-AuthenticodeSignature` 校验；
  有证书时报告指纹与有效期；**半配置直接失败**（只设证书不设密码）。
  macOS 侧导入 p12 后 `find-identity`、`pkgutil --check-signature`，
  可选 `notarytool` + `spctl`。
- `packaging/signing/Set-SigningSecrets.ps1 -DryRun`：校验私钥、EKU
  （1.3.6.1.5.5.7.3.3）、有效期后再 `gh secret set`。
- 真实证书跑通一次并记录结果（Windows `signtool verify /pa`、macOS
  `spctl -a -vv` + `stapler validate`）。
- 一个已知坑：AirPDF 的自签脚本用 .NET `CertificateRequest`，因为
  openssl 生成的 PKCS#12 会让 signtool 报 `TRUST_E_BAD_DIGEST`。

价值：把"能不能签"从发布当天的手工问题，变成随时可跑的工作流。

### 6.2 Store / MSIX（★）

- AirClean 完全没有；AirPDF 有 `packaging/msix/AppxManifest.xml` +
  `Build-Msix.ps1`、`msix-check.yml`（含 ARM64 包）、`docs/STORE_SUBMISSION.md`、
  `docs/store/`（listing、PRIVACY、截图）、`packaging/store/capture.ps1`。
- AirPDF 的 `msix-check.yml` 值得照搬形态：在真实 runner 上安装 MSIX、
  启动 12 秒、用"含空格 + 中文"的路径验证文件关联、断言 AppData 重定向，
  并在 `always()` 里卸载；Store 任务读取 `MSIX_*` secrets，缺失时跳过而非失败。
- 建议先做 MSIX + 隐私政策，即使暂不提交；Store 是 Windows 上建立信任的
  最短路径，也顺带解决安装器的 SmartScreen 问题。

估时：3–5 天（不含审核等待）。

### 6.3 版本与变更记录（★）

- 没有 `CHANGELOG.md`；AirPDF 从 0.1.0 到 0.8.0 有 11 条记录，格式接近
  Keep a Changelog。
- AirPDF 的 release job 会从 `CHANGELOG.md` 抽取 `## [版本]` 段作为 Release
  notes，tag 含 `-preview` 自动标预发布。AirClean 现在用 GitHub 的
  `generate_release_notes`，建议换成同一份 CHANGELOG，保证"仓库 / Release /
  应用内更新提示"三处说法一致。
- 建议：从 git 历史补 0.1.0 / 0.2.0；之后每个 tag 更新。

估时：0.5 天（补齐历史）+ 每次发布 10 分钟。

### 6.4 仓库规范（★）

缺以下文件（AirPDF 全部具备）：

- `CONTRIBUTING.md`（README 里已有一段"参与贡献"，可扩展成独立文件）
- `SECURITY.md`（漏洞报告渠道；README 只有一句"不要公开贴细节"）
- `CODE_OF_CONDUCT.md`
- `THIRD-PARTY-NOTICES.md`（Avalonia / SkiaSharp / Velopack / Inter 的许可证，
  MIT 分发应当包含）
- `.github/ISSUE_TEMPLATE/bug_report.yml`、`feature_request.yml`、`config.yml`
- `.github/PULL_REQUEST_TEMPLATE.md`

估时：1 天（模板可参考 AirPDF 后改写）。

### 6.5 文档（★）

- 缺 `docs/ARCHITECTURE.md`：DESIGN.md 偏产品与视觉，工程向的模块边界、
  线程模型、取消传播、AOT 约束、命名管道协议没有独立文档。
- 缺 `docs/index.md`：AirPDF 用它托管 Store 需要的隐私政策页面。
- 建议：ARCHITECTURE 从 DESIGN 4.x 抽取并补"为什么这样分层""新增一个规则
  要改哪些文件"两节。

估时：1–2 天。

### 6.6 测试（★）

| 项目 | AirClean | AirPDF |
| --- | --- | --- |
| Core | 50 | 122 |
| App（Headless） | 16 | 162 |
| 合计 | 66 | 284 |

现有 66 个测试覆盖了规则、路径守卫、扫描器、MFT 解析、删除、三态勾选、
目录聚合，以及三条主链路，质量不错；缺口在：

- 清理过程的取消与部分结果报告；
- 确认弹层的永久删除 / 废纸篓分支；
- 跳过报告与导出；
- 设置持久化与迁移；
- 语言 / 主题切换后的整页文案；
- 硬链接、junction、长路径、Unicode、只读文件；
- 托盘 / 单实例 / 崩溃日志（需要进程级测试）；
- 性能基准（DESIGN 5.3 要求 2 万文件回归阈值 ±20%）。

方法上可借鉴 AirPDF 的 App 测试：直接构造真实 `MainWindow`（真 XAML、
真实路由键事件、stub 对话框/窗口服务、每测临时设置目录），比只测 VM 更能
挡住 XAML 绑定与命令名回归。AirClean 目前只有 `WelcomeScreenTests` 构造了真实
窗口，其余是 VM 级测试。

估时：把 App 测试补到 60+、Core 补到 80+ 约 1 周。

### 6.7 CI（★）

已有且值得保留：三平台 build/test、三平台 AOT、IL 警告门禁、产物体积上限、
glibc 下限检查、MFT 定时验证工作流。

可补：

- Release 工作流先跑测试：AirPDF 的 `release.yml` 用 test job 作为打包门槛，
  AirClean 现在是 tag 直接进打包；
- `dotnet format --verify-no-changes` 与 `TreatWarningsAsErrors`；
- 依赖审查 / CodeQL；
- Release 产物 smoke test（启动后立即退出，例如 `--version` 或 `--check`）；
- 文档链接检查；
- 签名检查工作流（见 6.1）。

估时：1–2 天。

### 6.8 代码结构（★）

- `MainWindow.axaml` 1093 行 / 60 KB，`MainWindowViewModel` 1134 行；
  五个阶段（欢迎 / 扫描 / 结果 / 完成 / 确认弹层）都在一个文件里。
- 没有 DialogService / WindowService，弹层用 `IsVisible` 切换。
- 在加设置页、托盘、撤销之前建议先拆：每个阶段一个 `UserControl`，
  设置与确认弹层走服务，VM 按阶段拆成多个 partial 或子 VM。

估时：2–3 天（纯重构，靠现有 Headless 测试兜底）。

---

## 7. 与 AirPDF 的能力对照（速查）

| 能力 | AirPDF 0.8 | AirClean 0.2 | 建议 |
| --- | --- | --- | --- |
| 托盘 / 后台常驻 | ✅ | ❌ | P1 |
| 崩溃日志 | ✅ | ❌ | P0 |
| 单实例 | ❌ | ❌ | P1（清理器更需要） |
| 命令行参数 | ✅ | ❌ | P2 |
| 高对比度主题 | ✅ | ❌ | P2 |
| 无障碍名称 | ✅（约 48 处） | ❌ | P1 |
| 最近文件 / 最近清理 | ✅（阅读器：最近文件） | ❌（字符串已备） | P0 |
| 撤销 | ✅（批注 100 步，不是删除撤销） | ❌（仅打开回收站） | P0（清理器需要删除撤销） |
| 设置持久化 | ✅ | 部分（6 个字段） | P1 |
| 设置 schema 迁移 | ❌ | ❌ | 两边都没有，建议 AirClean 先做 |
| 声明式快捷键 | ❌（集中 switch） | 部分（2 个 KeyBinding） | 可做得更好 |
| 多窗口 / 标签 | ✅ | ❌ | 不适用 |
| 阅读位置 / 书签 / 批注 | ✅ | — | 不适用 |
| 打印 / OCR | ❌（路线图） | — | 不适用 |
| Microsoft Store / MSIX | ✅ | ❌ | P1 |
| 签名检查工作流 | ✅ | ❌ | P1 |
| CHANGELOG | ✅ | ❌ | P1 |
| 社区文档 / 模板 | ✅ | ❌ | P1 |
| 测试数量 | 284 | 66 | P1 |
| 三平台 AOT 门禁 | ✅ | ✅ | 保持 |

> 说明：AirPDF 也**没有**单实例、独立设置页、设置迁移、声明式快捷键和
> Store 之外的设置通道。这几项是清理器自身的需要（并发删除、扫描范围、
> 长期配置），不是"对标落后"，做的时候不必等 AirPDF 先有。

---

## 8. 不建议照搬的 AirPDF 能力

对标不是复制。以下能力属于阅读器场景，搬到清理器只会增加表面积：

- **多窗口 / 标签 / 阅读位置 / 书签 / 高亮 / 墨迹 / 打印 / OCR**：清理器是
  单任务流程，多窗口反而增加并发删除的风险。
- **自定义 Windows 标题栏 + Snap Layouts**：AirPDF 为此用了 `Win32Properties`
  和 `WM_NCMOUSEMOVE` 钩子；清理器单窗口，收益低于维护成本。
- **每文档会话状态**：清理器只需要"上次扫描 / 上次清理"两条状态。
- **PDFium 级别的原生依赖管理**：AirClean 没有对应的重依赖，保持现状即可。

值得借鉴的是它的**工程习惯**，按可迁移程度排序：

1. 崩溃日志的安装顺序（`CrashLog.Install()` 在 Velopack 与 UI 之前）与
   四个钩子（AppDomain / TaskScheduler / Dispatcher / 非零退出），512 KB 上限。
2. 签名检查工作流的"无证书也能跑"设计（自签证书 + 半配置失败）。
3. 从 CHANGELOG 生成 Release notes，tag 驱动预发布标记。
4. Headless 测试直接构造真实窗口，而不是只测 ViewModel。
5. 高对比度主题用独立 brush 字典；但要注意 Avalonia 11 不检测系统高对比度，
   要么显式给开关，要么接受它只是第三种主题。
6. 无障碍名称从本地化目录取，避免文案与读屏名称漂移。

另外两点不要照搬：AirPDF 的快捷键集中在一个 40 分支的 switch（AirClean 用
`KeyBinding` 更干净）；它的设置文件没有 schema 版本（AirClean 已经预留了
`SchemaVersion`，应该真正用起来，而不是退回去）。

---

## 9. 建议里程碑

| 版本 | 主题 | 范围 | 出口标准 |
| --- | --- | --- | --- |
| **0.3** | 信任闭环 | 撤销（三平台）+ 最近清理 + 跳过报告 + 完成页清空废纸篓 + 崩溃日志 + 单实例 + Journal v2/保留期 + §5 前 6 条缺陷 | 撤销在 Windows/macOS/Linux 真机通过；崩溃有日志；取消扫描保留结果 |
| **0.4** | 常驻与范围 | 设置页 + 快速/全盘扫描 + 排除项 + 阈值可配 + 托盘 + 定时提醒 | 默认 10 秒内出首屏结果；托盘三平台验证；设置重启后保留 |
| **0.5** | 正确性与可达性 | 硬链接/克隆 + 只读建议卡 + 键盘体系 + 无障碍 + 高对比度 + macOS TCC 引导 + Linux `.desktop` | 重复文件不误报；仅用键盘可完成全流程 |
| **1.0** | 分发与合规 | 签名/公证 + Store/MSIX + CHANGELOG/社区文档 + 测试 ≥150 + arm64 + 真机验收 | 签名检查工作流绿；Store 包可安装；macOS/Linux 验收清单全过 |

依赖关系：设置页（0.4）依赖 §6.8 的拆分；撤销（0.3）依赖 Journal v2；
托盘（0.4）建议在单实例之后做，否则"唤起已有窗口"没有落点。

0.3 的逐条拆分、依赖图、验收标准与 GitHub 标签见
[ISSUES-0.3.md](ISSUES-0.3.md)。

---

## 10. 现状速览（便于对外说明）

- 3 个源码项目：`AirClean.Core`（无 UI）、`AirClean.Platform`、`AirClean.App`。
- 2 个测试项目：50 个 Core 测试 + 16 个 Headless 测试。
- 9 个清理分类，其中 2 个需要管理员 helper（Windows Update / apt / journald）。
- 发布产物：`win-x64` 安装器与便携包、`osx-arm64` / `osx-x64` pkg 与便携包、
  `linux-x64` tar.gz。
- 已具备：MFT 快扫、提权 helper、三平台 AOT、双语双主题、自动更新。
- 未具备：撤销、最近清理、跳过报告、设置页、托盘、提醒、崩溃日志、
  签名验证、Store、CHANGELOG、社区文档。
