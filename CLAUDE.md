# FortiAnswer Orchestrator — Project Reference

> Keep this file updated whenever code, architecture, or significant decisions change.

## Working Rules

- **每次修改代码后，必须运行 `dotnet build` 验证编译通过**，并将结果（成功 / 错误信息）报告给用户，再继续其他工作。
- 如有相关 xUnit 测试，同步运行 `dotnet test` 并报告结果。
- 报告格式：简洁说明改了什么 → build 结果 → test 结果（如适用）。

---

## Project Overview

**FortiAnswer Orchestrator** is a C# .NET 8.0 Azure Functions backend for an AI-powered security incident response assistant. It combines Azure AI Search, Groq LLM, and structured slot-filling to help users diagnose and escalate security incidents.

- **Deployment:** Azure Functions v4, serverless, `dotnet-isolated` worker
- **Region:** Canada Central
- **Host URL:** `func-fortianswer-gccvakhgayenbdak.canadacentral-01.azurewebsites.net`
- **Solution file:** `fortianswer-orchestrator.sln`

---

## Repository Layout

```
c:\Dev\
├── fortianswer-orchestrator/
│   ├── src/orchestrator/FortiAnswer.Orchestrator/   ← main source
│   ├── tests/FortiAnswer.Orchestrator.Tests/        ← xUnit tests
│   ├── docs/                                        ← API contracts, guides, sprint docs
│   ├── .github/workflows/main_func-fortianswer.yml ← CI/CD
│   └── README.md
├── Dev.sln
└── appsettings.azure.json
```

---

## Architecture

```
HTTP Request
    ↓
Azure Function Endpoint (*Function.cs)
    ↓
Service Layer (Services/)
    ├── RetrievalService   → Azure AI Search (hybrid + keyword, role-filtered)
    ├── GroqClient         → Groq LLM (llama-3.3-70b-versatile)
    ├── PromptBuilder      → Assembles system + user prompts
    ├── SlotSessionService → Multi-turn guided intake (Table Storage)
    ├── WebSearchService   → Tavily web search fallback
    └── TicketsTableService→ Ticket CRUD (Table Storage)
```

### Role-Based Access
Three roles with separate data boundaries:
- **Customer** — self-service Q&A, can open tickets
- **Agent** — access to customer data, handles escalations
- **Admin** — full access, document ingestion

---

## Azure Functions (Endpoints)

| Function | HTTP Method | Route | Purpose |
|----------|-------------|-------|---------|
| `ChatFunction` | POST | `/api/chat` | Main AI pipeline: retrieval → LLM → slot filling → escalation |
| `AuthRegisterFunction` | POST | `/api/auth/register` | User registration |
| `AuthLoginFunction` | POST | `/api/auth/login` | Login + role lookup |
| `TicketCreateFunction` | POST | `/api/tickets` | Manual ticket creation |
| `TicketGetFunction` | GET | `/api/tickets/{id}` | Get single ticket |
| `TicketUpdateFunction` | PATCH | `/api/tickets/{id}?role=` | Update status / assignedTo / priority (agent/admin only) |
| `TicketListFunction` | GET | `/api/tickets?username=` | List tickets for a specific user (customer self-service) |
| `TicketAdminListFunction` | GET | `/api/admin/tickets?role=` | Full ticket overview with filters (agent/admin only) |
| `FeedbackFunction` | POST | `/api/feedback` | Submit thumbs up/down rating (with issueType + citations) |
| `FeedbackQueryFunction` | GET | `/api/feedback/summary?role=admin` | Satisfaction stats: overall + byIssueType + byCitation |
| `FeedbackQueryFunction` | GET | `/api/feedback/flagged?role=admin` | List low-rated unreviewed responses |
| `FeedbackQueryFunction` | PATCH | `/api/feedback/{requestId}/dismiss?role=admin` | Mark flagged item as reviewed |
| `KbDocumentsFunction` | GET | `/api/kb/documents?role=agent\|admin` | List all indexed KB documents (with classification filter) |
| `AdminDocumentUploadFunction` | POST | `/api/admin/documents?role=admin` | Upload a .pdf/.docx/.txt/.md to blob; auto-triggers ingestion |
| `SlotSessionCleanupFunction` | Timer | — | Daily 02:00 UTC — delete stale slot sessions |
| `ConversationListFunction` | GET | `/api/conversations` | Chat history per user |
| `IngestFunction` | POST | `/api/ingest` | Manual doc ingestion trigger |
| `BlobIngestTriggerFunction` | Event Grid | — | Auto-ingest on blob upload |
| `BlobDeletedEventGridTriggerFunction` | Event Grid | — | Auto-delete vectors on blob delete |
| `DeleteVectorsFunction` | DELETE | `/api/vectors` | Remove embeddings from search index |
| `HealthFunction` | GET | `/api/health` | Health check |

---

## Key Services

