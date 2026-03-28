# FortiAnswer — Demo Presentation Outline
# 供 ChatGPT 润色用，语言可中英双语
# Team 6 | Capstone Project | 2026

---

## Slide 1 — 封面
- 标题：FortiAnswer — AI-Powered Security Incident Response Assistant
- 副标题：Team 6 | Capstone Project | 2026
- 成员名单：Doris / Li / Ayo / Keto

---

## Slide 2 — 问题背景（非技术）
- 企业 IT 安全事件越来越频繁（钓鱼邮件、账户入侵、VPN 故障…）
- 员工遇到安全问题时：
  - 不知道该怎么办
  - 发邮件等 Agent 回复，响应慢
  - Agent 手动整理事件信息，效率低
- 核心痛点：**信息分散、响应慢、人工成本高**

---

## Slide 3 — 我们的解决方案（非技术）
- FortiAnswer = 一个 AI 安全助手 + 工单管理系统
- 员工可以直接用聊天界面提问，AI 秒回
- 如果问题严重，系统自动引导收集事件信息并创建工单
- Agent 在 Dashboard 查看和处理所有工单
- Admin 管理知识库文档，查看 AI 回答质量

一句话：**让员工自助解决 80% 的问题，剩下 20% 快速交给 Agent**

---

## Slide 4 — 三类用户 & 使用场景（非技术）

| 角色 | 他们能做什么 |
|------|-------------|
| 员工（Customer） | 聊天提问、查看自己的工单 |
| Agent | 查看所有工单、接单、改状态 |
| Admin | 管理知识库、查看 AI 满意率、处理低分回答 |

配图建议：三个角色的简单流程图

---

## Slide 5 — 用户旅程演示（非技术，Story-based）

**场景：Alice 收到一封可疑邮件**

1. Alice 打开 FortiAnswer，输入："I think I received a phishing email"
2. AI 立即回答处理建议，并引导 Alice 填写事件细节
   （是哪个账户？什么时候？有没有点链接？）
3. 信息收集完毕，系统自动创建 P1 工单
4. Agent John 在 Dashboard 看到工单，点击 "Assign to me"，开始处理
5. Alice 可以随时查看工单状态（Open → InProgress → Closed）
6. Alice 对 AI 回答点 👍，满意率被记录

**结果：从发现问题到工单创建 < 2 分钟**

---

## Slide 6 — 核心功能一览（非技术）

- 💬 **智能问答**：基于公司内部知识库，按角色权限返回内容
- 🎯 **引导式事件采集**：AI 自动问问题，结构化收集事件信息
- 🎫 **工单自动创建**：严重事件自动升级，无需人工干预
- 📊 **Agent Dashboard**：统一工单视图，支持筛选和分配
- 👍👎 **回答质量追踪**：用户评分 + Admin 可查低分回答
- 📚 **知识库管理**：Admin 直接上传/删除文档，自动 ingestion

---

## Slide 7 — 技术架构（Technical）

架构图（建议配图）：

```
用户 / Browser
    ↓
React Web UI (Li)
    ↓  x-api-key
Azure Functions v4 (.NET 8, isolated worker)
    ↓              ↓              ↓
Azure AI Search   Groq LLM      Azure Table Storage
(知识库检索)    (llama-3.3-70b)  (用户/工单/对话/反馈)
    ↓
Azure Blob Storage
(原始文档 → 自动 ingestion)
```

关键技术选型：
- **后端**：C# .NET 8, Azure Functions v4
- **AI 检索**：Azure AI Search（混合检索 + 角色过滤）
- **LLM**：Groq API（llama-3.3-70b-versatile）
- **存储**：Azure Table Storage + Blob Storage
- **前端**：React (TypeScript)

---

## Slide 8 — 技术亮点（Technical）

**1. RAG Pipeline（检索增强生成）**
- 用户问题 → Azure AI Search 语义检索 → 相关文档片段
- 片段 + 系统 Prompt → Groq LLM → 最终回答
- 引用文档可追溯（citations）

**2. 角色权限隔离**
- Public / Internal / Confidential / Restricted 四级分类
- Customer 只能看 Public 文档，Agent 看 Internal，Admin 全看
- 检索层强制过滤，LLM 层二次校验

**3. 多轮对话记忆**
- 每次对话携带最近 5 轮历史
- LLM 接收 [system, user, assistant, ...] 消息数组
- 告别"每句话都要重新解释背景"

**4. Slot Filling（引导式采集）**
- 检测到事件关键词 → 切换到引导模式
- 按 issueType 问不同问题（Phishing / VPN / MFA…）
- 全部收集完 → 自动建票，字段结构化

**5. 自动文档 Ingestion**
- Admin 上传 docx/pdf → Blob Storage
- Event Grid 触发 → 解析 → 分块 → Embedding → 写入 Search Index
- 全程自动，无需手动操作

---

## Slide 9 — API 设计（Technical，可选）

核心 Endpoints：

| 接口 | 说明 |
|------|------|
| POST /api/chat | 主聊天接口，RAG + LLM + Slot Filling |
| GET /api/tickets/all | Agent/Admin 工单总览（支持分页筛选） |
| PATCH /api/tickets/{id} | 更新工单状态/负责人 |
| POST /api/feedback | 提交 👍👎 评分 |
| GET /api/feedback/summary | Admin 满意率分析 |
| GET /api/kb/documents | 知识库文档列表 |
| POST /api/documents/upload | Admin 上传文档 |
| DELETE /api/documents/delete | Admin 删除文档（blob + index 一步清除） |
| GET /api/health | 依赖健康检查 |

安全：全局 API Key 验证（x-api-key header）+ 限流（10并发/20队列）

---

## Slide 10 — Demo 展示（现场演示）

演示顺序建议：
1. 员工登录 → 聊天提问 VPN 问题 → AI 回答（引用文档）
2. 触发 Phishing 场景 → Slot Filling 引导 → 工单自动创建
3. Agent 登录 → Dashboard 看到工单 → 接单 → 改状态
4. 员工对 AI 回答点 👍
5. Admin 登录 → 查看满意率 → 看知识库文档列表 → 上传新文档

---

## Slide 11 — 项目成果（非技术）

- ✅ 完整的 AI 问答 + 工单系统，端到端可运行
- ✅ 支持 3 种角色，权限严格隔离
- ✅ 知识库包含 33 份安全文档，覆盖主要 issue 类型
- ✅ 部署在 Azure，生产环境可访问
- ✅ 全部 API 在 Azure 上测试通过
- ✅ 77 个单元测试，全部通过

---

## Slide 12 — 我们学到了什么（反思）

- RAG 的检索质量直接决定回答质量，文档分类和 chunking 很重要
- Azure Functions isolated worker 与传统 ASP.NET 有很多细节差异
- AI 产品需要有 feedback loop，否则无法持续改进
- Demo 环境的安全保护（API Key）不可忽视

---

## Slide 13 — 未来计划（选填）

- JWT 真实身份验证（目前 role 是 query param）
- Groq API 重试逻辑，提升稳定性
- 对话记忆压缩（长对话摘要）
- 移动端适配

---

## Slide 14 — Q&A

- 感谢聆听
- 欢迎提问

---

## 给 ChatGPT 的润色 Prompt

```
请帮我将以下 PPT 大纲润色为正式的演示文稿内容。
要求：
- 每张 slide 的文字精炼，不超过 5 个要点
- 保留技术准确性，但非技术 slide 要通俗易懂
- 语言风格：专业但不枯燥，适合 Capstone 答辩场合
- 中英文混排 or 纯英文（请告知你的偏好）
- 可以适当加入过渡句和演讲备注
```
