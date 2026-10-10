# Waffo 正式收款上线状态（2026-10-10）

目标为 Optimizer Premium Lifetime，USD 19.90 一次性付款、一台活动设备。网站和授权服务已部署，正式收款尚未启用，未产生真实扣款。

## 已完成

- 独立发布工作区：`C:/Users/hp/.codex/worktrees/waffo-production/radiant optimizer`，基于 `26d436f`。带入本次支付代码，以及经过检查的当前 0.5.0 官网内容、品牌素材与客户端快照；保留主工作区其他未提交修改。
- 用户审查并批准后，`0001_fine_praxagora.sql` 已应用到生产 D1。13 条 CREATE 语句新增 `product_license`、`device_activation`、`desktop_auth_grant`、`license_event` 及索引，未删除现有数据。
- 本聊天最近手动部署版本：`ecf983e0-0785-441c-83e8-f2576778e748`；Worker `radiant-optimizer`，D1 `radiant-optimizer-db`。通过 `pnpm run cf:deploy` 发布，压缩体积 1688.40 KiB。
- 已生成独立生产授权签名密钥，key ID 为 `radiant-prod-2026-10-10-1`，私钥和 ID 已保存到 Worker secrets。没有更改现有 AUTH_SECRET / CONFIG_ENCRYPTION_KEY。私钥备份保存在忽略提交且限制 Windows 文件权限的本地文件中。
- 已构建连接 `https://radiantoptimizer.xyz` 的 Windows x64 0.5.0 客户端，内置匹配的生产签名公钥，不含私钥。仍为未做 Windows 代码签名的预览版。
- 安装包 59,473,665 字节，SHA256：`40fcf6725af9a010896d3ed610246b1b24ea8233fe9793b6373a5b22a92a37bf`。存放于私有 R2 桶 `radiant-optimizer-releases`，经 Worker 的受限下载接口提供；已从公网完整下载并校验哈希。
- `/api/desktop/keys` 返回的公钥与正式安装包一致；公开目录的价格为 1990 美分、币种 USD、版本 0.5.0。`checkoutEnabled=false`。
- Waffo 独立 Radiant 店铺的 `Optimizer Premium Lifetime` 已发布到生产产品目录，价格 USD 19.90，产品 ID `PROD_16g4Fe93JgA1LN9TLlhXTb`，销量 0。
- 经用户明确授权，生产 API 密钥 `radiant-optimizer-prod` 已创建。私钥目前仅安全保存在忽略提交、限制访问的本地文件；尚未写入官网后台配置。
- 生产 Webhook 已创建：`https://radiantoptimizer.xyz/api/payment/notify/waffo`，Raw 格式，只订阅 `order.completed` 与 `refund.succeeded`。生产验签公钥已保存于本地待配置。
- 修复 D1 不支持交互式事务导致后台设置无法保存的问题，D1 使用原子 batch upsert；用实际 Miniflare D1 验证加密、掩码保留、受保护字段过滤和整体回滚。

## 已验证

- 53 项自动测试通过，0 失败；TypeScript 检查通过；根工作区 `pnpm build` 与独立工作区正式发布构建均通过。
- 安全扫描检查 52 个改动文件，未发现问题。上线代码已同步到远端 main：`7def636308f10f1a6df4964a8e8a8decc923e6d3`，保留 CI 基础提交 `09703d7` 和发布清单修复 `92eb1cd`。提交前扫描 54 个文件、整合最新 main 后扫描 43 个文件均通过；整合后 build 通过。未提交本状态记录、退款草稿、商户资料、凭据或未完成的桌面端源码改动。
- 首页、定价、下载、features、登录页在 390 / 768 / 1440 宽度无横向溢出、无损坏图片；中英文价格一致。主要营销页使用固定深色设计。
- 公网首页、定价、中英文定价、下载、隐私、条款、sitemap、robots 均返回 200；下载 HEAD / GET 成功，错误 SHA 返回 404；匿名授权与管理 API 未泄露受保护数据。
- 未运行 Lighthouse / Core Web Vitals 实测；未完成真实支付、生产回调发证、原生客户端激活及退款的整条流程。

