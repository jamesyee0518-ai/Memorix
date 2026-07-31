# Memorix 用户计费中心与微信支付宝充值开发规划

> 版本：V1.0  
> 日期：2026-07-29  
> 状态：开发规划基线  
> 适用范围：Memorix Web、桌面端、移动 Web、Cloud API、Billing 与 Payment 模块  
> 上位文档：《Memorix AI 算力与 Token 计费模块开发规划 V1.0》  
> 首版支付范围：中国大陆人民币支付，微信支付 Native、支付宝电脑网站支付

---

## 1. 目标与原则

本文规划面向用户的计费中心，以及微信支付、支付宝在线充值所需的页面、接口、数据模型、支付状态机、安全控制、对账和分阶段开发任务。

用户侧统一回答五个问题：

1. 我还有多少可用算力点；
2. 算力点消耗在了哪里；
3. 如何购买算力点；
4. 每笔充值和消费是否可追溯；
5. 当前套餐、模型与业务能力如何计价。

核心原则：

- 计费主视觉使用“算力点（Credits）”，金额使用人民币；
- 产品文案使用“充值 / 购买算力点”，不把算力点描述为可提现现金；
- 服务端是余额、订单、支付结果和账单的唯一权威；
- 客户端不得提交可信金额、赠送额度、价格版本或支付成功状态；
- 支付同步跳转页不作为支付成功依据；
- 充值订单、支付流水、额度发放和消费账本分别建模；
- 充值付费额度与赠送额度分 Bucket 保存，支持不同有效期和退款规则；
- 所有云端计费页面与支付功能不得影响本地模式和 BYOK；
- 第一版先保证订单正确性、幂等、审计和对账，再扩展促销、订阅和多币种。

---

## 2. 当前项目基础与差距

### 2.1 已有能力

当前计费模块已具备：

- Billing Account 与 Workspace Billing Binding；
- Entitlement、价格版本和价格规则；
- Quota Bucket、Reservation、Usage Event、Charge、Provider Cost 与 Account Ledger；
- AI Job 估算、预占、实际用量结算和释放；
- `GET /api/billing/summary?workspaceId=`；
- `GET /api/billing/jobs/{jobId}?workspaceId=`；
- `GET /api/entitlements?workspaceId=`；
- Web `设置 → 使用量` 页面；
- 旧 `/api/usage` 非财务数据兼容展示。

### 2.2 当前缺口

当前尚无：

- 计费中心一级导航及多页面信息架构；
- 充值商品和服务端价格快照；
- 充值订单、支付尝试、支付通知和退款单；
- 微信支付、支付宝适配器；
- 回调验签、主动查单、订单关闭和支付对账；
- 支付成功后的原子额度发放；
- 充值记录、消费明细和月度账单页面；
- 面向用户的模型价格表和费用计算器；
- 发票申请、税务、促销、自动续费和多币种。

结论：

> 现有 Billing 继续负责“额度如何使用”；新增 Payment 子域负责“用户如何付款获得额度”。Payment 不直接改余额，支付确认后通过不可变 Ledger 与 Quota Bucket 发放额度。

---

## 3. 页面信息架构

### 3.1 一级导航

在主导航新增：

```text
计费中心
├── 概览
├── 用量
├── 充值
├── 账单
└── 价格
```

推荐路由：

| 页面 | 路由 | 说明 |
|---|---|---|
| 计费概览 | `/billing` | 余额、套餐、当月消耗、预警和快捷入口 |
| 用量 | `/billing/usage` | 财务级用量趋势、分类和 Job 明细 |
| 充值 | `/billing/recharge` | 充值商品、微信/支付宝支付 |
| 充值订单详情 | `/billing/recharge/orders/{orderId}` | 二维码、支付状态、失败与退款状态 |
| 账单 | `/billing/bills` | 消费、充值、退款、月结单和导出 |
| 账单明细 | `/billing/bills/{billId}` | 单笔 Charge、Ledger 或月结单明细 |
| 价格 | `/billing/pricing` | 套餐、模型单价、计费单位和计算器 |
| 支付结果 | `/billing/payment/result` | 支付渠道返回后的服务端查单页面 |

兼容策略：

- `/settings/usage` 在新页面稳定后重定向至 `/billing/usage`；
- 灰度期保留原入口，显示“新版计费中心”提示；
- 当前 Usage 页中的 Billing Summary 卡片迁移到计费概览；
- 旧 `UserUsageDaily` 只放在“历史参考数据”折叠区，并继续标记 `is_financial_truth=false`。

### 3.2 页面公共框架

所有计费页面共享：

- 当前费用归属账户；
- 当前 Workspace 筛选器；
- 数据更新时间；
- “财务数据 / 参考数据”标识；
- 余额不足和额度即将到期提醒；
- 充值按钮；
- 计费帮助与计价说明入口。

个人账户默认直接进入；存在多个团队费用账户时，只有具备权限的用户显示账户切换器。

---

## 4. 页面模块详细规划

### 4.1 计费概览

页面首屏：

| 模块 | 内容 |
|---|---|
| 可用算力点 | `available = granted - used - reserved`，显示服务端更新时间 |
| 本月使用 | 本月已结算 Credits、较上月变化 |
| 待结算 | 当前预占额度和运行中云任务数 |
| 当前套餐 | 套餐名、周期、下次发放时间或到期时间 |
| 快捷操作 | 充值、查看用量、价格计算、下载账单 |

次级模块：

