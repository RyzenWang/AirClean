# AirClean 0.5 Issue 列表（正确性与可达性）

> 目标版本：0.5.0。主题来自 [GAP-ANALYSIS.md](GAP-ANALYSIS.md) §9，并吸收
> [tw93/Mole](https://github.com/tw93/Mole)（GPL-3.0，仅借鉴思路、不复制代码）中
> 与「删得对」相关的工程做法。估时口径同 0.4：S ≤ 0.5 天、M ≤ 2 天、L > 2 天。

## 0. 里程碑定义

**主题**：让"可释放 X"这句话经得起验证，并让不想用鼠标的人也能走完全程。

**范围**：

- 删除目标的身份绑定：扫描时记录、删除前复验，掉包即跳过；
- 硬链接折叠：同一份数据只算一次，不再承诺释放不了的空间；
- 只读建议行：看得见、动不了、给命令，永远不进删除计划；
- `# SAFE:` 源码门禁与事故台账：把安全不变式交给 CI，而不是靠人守；
- 键盘体系与无障碍名称：Tab 顺序、焦点环、`Ctrl+,`、Space 勾选；
- 高对比度入口、macOS 完全磁盘访问引导、Linux `.desktop`。

**出口标准**：

- [ ] 同一 inode 的多个路径在重复文件结果里只出现一次，且体积只计一份；
- [ ] 扫描后替换同名同大小文件，删除会被拒绝并给出原因；
- [ ] 只读建议行不可勾选、不进入确认弹层、导出内容与界面一致；
- [ ] 裸 `File.Delete` / `Directory.Delete` / `File.Move` 无 `// SAFE:` 注记时 CI 失败；
- [ ] 仅用键盘可完成 扫描 → 勾选 → 确认 → 完成 → 撤销；
- [ ] 三平台 CI 绿；测试 ≥ 170；README / DESIGN / VERIFICATION-0.5 更新。

## 1. 进度

| Issue | 状态 | 说明 |
| --- | --- | --- |
| AC-501 | ✅ | `FileIdentity`（Windows `FILE_ID_INFO`，Unix `st_dev`+`st_ino`）+ `IFileIdentityProvider`；大文件与重复候选记录身份，`DeletionService` 删除前复验，不一致记 `ChangedSinceScan`；provider 不支持时退化为旧行为。 |
| AC-502 | ✅ | 重复文件按身份折叠硬链接；折叠数经 `CategoryScan`/`ScanResult` 上到结果页（"另有 N 个路径指向同一份数据"）；APFS 克隆与块克隆有独立文件 ID，注释明确不假装识别。 |
| AC-503 | ✅ | `ReadOnlySuggestion` + `IReadOnlySuggestionProbe` + `SuggestionScanner`（未知体积保留、已知 < 200 MB 丢弃）；Windows 探测休眠/页面文件/组件存储；结果页"值得了解"卡片，不进删除计划。 |
| AC-504 | ✅ | `SourceInvariantTests` 扫 `src/**/*.cs`，四类删除/移动调用必须同行 `// SAFE: <理由>`；23 处调用已补注记；[INCIDENTS.md](INCIDENTS.md) 记录事故与拦截测试。 |
| AC-505 | ⬜ | 键盘体系：↑↓/Space/`/`过滤/PgUp·PgDn/`Ctrl+,`，页脚图例；关键控件补 `AutomationProperties.Name`。 |
| AC-506 | ⬜ | 高对比度显式入口；macOS TCC 三态探测 + 一条深链；Linux 打包补 `.desktop`。 |
| AC-507 | ⬜ | 文档、截图、三平台验收与发版。 |

> 0.4.3 已先行修完两项"信任漏洞"，不在 0.5 重复：设置读取失败不再静默丢弃排除目录
> （改为回退 `settings.json.bak` + 暂停清理），扫描期无权限目录会把合计标成下限。

## 2. 详细 Issue

### AC-501 删除目标的身份绑定

**背景**
扫描与删除之间隔着用户的思考时间。现在只比 `Length` + `LastWriteTimeUtc`，
同名同大小同 mtime 的对象被换掉（符号链接替换、目录被挂载覆盖、别的进程重建同名缓存）
识别不出来。

**设计**
- `FileIdentity(ulong Volume, ulong Id)`；Windows 取 `FILE_ID_INFO`，Unix 取 `st_dev`+`st_ino`。
- `IFileIdentityProvider.TryGet(path, out identity)`，失败返回 false 而不是抛异常；
  平台不支持时退化为现有的 size/mtime 检查。
- 只在大文件与重复文件两处扫描时记录身份（条目有上限、本来就在 stat），缓存规则不动。
- `DeletionService` 在 size/mtime 通过后再复验一次身份；不一致记 `SkipReason.ChangedSinceScan`。

**验收**
- 身份一致 → 正常删除；身份不同 → 跳过并可导出原因；provider 不可用 → 行为不变。

**估时**：2 天 · `area:core` `area:platform`

---

### AC-502 硬链接与克隆去重

**背景**
重复文件先按大小分桶再比哈希，硬链接会被当成两份不同文件。界面承诺"可释放 X"，
删掉一份却一个字节都没释放——这是诚信问题，不是优化项。

**设计**
- 分组前按 `FileIdentity` 折叠：同一身份只保留一份参与分组与体积计算。
- 被折叠的路径数如实上报（`HardlinkDuplicatesCollapsed`），UI 用一行说明，不静默丢弃。
- APFS 克隆（同一 inode 不同块引用）无法用 inode 区分，**不做**假识别；代码注释写明限制。

**验收**
- 同一 inode 的两个路径只出现一次；可释放体积不含被折叠项；provider 不可用时行为与 0.4 一致。

**估时**：2 天 · `area:core`

---

### AC-503 只读建议行

**背景**
有些东西"看得见但不该由我们删"：Windows 组件存储、休眠文件、系统还原点、
macOS 时间机器本地快照、Docker/虚拟机镜像。现在它们要么完全不可见，要么混在可勾选列表里。

**设计**
- `ReadOnlyProbe` 返回 `{ Label, Bytes?, Fact, SuggestedAction? }`，探测失败返回 `null`，
  绝不让探测失败变成"没有"。
- 独立"建议"卡片：只有眼睛图标 + 琥珀色，无勾选框；可"复制命令"，永不进入 `DeletionPlan`。
- 降噪：已测全且 < 200 MB 不显示；部分测量或未知大小必须显示。

**验收**
- 建议行不可勾选、不进确认弹层；导出内容与界面一致；探测超时/无权限时按"未知"保留。

**估时**：1.5–2 天 · `area:core` `area:app`

---

### AC-504 `# SAFE:` 门禁与事故台账

**背景**
`DESIGN.md` S6 写了"架构测试扫描源码强制单点删除"，CI 里其实没有。安全不变式目前靠人守。

**设计**
- `tests/AirClean.Core.Tests/SourceInvariantTests.cs`：遍历 `src/**/*.cs`，
  出现 `File.Delete(` / `Directory.Delete(` / `File.Move(` / `Directory.Move(` 且同行无
  `// SAFE: <理由>` 即失败。先补齐现有调用点的注记，再开门禁。
- `docs/INCIDENTS.md`：每条事故记录"引入 commit / 修复 commit / 本该拦住的层 / 现在拦它的测试"。

**验收**
- 故意删掉一条 `// SAFE:` 注记，测试必须失败并点名文件与行号。

**估时**：0.5–1 天 · `area:core` `area:docs`

---

### AC-505 键盘体系与无障碍

**背景**
现在只有 `Ctrl+R` 与 `Esc`。DESIGN 承诺的 `Ctrl+,`、↑↓+Space、Tab 顺序都没实现。

**设计**
- 声明式 `KeyBinding`：↑↓/Home/End 移动、Space 勾选、`/` 过滤、`PgUp/PgDn` 翻页、
  `Ctrl+,` 打开设置、`Esc` 逐层退出；页脚显示当前界面的可用键。
- 关键按钮/列表补 `AutomationProperties.Name`，名称从本地化目录取，避免与读屏文案漂移。

**验收**
- 仅用键盘完成 扫描 → 勾选 → 确认 → 完成 → 撤销；焦点环在深浅主题下都可见。

**估时**：2–3 天 · `area:app`

---

### AC-506 平台补齐

- **高对比度**：显式入口（不依赖系统检测），独立 brush 字典。
- **macOS TCC**：`~/Library/Containers` 大量权限失败时，给一张卡 + 一条
  "系统设置 → 隐私与安全性 → 完全磁盘访问"的深链，而不是静默少报。
- **Linux `.desktop`**：打包时生成桌面入口，缺失时应用仍可运行。
- **产物**：`win-arm64` / `linux-arm64` 进入发布矩阵（可与 1.0 合并）。

**估时**：2–3 天 · `area:platform` `area:app`

---

### AC-507 文档与验收

- README：0.5 能力与已知限制；DESIGN：身份绑定、建议行、键位表；
- `docs/VERIFICATION-0.5.md`：三平台记录，未验证项如实写"未验证"；
- 截图：建议卡片、键盘焦点、高对比度。

**估时**：1 天 · `area:docs`

## 3. 明确不在 0.5

- 应用卸载器（会把产品从"清理"扩成"卸载"，且核心是 macOS bundle 证据体系）；
- `optimize`（DNS/QuickLook/数据库维护）、状态仪表盘、Touch ID sudo——平台专属，与定位冲突；
- 扫描结果跨次磁盘缓存（需先稳定身份/折叠语义，放到 0.6 再评估）；
- 签名证书、Store/MSIX、CHANGELOG 之外的社区模板——留到 1.0 分发与合规。