## 仍需完成

1. **管理员注册**：用户指定其私人邮箱账号为管理员，但生产库中尚无该账号。用户明确选择稍后注册。注册后按现有授权仅为该账号授予管理员权限，再通过后台保存加密的生产 Waffo 配置；不应创建临时后门、伪造账号或明文写入支付私钥。
2. **店铺审核**：2026-10-10，用户明确同意公开经营者姓名、退款政策、三项合规声明及官方费率。已勾选并提交独立 Radiant 店铺申请，页面确认「审核中」，通常 1–3 个工作日并通过邮件通知。提交前网站自检通过，检查时间 18:07:27（页面显示）。审核尚未通过，同账号其他店铺的状态不能替代此审核。
3. **公开联系邮箱**：用户已确认 `support@radiantoptimizer.xyz`，转发至此前指定的私人 Gmail。按用户授权已切换 Cloudflare，3 条 MX、SPF 与 DKIM 公网查询通过。support 路由启用、私人 Gmail 目标已验证，并实际收到 Waffo 发至 support 的验证邮件。Waffo 验证成功。官网首页、定价、下载、游戏、中英文条款与隐私页已发布客服邮箱，8 个页面均验证 200 且无私人邮箱泄露。
4. **销售条款已发布**：中英文服务条款已公开个人经营者 Zhenyu fang、USD 19.90 一次性终身授权及完整 7 天退款政策。定价页购买区增加退款摘要和条款链接。Waffo 生产产品说明已保存同样的退款规则及中英文条款链接，重新打开编辑页确认持久化。
5. **正式联调**：后台配置与店铺条件具备后，从网站购买入口创建 USD 19.90 订单，核验已验签回调、唯一许可证、客户端激活与退款撤销。真实付款由用户操作。已有沙盒支付是管理配置测试，不代表本站完整购买流程已通过。

## 发布与运行资料

- 官网：<https://radiantoptimizer.xyz>
- 商户审核：<https://pancake.waffo.ai/merchant/dashboard/STO_7R9wel999McoywmS8GktvG/settings/business-details>
- Cloudflare 账号：`e4efddba8ed58e7be7a8ee4fb831b40d`；D1 ID：`1abcfc20-d6b1-41dd-92fb-3a607b7ebf3a`。
- Waffo 商户：`MER_2Z8m8RhjBfFUmGq9lH0uOk`；店铺：`STO_7R9wel999McoywmS8GktvG`。
- 本地忽略提交的报告：独立工作区 `.tools/production-site-verification.json`、`.tools/production-release.json`；根工作区 `.tools/production-release-evidence.json`。报告不包含私钥。
- 生产客户端为独立快照；根工作区原本的本地预览安装包和对应验证文档没有被替换。

Waffo [官方账户审核说明](https://docs.waffo.ai/zh/mor/account-reviews)说明业务审核按店铺进行。配置步骤见 [waffo-payment-setup.md](./waffo-payment-setup.md)。

## main 同步后的自动部署

最新业务提交 34b95456eeedb4c8424228022e2a691946cd2fba（Publish approved operator and refund terms）已推送 main 并经自动部署上线。中英文条款、定价四个公网页面均返回 200，经营者与退款政策可见，无私人邮箱泄露。pnpm build 通过，7 文件提交前安全扫描通过，定价页 390 / 768 / 1440 宽度无横向溢出。之前自动构建记录 056c3b5e-71cf-4d2f-8f3a-a84288113b5d 对应 0f8af07；本次新内容已用公网验证，未取得新版本 ID（Wrangler 查询遇到网络错误）。自动构建保持既有配置。

客服邮箱最终状态：Cloudflare Overview 显示 Enabled，Total received 1 / Delivered-Forwarded 1；客服邮箱与官网域名均已通过 Waffo 验证。经营者公开、退款规则及审核声明已获用户确认并执行。管理员注册仍按用户选择留待稍后；生产私钥尚未保存到网站后台，checkoutEnabled=false，未产生真实扣款。