- 最近 30 天用量趋势；
- 用量最高的模型、业务类型和 Workspace；
- 最近 5 条充值/消费记录；
- 余额阈值、额度即将过期和异常任务提醒；
- 本地、BYOK、Memorix Cloud 三类运行模式说明。

空状态：

- 没有云端用量时说明本地/BYOK 不产生 Memorix 云算力扣费；
- 没有套餐时展示价格页入口；
- 充值功能未开放时隐藏支付按钮，不展示不可用的虚假入口。

### 4.2 用量页面

#### 4.2.1 参考界面落地结构

用量页采用参考界面的“余额与费用 → 筛选 → 汇总指标 → 趋势图 → 明细”信息层级，桌面端结构如下：

```text
用量
数据按账户时区展示，财务聚合最多延迟 5 分钟

┌──────────────────── 可用算力点 ────────────────────┐
│ Credits 主余额 / 付费与赠送额度详情 / 余额告警 / 充值 │
└────────────────────────────────────────────────────┘
┌──────────────────── 本月累计费用 ──────────────────┐
│ 已结算 Credits / 约等价 CNY / 待结算 Credits         │
└────────────────────────────────────────────────────┘

[时间] [Workspace] [来源] [模型] [更多筛选]      [导出]

┌────────── 消耗 Credits ──────────┐
┌────────── 云端请求数 ────────────┐
┌────────── Token 总量 ────────────┐

┌──────────── Credits / 费用趋势（全宽）─────────────┐
│ 维度切换：模型 / 来源 / Workspace                    │
└────────────────────────────────────────────────────┘

┌──────────── 请求数趋势 ──────────┐
┌──────────── Token 趋势 ──────────┐

┌────────────────── 财务级用量明细表 ────────────────┐
└────────────────────────────────────────────────────┘
```

与参考界面的适配规则：

- `Topped-up balance` 改为“可用算力点”，主值显示 Credits；
- 余额卡展开后显示付费充值、套餐、赠送、已预占和即将过期额度；
- `Total cost` 改为“本月累计费用”，主值显示已结算 Credits，人民币为辅助换算；
- 用户页面首版不同时突出 USD 与 CNY；供应商美元成本只在运营后台展示；
- `API Key` 单一筛选扩展为“来源”，选项包括 Web、Desktop、Mobile、Agent 和已脱敏的 API Key；
- API Key 只显示名称和后四位，不向页面返回密钥内容；
- `Cost` 图表默认显示 Credits，可切换为约等价人民币；
- 大额数字使用千分位，Token 可切换紧凑格式与精确值；
- 页面说明明确“财务聚合最多延迟 5 分钟”，原始事件仍以服务端时间为准；
- 数据库存储 UTC，页面默认按 Billing Account 时区展示，并允许切换 UTC；
- 顶部充值按钮只在用户具备账户充值权限且支付开关开启时显示。

#### 4.2.2 视觉规范

- 保留参考界面的大留白、浅色卡片、圆角容器和黑色主操作按钮；
- 卡片、按钮、输入框复用 Memorix 现有设计系统，不单独复制一套样式；
- 趋势主色使用产品蓝；请求数使用蓝色面积图，Token 使用浅蓝柱状图；
- 图表网格线弱化，Y 轴从 0 开始，异常峰值提供 Tooltip 精确值；
- Credits/费用图支持按模型、来源和 Workspace 切换，不在同一视图堆叠过多维度；
- 筛选条件同步到 URL，刷新、分享和返回页面时可恢复；
- 图表与表格使用同一筛选范围，导出也必须携带同一组服务端筛选条件；
- 无数据、加载、部分失败、无权限和非财务参考数据使用不同状态；
- 颜色不能作为状态的唯一表达，同时显示文字、图标或线型；
- 桌面端顶部两张大卡并排、三张指标卡并排、底部两张图并排；
- 窄屏按“余额 → 费用 → 筛选 → 指标 → 图表 → 明细”顺序单列堆叠；
- 移动端筛选收进抽屉，充值和导出保留明确文字标签。

筛选条件：

- 时间：7 天、30 天、本月、上月、自定义；
- Workspace；
- 来源：Web、Desktop、Mobile、Agent、API Key；
- 用户/成员；
- 模型；
- 业务类型；
- 运行模式；
- 结算状态；
- Job ID。

图表与汇总：

- Credits 日趋势；
- 输入、输出、缓存、推理和 Embedding Token；
- 按模型、业务类型、Workspace、成员的用量占比；
- 预估费用与实际费用偏差；
- 已结算、待结算、已冲正；
- 本地/BYOK 用量单独展示，不计入 Memorix 财务金额。

明细表：

| 字段 | 说明 |
|---|---|
| 时间 | Job 创建/结算时间 |
| 业务 | 问答、摘要、Embedding、报告等 |
| 模型 | 逻辑模型名与实际供应商模型 |
| Token | 输入、输出、缓存、推理等 |
| Credits | 实际扣减 |
| 金额 | 按价格快照计算的等价金额 |
| 状态 | 运行中、已结算、失败、取消、已冲正 |
| 费用归属 | Billing Account / Workspace |
| 操作 | 查看 Job 计量和扣费解释 |

用量页面必须支持 CSV 导出。大数据量导出走异步任务，不在浏览器内一次性拼装。

### 4.3 充值页面

页面分为四个区域：

1. 当前可用算力点和账户；
2. 充值商品；
3. 支付方式；
4. 充值说明和常见问题。

充值商品建议第一版只允许后台配置的固定档位：

