# AirClean 0.5 验收记录

> 对应 [ISSUES-0.5.md](ISSUES-0.5.md)。只记录实际执行过的验证；未执行项写"未验证"。

## 环境

| 项目 | 值 |
| --- | --- |
| 版本 | 0.5.0（工作区） |
| Windows | Windows 10 22H2，x64，.NET SDK 10.0.403 |
| macOS / Linux | 本地未执行；CI 覆盖构建与 Headless 测试 |

## 自动化

| 项目 | 结果 | 说明 |
| --- | --- | --- |
| `dotnet build AirClean.sln -c Release` | 通过 | 0 警告 / 0 错误 |
| `dotnet test AirClean.sln -c Release` | 通过 | Core 117 + App 49 = **166**（0.4.3 为 136） |
| NativeAOT `win-x64` 发布 | 通过 | 42.4 MB / 4 文件，**0 个 IL 警告**（工作流上限 90 MB） |
| 设置 fail-closed | 通过（单测） | 损坏上报、备份恢复、坏文件不覆盖好备份 |
| 部分扫描计量 | 通过（Headless） | `SkippedPermission>0` → 合计带 `+`、结果页/完成页提示 |
| 删除身份复验 | 通过（单测 + 真机） | 身份不同 → `ChangedSinceScan`；provider 不支持 → 退化为 size/mtime |
| 生产身份 provider | 通过（真机） | `FileIdentityProviders.Create()` 对真实文件两次读到同一身份、独立副本身份不同、目录可读、缺失路径返回 false |
| 硬链接折叠 | 通过（真机硬链接） | 同一 inode 的两个路径折叠为 1 项，`collapsed=1`，可释放体积不重复计算 |
| 无 provider 时行为不变 | 通过（单测） | 三条路径仍按 3 份重复计算，`collapsed=0` |
| 只读建议降噪 | 通过（单测） | 已知 250 MB 保留、已知 100 MB 丢弃、未知保留、探测抛异常不影响其它行、排序 |
| 建议行不进删除计划 | 通过（Headless） | 建议卡片可见，`CanClean` 仍只由分类勾选决定，确认标题不含建议体积 |
| `// SAFE:` 门禁 | 通过（单测 + 反向验证） | 临时删除一条注记后门禁失败并报出 `AirClean.Core/Services/SettingsService.cs:164`，恢复后通过 |

## 手动验收清单

| # | 项目 | 结果 | 备注 |
| --- | --- | --- | --- |
| 1 | 真实系统盘全盘扫描后出现建议卡 | 未验证 | 需要桌面会话；测试用假 probe 覆盖卡片渲染 |
| 2 | 复制命令写入系统剪贴板 | 未验证 | Headless 只验证注入的写入器，未读真实剪贴板 |
| 3 | 扫描→替换文件→删除被拒绝 | 未验证（真机） | 单测用假 provider 覆盖；真机需手工在扫描与删除之间换文件 |
| 4 | macOS / Linux 身份 provider | 未验证 | Unix 路径只在本机编译通过；未在真机运行 |

## 已知限制（0.5）

- Unix 身份实现依赖 64 位 `struct stat` 前缀布局（Linux glibc/musl、Darwin `stat$INODE64`）；
  ABI 不符时返回 false 并退化为 size/mtime，不会返回猜的值。**未在真机验证。**
- APFS 克隆与 Windows 块克隆有独立文件 ID，按普通副本计；不识别、不假装识别。
- 只读建议目前只有 Windows 探测；macOS 时间机器快照与 Linux 缓存未实现，卡片在那些平台为空。
- 设置读取失败后仍需重启才能恢复清理（有意的，避免把空排除列表静默写回）。
- 扫描期无权限目录只有计数没有路径，因此没有进 CSV 跳过报告。

## 0.5.1 回归：macOS 身份 provider

0.5.0 首次推送后 **CI 在 macOS 上红**：三个生产 provider 测试全部失败，Linux 与 Windows 通过。
原因是在 arm64 Darwin 上 `stat$INODE64` 这个符号不存在——arm64 macOS 只提供 64 位 inode 布局，
导出名就是普通的 `stat`。

- 修复：先试 `stat$INODE64`；只有 `EntryPointNotFoundException` 且进程架构是 arm64 时，
  才回退到普通 `stat`（Intel 上普通符号可能是旧的 32 位布局，不猜，直接放弃）。
- 这是一次真实的"只在真机 CI 上才暴露"的问题：本地 Windows 全绿，Linux 绿，macOS 红。
  0.5.0 的产物在 macOS 上只是退化为 size/mtime 检查，不会误删。

## 结论

0.5 把两处"看起来精确、实际在骗人"的地方修掉了（删除身份绑定、硬链接折叠），
并把安全不变式交给 CI（`// SAFE:` 门禁 + 事故台账）。键盘体系与平台补齐
（AC-505 / AC-506）尚未开始，因此本版仍按预览版发布。