| Service | File | Responsibility |
|---------|------|---------------|
| `RetrievalService` | `RetrievalService.cs` | Azure AI Search queries, hybrid/keyword, role filters |
| `GroqClient` | `GroqClient.cs` | LLM API calls |
| `PromptBuilder` | `PromptBuilder.cs` | System + user prompt construction |
| `SlotDefinitions` | `SlotDefinitions.cs` | Per-issueType questions + priority rules |
| `SlotSessionService` | `SlotSessionService.cs` | Slot session state (Table Storage) |
| `TicketsTableService` | `TicketsTableService.cs` | Ticket CRUD — CreateAsync, GetByIdAsync, GetByUsernameAsync, GetAllAsync (agent/admin), UpdateAsync |
| `FeedbackTableService` | `FeedbackTableService.cs` | Feedback storage — RecordAsync, GetSummaryAsync, GetFlaggedAsync, DismissAsync |
| `AzureAiSearchIngestService` | `AzureAiSearchIngestService.cs` | Vector indexing + ListDocumentsAsync (facets-based distinct doc enumeration) |
| `TableStorageService` | `TableStorageService.cs` | Conversation logging + GetRecentTurnsAsync (multi-turn memory) |
| `UsersTableService` | `UsersTableService.cs` | User account management |
| `WebSearchService` | `WebSearchService.cs` | Tavily fallback search |
| `IngestionOrchestrator` | `IngestionOrchestrator.cs` | Doc ingestion pipeline coordination |
| `AzureAiSearchIngestService` | `AzureAiSearchIngestService.cs` | Vector indexing |
| `OpenAiEmbeddingService` | `OpenAiEmbeddingService.cs` | text-embedding-3-small embeddings |
| `PdfTextExtractor` | `PdfTextExtractor.cs` | PDF text extraction |
| `DocxTextExtractor` | `DocxTextExtractor.cs` | DOCX text extraction |
| `TextChunker` | `TextChunker.cs` | Chunking for embedding |

---

## Models

- `ChatModels.cs` — Request/response types for the chat pipeline
- `SlotModels.cs` — Slot filling session structures (issueType, collected slots, etc.)

---

## External Dependencies

| Service | Purpose | Config Key |
|---------|---------|-----------|
| Azure AI Search | Vector + keyword retrieval | `AzureSearchEndpoint`, `AzureSearchApiKey`, index: `fortianswer-index` |
| Groq API | LLM inference (`llama-3.3-70b-versatile`) | `GroqApiKey` |
| OpenAI | Embeddings (`text-embedding-3-small`) | `OpenAiApiKey` |
| Tavily | Web search fallback | `TavilyApiKey` |
| Azure Blob Storage | Document repository | `BlobStorageConnection` (`blobfortianswer`) |
| Azure Table Storage | Users, tickets, conversations, slot sessions, feedback | `StorageConnection` / `BLOB_CONNECTION` |
| API Key Guard | All HTTP endpoints protected via `x-api-key` header | `API_KEY` env var (bypass if missing = local dev) |
| Azure Document Intelligence | Doc parsing | `DocumentIntelligenceEndpoint` |
| Application Insights | Monitoring | `APPLICATIONINSIGHTS_CONNECTION_STRING` |

---

## NuGet Dependencies

```xml
Azure.Data.Tables 12.11.0
Azure.Storage.Blobs 12.27.0
Microsoft.Azure.Functions.Worker 2.51.0
Microsoft.Azure.Functions.Worker.Extensions.EventGrid 3.6.0
Microsoft.Azure.Functions.Worker.Extensions.Storage.Blobs 6.8.0
Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore 2.0.2
DocumentFormat.OpenXml 3.4.1
BCrypt.Net-Next 4.0.3
Microsoft.ApplicationInsights.WorkerService 2.23.0
```

---

## Local Development

```bash
cd src/orchestrator/FortiAnswer.Orchestrator
cp local.settings.template.json local.settings.json
# Fill in keys in local.settings.json
func start
# Health check: GET http://localhost:7071/api/health
```

---

## CI/CD

- **Trigger:** Push to `main` or manual dispatch
- **File:** `.github/workflows/main_func-fortianswer.yml`
- **Steps:** dotnet restore → build (Release) → publish → Azure login → deploy

---

## Testing

```bash
cd tests/FortiAnswer.Orchestrator.Tests
dotnet test
```

Test files cover:
- Slot definitions
- Models
- Slot session state logic
- Ticket priority rules

REST test files: `Localtest.http` (local) and `requests.http` (prod)

---

## Chat Pipeline (ChatFunction flow)

1. Receive `ChatRequest` (userId, role, message, conversationId)
2. If active slot session → continue slot filling
3. Else → run retrieval (Azure AI Search, role-filtered)
4. If no results → Tavily web search fallback
5. Load last 5 conversation turns from Table Storage (`GetRecentTurnsAsync`)
6. Build system prompt (`BuildSystemPrompt`) + append escalation/issue hints
7. Call `GroqClient.ChatWithHistoryAsync` — sends [system, ...history turns, user] message array
8. Detect escalation triggers → auto-create ticket if needed
9. Detect slot-fill trigger → start guided intake session
10. Log conversation to Table Storage (with userMessage + botAnswer for future history)
11. Return `ChatResponse`

---

## Project Status

| Sprint | Features | Status |
|--------|----------|--------|
| 1 | Scaffold, AI Search, Groq LLM, role-based retrieval, auto-escalation | ✅ Done |
| 2 | Auth, tickets, conversation history, slot filling, doc ingestion, web search fallback | ✅ Done |
| Post-S2 | Agent/Admin ticket overview (`GET /api/admin/tickets`), ticket update (`PATCH /api/tickets/{id}`), `InProgress` status | ✅ Done |
| Sprint 3 | API key protection (`ApiKeyMiddleware`), rate limiting (host.json), feedback system (US17), conversation memory (multi-turn LLM), KB document list | ✅ Done |
| Post-S3 | Admin ticket pagination (`page`/`pageSize`), slot session cleanup (Timer), health check with dependency pings, admin document upload (`POST /api/admin/documents`), citation `fileName` in feedback summary | ✅ Done |

---

## Key Docs

- `docs/ui-integration-guide.md` — Full API reference with schemas for Li (frontend)
- `docs/sprint3-backend-changes-for-li.md` — Sprint 3 change summary for Li (in Chinese)
- `docs/local-run-guide.md` — Local setup walkthrough
- `docs/api-contract.md` — API contract
- `docs/retrieval-contract.md` — Retrieval service spec
- `README.md` — Architecture overview