```text
商品显示：支付 ¥X → 获得 Y Credits
可选显示：另赠 Z Credits（有效期至某日）
```

第一版不开放任意金额输入。客户端只提交 `rechargeProductId` 和支付渠道；金额、付费 Credits、赠送 Credits、活动和有效期全部由服务端读取并生成快照。

支付方式：

| 客户端 | 微信支付 | 支付宝 |
|---|---|---|
| PC Web | Native 二维码 | 电脑网站支付 |
| 桌面端 | Native 二维码 | 系统浏览器打开电脑网站支付 |
| 手机外部浏览器 | Phase 2：H5 支付 | Phase 2：手机网站支付 |
| 微信内网页 | Phase 3：JSAPI，需要 OpenID | 不在首版 |
| 原生移动 App | Phase 3：APP 支付 | Phase 3：APP 支付 |

微信 Native 交互：

- 创建订单后在弹窗展示二维码；
- 显示应付金额、算力点、订单号后 8 位和剩余时间；
- 客户端每 2～3 秒查询 Memorix 服务端订单状态；
- 页面失焦后降低轮询频率，重新激活立即查单；
- 支付成功后关闭二维码，刷新服务端余额；
- 二维码过期后提示重新创建订单，不复用旧订单号；
- 用户关闭弹窗不等于关闭支付订单。

支付宝电脑网站支付交互：

- 服务端创建订单并返回受控支付跳转信息；
- Web 跳转支付宝收银台；
- 桌面端通过系统浏览器打开，避免在 WebView 内处理支付站点；
- 返回 `/billing/payment/result?orderId=...` 后向 Memorix 服务端查单；
- 返回页显示“正在确认”，不得仅因 `return_url` 参数展示充值成功。

通用支付状态：

```text
CREATED
  └── PAYING
        ├── PAID
        ├── CLOSED
        └── FAILED

PAID
  └── REFUNDING
        ├── PARTIALLY_REFUNDED
        └── REFUNDED
```

页面文案不得使用“付款失败，请再次点击”诱导重复支付；状态不确定时先查原订单。

### 4.4 账单页面

使用四个标签页：

| 标签 | 内容 |
|---|---|
| 消费明细 | AI Charge、冲正和结算状态 |
| 充值记录 | 订单金额、支付渠道、获得 Credits、状态 |
| 退款记录 | 原订单、退款金额、退回 Credits、渠道状态 |
| 月度账单 | 月初/月末余额、发放、消费、退款、调整和汇总 |

公共筛选：

- 账户；
- Workspace；
- 时间；
- 业务类型；
- 交易类型；
- 状态；
- 订单号/Job ID。

单笔充值详情展示：

- Memorix 订单号；
- 支付渠道；
- 渠道交易号脱敏值；
- 下单、支付和入账时间；
- 支付金额和币种；
- 付费 Credits 与赠送 Credits；
- 订单状态和退款状态；
- 发放对应的 Ledger 引用；
- 客服排查所需 Trace ID。

不向普通用户暴露内部 Provider Cost、供应商密钥、原始回调报文或内部重试标签。

### 4.5 价格页面

价格页由三部分组成：

1. 套餐对比；
2. 模型与业务单价；
3. 费用计算器。

模型价格表至少包含：

- 模型名称；
- 输入 Token 单价；
- 输出 Token 单价；
- 缓存读写或推理 Token 单价；
- Embedding 单价；
- 计费单位；
- 对应 Credits；
- 生效时间；
- 是否含税及适用区域说明。

费用计算器输入：

- 模型；
- 预计输入/输出 Token；
- 请求次数；
- 可选缓存、推理或 Embedding 数量。

输出：

- 预计 Credits；
- 约等价人民币金额；
- 估算依据和价格版本；
- “实际费用以服务端记录的真实用量为准”。

历史 Job 和账单永远显示当时的价格快照，不随当前价格页变化。

---

## 5. 用户流程

### 5.1 充值主流程

```text
选择充值商品
  → 选择支付方式
  → Cloud API 创建 Memorix 充值订单
  → Payment Adapter 向渠道下单
  → 用户在微信/支付宝完成付款
  → 渠道异步通知 Memorix
  → 服务端验签、核对订单与金额
  → 原子更新订单 + 发放 Credits + 写 Ledger/Outbox
  → 客户端查到 PAID
  → 刷新余额和充值记录
```

### 5.2 回调未及时到达

```text
用户完成支付并返回
  → 页面查询 Memorix 订单
  → 订单仍为 PAYING
  → 服务端主动查询支付渠道
  → 渠道确认成功
  → 复用同一入账事务完成额度发放
```

回调处理和主动查单必须调用同一个 `ConfirmPaymentAsync` 领域用例，防止两条路径重复发放。

### 5.3 断网与中断

- 创建订单前断网：不创建本地待支付订单；
- 已展示二维码后断网：恢复网络后查询原订单；
- 支付宝跳转后应用被关闭：用户重新进入充值记录即可查询；
- 客户端缓存只用于展示，不能据此增加余额；
- 订单状态不确定时显示“确认中”，不显示成功或失败；
- 支付渠道下单超时后，先按商户订单号查询，再决定是否重试。

---

## 6. 权限模型

