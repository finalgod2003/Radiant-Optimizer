# Windows 产品预览版交付说明

2026-10-10 · 0.2.0 Preview · 独立实现

用户已确认 Windows 客户端 + 官网、账号和付费授权的完整产品方向，并于本轮要求继续游戏自动配置和高级监控。0.2 在首版真实检测、恢复及激活之上，加入受格式校验的游戏预设、持续硬件监控和 PresentMon 帧采集。它仍是本地 Windows x64 预览版，不代表与原产品全部功能对齐。

0.1 的实际构建、授权联调和浏览器验收记录保留于 [首版验收记录](optimizer-verification.md)。新增引擎的兼容边界分别见 [游戏配置支持](game-profile-support.md) 和 [监控支持](monitoring-support.md)；本轮执行结果以 [第二阶段验收记录](optimizer-phase2-verification.md) 为准。

## 当前能力

| 范围               | 已实现                                                                                          | 验证边界                                                                                                        |
| ------------------ | ----------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| 系统概览与硬件监控 | CPU 型号、占用历史、逻辑处理器、内存、磁盘、GPU 名称及系统版本；支持时读取 GPU 占用、温度和显存 | NVIDIA NVML 依赖已安装驱动和具体传感器；Windows GPU 计数器作为可用时的回退；不提供 CPU 温度                     |
| 进程               | 进程名、PID、采样 CPU 和内存                                                                    | 读取失败明确显示不可用，不结束任何进程                                                                          |
| 游戏配置           | 发现 CS2、Fortnite、VALORANT 本机文件；竞技/均衡预设；逐项预览；应用前备份原始字节；历史与恢复  | 仅支持明确版本和键集合；未知格式、游戏运行、只读路径或外部改动会阻止写入；未以真实游戏启动验证全部兼容性        |
| 帧采集与浮层       | PresentMon 2.6.0 目标进程采集、平均 FPS、帧时间 P95/P99、1% low、独立置顶浮层及本地 CSV 导出    | 数据反映应用提交帧的节奏，含丢弃帧，不等于屏幕实际显示 FPS 或输入延迟；需要 Windows 事件权限；每次采集 5–300 秒 |
| Windows 设置       | 用户级游戏模式、游戏录制、后台捕获；切换已有电源计划                                            | 固定白名单；回读；每次会话必须主动启用实验功能；未在各目标 Windows 版本执行隔离写入验收                         |
| 恢复               | 写入前保存原值及类型；不存在的注册表值可恢复为不存在；部分失败、进程中断、外部修改冲突          | 使用内存适配器和真实磁盘日志测试；无授权也可恢复                                                                |
| 网络               | 用户指定目标的最多 8 次 ICMP 请求；Windows DNS 解析耗时                                         | 不代表游戏延迟、带宽或公共 DNS 服务商对比                                                                       |
| 输入工具           | 点击/CPS、窗口内键盘响应、反应时间、瞄准练习、显示纯色检查、XInput 手柄读取                     | 无全局键盘钩子；手柄实际硬件兼容性需进一步验证                                                                  |
| 官网               | 双语产品页、功能、价格、下载；游戏目录和计算器保留                                              | 下载链接来自实际文件清单及 SHA-256 校验                                                                         |
| 授权               | 网站账号、5 分钟设备码、单活动设备、一次性兑换、解绑、管理员撤销                                | 真实 SQLite 和真实 C# HTTP 客户端联调；用户不能处理他人授权                                                     |
| 付款               | Stripe 一次性购买、签名 webhook、重复事件幂等、退款撤销                                         | 使用 Stripe SDK 签名和标记测试订单；尚无真实商户扣款验收                                                        |

免费版包含检测、游戏发现与预览、硬件传感器历史与 CSV 导出、输入工具、三个入门设置和恢复。Premium 增加游戏预设应用、现有电源计划切换、FPS 录制、独立浮层和帧数据 CSV 导出。系统与游戏写入仍为实验功能，每次会话默认关闭，具体操作再次确认。参考价格为 57.77 USD，支付默认关闭。

本阶段没有加入 NVIDIA 驱动调校、普遍适用的 CPU 温度、独占全屏注入浮层、带宽/负载延迟测试、强退应用或任意游戏支持。画质预设不承诺 FPS 提升，网页不读取或模拟本机硬件数据。

