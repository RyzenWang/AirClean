# AirClean 0.3 Issue 列表（信任闭环）

> 来源：[GAP-ANALYSIS.md](GAP-ANALYSIS.md) §9 的 0.3 里程碑。
> 状态：草案，尚未录入 GitHub。
> 用法：每条 issue 的"背景 / 目标 / 改动点 / 任务 / 验收"可直接作为 GitHub
> issue body；§2 索引用于建里程碑看板，§4–§5 用于排期与风险沟通。
> 估时口径：单人专注工作日，S ≤ 0.5 天、M ≤ 2 天、L > 2 天；不含评审与真机等待。

## 实现进度

| Issue | 状态 | 说明 |
| --- | --- | --- |
| AC-301 | ✅ | `TrashMoveResult` + `TryRestore` 契约；Windows 返回空路径、Unix 返回目标路径；`TestTrash` / `FakeTrash` 已更新。 |
| AC-302 | ✅ | `JournalStore` v2：每批一个 JSON 文件、原子写、30 天 / 200 批裁剪、v1 JSONL 只读；新增 11 个 Core 测试。 |
| AC-303 | ✅ | Linux：按 journal 路径恢复，缺失时回退 `.trashinfo` 查找；2 个 Linux 守卫测试已在 CI 通过。 |
| AC-304 | ✅ | Windows：`$I` v1/v2 解析器（Core，11 个单测）、按原路径定位 `$R`、长路径前缀、删除 `$I`；本机回收站往返测试通过。 |
| AC-300 | ✅ | 完成页抽成 `Views/CompletionView.axaml`，`MainWindow.axaml` 缩减约 85 行，现有截图流程无回归。 |
| AC-306 | ✅ | 完成页“撤销本次清理”：逐项恢复、部分失败逐条说明、扣减已移入体积、清空后入口消失；新增 Headless 流程测试。 |
| AC-307 | ✅ | 欢迎页最近 3 批（时间 / 文件数 / 体积），行内撤销；无记录显示空状态；新增 Headless 测试与截图。 |
| AC-309 | ✅ | 完成页“清空废纸篓”：独立确认层、后台清空、报告更新、journal 标记 `trashEmptied`，撤销与最近清理同步失效；新增 journal 单测与 Headless 测试。 |
| AC-310 | ✅ | `CrashLog`：AppDomain / TaskScheduler / ProcessExit / Dispatcher 四类钩子，512 KB 滚动，仅本地；新增写入与轮转测试。 |
| AC-311 | ✅ | 单实例：PID 锁文件 + 信号文件 + 轮询唤醒，跨平台一致；第二个进程退出码 0；双实例测试已过三平台 CI。 |
| AC-312 | ✅ | 取消扫描保留已完成分类：新增 `IVolumeScanService`、结果页提示、分类“未完成”状态；新增 fake 扫描器测试。 |
| AC-313 | ✅ | 重复组截断：`DuplicateScanner(maxGroups)`、`LimitedByGroupCap`、`DuplicateGroupCount`，UI 区分预算/组数上限；新增 2 个 Core 测试。 |
| AC-308 | ✅ | 跳过报告：`SkippedItem` 样本（上限 500）、按原因分组展开、CSV 导出（UTF-8 BOM）、截断提示；新增 CSV/报告测试与 Headless 导出测试。 |
| AC-314 | ✅ | 截图（撤销 / 跳过报告 / 最近清理 / 取消扫描 / 清空废纸篓）、[VERIFICATION-0.3.md](VERIFICATION-0.3.md)、README 中英文与 DESIGN 同步。 |
| AC-305 | ✅ | macOS：`NSFileManager.trashItemAtURL` 已接入（objc 桥，失败自动回退到 `~/.Trash`）；主卷恢复守卫测试已在 macOS CI 通过，外置卷与 Finder 交互未验证。 |
| AC-314 | ✅ | 截图（撤销 / 跳过报告）、[VERIFICATION-0.3.md](VERIFICATION-0.3.md)、README 中英文与 DESIGN 同步。 |

> 实现状态：**15/15 个 issue 完成**；构建零警告，测试 66 → **107**（Core 77 + App 30）。
> 三平台 CI 全绿（run 42794759063，提交 `de476cf`），Linux/macOS 的恢复守卫测试已在
> 真实 runner 上执行。剩余缺口是交互式真机项（macOS 外置卷 / Finder、Linux `xdg-open`、
> Windows 真实文件选择器 / 真实清空回收站 / 真实崩溃 / 双进程手测），见
> [VERIFICATION-0.3.md](VERIFICATION-0.3.md)。

> 状态标记：✅ 已完成并通过构建/测试；🚧 进行中；⬜ 未开始。

## 0. 里程碑定义

**主题**：清理之后的信任闭环——能撤销、能回看、能解释、能真正释放。

**范围**：

- Journal v2：每批清理的恢复信息、读取、保留期、v1 兼容；
- 三平台撤销：Windows 回收站、macOS 废纸篓、Linux freedesktop Trash；
- 完成页：撤销上一次清理、清空废纸篓、可展开/导出的跳过报告；
- 欢迎页：最近清理；
- 可靠性：崩溃日志、单实例；
- 两个已有缺陷：取消扫描保留结果、重复组截断透明；
- 文档、截图与三平台验收记录。

**出口标准（DoD）**：