| 能力 | 个人账户 Owner | 团队 Owner / Billing Admin | Workspace Admin | Member |
|---|---:|---:|---:|---:|
| 查看账户余额 | 是 | 是 | 可配置 | 可配置 |
| 查看全部用量 | 是 | 是 | 本 Workspace | 仅本人/可配置 |
| 查看价格 | 是 | 是 | 是 | 是 |
| 发起充值 | 是 | 是 | 否 | 否 |
| 查看充值与退款 | 是 | 是 | 否 | 否 |
| 下载月度账单 | 是 | 是 | 否 | 否 |
| 申请退款 | 是 | 是 | 否 | 否 |
| 配置预算告警 | 是 | 是 | 本 Workspace | 否 |

第一版团队 Billing Admin 尚未实现时，团队财务操作只授权给 Owner；不得临时放宽为“所有 Workspace 成员”。

---

## 7. 支付服务架构

```text
Web / Desktop / Mobile Web
          │
          ▼
      Memorix Cloud API
          │
          ▼
   Payment Orchestrator
      ├── WeChat Pay Adapter
      └── Alipay Adapter
          │
          ▼
     支付渠道 API / 回调

支付确认
  → RechargeOrder
  → QuotaBucket（TOP_UP / PROMO）
  → AccountLedger
  → Outbox
  → Billing Summary 缓存失效
```

边界要求：

- 支付渠道密钥、证书、APIv3 Key 和商户号只存在于 Cloud 服务端安全配置；
- Web、Desktop 和 Mobile 不直接调用支付渠道下单 API；
- Payment Adapter 只处理渠道协议；业务金额、Credits 和发放规则由 Payment Orchestrator 控制；
- 支付服务不得直接修改现有可变余额字段；
- 支付确认与额度发放在单个 PostgreSQL 本地事务完成；
- 与支付渠道之间采用状态机、回调、查单和对账实现最终一致性，不使用跨渠道分布式事务。

---

## 8. 数据模型

### 8.1 `recharge_product`

| 字段 | 说明 |
|---|---|
| `id` | 商品 ID |
| `code` | 稳定业务编码 |
| `display_name` | 展示名 |
| `currency` | 首版固定 CNY |
| `amount_minor` | 支付金额，人民币分 |
| `paid_credits` | 付费算力点 |
| `bonus_credits` | 赠送算力点 |
| `bonus_expires_in_days` | 赠送额度有效期 |
| `status` | DRAFT / ACTIVE / INACTIVE |
| `effective_from/to` | 有效期 |
| `sort_order` | 展示顺序 |
| `version` | 乐观锁 |

### 8.2 `recharge_order`

| 字段 | 说明 |
|---|---|
| `id` | 内部 ID |
| `order_no` | 全局唯一商户订单号 |
| `billing_account_id` | 充值归属账户 |
| `initiated_by_user_id` | 发起用户 |
| `recharge_product_id` | 商品引用 |
| `channel` | WECHAT / ALIPAY |
| `channel_scene` | NATIVE / PAGE / H5 / WAP / JSAPI / APP |
| `currency` | 币种 |
| `amount_minor` | 服务端快照金额 |
| `paid_credits` | 付费 Credits 快照 |
| `bonus_credits` | 赠送 Credits 快照 |
| `pricing_snapshot_json` | 商品、活动和有效期快照 |
| `status` | 业务订单状态 |
| `expires_at` | 支付有效期 |
| `paid_at` | 渠道确认时间 |
| `fulfilled_at` | Credits 入账时间 |
| `closed_at` | 关闭时间 |
| `idempotency_key` | 创建订单幂等键 |
| `created_at/updated_at` | 审计时间 |

唯一约束：

- `order_no`；
- `(billing_account_id, idempotency_key)`；
- 一个订单最多存在一个成功发放结果。

### 8.3 `payment_attempt`

保存一次具体渠道下单：

- `recharge_order_id`；
- 渠道、场景；
- 渠道交易号；
- `code_url` 或跳转令牌的加密/短期引用；
- 渠道状态；
- 请求 ID、错误码和错误分类；
- 创建、过期和最后查询时间。

不得长期保存完整支付跳转表单、用户支付凭据或二维码内容。短期数据应设置 TTL。

### 8.4 `payment_notification`

用于回调幂等和审计：

- 渠道通知 ID 或稳定去重键；
- 商户订单号；
- 渠道交易号；
- 通知类型；
- 签名校验结果；
- 请求时间戳和随机串；
- 原始请求体哈希；
- 最小化后的业务字段；
- 处理结果和失败原因；
- 接收与处理时间。

原始回调中的个人标识不得无期限明文保存。

### 8.5 `payment_refund`

- `refund_no`；
- 原充值订单；
- 申请人和审批人；
- 退款金额；
- 需回收的付费/赠送 Credits；
- 渠道退款单号；
- 状态；
- 原因码；
- 申请、受理、成功和失败时间；
- 幂等键。

### 8.6 额度与账本映射

支付成功后：

1. 付费 Credits 创建 `QuotaBucket.Source=TOP_UP`；
2. 赠送 Credits 单独创建 `QuotaBucket.Source=PROMO`；
3. 付费与赠送分别写不可变 Ledger；
4. Ledger 引用 `recharge_order_id`；
5. 写 `payment_confirmed` 与 `credits_granted` Outbox；
6. 更新订单 `fulfilled_at`。

推荐默认：

- 付费 Credits 不设置短期过期时间；
- 赠送 Credits 可按活动规则过期；
- 先到期赠送额度优先消耗；
- 退款必须根据未使用的付费额度和产品规则计算；
- 算力点不可提现，退款原路返回。

具体有效期和退款规则上线前须经产品、运营、财务与法务确认。

---

## 9. API 规划

### 9.1 用户计费 API