## 游戏配置与采集边界

| 游戏             | 识别条件                                                                                       | 预设范围                                  |
| ---------------- | ---------------------------------------------------------------------------------------------- | ----------------------------------------- |
| Counter-Strike 2 | 已有 `cs2_video.txt`，根节点 `video.cfg`，`Version` 为 15 或 16                                | 已有垂直同步与 MSAA 键                    |
| Fortnite         | `GameUserSettings.ini` 的 Fortnite 节，`FortniteReleaseVersion` 为 14；如有 `Version` 则须为 5 | 帧率上限、垂直同步及已有的受支持画质键    |
| VALORANT         | `RiotUserSettings.ini` 的 `[Settings]`，`LocalSettingsVersion` 为 5 或 7                       | 识别的 Riot 帧率限制及已有材质/细节画质键 |

这些是有限格式适配器。必要版本或键缺失时不会猜测、补写或覆盖。分辨率、按键、灵敏度、账号字段及未识别内容保持原样。用户可先免费检查完整路径和差异，应用时重新验证进程状态及文件内容。原始字节先落盘，恢复时检查外部变更；授权失效后仍可恢复已有记录。写入测试只使用临时测试目录，不修改开发者电脑上的真实游戏配置。

硬件监控的单项失败不会被显示为 0，也不会通过模拟数据填补。GPU 温度来自支持的 NVIDIA 驱动传感器，性能计数器回退不创造温度读数。FPS 采集验证目标 PID 和启动身份；没有样本时显示原因。P95/P99 和 1% low 的具体计算口径见监控支持文档，不能直接与采用其他统计口径的工具比较。

浮层是独立置顶窗口，支持窗口/无边框使用；独占全屏可能遮挡。它不向目标游戏注入，也不自动获取管理员权限。采集失败时按客户端提示检查事件访问权限。帧数据、硬件历史和 CSV 在本机处理，不自动上传。停止或退出时清理本应用的采集任务，不关闭其他监控工具。

## 本地运行

1. 使用本交付附带的 `codex/optimizer-desktop` 工作树；保留原工作区已有变更。
2. `pnpm install`；复制 `.env.example` 到 `.env.development`，设置随机 `AUTH_SECRET`，SQLite 数据库路径及 `VITE_APP_URL`。
3. `pnpm db:push` 建立开发表。工作树使用 `file:data/optimizer-dev.db`，与原项目数据库隔离。
4. `node scripts/setup-desktop-keys.mjs` 创建本地持久 RSA 签名密钥。已有密钥不会被替换，私钥不会打印。
5. `pnpm dev --port 3010`。本次客户端和本地配置均指向 `http://localhost:3010`；网页、服务端验证地址和客户端 origin 必须一致。
6. `pnpm desktop:build` 需要 .NET 10 SDK 和 Windows 的 `tar.exe`。可通过 `DOTNET_HOST_PATH` 指定 SDK 中的 dotnet.exe。构建不改全局 PATH。
7. 解压 `public/downloads/optimizer-0.2.0-win-x64-preview.zip`，阅读内附第三方工具和运行说明后运行 `Optimizer.exe`。包已包含 .NET 运行时，无需另装 SDK。

客户端安装包未进行 Authenticode 签名。本地包中的授权服务地址是 localhost，不应直接当作线上商用发行包。无需账号即可使用免费检测；没有预置管理员或共享默认密码。

账号由网站正常注册。初始化管理员权限使用 `pnpm rbac:init`，再按 `scripts/assign-role.ts` 的参数给自己明确选择的账号分配角色。在 `/admin/settings` 设置 Stripe 商户参数；新产品仅接受 Stripe，其他模板支付方式不为该产品发放授权。

## 密钥、授权与撤销