- [ ] 三平台 `dotnet test` 全绿，0.3 新增测试 ≥ 20 个；
- [ ] Windows：清理 → 撤销 → 文件回到原路径，内容一致；
- [ ] Linux：同上（CI `ubuntu-22.04` 集成测试）；
- [ ] macOS：同上（CI `macos-latest` 集成测试），或 UI 明确标注不可撤销；
- [ ] 欢迎页显示最近 3 批清理，可撤销，空状态正确；
- [ ] 跳过报告可按原因展开并导出 CSV（Excel 打开无乱码）；
- [ ] 完成页可清空废纸篓，清空后撤销入口消失；
- [ ] 人为抛异常能生成 `crash.log`，应用可正常重启；
- [ ] 第二次启动聚焦已有窗口，不产生第二个进程；
- [ ] 取消扫描后保留已完成分类，并标注"未完成"；
- [ ] 重复组被截断时界面有说明；
- [ ] README / DESIGN / 截图更新，验收记录归档。

**明确不在 0.3**：

- 设置页、快速/全盘扫描、排除项、阈值可配（0.4）；
- 托盘、定时提醒（0.4）；
- 硬链接去重、只读建议卡、键盘/无障碍、高对比度（0.5）；
- 签名检查、Store/MSIX、社区文档（1.0）；CHANGELOG 的主体工作也在 1.0，
  0.3 只在 AC-314 里决定是否顺手建立；
- 删除失败保护（30% 中止 / 磁盘满回退）——排在 0.4 第一个 issue；
- `IFileOperation` 长路径删除；0.3 只记录已知限制，不重构 Windows 删除路径。

---

## 1. 依赖与并行

```
AC-301 ──> AC-302 ──┬──> AC-303 ──┐
                    ├──> AC-304 ──┼──> AC-306 ──> AC-307
                    └──> AC-305 ──┘        │
AC-300 ────────────────────────────────────┼──> AC-308
                                           └──> AC-309（复用撤销状态做互斥）

AC-310 崩溃日志        （独立）
AC-311 单实例          （独立）
AC-312 取消保留结果    （独立，建议先加 IVolumeScanService）
AC-313 重复组截断      （独立）
AC-314 文档与验收      （最后）
```

**建议排期（2 人）**：

| 时间 | 开发 A（Core/Platform） | 开发 B（App） |
| --- | --- | --- |
| 第 1–2 天 | AC-301 → AC-302 | AC-300、AC-310、AC-311、AC-313 |
| 第 3–5 天 | AC-304（最长）、AC-303 | AC-305、AC-312 |
| 第 6–8 天 | AC-306 | AC-307、AC-308、AC-309 |
| 第 9–10 天 | AC-314 + 缓冲 | AC-314 + 缓冲 |

单人串行约 16.5 个工作日；两人并行约 2 周（含缓冲）。

**建议 PR 顺序**：

1. `feat(core): trash result + journal v2`——AC-301 + AC-302（无 UI 变化）；
2. `feat(win): restore from recycle bin`——AC-304；
3. `feat(unix): restore from trash`——AC-303 + AC-305；
4. `refactor(app): extract completion view`——AC-300；
5. `feat(app): undo last cleanup`——AC-306；
6. `feat(app): recent cleanups on welcome`——AC-307；
7. `feat(app): skip report and csv export`——AC-308；
8. `feat(app): empty trash from completion`——AC-309；
9. `chore(app): crash log`——AC-310；
10. `feat(app): single instance`——AC-311；
11. `fix(app): keep partial results on cancel`——AC-312；
12. `fix(core): report duplicate truncation`——AC-313；
13. `docs: 0.3 docs, screenshots and verification`——AC-314。

---

## 2. Issue 索引

| ID | 标题 | 类型 | 优先级 | 估时 | 依赖 | 标签 |
| --- | --- | --- | --- | --- | --- | --- |
| AC-300 | 抽取 CompletionView，拆分完成页 | chore | P0 | S | — | `area:app` `type:chore` |
| AC-301 | 扩展 ITrashService：移动结果与恢复契约 | feature | P0 | S | — | `area:core` `area:platform` |
| AC-302 | Journal v2：批次文件、读取、裁剪、v1 兼容 | feature | P0 | M | AC-301 | `area:core` `data` |
| AC-303 | Linux：从 freedesktop Trash 恢复 | feature | P0 | S | AC-301, AC-302 | `area:platform` `os:linux` |
| AC-304 | Windows：从回收站恢复（`$I` 解析） | feature | P0 | L | AC-301, AC-302 | `area:platform` `os:windows` |
| AC-305 | macOS：从废纸篓恢复（NSFileManager） | feature | P0 | M | AC-301, AC-302 | `area:platform` `os:macos` |
| AC-306 | 完成页：撤销上一次清理 | feature | P0 | M | AC-300, AC-302, 任一平台恢复 | `area:app` `undo` |
| AC-307 | 欢迎页：最近清理 | feature | P0 | M | AC-300, AC-302, AC-306 | `area:app` |
| AC-308 | 跳过报告：分组、展开、导出 CSV | feature | P0 | M | AC-300 | `area:app` `area:core` |
| AC-309 | 完成页：清空废纸篓 | feature | P0 | M | AC-300, AC-306 | `area:app` |
| AC-310 | 崩溃日志（`crash.log`） | reliability | P0 | S | — | `area:app` `reliability` |
| AC-311 | 单实例：第二次启动聚焦已有窗口 | reliability | P0 | S | — | `area:app` `reliability` |
| AC-312 | 取消扫描保留已完成结果 | bug | P0 | M | — | `area:app` `bug` |
| AC-313 | 重复文件组截断要在 UI 说清楚 | bug | P1 | S | — | `area:core` `area:app` `bug` |
| AC-314 | 0.3 文档、截图与三平台验收 | docs | P0 | M | 全部 | `area:docs` |

---

## 3. 详细 Issue

### AC-300 抽取 CompletionView，拆分完成页

**背景**
完成页在 `MainWindow.axaml` 的 831–915 行，与欢迎、扫描、结果、确认弹层共处
一个 1093 行文件；`MainWindowViewModel` 1134 行。0.3 要在完成页同时加撤销、
清空废纸篓、可展开的跳过报告，继续堆会不可维护。

