# Sprint 3 Backend Changes — For Li
**Date:** 2026-03-21
**From:** Doris
**To:** Pingchi Li (Frontend)

这份文档说明今天后端做了哪些改动、为什么改、以及你在前端需要怎么配合。

---

## 今日改动汇总

| # | 改动 | 你需要配合 |
|---|------|-----------|
| ⚠️ | **所有请求加 `x-api-key` header** | 🔴 必须先做，否则全部 401 |
| 1 | 新增 Agent/Admin Ticket 总览接口 | 做 ticket 列表页 |
| 2 | 新增 Ticket 分配 / 改状态接口 | 做 Assign + 状态操作 |
| 3 | Ticket 新增 `InProgress` 状态 | 所有 status 显示支持三个值 |
| 4 | 新增用户反馈系统（👍 👎） | 聊天回答加评分按钮 + Admin dashboard |
| 5 | 新增知识库文档列表接口 | Admin 面板加 KB 文档页 |
| 6 | 加了限流 | 加 429 错误处理 |
| **7** | **Ticket 列表接口加分页** | **response 结构变了，需要更新** |
| 8 | Admin 文档上传接口 | Admin 面板加上传功能 |
| 9 | Health check 加依赖检测 | 可选：做一个状态页 |
| 10 | Feedback summary 新增 `fileName` 字段 | 直接用，不用自己解析路径 |

---

## ⚠️ 最重要 — 所有请求现在必须带 API Key

### 改了什么
加了一个全局 API Key 验证。所有请求必须在 header 里带 `x-api-key`，否则返回 401。

### 为什么改
防止 URL 泄露后被人白嫖我们的 Groq 和 Azure 费用，demo 期间尤其重要。

### 你需要怎么做
**每一个 API 请求都必须加这个 header：**

```http
x-api-key: fortianswer-t6-2026
```

Key 的值我会单独发给你（不写在文档里）。

**React/JS 示例 — 建议封装成一个统一的 fetch helper：**

```js
const API_KEY = process.env.REACT_APP_API_KEY; // 存在 .env 文件里

async function apiFetch(path, options = {}) {
  return fetch(`${BASE_URL}${path}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      "x-api-key": API_KEY,
      ...options.headers,
    },
  });
}
```

以后所有 API 调用都用 `apiFetch` 代替 `fetch`，这样只需要改一个地方。

**没带 key 或 key 错误时，服务端返回：**
```json
HTTP 401
{ "error": { "code": "Unauthorized", "message": "Missing or invalid x-api-key header." } }
```

---

## 1. 新增：Agent/Admin Ticket 总览

### 改了什么
新增 `GET /api/tickets/all` endpoint。

### 为什么改
原来 `GET /api/tickets?username=` 只能查自己的 ticket，Agent 和 Admin 没有办法看到所有人的 ticket。这个是 Sprint 3 的 Agent Dashboard 功能。

### 接口

```
GET /api/tickets/all?role=agent
GET /api/tickets/all?role=admin
```

**可选 filter 参数（任意组合）：**

| 参数 | 说明 | 示例 |
|------|------|------|
| `role` | **必填**，`agent` 或 `admin` | `role=agent` |
| `status` | 筛选状态 | `status=Open` |
| `priority` | 筛选优先级 | `priority=P1` |
| `issueType` | 筛选类型 | `issueType=Phishing` |
| `assignedTo` | 筛选负责人 | `assignedTo=john` |
| `page` | 页码，默认 1 | `page=2` |
| `pageSize` | 每页条数，默认 20，最大 100 | `pageSize=20` |

> ⚠️ **注意：response 结构已更新，加了分页字段（见改动 7）**

**Response 200：**
```json
{
  "total": 50,
  "page": 1,
  "pageSize": 20,
  "totalPages": 3,
  "tickets": [
    {
      "ticketId": "a1b2c3d4e5f6",
      "status": "Open",
      "priority": "P1",
      "issueType": "Phishing",
      "createdByUser": "alice",
      "assignedTo": null,
      "summary": "Suspicious email with malicious link",
      "createdUtc": "2026-03-21T10:00:00Z",
      "updatedUtc": "2026-03-21T10:00:00Z"
    }
  ]
}
```

**你需要做：** Agent/Admin 登录后显示这个列表页，支持按 status 和 priority 筛选，加分页控件。

---

## 2. 新增：更新 Ticket（分配 / 改状态）

### 改了什么
新增 `PATCH /api/tickets/{id}` endpoint。

### 为什么改
Agent 需要能接单（assign to me）和改状态（Open → InProgress → Closed）。

### 接口

```
PATCH /api/tickets/{ticketId}?role=agent
Content-Type: application/json
x-api-key: ...
```

**Request body（三个字段都是可选的，只传你要改的）：**
```json
{
  "status": "InProgress",
  "assignedTo": "agent-john",
  "priority": "P2"
}
```

**Response 200** — 返回更新后的完整 ticket：
```json
{
  "ticketId": "a1b2c3d4e5f6",
  "status": "InProgress",
  "assignedTo": "agent-john",
  "priority": "P2",
  ...
}
```

**常见场景：**

```js
// Agent 接单
await apiFetch(`/api/tickets/${ticketId}?role=agent`, {
  method: "PATCH",
  body: JSON.stringify({ assignedTo: currentUsername, status: "InProgress" }),
});

