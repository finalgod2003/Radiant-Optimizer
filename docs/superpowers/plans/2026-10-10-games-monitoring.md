# 游戏自动配置与高级监控实施计划

> **For agentic workers:** Use executing-plans with independent implementation agents for the two engine boundaries; the parent integrates and reviews. Steps use checkbox syntax for tracking.

**Goal:** 在已有 Windows 客户端中交付可恢复的游戏自动配置、真实持续监控、FPS/帧时间采集、浮层和本地导出。

**Architecture:** 继续使用已附加的 `codex/optimizer-desktop` 工作树。游戏文件引擎、只读监控引擎与 WPF 页面分开；后端授权协议保持现有边界。游戏引擎自行保存精确原文件，UI 不直接写配置；监控通过系统/驱动接口与固定 PresentMon 子进程读取数据。

**Tech Stack:** .NET 10、C#、WPF、Windows 性能接口、NVIDIA NVML、PresentMon ETW；网站继续 TanStack Start 和 Paraglide。

## 范围与约束

- 用户已确认完整产品设计，并于本轮明确要求继续游戏自动配置及高级监控，无需重新批准同一架构。
- 优先 CS2、Fortnite、VALORANT；对每个适配器记录已验证格式、来源及支持边界。未知格式、游戏运行、只读文件或外部修改时阻止覆盖。
- 本阶段不包含驱动调校、应用卸载/强退、带宽端点或显存压力测试；这些属于原方案其他独立任务。
- 不修改用户电脑中的真实游戏配置进行验收；写入/恢复测试仅针对临时测试目录。实际硬件与进程只读测试。
- 不触碰反作弊、驱动安装、游戏进程注入、账号数据或游戏联网逻辑；浮层是独立置顶窗口。
- Premium 解锁游戏配置应用及 FPS 浮层/录制；读取、预览、传感器监控与恢复免费。每个付费操作重新验证缓存租约，网页同步准确范围。
- 缺失/过期/不支持数据用空值和原因表示，不使用模拟数值。持续任务支持取消、资源上限和关闭清理。
- 原工作树及已有改动不覆盖；不部署、不扣款、不生成生产迁移；验收后保留可审阅的工作树。

## 任务 1：游戏配置引擎

**Files:** `desktop/Radiant.Games/`、`desktop/Radiant.Games.Tests/`、`docs/game-profile-support.md`。

**Interface:** 引擎提供安装/配置发现、预设预览、应用、历史及恢复。预览包含游戏、完整路径、格式、原始哈希和逐项修改；应用只能消费已验证预览，重新检查进程、内容哈希及路径。

- [x] 查验厂商/引擎文档与原有配置格式，确定支持的节/键、值类型与版本识别规则；保持分辨率、输入绑定、灵敏度、账号字段及未识别内容原样。
- [x] 在临时目录建立真实字节样本；覆盖 BOM/换行、重复键/异常格式、原文件不存在、只读/重解析路径、外部更改、备份失败和进程正在运行。
- [x] 实现自动发现及明确选定本地文件入口；有限枚举用户目录，不做全盘扫描。预设仅调整受支持画质/垂直同步/帧率限制。
- [x] 备份和待执行记录落盘后才允许替换配置，应用后回读；恢复精确原始字节，外部更改提示冲突；重新打开客户端仍能恢复。
- [x] 运行 `dotnet run --project desktop/Radiant.Games.Tests -c Release`，全部写入位于生成的临时测试目录。

## 任务 2：只读监控引擎与帧统计

**Files:** `desktop/Radiant.Monitoring/`、`desktop/Radiant.Monitoring.Tests/`、`docs/monitoring-support.md`。

**Interface:** 系统采样返回时间戳及可空 CPU/RAM、逐 GPU 使用率/温度/显存与不可用原因；选定进程带 PID 和启动时间。帧统计消费经过验证的 PresentMon CSV 数据，返回采样时长、帧数、平均 FPS、帧时间 P95/P99、1% low（明确公式）。

- [x] 查询 Microsoft、NVIDIA 和 PresentMon 官方文档。优先系统性能计数器及随 NVIDIA 驱动安装的 NVML；只读调用、从可信系统位置加载。
- [x] 实现持续采样、有限时间序列与可取消会话；单个传感器失败不终止其他读取。重用已有 Windows CPU/RAM 采集。
- [x] 实现独立且固定版本的 PresentMon 控制台适配；参数使用 ArgumentList、输出使用内存中的标准输出流、进程身份复核、没有样本不显示 FPS。
- [x] 统计测试覆盖错列、引号、非数值、负值、进程混杂、多个交换链、空样本、分位数和过期样本；导出使用固定列及转义。
- [x] 采集有时长/输出大小上限，取消或退出关闭自身子进程与 ETW 会话，不影响其他监控工具。运行测试及真实本机只读采样。

## 任务 3：原生界面、工具打包及端到端验收

**Files:** `desktop/Radiant.App/GamePages.cs`、`MonitoringPages.cs`、`MonitorOverlay.cs`、`MainWindow.cs`、`App.xaml.cs`、`scripts/build-desktop.mjs`、`scripts/test-desktop.mjs`。

- [x] 加入游戏/高级监控导航与中英文文案。游戏页面提供发现、预设、变更预览、确认应用和恢复；延续实验写入开关和每次动作确认。
- [x] 监控页面提供开始/暂停、目标进程、指标趋势、显卡选择、PresentMon 状态及错误原因。FPS 明确采集方法与权限要求。
- [x] 独立浮层支持置顶、位置调整、鼠标穿透、关闭；仅在窗口/无边框模式保证可见，不宣称独占全屏兼容。
- [x] 下载并固定官方 PresentMon 控制台、校验哈希及许可证；自带工具不自动提权，缺权限时显示操作说明；打包清单列出第三方工具。
- [x] 导出本地 CSV、停止/导航/关闭时清理采样任务；模拟器只用于测试项目，不接入产品运行路径。
- [x] 升级预览版本、重新生成 ZIP/manifest、只读诊断及新增页面截图；在本机 WPF 测试渲染进程上验证真实帧采集或精确记录外部权限限制。

## 任务 4：网站范围、回归与交付

**Files:** `messages/en.json`、`messages/zh.json`、`src/blocks/optimizer/`、`README.md`、`docs/optimizer-preview.md`、`docs/optimizer-phase2-verification.md`。

- [x] 同步当前 Free/Premium 范围、游戏支持列表、监控条件及版本；保持网页游戏目录 `/games`。
- [x] `pnpm desktop:test`、新增原生测试、`pnpm exec tsc --noEmit`、`pnpm exec tsx --test tests/*.test.ts`、`pnpm build`。
- [x] 验证中文/英文功能、价格、下载及移动布局；HTTP 获取最终 ZIP，核对 SHA-256。
- [x] 按 security-scan 检查变更、驱动/工具加载路径、配置路径边界与日志；记录真实通过项和未在目标游戏/显卡上验证的兼容性。