**目标**
把完成页抽成独立的 `CompletionView`（`UserControl`），行为与样式不变，作为
AC-306 / AC-308 / AC-309 的落点。

**改动点**
- 新增 `src/AirClean.App/Views/CompletionView.axaml` 与 `.axaml.cs`；
- `MainWindow.axaml` 用 `<views:CompletionView />` 替换原区块；
- 绑定保持 `x:DataType="vm:MainWindowViewModel"`，不改 VM 结构。

**任务**
- [ ] 迁移 Markup：完成图标、标题、副标题、按钮、明细卡片、提示条；
- [ ] 迁移样式类，确认浅色/深色截图无差异；
- [ ] 保留 `IsVisible="{Binding IsCompletedVisible}"` 的显示逻辑。

**验收**
- [ ] `WelcomeScreenTests.Scan_then_confirm_then_clean_produces_results_and_completion` 通过；
- [ ] 完成页布局保持原样（撤销按钮由 AC-306 新增）；
- [ ] 完成页区块移出，`MainWindow.axaml` 净减约 50 行（含欢迎页新增的最近清理）。

**测试**
现有 Headless 完成流程测试 + 截图对比。

**风险**
`x:Name` 引用的元素（如 `RingArc`）不能一起搬走，只搬完成页内容。

**估时**：0.5 天 · `area:app` `type:chore`

---

### AC-301 扩展 ITrashService：移动结果与恢复契约

**背景**
`ITrashService.MoveToTrash(string path, out string? error)` 只返回成功/失败，
不知道文件被放到废纸篓的哪个路径；接口也没有恢复能力。撤销、最近清理都依赖
这两个信息。

**目标**
在不改变清理行为的前提下，让"移到废纸篓"返回回收站内的路径，并定义恢复契约。

**设计**
```csharp
public readonly record struct TrashMoveResult(bool Success, string? TrashPath, string? Error);

public interface ITrashService
{
    bool IsSupported { get; }
    TrashSample? Probe(string? rootPath = null);
    TrashMoveResult MoveToTrash(string path);
    bool TryRestore(string? trashPath, string originalPath, out string? error);
    long Empty(string? rootPath = null);
    void OpenTrash();
}
```
- `trashPath` 为 `null` 表示平台暂时拿不到（Windows v1 预期如此）；
- `TryRestore` 在 `trashPath` 为 null 时按 `originalPath` 在回收站里查找；
- 失败返回英文原因串，调用方负责映射本地化文案；

**改动点**
- `src/AirClean.Core/Trash/ITrashService.cs`；
- `src/AirClean.Platform/Windows/WindowsTrashService.cs`：先返回 `TrashPath = null`；
- `src/AirClean.Platform/Unix/UnixTrashService.cs`：返回实际目标路径；
- `src/AirClean.Core/Deleting/DeletionService.cs`：改用 `TrashMoveResult`（先忽略路径）；
- `tests/AirClean.Core.Tests/TestTrash.cs`、`tests/AirClean.App.Tests/WelcomeScreenTests.cs` 的 `FakeTrash`。

**任务**
- [ ] 改接口与三处实现，编译通过；
- [ ] Linux/macOS 返回真实目标路径，Windows 返回 null 并注释原因；
- [ ] `TryRestore` 先在各平台返回 `false, "not implemented"`，由 AC-303/304/305 填充；
- [ ] 更新测试替身，记录 `Restored` 列表供 UI 测试断言。

**验收**
- [ ] 清理流程行为不变，现有删除测试通过；
- [ ] Linux/macOS 移动后 `TrashPath` 非空且文件存在；
- [ ] 接口 XML 注释写清 `trashPath == null` 的语义。

**测试**
Core 删除测试、各平台移动测试（Linux CI 可跑，macOS/Windows 由对应 CI 跑）。

**估时**：0.5 天 · `area:core` `area:platform`

---

### AC-302 Journal v2：批次文件、读取、裁剪、v1 兼容

**背景**
`JournalPayload` v1 只有 `{p,s}`，写在 `journal/yyyy-MM.jsonl`，
`JournalWriter` 用 `File.AppendAllText`，没有读取端、没有裁剪、没有恢复状态
（`JournalJsonContext.cs`、`DeletionService.cs:250-279`）。撤销和最近清理都
没有数据基础。

**目标**
每次清理落一条可回放的批次记录，能读最近 N 批、能标记已恢复、能按策略裁剪，
并兼容已有 v1 文件。

**设计决策**
- 存储改为**每批一个文件**：`<journal>/<yyyy-MM>/<utcTicks>-<batchId:N>.json`；
  原子写：写 `.tmp` 后 `File.Move(..., overwrite: true)`。
  理由：撤销要更新批次状态，JSONL 就地改行不可靠；单文件便于裁剪与损坏隔离。
- `JournalPayload` 升级到 `V=2`：
```json
{
  "v": 2,
  "batchId": "…",
  "at": "2026-11-10T05:35:00Z",
  "mode": "trash",
  "canceled": false,
  "deleted": 64210,
  "trashedBytes": 12400000000,
  "freedBytes": 0,
  "skipped": { "inUse": 812, "permission": 214 },
  "items": [
    { "p": "C:\\…\\a.bin", "s": 1048576, "r": "C:\\$Recycle.Bin\\S-1-5-21-…\\$R…", "restored": false }
  ]
}
```
- 读取：`ReadRecent(int max = 5)` 按 `at` 倒序；`Find(Guid batchId)`；
- 恢复：`MarkRestored(Guid batchId, IReadOnlyCollection<string> restoredPaths)`
  重写该批次文件；