```text
GET  /api/billing/overview?billingAccountId=&workspaceId=
GET  /api/billing/usage?from=&to=&workspaceId=&model=&cursor=
GET  /api/billing/charges?from=&to=&workspaceId=&cursor=
GET  /api/billing/ledger?from=&to=&type=&cursor=
GET  /api/billing/pricing?region=CN&currency=CNY
GET  /api/billing/statements?year=
GET  /api/billing/statements/{statementId}
POST /api/billing/exports
GET  /api/billing/exports/{exportId}
```

所有财务响应包含：

- `is_financial_truth=true`；
- `currency`；
- `as_of`；
- 价格快照/版本引用；
- 游标分页信息。

### 9.2 充值 API

```text
GET  /api/billing/recharge-products
POST /api/billing/recharge-orders
GET  /api/billing/recharge-orders?cursor=
GET  /api/billing/recharge-orders/{orderId}
POST /api/billing/recharge-orders/{orderId}/close
POST /api/billing/recharge-orders/{orderId}/refresh
```

创建订单请求：

```json
{
  "billing_account_id": "uuid",
  "recharge_product_id": "uuid",
  "payment_channel": "WECHAT",
  "payment_scene": "NATIVE",
  "idempotency_key": "client-generated-uuid"
}
```

请求中不接受金额和 Credits。服务端响应只返回展示支付所必需的短期信息。

### 9.3 支付渠道回调

```text
POST /api/payments/wechat/notify
POST /api/payments/alipay/notify
POST /api/payments/wechat/refund-notify
POST /api/payments/alipay/refund-notify
```

回调接口不使用用户登录令牌，但必须执行渠道签名校验、时间窗口校验、订单核对、金额核对、商户身份核对和幂等处理。

### 9.4 退款管理 API

```text
POST /api/billing/recharge-orders/{orderId}/refund-requests
GET  /api/billing/refunds/{refundId}
POST /api/internal/billing/refunds/{refundId}/approve
POST /api/internal/billing/refunds/{refundId}/reject
```

首版可以只开放客服后台发起退款，用户端展示退款进度。

---

## 10. 支付渠道接入策略

### 10.1 微信支付

首版使用 Native 支付：

- Cloud API 调用 `/v3/pay/transactions/native`；
- 使用唯一 `out_trade_no`；
- 订单金额使用整数分并固定 `CNY`；
- 设置公网 HTTPS `notify_url`；
- 将 `code_url` 短期返回前端生成二维码；
- 未收到回调时通过订单查询 API 主动确认；
- 超过业务有效期后查单，再关闭未支付订单；
- 使用交易账单与资金账单执行日对账。

支付回调：

- 使用 `Wechatpay-Timestamp`、`Wechatpay-Nonce`、请求体和 `Wechatpay-Signature` 验签；
- 根据 `Wechatpay-Serial` 选择微信支付公钥/平台证书；
- 使用 APIv3 Key 解密通知资源；
- 核对 `mchid`、`appid`、`out_trade_no`、币种和金额；
- 验签失败不得更新订单；
- 成功处理后按官方协议返回确认响应。

移动端后续：

- 手机外部浏览器使用 H5 支付；
- 微信内网页使用 JSAPI，并增加 OpenID 获取和域名配置；
- 原生 App 使用 APP 支付；
- H5 支付不用于 App 内 WebView。

### 10.2 支付宝

首版使用电脑网站支付：

- 使用 `alipay.trade.page.pay`；
- 服务端生成受签名保护的支付请求；
- `out_trade_no` 使用 Memorix 订单号；
- 设置 `notify_url` 和受控 `return_url`；
- 未收到异步通知时调用 `alipay.trade.query`；
- 订单取消使用 `alipay.trade.close`；
- 退款使用 `alipay.trade.refund`；
- 通过对账单下载能力执行日对账。

支付回调：

- 使用支付宝公钥/证书执行 RSA2 验签；
- 核对 `app_id`、卖家身份、`out_trade_no`、`trade_no`、`total_amount` 和交易状态；
- 只接受符合业务规则的成功状态；
- 重复通知只返回成功确认，不重复发放 Credits；
- `return_url` 只用于用户体验，不触发入账。

移动端后续使用 `alipay.trade.wap.pay`。桌面端首版从系统浏览器打开支付页面。

### 10.3 适配器接口

```text
IPaymentProvider
├── CreatePaymentAsync
├── QueryPaymentAsync
├── ClosePaymentAsync
├── VerifyAndParseNotificationAsync
├── CreateRefundAsync
├── QueryRefundAsync
└── DownloadReconciliationBillAsync
```

业务层只使用统一状态，不直接依赖渠道状态字符串。

---

## 11. 安全与一致性

### 11.1 服务端权威

- 客户端余额缓存只用于 UI；
- 断网时不能创建可自动执行的云充值请求；
- 客户端不能指定价格、赠送规则、到账状态或 Ledger；
- 支付完成必须由服务端回调或服务端主动查单确认；
- 所有订单查询先校验用户对 Billing Account 的权限；
- 充值必须落到 Billing Account，不能仅凭 Workspace ID 决定归属。

### 11.2 幂等

至少设置：

| 场景 | 幂等键 |
|---|---|
| 创建充值订单 | `billing_account_id + client_idempotency_key` |
| 渠道下单 | `order_no + channel + attempt_no` |
| 支付通知 | 渠道通知 ID 或签名业务去重键 |
| 支付确认 | `channel + provider_trade_no` |
| Credits 发放 | `recharge_order_id + grant_type` |
| 退款申请 | `recharge_order_id + client_idempotency_key` |
| 退款入账 | `refund_no + settlement_action` |