// 关闭 ticket
await apiFetch(`/api/tickets/${ticketId}?role=agent`, {
  method: "PATCH",
  body: JSON.stringify({ status: "Closed" }),
});
```

**你需要做：** Ticket 详情页加 "Assign to me" 按钮 + 状态下拉选单。

---

## 3. Ticket 新增 `InProgress` 状态

### 改了什么
`status` 字段从原来 2 个值变成 3 个：

| 状态 | 意思 | 谁来设 |
|------|------|-------|
| `Open` | 新建，没人处理 | 系统自动（创建时） |
| `InProgress` | Agent 已接单 | Agent 通过 PATCH 设置 |
| `Closed` | 已解决 | Agent 通过 PATCH 设置 |

### 你需要做
所有显示 ticket status 的地方都要支持这三个值。建议用颜色区分：

```
Open       → 蓝色
InProgress → 黄色
Closed     → 绿色
```

---

## 4. 新增：用户反馈系统（👍 👎 + Admin 分析）

### 改了什么
新增 4 个 feedback 相关 endpoint：

| Endpoint | 用途 |
|---------|------|
| `POST /api/feedback` | 用户提交评分 |
| `GET /api/feedback/summary?role=admin` | Admin 看满意率汇总 |
| `GET /api/feedback/flagged?role=admin` | Admin 看待审核的低分回答 |
| `PATCH /api/feedback/{requestId}/dismiss?role=admin` | Admin 标记已处理 |

### 为什么改
US17 用户故事要求。👎 会自动标记为待审核，Admin 可以追查是哪个 KB 文档质量差，然后去修。

---

### 提交评分 — `POST /api/feedback`

**关键：需要传 `issueType` 和 `citations`**，用于后端统计哪类问题和哪些文档满意率低。

```json
{
  "requestId": "ccf3fed0b5d545b8ae5993ffab2a952e",
  "username":  "alice",
  "rating":    "up",
  "issueType": "VPN",
  "citations": ["public/faq-vpn.docx", "public/vpn-setup-guide.pdf"]
}
```

| 字段 | 必填 | 怎么取 |
|------|------|-------|
| `requestId` | 是 | `chatResponse.requestId` |
| `username` | 是 | 当前登录用户名 |
| `rating` | 是 | `"up"` 或 `"down"` |
| `issueType` | 否 | 你发 chat 请求时用的 `issueType`，存在 state 里直接传 |
| `citations` | 否 | `chatResponse.citations.map(c => c.urlOrId)` |

```js
async function submitFeedback(rating) {
  await apiFetch("/api/feedback", {
    method: "POST",
    body: JSON.stringify({
      requestId: chatResponse.requestId,
      username:  currentUsername,
      rating,
      issueType: currentIssueType,
      citations: chatResponse.citations?.map(c => c.urlOrId) ?? []
    }),
  });
}
```

点击后把按钮设为选中状态，不需要弹任何提示，fire and forget。

---

### Admin Dashboard 需要的三个接口

**满意率汇总：**
```
GET /api/feedback/summary?role=admin
```
```json
{
  "totalUp": 84, "totalDown": 16, "totalRatings": 100, "satisfactionRate": 84.0,
  "byIssueType": [
    { "issueType": "VPN", "up": 20, "down": 10, "satisfactionRate": 66.7 }
  ],
  "byCitation": [
    {
      "documentId": "public/faq-vpn.docx",
      "fileName": "faq-vpn.docx",
      "up": 15, "down": 8, "totalRatings": 23, "satisfactionRate": 65.2
    }
  ]
}
```

> ⚠️ `byCitation` 里现在多了 `fileName` 字段（见改动 10），展示文档名直接用这个，不用自己截字符串。

**待审核低分回答：**
```
GET /api/feedback/flagged?role=admin
```
```json
{ "total": 3, "items": [{ "requestId": "...", "username": "alice", "issueType": "VPN", "citations": [...], "createdUtc": "..." }] }
```

**Admin 处理完后标记已审核：**
```
PATCH /api/feedback/{requestId}/dismiss?role=admin
```
```json
{ "dismissed": true }
```

---

## 5. 新增：知识库文档列表

### 改了什么
新增 `GET /api/kb/documents` endpoint，Admin/Agent 可以查看知识库里有哪些文档。

### 接口

```
GET /api/kb/documents?role=admin
GET /api/kb/documents?role=admin&classification=internal
```

**Response 200：**
```json
{
  "total": 3,
  "documents": [
    {
      "path":           "internal/vpn-setup-guide.docx",
      "source":         "vpn-setup-guide.docx",
      "classification": "internal",
      "createdUtc":     "2026-03-10T08:00:00Z",
      "chunkCount":     14
    }
  ]
}
```

**你需要做：** Admin 面板里加一个 "Knowledge Base" 页面，显示文档列表。可以按 `classification` 分组或筛选。`chunkCount` 可以展示出来，chunk 越多说明文档越长。

---

## 6. 加了限流

### 改了什么
后端同时最多处理 10 个请求，队列超过 20 个自动返回 429。

### 你需要做
加一个对 `429 Too Many Requests` 的处理，提示用户稍后再试：

```js
if (response.status === 429) {
  showMessage("服务繁忙，请稍后再试");
  return;
}
```

---

## 7. ⚠️ 变更：Ticket 列表接口加了分页

### 改了什么
`GET /api/tickets/all` 的 response 结构更新了，**新增了四个字段**：

| 字段 | 说明 |
|------|------|
| `page` | 当前第几页 |
| `pageSize` | 每页几条 |
| `totalPages` | 总页数 |
| `total` | 过滤后的总条数（没变） |

### ⚠️ 你需要注意
如果你之前已经按照旧文档做了 ticket 列表，需要确认：
1. 取 tickets 用 `response.tickets`（没变）
2. 显示总数用 `response.total`（没变）
3. **新增：加翻页控件**，用 `response.totalPages` 判断有几页，点击时传 `?page=N`

**分页请求示例：**
```js
// 第一页（默认）
GET /api/tickets/all?role=agent