- 裁剪：保留最新 200 批、且不早于 30 天；启动时执行一次，追加后执行一次；
- v1 兼容：读取旧 `yyyy-MM.jsonl` 的每一行，映射为 `Legacy = true` 的批次
  （`TrashPath = null`、`CanUndo = false`）；裁剪不删除旧文件；
- 损坏行/文件跳过，不抛异常。

**改动点**
- 新增 `src/AirClean.Core/Deleting/JournalStore.cs`、`JournalBatch.cs`；
- 重写 `JournalJsonContext.cs` 的 payload 与 DTO，保留 legacy DTO；
- `DeletionService` 构造函数改为接收 `JournalStore`（或目录并内部构造），
  移动时写入 `TrashPath`；
- `tests/AirClean.Core.Tests/DeletionServiceTests.cs` 适配；
- 新增 `JournalStoreTests.cs`。

**任务**
- [ ] 模型与序列化（含 v1 DTO）；
- [ ] 原子写、读取、`MarkRestored`、`Prune`；
- [ ] 异常与并发：读取时忽略被占用的文件；
- [ ] README 的 journal 说明更新（路径与隐私）。

**验收**
- [ ] 新批次写入后 `ReadRecent` 能读回，字段完整；
- [ ] v1 文件能读，`CanUndo == false`；
- [ ] 201 批后最老批次被删；31 天前的批次被删；
- [ ] 恢复标记后重新读取，`Restored` 为 true；
- [ ] 手动损坏一个批次文件，读取不抛异常。

**测试**
新增 `JournalStoreTests` 约 8 个用例；`DeletionServiceTests` 补批次内容断言。

**风险**
并发写：0.3 且 AC-311 单实例会缓解；文件锁竞争仍要 try/catch。

**估时**：1.5 天 · `area:core` `data`

---

### AC-303 Linux：从 freedesktop Trash 恢复

**背景**
`UnixTrashService` 已把文件移到 `$XDG_DATA_HOME/Trash/files/` 并写
`info/*.trashinfo`（含 `Path=` 原始路径），但没有恢复方法。

**目标**
`TryRestore` 在 Linux 上把文件移回原路径，处理冲突与元数据。

**设计**
- 优先用 journal 的 `trashPath`；为空时按 `originalPath` 在 `Trash/files` 中查找
  同名文件并读 `.trashinfo` 校验；
- 恢复前检查：原路径不存在、父目录存在、同名文件不覆盖；
- `File.Move` 失败（跨设备）时返回错误，不做复制删除；
- 成功后删除对应的 `.trashinfo`。

**改动点**
- `src/AirClean.Platform/Unix/UnixTrashService.cs`；
- 测试通过 `PlatformServices.CreateTrashService()` 拿 `ITrashService` 走行为测试，
  不直接测 `internal` 解析方法（Platform 目前没有 `InternalsVisibleTo`，也不必加）。

**任务**
- [ ] `MoveToTrash` 返回目标路径；
- [ ] `TryRestore` 实现与错误原因；
- [ ] 单测：正常恢复、目标已存在、原目录不存在、元数据缺失；
- [ ] CI Linux 集成测试：临时文件 → 移入 → 恢复 → 内容一致。

**验收**
- [ ] 恢复后原路径存在且 SHA-256 与删除前一致；
- [ ] 目标存在时返回 false，不覆盖；
- [ ] `.trashinfo` 被清理。

**估时**：0.5 天 · `area:platform` `os:linux`

---

### AC-304 Windows：从回收站恢复（`$I` 解析）

**背景**
`WindowsTrashService` 用 `SHFileOperation` + `FOF_ALLOWUNDO`，不返回回收站内
路径；`TryRestore` 没有实现。DESIGN 4.6 早就计划了解析 `$Recycle.Bin\<SID>\$I*`。

**目标**
按原始路径在回收站里定位条目并恢复，长路径与权限失败有明确降级。

**设计**
- `$I` 文件格式：
  - 8 字节 header（版本 1 或 2）；
  - 8 字节原始文件大小；
  - 8 字节删除时间（FILETIME）；
  - v1：4 字节长度（260）+ 固定 520 字节 UTF-16 路径；
  - v2：4 字节长度 + 变长 UTF-16 路径（含结尾 NUL）。
- `$R` 文件与 `$I` 同目录、同后缀；
- 枚举 `C:\$Recycle.Bin\<SID>`（其他盘符同理），解析 `$I`，匹配原始路径；
  多个匹配取删除时间最新；匹配后检查 `$R` 是文件还是目录；