数据库必须用唯一约束兜底，不只依赖 Redis 锁。

### 11.3 金额与快照

- 数据库存储 `amount_minor BIGINT` 和 ISO 4217 币种；
- 微信金额单位为分；
- 支付宝适配器负责金额字符串与内部分值的精确转换；
- 禁止使用浮点数处理支付金额；
- 订单保存商品和活动快照；
- 回调金额与订单快照不一致时进入人工复核，不自动入账；
- 历史账单不读取当前商品配置重新计算。

### 11.4 密钥与日志

- 商户私钥、APIv3 Key 和证书从密钥管理服务或受保护环境变量加载；
- 支持证书/公钥轮换和序列号匹配；
- 日志不打印私钥、完整支付表单、解密后的个人标识和二维码内容；
- 保存请求 ID、订单号、状态、错误码和哈希用于排查；
- 回调接口限流，但不得因通用限流误拦正常渠道重试；
- 支付跳转域名使用严格 Allowlist；
- Web 配置 CSP，禁止任意脚本拼接支付表单。

### 11.5 迟到与乱序

- 已关闭订单收到支付成功：查渠道确认后允许进入“迟到支付”处理，不得吞款；
- 回调和主动查单并发：数据库条件更新保证只入账一次；
- 退款回调先于本地查询：允许直接推进退款状态；
- PAID 不可被迟到的 PAYING/CLOSED 覆盖；
- REFUNDED 不可回退为 PAID；
- 所有异常状态进入可检索的运营队列。

---

## 12. 对账与运营

### 12.1 实时补偿

- 支付页等待超过阈值时触发服务端查单；
- PAYING 订单由后台任务周期查单；
- 到期订单先查单，确认未支付后关单；
- 已支付但未 `fulfilled_at` 的订单自动重放入账事务；
- 回调处理失败进入重试队列和死信告警。

### 12.2 日对账

每日执行：

1. 下载微信/支付宝交易账单；
2. 计算并校验文件哈希；
3. 匹配渠道交易号、商户订单号、金额和状态；
4. 查找“渠道成功、本地未支付”；
5. 查找“本地已支付、渠道无记录”；
6. 查找金额、退款和手续费差异；
7. 自动修复可确定项；
8. 生成需要人工处理的差异单。

### 12.3 监控指标

- 充值页到下单转化率；
- 下单到支付成功率；
- 各渠道/场景错误率；
- 回调验签失败率；
- 回调 P95 延迟；
- 支付成功到 Credits 入账 P95；
- 重复回调数量；
- 主动查单确认占比；
- 支付成功未入账数量；
- 日对账差异笔数与金额；
- 退款成功率和平均时长。

建议告警：

```text
支付成功未入账 > 0：高优先级
回调验签失败突增：高优先级
单渠道连续失败：高优先级
日对账差异金额 > 阈值：高优先级
PAYING 超时积压 > 阈值：中优先级
```

---

## 13. 前后端代码落点

### 13.1 Web

建议新增：

```text
web/src/app/(main)/billing/layout.tsx
web/src/app/(main)/billing/page.tsx
web/src/app/(main)/billing/usage/page.tsx
web/src/app/(main)/billing/recharge/page.tsx
web/src/app/(main)/billing/recharge/orders/[orderId]/page.tsx
web/src/app/(main)/billing/bills/page.tsx
web/src/app/(main)/billing/pricing/page.tsx
web/src/app/(main)/billing/payment/result/page.tsx
web/src/components/billing/*
```

修改：

- `web/src/app/(main)/layout.tsx`：新增“计费中心”主导航；
- `web/src/app/(main)/settings/usage/page.tsx`：兼容跳转；
- `web/src/lib/api.ts`：新增 Billing/Recharge API；
- `web/src/lib/types.ts`：新增页面 DTO；
- Query Key 必须包含 Billing Account、Workspace 和筛选条件；
- 支付成功后使 overview、orders、ledger、bills 查询失效并重新获取。

### 13.2 Backend

建议新增：

```text
Domain
  RechargeProduct
  RechargeOrder
  PaymentAttempt
  PaymentNotification
  PaymentRefund
  BillingStatement

Application
  IPaymentService
  IPaymentProvider
  IRechargeOrderService
  IReconciliationService
  Payment DTO / Commands

Infrastructure
  PaymentService
  WeChatPayProvider
  AlipayProvider
  PaymentRecoveryWorker
  PaymentReconciliationWorker

API
  RechargeController
  PaymentNotificationController
  BillingStatementsController
  InternalRefundsController
```

保留现有 `AiBillingService` 职责，不把微信/支付宝协议写入该服务。

---

## 14. 分阶段实施

### Phase U0：规则、资质与设计冻结

- 确认充值商品、付费/赠送 Credits、有效期和退款规则；
- 确认微信/支付宝商户主体与签约产品；
- 准备备案域名、公网 HTTPS 回调地址和密钥管理；
- 确认 Billing Account 权限；
- 完成页面原型、状态文案和错误码；
- 保持真实支付开关关闭。

退出条件：

- 产品、财务、运营、安全和法务确认首版规则；
- 支付渠道资质和测试环境可用；
- 回调域名和证书轮换方案明确。

### Phase U1：计费中心只读页面

- 新增计费中心导航和概览；
- 迁移用量页面；
- 实现账单只读列表；
- 实现价格页和计算器；
- 接入当前 Billing Summary、Charge、Ledger 和价格数据；
- 保留旧 Usage 参考区。