// 第二页，每页 20 条
GET /api/tickets/all?role=agent&page=2&pageSize=20

// 带筛选条件的第三页
GET /api/tickets/all?role=admin&status=Open&page=3&pageSize=20
```

---

## 8. 新增：Admin 上传知识库文档

### 改了什么
新增 `POST /api/documents/upload` endpoint。Admin 可以直接通过 API 上传文档，后端自动处理 ingestion，不需要手动去 Azure Portal。

### 接口

```
POST /api/documents/upload?role=admin&classification=internal&filename=vpn-guide.docx
Content-Type: application/octet-stream
x-api-key: ...

[文件二进制内容]
```

| 参数 | 必填 | 说明 |
|------|------|------|
| `role` | 是 | 必须是 `admin` |
| `filename` | 是 | 文件名，必须以 `.pdf` / `.docx` / `.txt` / `.md` 结尾 |
| `classification` | 否 | `public` / `internal` / `confidential` / `restricted`，默认 `public` |

**Response 201：**
```json
{
  "blobPath": "internal/vpn-guide.docx",
  "filename": "vpn-guide.docx",
  "classification": "internal",
  "message": "Upload successful. Ingestion will begin shortly."
}
```

**JS 示例（file input）：**
```js
async function uploadDocument(file, classification = "public") {
  const res = await apiFetch(
    `/api/documents/upload?role=admin&classification=${classification}&filename=${encodeURIComponent(file.name)}`,
    {
      method: "POST",
      headers: { "Content-Type": "application/octet-stream" },
      body: file,  // File 对象，直接从 <input type="file"> 取
    }
  );
  return res.json();
}
```

注意：`apiFetch` 里默认会带 `"Content-Type": "application/json"`，上传文件时要覆盖掉，改成 `"application/octet-stream"`。上面示例里 `...options.headers` 会覆盖默认值，没问题。

**你需要做：** Admin 面板的 KB 文档页加一个上传按钮（file input + 选 classification 下拉 + 上传）。上传成功后刷新文档列表。

---

## 9. 新增：Health Check 加依赖检测

### 改了什么
`GET /api/health` 以前只返回 `{"status":"ok"}`，现在会实际检测 Table Storage、Azure AI Search、Groq API key 三个依赖。

**Response 200：**
```json
{
  "status": "ok",
  "checks": {
    "tableStorage": "ok",
    "search": "ok",
    "groq": "ok"
  },
  "timestamp": "2026-03-21T10:00:00Z"
}
```

如果有问题，`status` 会是 `"degraded"`，对应 check 字段会是 `"fail"`。HTTP 状态码始终是 200。

### 你需要做
这个接口可选用。如果你想做一个"系统状态"指示灯（比如 Admin 面板里显示后端是否正常），可以定期 ping 这个接口，根据 `status` 字段显示绿灯/黄灯。

---

## 10. 变更：Feedback Summary 新增 `fileName` 字段

### 改了什么
`GET /api/feedback/summary` 返回的 `byCitation` 数组里，每条记录新增了 `fileName` 字段，是从 `documentId`（blob 路径）里自动提取的文件名。

**之前：**
```json
{ "documentId": "public/faq-vpn.docx", "up": 15, "down": 8, ... }
```

**现在：**
```json
{ "documentId": "public/faq-vpn.docx", "fileName": "faq-vpn.docx", "up": 15, "down": 8, ... }
```

### 你需要做
展示文档名时直接用 `fileName`，不需要自己截字符串了。

---

## 汇总 — 你的前端任务清单（含今日新增）

| 任务 | 对应改动 | 优先级 |
|------|---------|-------|
| 所有请求加 `x-api-key` header | API Key | 🔴 **必须先做，否则全部 401** |
| Priority 颜色 badge（P1红/P2橙/P3黄/P4灰） | 已有数据，只加样式 | 🔴 高 |
| Agent ticket dashboard 列表页（含分页） | `GET /api/tickets/all` | 🔴 高 |
| ⚠️ 更新已有 ticket 列表代码以兼容新 response 结构 | 分页字段变更 | 🔴 如果已经做了必须改 |
| Ticket 详情页加 Assign + 状态操作 | `PATCH /api/tickets/{id}` | 🟡 中 |
| 支持 `InProgress` 状态显示 | status 新增值 | 🟡 中 |
| 每条回答加 👍 👎 按钮（带 issueType + citations） | `POST /api/feedback` | 🟡 中 |
| Admin dashboard 加满意率卡片（用 `fileName` 显示文档名） | `GET /api/feedback/summary` | 🟡 中 |
| Admin dashboard 加待审核列表 | `GET /api/feedback/flagged` + dismiss | 🟡 中 |
| Admin KB 文档页加上传功能 | `POST /api/documents/upload` | 🟡 中 |
| 加 429 错误处理 | 限流 | 🟢 低 |
| Admin 面板状态指示灯（可选） | `GET /api/health` | 🟢 低 |

---

## Priority 颜色参考

| Priority | 颜色 | CSS |
|----------|------|-----|
| P1 Critical | 红 | `#DC2626` |
| P2 High | 橙 | `#EA580C` |
| P3 Medium | 黄 | `#CA8A04` |
| P4 Low | 灰 | `#6B7280` |

`priority` 字段在所有 ticket response 里都有，直接读就行，不需要自己计算。

---

有问题找 Doris。