- 服务端 `OPTIMIZER_LICENSE_PRIVATE_KEY` 是 PKCS#8 DER 的 base64url，`OPTIMIZER_LICENSE_KEY_ID` 是稳定标识。私钥不进入 VITE 变量、仓库、客户端或发布包。
- 客户端构建优先读取 `OPTIMIZER_LICENSE_PUBLIC_KEYS`（`[{"keyId":"…","spki":"…"}]`）。本地开发可从服务端私钥推导公钥；发布构建机器只需公钥。
- 公钥作为 exe 嵌入资源，缓存不能替换信任根。更换服务器签名密钥前，必须先发布信任新公钥的客户端。支持在公钥数组中预置多个轮换密钥。
- 设备 token 只存摘要于服务端；客户端使用当前 Windows 用户的 DPAPI 存储。网页只接收短期用户码，不接收设备 token 或网站 session。
- 7 天离线租约校验签名、产品、安装 ID、设备 ID 和时间。解绑、退款、管理员撤销后，下一次在线刷新失效；已离线的合法租约最迟到期后失效，不宣称即时远程封禁。
- 损坏的授权缓存会保留为 `.unreadable-*` 并降级免费，仍可打开恢复中心。恢复日志独立于授权缓存。
- 本地状态位于 `%LOCALAPPDATA%\OptimizerPreview`。游戏备份保存完整原文件，可能包含原有游戏或账号标识；采集和导出可能包含进程名、PID、时间戳及硬件读数。主动分享前检查内容。卸载前先恢复需要撤销的设置和游戏文件，再关闭客户端并删除解压目录；保留状态目录直到不再需要恢复记录。

## 付款及数据库

`product_license` 的订单关联唯一；`device_activation` 保存设备记录；`desktop_auth_grant` 保存一次性请求；`license_event` 保存审计事件。三份 SQLite/PostgreSQL/MySQL 模板同步。当前只对 SQLite 做过运行测试。

API 层编排付款与授权，授权模块不直接调用其他模块。发放检查真实订单的产品、金额、币种、一次性类型、Stripe 会话/交易关联、用户、metadata、支付状态及测试/生产环境；退款/撤销不会被旧成功回调重新开启。不能通过模板的测试金额设置降低此产品价格。

开发使用 `db:push`。上线应先生成并审查目标数据库迁移，再应用；本次没有运行生产迁移、部署或真实支付。

## 验证命令

首版实际执行结果见 [0.1 验收记录](optimizer-verification.md)，本阶段新增检查与最终包校验见 [0.2 验收记录](optimizer-phase2-verification.md)。

```powershell
pnpm desktop:test
pnpm exec tsx --test tests/*.test.ts
pnpm exec tsc --noEmit
pnpm build
pnpm exec tsx scripts/with-env.ts tsx tests/desktop-activation.run.ts http://localhost:3010
```

HTTP 联调脚本仅允许 loopback、开发环境及指定 `data/optimizer-dev.db` / `optimizer-activation-test*.db` 路径，创建随机 `.invalid` 测试账号与显式标记的订单，测试后仅清理自身行。没有用于生产的免费授权后门，也不进行扣款。

打包程序另提供只读诊断与渲染验证入口：

```powershell
Optimizer.exe --diagnostics-output diagnostics.json --data-directory preview-state
Optimizer.exe --snapshot overview.png --snapshot-page overview --data-directory preview-state
```

`--snapshot-page` 支持 overview、optimize、processes、network、tools、recovery、account，并在 0.2 增加游戏和监控页面。具体新页面标识及采集验收命令见第二阶段验收记录。这些验证入口不写真实游戏配置或系统设置。诊断文件包含本机硬件及进程信息，默认不上传服务器。

## 发布前尚需完成

- 在可回滚的 Windows 10/11 测试环境验证真实设置写入、读取和恢复，以及系统策略和不同硬件兼容性。
- 配置正式域名、持久数据库、商户账号、退款运营流程和发布签名；确认定价与条款，完成支付提供商测试环境验收。
- 为目标部署生成数据库迁移。Workers 的下载文件应先发布到自己的静态存储，再设置已核验的 `OPTIMIZER_RELEASE_JSON`；本地 ZIP 不自动上传。
- 在受支持的具体游戏版本上验证预设应用后的启动、画面、游戏重新写入及恢复行为；添加适配器前先确认版本和关键字段，不能因名称相同就扩大兼容承诺。
- 在不同 GPU/驱动、Windows 权限和窗口模式下验证实时传感器、帧捕获及浮层；扩展驱动调校、应用控制或带宽测试需要独立实现和验收。