退出条件：

- 财务数据与旧统计明确区分；
- Workspace 和账户权限测试通过；
- 不开放充值按钮。

### Phase U2：充值订单与模拟支付

- 实现 Recharge Product、Order、Attempt、Notification 和 Refund Schema；
- 实现创建、查询、过期和关闭订单；
- 实现统一支付适配器接口；
- 使用 Fake Provider 验证回调、查单、重复通知和原子入账；
- 完成充值页、订单详情和支付结果页；
- 完成后台补偿任务。

退出条件：

- 并发回调/查单不重复发放；
- 异常状态可恢复；
- 所有订单可审计。

### Phase U3：微信 Native 与支付宝电脑网站支付

- 接入微信 APIv3 Native；
- 接入支付宝电脑网站支付；
- 完成回调验签、查单、关单、退款和日对账；
- 桌面端支付宝使用系统浏览器；
- 内部账户小额实付灰度；
- 完成渠道故障开关。

退出条件：

- 真实支付、退款和账单对账闭环；
- 支付成功到 Credits 入账符合目标；
- 安全评审、渠道验收和运营 SOP 通过。

### Phase U4：生产灰度

- 员工账户；
- 指定个人账户；
- 指定团队 Owner；
- 5% 用户；
- 25% 用户；
- 全量。

任一阶段出现未入账、重复入账或对账差异扩大，立即关闭新订单创建；保留查单、回调、退款和对账处理。

### Phase U5：移动与商业扩展

- 微信 H5 / JSAPI / APP；
- 支付宝 WAP / APP；
- 自动续费；
- 发票与税务；
- 优惠券和活动；
- 企业信用额度；
- 多币种和汇率；
- 聚合支付或其他支付方式。

---

## 15. Feature Flags

```text
billing.center.enabled
billing.new_usage_page.enabled
billing.bills.enabled
billing.pricing.enabled
payment.recharge.enabled
payment.fake_provider.enabled
payment.wechat.native.enabled
payment.wechat.h5.enabled
payment.wechat.jsapi.enabled
payment.alipay.page.enabled
payment.alipay.wap.enabled
payment.refund.enabled
payment.reconciliation.enabled
```

开关要求：

- 关闭新支付不影响回调、查单、退款和对账；
- 渠道开关可独立关闭；
- 不得通过前端隐藏代替服务端开关；
- Shadow Pricing 关闭并不自动开启真实支付；
- AI Gateway 未完成财务级用量闭环前，即使允许充值，也不得对不完整用量进行现金级扣费。

---

## 16. 测试与验收

### 16.1 核心测试

- 固定商品正常下单；
- 篡改金额、Credits、账户和商品；
- 同一幂等键重复创建；
- 微信二维码过期和重新下单；
- 支付宝返回早于异步通知；
- 回调早于客户端返回；
- 回调重复、乱序和并发；
- 回调验签失败；
- 商户号、App ID、卖家、币种或金额不一致；
- 渠道下单超时但实际创建成功；
- 主动查单和回调同时确认；
- 已支付未入账恢复；
- 迟到支付；
- 订单关闭；
- 全额/部分退款；
- 退款回调重复；
- 支付成功后额度和 Ledger 一致；
- 付费与赠送 Bucket 分离；
- 跨账户和跨 Workspace 越权；
- 支付渠道停用；
- 断网恢复；
- 日对账差异发现和修复。

### 16.2 UI 验收

- PC Web、桌面端常用分辨率正常；
- 二维码不会被缩放到无法扫描；
- 金额、Credits、有效期和退款规则下单前可见；
- 所有等待状态有明确反馈；
- 不确定状态不误报成功/失败；
- 页面刷新后能恢复原订单；
- 支付成功后余额、充值记录和 Ledger 同步刷新；
- 本地匿名用户不显示云充值入口；
- 本地/BYOK 不因余额不足被禁用。

### 16.3 性能目标

```text
计费概览 API P95 < 300ms
创建充值订单 P95 < 1s（不含渠道故障）
支付状态查询 P95 < 300ms
支付回调处理 P95 < 500ms
支付确认到 Credits 入账 P95 < 3s
正常支付对账最终一致性 < 24h
```

---

## 17. 产品待确认项与工程默认值

| 待确认项 | 工程默认值 |
|---|---|
| 首发币种 | 仅 CNY |
| 充值金额 | 仅后台固定档位，不开放任意金额 |
| 充值归属 | Billing Account，不直接归属单一 Workspace |
| 首发渠道 | 微信 Native、支付宝电脑网站支付 |
| 订单有效期 | 15 分钟，渠道能力不足时由服务端业务超时控制 |
| 付费额度有效期 | 默认不设置短期过期 |
| 赠送额度有效期 | 按商品快照，可过期 |
| 扣减顺序 | 先到期赠送 → 周期套餐 → 付费充值 → 企业信用 |
| 支付成功依据 | 服务端验签回调或主动查单 |
| 退款路径 | 原支付渠道 |
| 退款入口 | 首版用户提交申请、客服审核处理 |
| 提现 | 不支持 |
| 发票 | 首版只预留入口和数据结构，不承诺自动开票 |
| 团队充值权限 | Owner；Billing Admin 实现后再开放 |
| 移动支付 | 放在 PC Web/桌面端稳定后的 Phase U5 |

上线前仍须明确：

