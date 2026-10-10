# Waffo Pancake 支付配置

当前代码已接入 Optimizer Premium 的 **USD 19.90 一次性终身授权**，对应本站产品 `optimizer_premium_lifetime`。付款、重复通知、许可证发放和退款撤销沿用现有订单系统。Waffo 接口通过服务器 RSA 签名调用，无须新增支付 SDK。

## 本次已完成（2026-10-10）

- 已创建独立的 **Radiant Optimizer** 测试店铺及 **Optimizer Premium Lifetime** 一次性产品，按用户最终指定价格调整为 USD 19.90，类别 Digital Goods。
- [测试产品管理](https://pancake.waffo.ai/merchant/dashboard/STO_7R9wel999McoywmS8GktvG/products)：店铺 ID `STO_7R9wel999McoywmS8GktvG`，产品 ID `PROD_16g4Fe93JgA1LN9TLlhXTb`。
- 已在用户确认后创建 `radiant-optimizer-test` API 密钥，并把商户 ID、店铺/产品 ID、私钥及测试 Webhook 公钥保存到**本地开发数据库**的管理配置。已开启本地 Waffo Test 并设为默认渠道；密钥不在代码或本文档中。
- 已配置测试环境的 Raw Webhook，地址为 `https://radiantoptimizer.xyz/api/payment/notify/waffo`，订阅 `order.completed` 与 `refund.succeeded`。
- 已用真实 Waffo API 创建收银台，并用官方沙盒测试卡完成 **USD 57.77 支付成功**验证，没有真实扣款。
- 已对该笔旧价沙盒订单提交全额测试退款；Waffo 最后检查时仍显示「退款中」，尚未产生 `refund.succeeded` 通知。退款验签与撤销授权已有自动化覆盖，实际商户退款通知尚待渠道完成后验证。
- 按用户要求改价后，已重新通过实际 API 创建收银台，并在 Waffo 页面确认合计为 **US$19.90**。
- 最新发布验证：53 项测试通过，独立发布工作区完整 TypeScript 检查及 `pnpm build` 通过，包含新价下单、旧价订单付款/退款兼容和受限安装包下载。

**代码已部署，正式收款尚未启用。** 沙盒付款来自管理配置测试，不生成本站订单或许可证。生产 API 密钥、Raw Webhook 和 USD 19.90 产品已在 Waffo 创建；官网仍待指定管理员注册后，将生产凭据加密保存到后台配置。独立 Radiant 店铺审核和最终销售条款也尚未完成。当前公开目录 `checkoutEnabled=false`，不能把部署成功或沙盒付款视为本站完整购买链路已经通过。

已发布的 Windows x64 0.5.0 预览客户端连接正式官网，内置与 `/api/desktop/keys` 一致的生产公钥。安装包通过私有 R2 的 `OPTIMIZER_RELEASES` 绑定流式下载；`OPTIMIZER_RELEASE_JSON` 指向已验证的版本、大小和 SHA256。此接口只允许下载当前清单指定的文件，不暴露桶列表。客户端仍未做 Windows 代码签名。

## 1. 在 Waffo 创建产品

当前项目的测试产品已经创建；以下字段供重建或创建生产产品时参考。登录 [Waffo Pancake 商户后台](https://pancake.waffo.ai/merchant/auth/signin)，进入目标店铺，先切换 **Test** 环境，在 Products 中创建产品：

| 字段         | 填写内容                                                                       |
| ------------ | ------------------------------------------------------------------------------ |
| 产品名称     | Optimizer Premium Lifetime                                                     |
| 描述         | Premium license for one Windows device. One-time payment with lifetime access. |
| 付款类型     | One-Time（一次性付款）                                                         |
| 币种         | USD                                                                            |
| 买家支付总价 | $19.90；若界面明确使用 cents / 最小货币单位，则填写 1990                       |
| 税务类别     | Digital Goods                                                                  |
| 税务方式     | Inclusive（含税）                                                              |
| 状态         | Active                                                                         |

保存后复制 `PROD_...` 产品 ID 和该产品所属的 `STO_...` 店铺 ID。本站新结账会再次用服务器目录中的 1990 美分锁定收款金额；浏览器传来的价格不参与计算。此前已生成的 USD 57.77 订单仍按其原始金额验签、发放授权和处理退款。

产品后台操作参考：[Waffo 产品文档](https://docs.waffo.ai/dashboard/products)。

## 2. 填写本站管理后台

部署本次代码后，在本站 `/admin/settings` 的「支付」页找到 **Waffo Pancake**：

| 配置项               | 内容                                                                                      |
| -------------------- | ----------------------------------------------------------------------------------------- |
| 支付环境             | 先选择 Test（`test`）                                                                     |
| 商户 ID              | `MER_...`                                                                                 |
| 店铺 ID              | 上一步的 `STO_...`                                                                        |
| 一次性付款产品 ID    | 上一步的 `PROD_...`                                                                       |
| 商户 RSA 私钥        | Waffo Integration 中 **Test API Key** 的私钥；支持后台直接复制的 Base64 或 PEM            |
| Webhook 公钥         | 店铺 Settings → Webhooks 中的 **Test** 公钥；支持 Base64 或 PEM，它不是商户私钥对应的公钥 |
| 启用 Waffo Pancake   | 开启                                                                                      |
| Basic → 默认支付渠道 | Waffo Pancake                                                                             |

点击保存。私钥使用现有后台的秘密字段掩码；设置 `CONFIG_ENCRYPTION_KEY` 时也会加密存储。凭据只保存在服务端配置中，不需要加入 `VITE_*` 或发送到聊天里。

Waffo 通过商户私钥确定请求的真实环境，所以私钥、Webhook 公钥、产品环境和本站 Test/Production 选择必须对应。[认证说明](https://docs.waffo.ai/api-reference/authentication)。

## 3. 配置 HTTPS 地址和通知

项目 `wrangler.jsonc` 配置的正式域名为 `https://radiantoptimizer.xyz`，对应通知地址：

```text
https://radiantoptimizer.xyz/api/payment/notify/waffo
```

在 Waffo 目标店铺的相同环境中新增 HTTP Webhook，订阅：

- `order.completed`
- `refund.succeeded`

本站 App URL 必须与运行环境的公网 HTTPS 地址一致。本地 `http://localhost:3000` 不能直接用于 Waffo 支付；本地联调需要 HTTPS 隧道及正确的 App URL，或部署到可访问的 HTTPS 测试环境。正式代码已经部署到上述域名，但生产支付配置仍待保存。

Waffo 的收银台要求绝对 HTTPS 成功返回地址。用户完成付款后点击 **Done** 返回本站；浏览器返回本身不会发放许可证，只有已验签支付通知才会。许可证页打开后的前两分钟会每五秒更新一次，也支持手动刷新。[收银台说明](https://docs.waffo.ai/api-reference/endpoints/orders/create-checkout-session)、[Webhook 说明](https://docs.waffo.ai/api-reference/webhooks)。

## 4. 测试一次完整购买

1. 使用 Test 密钥和 Test Webhook 公钥保存配置，点击本站 Waffo 组的「测试」。该按钮只创建测试收银台，不生成本站订单或许可证，且拒绝 Production 模式。
2. 登录一个测试买家账号，从本站 `/pricing` 点击购买，完成 Waffo 测试付款。使用 [Waffo 测试环境文档](https://docs.waffo.ai/features/test-mode) 中的测试方式。
3. 检查本站 `/admin/payments`：订单应为 paid、金额 1990 美分、币种 USD、渠道 waffo。
4. 检查 `/settings/licenses` 或 `/admin/licenses`：该订单应只有一份许可证，可用于客户端激活。
5. 在 Waffo 发起测试退款，确认许可证撤销，客户端不能继续刷新授权。本站当前规则是**任何成功的正额部分退款或全额退款都会撤销该单授权**。

不要使用 Waffo 的固定样本「Send Test Event」判断许可证是否发放：样本没有本站真实订单且金额为零，本站会拒绝其作为付款凭证。必须从本站购买入口测试真实的测试订单。

## 5. 切换正式收款

先在 [Radiant Optimizer 商业资质页面](https://pancake.waffo.ai/merchant/dashboard/STO_7R9wel999McoywmS8GktvG/settings/business-details) 完成 Waffo 要求的正式收款开通和产品发布，再在本站选择 Production（`prod`），填入对应生产私钥及生产 Webhook 公钥，并在 Waffo **Production** 环境单独配置同一路径的通知。确认产品 ID、店铺 ID 和正式站点的许可证签名配置正确。测试配置用于开发验证，不能作为公开正式站点的售卖渠道启用。

本次实现范围是当前 Optimizer 的一次性 USD 产品。订阅、其他币种或其他模板套餐不会通过 Waffo 创建收银台。

## 验证与边界

代码测试使用自动生成的 RSA 密钥、模拟的 Waffo HTTP 响应和独立临时 SQLite 数据库，不会收费或修改商户账户。验签覆盖原始请求正文、重试时间窗口、环境与店铺绑定。授权检查要求实际收款金额 `chargedAmount`，退款要求 `refundedAmount` 和 `originalChargedAmount`；缺少实际金额时不会用商品标价代替。

当前已完成本地配置、商户沙盒收银台付款验证、生产代码部署和安装包公网下载验证；店铺审核、正式支付配置及本站完整购买链路仍待完成。公开客服邮箱为 `support@radiantoptimizer.xyz`。退款期限尚未确定，不应根据自动撤销授权逻辑推断对外退款承诺。