- 恢复：确保原目录存在 → `File.Move`/`Directory.Move` 加 `\\?\` 前缀 →
  删除 `$I`；
- 权限不足或找不到时返回原因，UI 降级为"打开回收站"。

**改动点**
- 新增 `src/AirClean.Platform/Windows/RecycleBin.cs`（定位 + 恢复）；
- `WindowsTrashService.TryRestore` 调用它；
- 当前只有 Core/App 测试项目，**决策**：把 `$I` 纯字节解析放在
  `AirClean.Core/Platform/Windows/RecycleBinEntry.cs`（跨平台可测），Platform
  层只做文件系统与 SID 解析；单测放 `AirClean.Core.Tests`，不需要新测试项目。

**任务**
- [ ] 纯字节解析器 + 单测（v1/v2、长路径、截断、非法长度）；
- [ ] SID 与盘符枚举；
- [ ] 恢复实现与错误码；
- [ ] Windows CI 集成测试：临时文件 → 回收 → 恢复 → 内容一致；
- [ ] 记录已知限制（`SHFileOperation` 的 MAX_PATH）。

**验收**
- [ ] 单测覆盖 v1/v2 与损坏输入；
- [ ] Windows CI 上恢复成功且 `$I` 被删除；
- [ ] 目标已存在时不覆盖并给出原因；
- [ ] 多盘符时只查对应卷。

**风险**
- `$I` 格式在不同 Windows 版本有差异 → 以 v2 为主、v1 兼容、损坏即跳过；
- `$Recycle.Bin` 权限 → 捕获 `UnauthorizedAccessException` 并降级；
- 同路径多次删除 → 取最新删除时间，文档注明。

**估时**：2.5 天 · `area:platform` `os:windows`

---

### AC-305 macOS：从废纸篓恢复（NSFileManager）

**背景**
`UnixTrashService` 在 macOS 上直接 `File.Move` 到 `~/.Trash`，没有走
`NSFileManager.trashItemAtURL`，拿不到 `resultingItemURL`；外置卷的
`.Trashes/<uid>` 也没有处理。

**目标**
用 `NSFileManager` 移入废纸篓并记录返回 URL；按原路径恢复。

**设计**
- 通过 `libobjc` 的 `objc_getClass` / `sel_registerName` / `objc_msgSend`
  调用 `[NSFileManager defaultManager]` 的
  `trashItemAtURL:resultingItemURL:error:`；
- `NSURL.fileURLWithPath:` 创建源 URL；结果 URL 转回路径作为 `TrashPath`；
- 恢复：`File.Move(trashPath, originalPath)`，目标存在则失败；
- 外置卷由系统处理，不再手写 `~/.Trash`；
- P/Invoke 代码集中在 `src/AirClean.Platform/Unix/MacTrash.cs`，加
  `[SupportedOSPlatform("macos")]`；
- 若 2 天内无法稳定（AOT/封送问题），降级方案：保留 `File.Move`，
  `TryRestore` 返回 `false`，UI 明确"macOS 暂不支持撤销"，并在 0.4 补。

**改动点**
- `src/AirClean.Platform/Unix/UnixTrashService.cs`（macOS 分支）；
- 新增 `MacTrash.cs`；
- `Probe` / `Empty` 改用真实废纸篓路径（`~/.Trash` 仍是主卷）。

**任务**
- [ ] objc 互操作：类、选择器、URL、错误指针；
- [ ] 移入返回 URL，恢复实现；
- [ ] macOS CI 集成测试：临时文件 → 移入 → 恢复 → 内容一致；
- [ ] 失败降级路径与文案。

**验收**
- [ ] macOS CI 上恢复成功；
- [ ] Finder 废纸篓里能看到并清空；
- [ ] 目标存在时不覆盖；
- [ ] NativeAOT 发布后同样可用（CI AOT 已覆盖编译，需手测一次）。

**风险**
objc 封送在 NativeAOT 下容易出问题；时间盒 2 天，超时走降级方案。

**估时**：1.5 天 · `area:platform` `os:macos`

---

### AC-306 完成页：撤销上一次清理

**背景**
完成页只有"完成 / 打开回收站"，`_lastReport` 只用于显示体积；journal 没有
恢复数据（见 AC-302）。DESIGN 4.6 明确要求"不能给一个点了没反应的撤销按钮"。

**目标**
清理完成后，如果批次里还有可恢复项且废纸篓没被清空，提供"撤销上一次清理"，
逐项恢复并如实报告部分成功。

**设计**
- VM 保存 `_lastBatch`（`report.BatchId` → `JournalStore.Find`）；
- `CanUndo`：`RestorableCount > 0`（有条目可恢复）、尚未全部恢复、且
  `!TrashEmptied`；
- `UndoLastCleanCommand`：
  - 反序遍历 `Items`，跳过已恢复项；
  - `trash.TryRestore(item.TrashPath, item.Path, out error)`；
  - 汇总 `restored / failed`，`JournalStore.MarkRestored`；
  - 完成后刷新按钮与摘要，失败项给出原因（目标已存在 / 原目录不存在 / 已被清空）；
- 撤销执行后按恢复成功的字节数扣减 `_lastReport.TrashedBytes`（`with` 生成新
  report），完成页的"已移到废纸篓"与提示条同步减少，不能把已恢复的文件继续
  算作已清理；
- 不做二次确认（撤销是安全方向），但运行中禁用按钮；
- 文案：`L.UndoLastClean`、`L.Undoing`、`L.UndoResultFormat`、
  `L.UndoPartialFormat`、`L.UndoUnavailableTrashEmptied`、`L.UndoUnavailableNoRecord`。

**改动点**
- `src/AirClean.App/ViewModels/MainWindowViewModel.cs`；
- `src/AirClean.App/Views/CompletionView.axaml`（AC-300 产出）；
- `src/AirClean.App/Services/LocalizationService.cs`；
- `tests/AirClean.App.Tests` 新增撤销流程测试。

**任务**
- [ ] 批次加载与按钮状态；
- [ ] 撤销执行、进度与结果摘要；
- [ ] 部分失败与"废纸篓已清空"分支；
- [ ] 语言切换后文案即时更新；
- [ ] Headless 测试：全部成功、部分失败、已清空、无记录。

**验收**
- [ ] 撤销后文件回到原路径，内容一致；
- [ ] 撤销后"已移到废纸篓"的体积按恢复量减少，标题不虚报；
- [ ] 部分失败时显示成功数与失败原因；
- [ ] 清空废纸篓后按钮消失；
- [ ] 批次文件仍在时，重新读取（模拟重启）后仍能撤销。

**估时**：1.5 天 · `area:app` `undo`

---

### AC-307 欢迎页：最近清理

**背景**
`L.RecentCleanups` / `L.NoRecentCleanups` 已存在但没有任何 UI 使用
（`LocalizationService.cs:54-55`）；journal 也没有读取入口。欢迎页左侧目前是
主按钮、卷选择与底部 meta，中间没有"历史"位置。

**目标**
欢迎页显示最近 3 批清理（时间、体积、文件数、方式），可直接撤销，空状态友好。

**设计**
- `RecentCleanupViewModel`：`WhenText`、`SizeText`、`CountText`、
  `ModeText`、`CanUndo`、`UndoCommand`；
- VM 启动时 `_journal.ReadRecent(3)`；`HasRecentCleanups`；
- 位置：欢迎页左侧、扫描目标（卷选择）与底部 meta 之间；不插到主按钮与卷选择
  之间，避免把 hero 构图挤散；
- 行内"撤销"文字按钮，调用 AC-306 的撤销逻辑（抽成共享方法）；
- 撤销后刷新列表；"最近清理"不提供删除单条记录的入口。

**改动点**
- `src/AirClean.App/ViewModels/RecentCleanupViewModel.cs`（新）；
- `MainWindowViewModel.cs`、`MainWindow.axaml`（欢迎区块）；
- `LocalizationService.cs`（如 `L.RecentCleanupFormat`）；
- Headless 测试：fixture journal 两条记录 + 空目录。

**任务**
- [ ] 读取与排序；
- [ ] UI 列表与空状态；
- [ ] 行内撤销与刷新；
- [ ] 文案与语言切换；
- [ ] 截图（浅色/深色、中/英）。

**验收**
- [ ] 有 4 批记录时只显示最近 3 条，时间/体积正确；
- [ ] 无记录显示"No cleanups yet"；
- [ ] 撤销后该行状态更新，文件回到原路径；
- [ ] 布局在 920×620 下不溢出。

**估时**：1.5 天 · `area:app`

---

### AC-308 跳过报告：分组、展开、导出 CSV

**背景**
完成页只有三类计数（`RefreshCompletionTexts`），`DeletionReport` 只有
`Skipped` 字典，没有条目；DESIGN 2.2 的"跳过报告"页未实现。

**目标**
把跳过原因变成可解释、可导出、可复现的列表。

**设计**
- Core：新增 `SkippedItem(string Path, SkipReason Reason)`；
  `DeletionReport.SkippedItems`（最多 500 条）与 `SkippedTruncated`；
  `DeletionService` 在跳过时收集；
- App：`SkipGroupViewModel`（原因、计数、条目、是否展开），按原因排序；
- 完成页卡片可展开；每条显示路径（过长省略，tooltip 全路径）；
- 导出：`IReportExporter` 接口（`Task<bool> SaveAsync(string suggestedName, string content)`），
  App 层用 `TopLevel.StorageProvider.SaveFilePickerAsync` 实现，测试注入 fake；
- CSV：UTF-8 BOM、CRLF、列 `path,reason,at`；转义引号/逗号/换行；文件名
  `airclean-skipped-20261110.csv`；
- 另给"复制到剪贴板"按钮（无文件系统依赖，便于测试）。

**改动点**
- `Core/Models/ScanResult.cs`、`Core/Deleting/DeletionService.cs`；
- `App/ViewModels/SkipGroupViewModel.cs`、`CompletionView.axaml`、`MainWindowViewModel.cs`；
- `App/Services/ReportExporter.cs`（新）；
- Core 测试 + App 测试。

**任务**
- [ ] 收集与截断；
- [ ] 分组与展开 UI；
- [ ] CSV 生成与导出；
- [ ] 复制到剪贴板；
- [ ] 语言切换；
- [ ] 测试：500 条截断、CSV 转义（逗号/引号/中文/换行）、导出取消。

**验收**
- [ ] 每个原因显示计数与前 N 条路径；
- [ ] 超过 500 条时提示"仅显示前 500 条"；
- [ ] 导出的 CSV 在 Excel 中中文不乱码；
- [ ] 取消保存对话框不报错、不写文件。

**估时**：2 天 · `area:app` `area:core`

---

### AC-309 完成页：清空废纸篓

**背景**
默认清理方式是把文件移到废纸篓，完成页提示"还有 X 在废纸篓里，清空后才会释放"，
但没有就地清空的入口；用户只能回欢迎页重扫、勾选废纸篓分类再清理。

**目标**
在完成页直接清空**该卷**的废纸篓，明确说明影响范围，并让撤销入口失效。

**设计**
- 按钮可见：`TrashedBytes > 0 && trash.IsSupported`；
- 点击弹出小确认层（独立于清理确认层）：
  "将永久删除该磁盘废纸篓中的全部内容（包括其他应用和之前清理的文件），
  无法恢复"；
- 执行 `trash.Empty(volumeRoot)` 于后台线程，返回释放字节；
- 更新完成页：`FreedBytes` 增加、`TrashedBytes` 归零、提示条切换；
- `TrashEmptied = true` → `CanUndo = false`、`CanOpenTrash = false`；
- 失败时保留原状态并显示原因。

**改动点**
- `MainWindowViewModel.cs`、`CompletionView.axaml`；
- `LocalizationService.cs` 新增 4–5 个键；
- Headless 测试：确认、取消、失败。

**任务**
- [ ] 按钮与确认层；
- [ ] 后台清空与进度态；
- [ ] 状态更新与撤销失效；
- [ ] 测试：`FakeTrash.Empty` 返回字节、取消不调用、失败提示。

**验收**
- [ ] 清空后完成页体积与提示同步变化；
- [ ] 清空后撤销按钮消失；
- [ ] 确认文案明确"包括其他应用与之前清理的文件"；
- [ ] 清空失败不丢失页面状态。

**估时**：1 天 · `area:app`

---

### AC-310 崩溃日志（`crash.log`）

**背景**
`Program.Main` 只做助手分流与 `VelopackApp.Run`，没有任何异常处理；
WinExe 崩溃后进程直接消失，用户反馈无从排查（AirPDF 有 `CrashLog`）。

**目标**
把未处理异常写到本地有界日志，不联网、不上传。

**设计**
- `src/AirClean.App/CrashLog.cs`：
  - `Install()`：`AppDomain.UnhandledException`、
    `TaskScheduler.UnobservedTaskException`、非零 `ProcessExit`；
  - `Write(source, exception)`：UTC 时间、版本、OS、来源、`ToString()`；
  - 路径：`SettingsService.GetDefaultDirectory()/crash.log`；
  - 上限 512 KB，超出时把旧文件改名为 `crash.1.log` 再新建；
  - 全部 try/catch，写失败不能影响应用。
- `Program.Main` 在 `VelopackApp.Build().Run()` 之前 `Install()`；
- `App.OnFrameworkInitializationCompleted` 挂 `Dispatcher.UIThread.UnhandledException`；
- README 隐私一节说明：仅本地、不包含文件内容、不上传。

**任务**
- [ ] 日志写入与滚动；
- [ ] 三个钩子 + 调度器钩子；
- [ ] 版本与 OS 信息；
- [ ] 单测：滚动、内容格式、目录不可写不抛；
- [ ] 手动验证：启动后人为抛异常。

**验收**
- [ ] 崩溃后配置目录下的 `crash.log` 存在且含堆栈（Windows
      `%APPDATA%\AirClean`，macOS/Linux 为各自配置目录）；
- [ ] 文件不超过 512 KB；
- [ ] 正常退出不写错误行；
- [ ] NativeAOT 下可用（CI AOT 编译 + 手测一次）。

**估时**：0.5 天 · `area:app` `reliability`

---

### AC-311 单实例：第二次启动聚焦已有窗口

**背景**
`Program.Main` 没有互斥体；两个实例可同时扫描并清理同一目录，journal 也会
并发写。对清理器来说这是安全问题。

**目标**
同一用户只允许一个实例（跨会话亦然）；第二次启动把已有窗口带到前台后退出。

**设计**
- `src/AirClean.App/SingleInstance.cs`：
  - `Mutex`：`Global\AirClean.SingleInstance.<SID>`，同一用户跨会话也只允许一个；
    创建失败（权限）时退回 `Local\AirClean.SingleInstance` 并记一条日志；
  - `EventWaitHandle`：同名 `Global\AirClean.Activate.<SID>`，第二次启动 `Set()`
    后退出 0；
  - 首实例后台线程 `WaitOne`，收到信号后
    `Dispatcher.UIThread.Post(Activate)`；
  - `Activate`：`Show()`、`WindowState = Normal`（若最小化）、`Activate()`；
- `Program.Main`：`--admin-helper` 分流之后、`StartWithClassicDesktopLifetime`
  之前获取；拿不到直接 `return`；
- 释放：`desktop.Exit` 时 Dispose；
- 文档：单用户单实例的边界、托盘（0.4）后的行为差异。

**任务**
- [ ] Mutex + EventWaitHandle；
- [ ] 激活回调；
- [ ] 退出与释放；
- [ ] 手动测试：双开、最小化后双开、管理员助手模式不受影响；
- [ ] 可选：Windows CI 进程级测试。

**验收**
- [ ] 第二次启动进程退出码 0，首实例窗口到前台；
- [ ] 最小化/后台时也能恢复；
- [ ] `--admin-helper` 不抢单实例锁；
- [ ] 关闭首实例后可再次正常启动。

**估时**：0.5 天 · `area:app` `reliability`

---

### AC-312 取消扫描保留已完成结果

**背景**
`RunScanAsync` 捕获 `OperationCanceledException` 后直接回欢迎页
（`MainWindowViewModel.cs:494-497`），但扫描更新是流式的，已完成的分类其实
已经在 `Categories` 里。DESIGN 2.3 要求"取消后已发现结果保留可查看"。

**目标**
取消后如果已有结果，进入结果页并明确标注"扫描已取消，以下为已完成分类"。

**设计**
- 先加 `IVolumeScanService` 接口（`ScanAsync` 签名不变），`VolumeScanService`
  实现它，VM 依赖接口——便于用 fake 测取消路径；
- 取消分支：`Categories.Any(c => c.IsApplicable)` → `Stage = Results`、
  `WasScanCanceled = true`；否则欢迎页（跑完但 0 个文件也算"有结果"，显示
  "没有发现可清理的内容"比假装没扫过更诚实）；
- 未完成的分类：`IsScanning = false`、`IsCanceled = true`，行显示"未完成"；
- 结果页顶部提示条 + "重新扫描"按钮；状态栏 `L.ScanCanceled`；
- `ScanElapsed` 保留实际耗时。

**改动点**
- `Core/Scanning/IVolumeScanService.cs`（新）；
- `VolumeScanService` 实现接口；
- `MainWindowViewModel.cs`、`MainWindow.axaml`；
- `LocalizationService.cs`；
- 测试：fake 扫描服务先发一个分类更新再取消。

**任务**
- [ ] 接口与 fake；
- [ ] 取消分支与状态；
- [ ] 结果页提示条；
- [ ] 测试：有结果取消、无结果取消、取消后重扫。

**验收**
- [ ] 取消后停在结果页，已完成分类可见且可勾选；
- [ ] 未完成分类显示"未完成"，不显示"等待中"；
- [ ] 无任何结果时仍回欢迎页；
- [ ] 重新扫描从干净状态开始。

**估时**：1 天 · `area:app` `bug`

---

### AC-313 重复文件组截断要在 UI 说清楚

**背景**
`DuplicateScanner` 只保留前 200 组（`DuplicateScanner.cs:240-243`），
`Limited` 只反映哈希预算；`MatchedCount/MatchedBytes` 也只按截断后的组计算。
用户看到的"可释放"可能低于实际，且没有任何提示。

**目标**
截断时 `Limited` 为真，UI 明确说明"只显示前 200 组"。

**设计**
- `DuplicateScanner(int maxGroups = MaxGroups)` 便于测试；
- 统计截断前的组数、可释放体积；`CategoryScan.MatchedCount/MatchedBytes`
  用全量；
- `ScanResult` 增加 `LimitedByGroupCap` 与 `DuplicateGroupCount`（截断前的总组数，
  用于"另有 N 组"）；`Limited` 同步为真；
- `VolumeScanService` 聚合；VM 选择文案：
  - 预算：`L.LimitedNote`（已有）；
  - 组数：`L.LimitedGroupsNoteFormat`（新增，参数为 `DuplicateGroupCount - 200`）。

**任务**
- [ ] 构造参数与统计；
- [ ] `Limited` / `LimitedByGroupCap` 传递；
- [ ] 文案与 UI 条件；
- [ ] 测试：3 组、上限 2 → 只出 2 组、`LimitedByGroupCap` 为真、`MatchedCount` 为全量。

**验收**
- [ ] 截断时结果页出现"另有 N 组未列出"（N = `DuplicateGroupCount - 200`）；
- [ ] 未截断时不出现该提示；
- [ ] 现有重复文件测试通过。

**估时**：0.5 天 · `area:core` `area:app` `bug`

---

### AC-314 0.3 文档、截图与三平台验收

**背景**
0.3 新增撤销、最近清理、跳过报告、清空废纸篓、崩溃日志、单实例，README 与
DESIGN 的"还没做 / 路线图"已经过时；Headless 截图需要重新生成。

**目标**
对外文档与代码一致，三平台验收有记录。

**任务**
- [ ] README：功能、限制（未实现撤销的平台）、隐私（crash.log、journal 路径）；
- [ ] README.en.md 同步；
- [ ] DESIGN：4.6 撤销状态、2.2 界面清单勾选、5.2 里程碑；
- [ ] 重新生成 `docs/screens/*`：完成页（撤销 + 报告展开）、欢迎页（最近清理）；
- [ ] 新增 `docs/VERIFICATION-0.3.md`，记录三平台结果与已知问题；
- [ ] 可选（不阻塞 0.3）：建立 `CHANGELOG.md`，补 0.1.0 / 0.2.0 / 0.3.0。

**验收**
- [ ] 文档中不再出现"只能打开回收站"等过时描述；
- [ ] 截图为浅色/深色、中/英四个组合中至少各一张；
- [ ] 验收表每一行有结论（通过 / 已知限制 / 不适用）。

**估时**：1 天 · `area:docs`

---

## 4. 测试矩阵

| 层级 | 内容 | 位置 |
| --- | --- | --- |
| 单元 | `$I` 解析、Journal 读写/裁剪、CSV 转义、重复组截断、崩溃日志滚动 | `AirClean.Core.Tests` |
| 平台集成 | Windows 回收站、macOS 废纸篓、Linux Trash 的移入/恢复 | 现有 Core.Tests + `[SupportedOSPlatform]` 守卫，三平台 CI 自动跑 |
| Headless UI | 撤销成功/部分失败/已清空、最近清理、跳过报告导出、取消保留结果、单实例激活（模拟） | `AirClean.App.Tests` |
| 手动 | 双开、崩溃、托盘（0.4 前不涉及）、真实磁盘清理与撤销 | `docs/VERIFICATION-0.3.md` |

**测试替身**：`TestTrash` / `FakeTrash` 增加 `Restored` 列表、
`FailRestore`、`TrashPathFactory`；`JournalStore` 测试用临时目录。

---

## 5. 风险登记

| 风险 | 影响 | 缓解 |
| --- | --- | --- |
| `$I` 格式差异导致 Windows 撤销不可用 | 主平台功能缺失 | 先单测解析器；匹配失败降级为"打开回收站"并说明 |
| objc 互操作在 NativeAOT 下不稳 | macOS 无法按时 | 时间盒 2 天；降级为"macOS 暂不支持撤销"，0.4 补 |
| 清空废纸篓会删除更早批次 | 用户数据丢失 | 确认文案明确"包括之前清理的文件"；清空后撤销失效 |
| Journal 重写与并发 | 记录损坏 | 原子写、单实例、读取容错 |
| CSV 导出依赖 StorageProvider | Headless 测试难 | 抽 `IReportExporter` 接口，窗口层实现 |
| 撤销大批次耗时 | UI 卡顿 | 后台线程 + 进度 + 可取消（0.3 至少显示进度） |
| 取消扫描后部分分类状态不一致 | 误显示"等待中" | 明确 `IsCanceled` 状态并在 `Finish` 之外单独处理 |

---

## 6. 录入 GitHub 的建议

**标签**（不存在时先建）：

```
area:app, area:core, area:platform, area:docs
type:feature, type:bug, type:chore, reliability
priority:p0, priority:p1
size:S, size:M, size:L
os:windows, os:macos, os:linux
undo, data
```

**里程碑**：`0.3`。

**示例**：

```bash
# 需先把该 issue 拆成 docs/issues/AC-302.md
gh issue create \
  --title "AC-302 Journal v2：批次文件、读取、裁剪、v1 兼容" \
  --label "area:core,type:feature,priority:p0,size:M,data" \
  --milestone "0.3" \
  --body-file docs/issues/AC-302.md
```

若需要，我可以再把每个 AC 拆成单独的 `docs/issues/AC-XXX.md`，用于
`--body-file` 批量创建。

---

## 7. 0.3 之后（0.4 候选，先记录不展开）

1. 删除失败保护：30% 自动中止、磁盘满回退、删除后复查。
2. 设置页：扫描范围、排除项、阈值、删除方式、更新通道、日志开关。
3. 快速扫描 vs 全盘扫描。
4. 托盘、退出到托盘、定时提醒与登录自启。
5. `IFileOperation` 长路径删除。
6. 硬链接 / APFS 克隆去重（0.5）。