1. 具体充值档位和 Credits 汇率；
2. 是否有充值赠送及其到期时间；
3. 已消费付费 Credits 的退款规则；
4. 部分退款时付费与赠送额度如何回收；
5. 发票税目、含税口径和开票主体；
6. 未成年人、企业采购和消费者权益要求；
7. 微信/支付宝签约主体、费率和结算账户；
8. Billing Admin 的授权与审计流程；
9. 客服处理迟到支付、重复支付和未入账的 SOP；
10. 移动端是否优先 H5/WAP，还是直接 APP 支付。

---

## 18. 官方接入依据

实施时以支付渠道实时官方文档为准：

- [微信支付 Native 下单](https://pay.wechatpay.cn/doc/v3/merchant/4012791877)
- [微信支付 Native 开发指引](https://pay.wechatpay.cn/doc/v3/merchant/4012791891)
- [微信支付成功回调通知](https://pay.wechatpay.cn/doc/v3/merchant/4012791861)
- [微信支付 H5 产品介绍](https://pay.wechatpay.cn/doc/v3/merchant/4012791832)
- [微信支付 H5 下单](https://pay.wechatpay.cn/doc/v3/merchant/4012791834)
- [微信支付订单退款指引](https://pay.wechatpay.cn/doc/v3/merchant/4013071031)
- [微信支付下载账单指引](https://pay.wechatpay.cn/doc/v3/merchant/4013071218)
- [支付宝网页/移动应用接入](https://open.alipay.com/module/webApp)
- [支付宝开发者工具与 SDK](https://open.alipay.com/tool)
- [支付宝官方多语言 Easy SDK](https://github.com/alipay/alipay-easysdk)
- [支付宝帮助中心：签名、验签、沙箱和电脑网站支付](https://open.alipay.com/support/supportCenter.htm)

---

## 19. 最终落地结论

Memorix 用户计费中心采用“概览、用量、充值、账单、价格”五个主页面。当前 `/settings/usage` 迁移为财务级用量页，现有 Billing Account、Quota Bucket、Charge 和 Ledger 继续作为消费侧真值。

充值侧新增独立 Payment 子域。首版以人民币固定充值商品为主，PC Web 和桌面端分别接入微信 Native 二维码与支付宝电脑网站支付。支付确认必须经过服务端验签回调或主动查单，并在单个数据库事务中完成订单确认、付费/赠送额度分 Bucket 发放、不可变 Ledger 和 Outbox 写入。

上线顺序为：

```text
只读计费中心
  → 模拟支付与幂等入账
  → 微信/支付宝小额实付
  → 日对账与退款闭环
  → 生产灰度
  → 移动支付、发票、订阅与促销
```

在真实支付、Gateway 用量闭环、退款和对账全部通过验收之前，不得开启现金级自动扣费。

---

## 20. 首版实施记录（2026-07-29）

### 20.1 已完成

| 范围 | 实施结果 |
|---|---|
| 计费中心 | 已实现 `/billing`、`/billing/usage`、`/billing/recharge`、`/billing/bills`、`/billing/pricing` |
| 旧入口迁移 | `/settings/usage` 已重定向到财务级用量页 |
| 用量体验 | 已实现余额/消费卡片、日期范围、Credits/请求/Token 趋势、明细和 CSV 导出 |
| 充值商品 | 已实现固定档位、服务端价格快照和商品同步；金额与 Credits 不接受客户端传入 |
| 支付订单 | 已实现幂等创建、查询、主动查单、过期关单和后台恢复 |
| 微信支付 | 已实现 APIv3 Native 下单、二维码、查单、关单、RSA 验签、AES-GCM 解密和商户/金额校验 |
| 支付宝 | 已实现电脑网站支付、RSA2 签名、查单、关单、响应/通知验签和商户/金额校验 |
| 入账 | 已在串行化事务中完成订单确认、付费/赠送 Bucket 分离发放和不可变 Ledger |
| 安全开关 | `Payment:Enabled` 默认 `false`；关闭新订单不影响已启用渠道的回调与查单处理 |
| 自动化验证 | 服务端编译、前端生产构建、计费回归测试、支付幂等与重复确认测试已通过 |
| UI 验收 | 已在 1280px Web 视口检查用量页、充值页和微信二维码支付弹层 |

### 20.2 当前工程默认商品

以下档位仅作为联调和首版 UI 默认值，生产启用前必须由产品、财务和运营书面确认：

| 商品 | 金额 | 付费 Credits | 赠送 Credits | 赠送有效期 |
|---|---:|---:|---:|---:|
| 体验包 | ¥10 | 10,000 | 0 | — |
| 标准包 | ¥50 | 50,000 | 2,000 | 90 天 |
| 进阶包 | ¥100 | 100,000 | 8,000 | 90 天 |
| 团队包 | ¥300 | 300,000 | 30,000 | 90 天 |

### 20.3 真实支付启用前仍需完成

1. 确认充值档位、Credits 汇率、赠送规则和退款政策；
2. 通过密钥管理系统注入微信商户私钥、APIv3 Key、微信支付公钥和支付宝密钥，不把生产密钥写入配置文件；
3. 配置公网 HTTPS 回调地址和支付宝返回页；
4. 完成微信/支付宝沙箱或小额实付、并发回调和迟到支付验证；
5. 实现并验收退款执行、退款额度回收、渠道账单下载和日对账任务；
6. 完成客服 SOP、安全评审、财务验收和分阶段灰度。

在以上事项完成前，保持：

```text
Payment:Enabled = false
Payment:WeChat:Enabled = false
Payment:Alipay:Enabled = false
```
