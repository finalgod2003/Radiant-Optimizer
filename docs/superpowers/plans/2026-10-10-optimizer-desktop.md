# Optimizer Windows 客户端实施计划

**Goal:** 交付读取真实本机信息、可恢复设置及联网激活的 Windows x64 客户端。

**Architecture:** .NET 10 WPF UI、纯核心模型与恢复流程、Windows 适配器、独立授权客户端。系统设置只接受固定动作 ID；开发机只做读取，写入/恢复通过隔离适配器测试。

**Tech Stack:** C#、.NET 10、WPF、Windows API、DPAPI、HTTPS、RSA SHA-256。

## 全局约束

- 对齐已确认设计及 `src/lib/optimizer-contracts.ts` 中 API 与签名字段。
- 项目内不包含服务端签名私钥；本地 token 用 DPAPI 保护。
- 原始值不存在与实际为零必须区别；备份先于写入；失败、外部修改与恢复冲突要可见。
- 只有真实实现和测试过的能力可标为可用。恢复不会被付费授权锁住。
- 代码放在 `desktop/`；工具链与打包输出在忽略目录，不改系统全局 PATH。

## 任务 1：系统模型与适配器

- [x] `desktop/Radiant.Core/` 定义系统快照、优化项、设置值、操作日志、能力模型。
- [x] `desktop/Radiant.Windows/` 读取 CPU、内存、磁盘、显卡、系统版本、进程与电源计划。
- [x] 实现用户级游戏模式、录制设置和电源计划的固定适配器；写入后回读。
- [x] `desktop/Radiant.Tests/` 使用内存适配器验证原值保存、写入失败、精确恢复及外部更改冲突。

## 任务 2：原生 UI 与诊断

- [x] `desktop/Radiant.App/` 创建可导航、可缩放、支持中英文的 WPF 窗口。
- [x] 概览显示真实数值，设置页逐项预览再应用，恢复页显示动作结果并允许恢复。
- [x] 进程页显示资源读数；网络页执行有界 Ping/DNS 测试；输入工具仅响应应用窗口内用户操作。
- [x] 增加只读 `--diagnostics-output` 模式，便于验证打包后的真实采集能力而不修改系统。

## 任务 3：客户端授权

- [x] `desktop/Radiant.Licensing/` 完成 HTTP envelope 解析、设备请求、轮询与刷新。
- [x] 校验 RSA 签名、设备 ID、有效期；将 token 与缓存 lease 用用户级 DPAPI 保存。
- [x] 账号页显示设备码、打开本站浏览器授权页、处理用户取消/到期/撤销；断网仅使用未到期且签名正确的缓存。

## 任务 4：打包与端到端验证

- [x] `dotnet build`、恢复状态机测试、签名协议测试、打包后 `--diagnostics-output`。
- [x] 发布 Windows x64 self-contained 包，生成 SHA-256 与网站 release manifest。
- [x] 创建构建脚本及安装/卸载说明，明确本地开发包签名状态。
- [x] 联调后核对网站与客户端宣传的能力一致；记录仍需真实目标硬件验证的项目。

第二、三批高级功能按照设计独立扩展，不把它们的未完成状态隐去。

验收结果见 [首批验收记录](../../optimizer-verification.md)。勾选范围仅为本计划的预览代码交付；正式商户扣款、真实系统写入、生产部署和后续高级功能未计作完成。
