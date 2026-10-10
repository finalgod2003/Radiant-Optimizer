# Optimizer 授权与网站实施计划

**Goal:** 交付与 Windows 客户端配套的网站、付款授权和设备激活闭环。

**Architecture:** 保留现有 TanStack Start 模板；独立授权模块通过 Drizzle 访问授权及订单表，API 路由编排支付与授权。前端只使用共享契约、TanStack Query 与 api-client。

**Tech Stack:** TypeScript、React、TanStack Start/Query/Form、Paraglide、Drizzle、Web Crypto RSA。

## 全局约束

- 已确认设计：`docs/superpowers/specs/2026-10-10-radiant-product-design.md`。
- API 字段、路径和签名编码使用 `src/lib/optimizer-contracts.ts`。
- 不修改其他模块边界；三份 schema 模板同步，修改生成 schema 后立即执行开发库 db:push。
- 在独立 worktree 完成；不部署，不操作生产数据库，不进行真实扣款。
- 不回滚工作区原有改动。新前端文案写入两个语言文件。

## 任务 1：授权与设备数据

- [x] 新建 `src/modules/licenses/service.ts` 与必要的模块内文件，三份数据库模板添加授权、单设备激活、短期授权请求、事件记录。
- [x] 对订单关联、设备槽位和兑换请求增加数据库约束；服务端核实用户归属与订单支付状态。
- [x] 实现共享契约列出的用户/管理员授权 API，添加分页、会话权限和 mutation origin 校验。
- [x] 创建实际 SQLite 集成测试：重复发放只得一个授权，用户不能读取/解绑他人设备，并发激活只能占一个槽位，撤销不能被重复付款事件重开。

## 任务 2：桌面设备协议

- [x] 实现 start/approve/poll/refresh/keys；5 分钟设备验证码，高熵轮询凭据，摘要存储，受限设备 token。
- [x] RSA SHA-256 签名离线租约，有效期 7 天。私钥仅来自服务端配置，不自动在生产内存生成。
- [x] 测试一次性兑换、超时、轮询限流、无效设备 token、已撤销 token、到期时间与签名核验。

## 任务 3：商品与支付

- [x] 在服务端商品目录新增一次性产品；本地开发参考价 5777 美分，仅在商户配置就绪时启用支付。
- [x] API 层在已验证付款后幂等发放；处理支付返回与 webhook 两条确认路径，付款失败不得发放。
- [x] 接入至少一个提供商的退款撤销路径，忽略旧成功回调对 revoked/refunded 授权的覆盖。
- [x] 拒绝新产品的生产测试金额覆盖；测试模式不被描述成实际收款验证。

## 任务 4：产品网站

- [x] 新产品首页、功能页、定价与下载；游戏目录迁到 `/games`，计算器保留 `/tools`。
- [x] 完成设备授权确认、用户授权列表、设备解绑、管理员撤销页面。
- [x] 更新双语文案、导航、站点地图与元数据；下载信息取实际发布清单，不用无效下载按钮。
- [x] 浏览器验证语言、页面移动布局、未登录跳转、无授权空状态及真实测试授权流程。

## 任务 5：联调与交付

- [x] `pnpm exec tsc --noEmit`、`pnpm exec tsx --test tests/*.test.ts`、`pnpm build`。
- [x] 使用桌面真实 API 客户端完成激活、刷新、解绑与拒绝路径。
- [x] 运行 security-scan，记录构建与功能测试证据；保留可审阅工作树，不提交混合来源的原有修改。

验收结果见 [首批验收记录](../../optimizer-verification.md)。勾选范围仅为本计划的预览代码交付；正式商户扣款、真实系统写入、生产部署和后续高级功能未计作完成。
