# AirClean 0.3 验收记录

> 对应 [ISSUES-0.3.md](ISSUES-0.3.md)。本文件只记录验证结果；未执行的项必须写"未验证"，
> 不能因为 CI 绿就当成真机通过。

## 环境

| 项目 | 值 |
| --- | --- |
| 版本 | 0.3.0（提交 `de476cf`） |
| Windows | Windows 10 22H2，x64，.NET SDK 10.0.403 |
| macOS | CI `macos-latest` 全绿（2026-11-10，run 42794759063）；未做交互式真机测试 |
| Linux | CI `ubuntu-22.04` 全绿（2026-11-10，run 42794759063）；未做交互式真机测试 |

## 自动化

| 项目 | 结果 | 说明 |
| --- | --- | --- |
| `dotnet build AirClean.sln -c Debug` | 通过 | 0 警告 / 0 错误 |
| `dotnet test AirClean.sln` | 通过 | Core 77 + App 30 = 107 |
| CI（三平台） | 通过 | windows-latest / macos-latest / ubuntu-22.04，含 AOT 门禁 |
| Windows 回收站往返 | 通过 | 本机 App.Tests：回收 → 恢复 → 内容一致 |
| Linux Trash 往返 | 通过（CI） | `ubuntu-22.04` 上执行移入 / 恢复守卫测试 |
| macOS 废纸篓往返 | 通过（CI） | `macos-latest` 上执行 `NSFileManager` 移入 / 恢复守卫测试 |
| 多实例 | 通过（CI 三平台） | 锁文件与信号文件路径；真实双进程手测仍未做 |
| 崩溃日志 | 通过（单测） | 写入与 512 KB 轮转；未做真实崩溃手测 |

## 手动验收清单

### Windows 10/11

| # | 项目 | 结果 | 备注 |
| --- | --- | --- | --- |
| 1 | 扫描 → 勾选 → 确认 → 清理 | 通过 | Headless 流程测试覆盖 |
| 2 | 完成页“撤销本次清理” | 通过 | Headless 流程；真实回收站在 App.Tests 中往返验证 |
| 3 | 最近清理列表 + 行内撤销 | 通过 | Headless 流程 |
| 4 | 跳过报告展开 + 导出 CSV | 通过 | fake 导出器；真实文件选择器未手测 |
| 5 | 完成页清空废纸篓 | 通过 | fake 废纸篓；真实 `SHEmptyRecycleBin` 未手测 |
| 6 | 双开应用 | 未验证 | 仅有进程内测试 |
| 7 | 强制崩溃后 `crash.log` | 未验证 | 仅有写入/轮转单测 |
| 8 | 深色 / 浅色 / 中英切换 | 通过 | 截图与现有测试 |
| 9 | 取消扫描保留已完成分类 | 通过 | fake 扫描器 |
| 10 | 重复组超过 200 时提示 | 通过（Core） | UI 提示未用真实 200+ 组数据手测 |

### macOS

| # | 项目 | 结果 | 备注 |
| --- | --- | --- | --- |
| 1 | 构建与 Headless 测试 | 通过（CI） | `macos-latest` 全绿 |
| 2 | 主卷 `~/.Trash` 移入 / 恢复 | 通过（CI） | `NSFileManager` 守卫测试在 CI 上通过 |
| 3 | 外置卷 `.Trashes` | 未验证 | `NSFileManager` 已接入，CI 只覆盖主卷临时文件 |
| 4 | 窗口、主题、回收站、在文件夹中显示 | 未验证 | 需真机 |

### Linux

| # | 项目 | 结果 | 备注 |
| --- | --- | --- | --- |
| 1 | 构建与 Headless 测试 | 通过（CI） | `ubuntu-22.04` 全绿 |
| 2 | freedesktop Trash 移入 / 恢复 | 通过（CI） | 两种守卫测试均在 CI 上通过 |
| 3 | 托盘（0.4 范围） | 不适用 | — |
| 4 | `xdg-open` 打开回收站 | 未验证 | 需真机 |

## 已知限制（0.3）

- Windows 删除仍走 `SHFileOperation`，超过 MAX_PATH 的路径不支持；恢复用 `\\?\`。
- macOS 已接入 `NSFileManager.trashItemAtURL`；外置卷与交互式 Finder 行为未验证。
- Linux 跨设备恢复直接报错，不做复制删除。
- 清空废纸篓会删除该卷所有批次（含其他应用的文件），确认层已明确说明；Unix 为全局废纸篓。
- 跳过报告只保留前 500 条样本，界面会提示截断。

## 结论

三平台 CI（run 42794759063，提交 `de476cf`）全绿，Linux/macOS 的废纸篓恢复测试已在
真实 runner 上执行；Windows 核心链路另有本机自动化证据。仍缺的是交互式真机检查：
macOS 外置卷、Finder 行为，Linux 的 `xdg-open`，以及 Windows 的真实文件选择器、
真实清空回收站、真实崩溃与双进程手测。发布前应补跑这些项并回填本表。
