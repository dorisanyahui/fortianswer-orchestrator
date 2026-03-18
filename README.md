# FortiAnswer Orchestrator

AI-powered security incident response assistant built on Azure Functions. Accepts natural language questions from service desk agents and end users, retrieves relevant knowledge from a secured knowledge base, and either answers directly or escalates to a structured ticket with full context.

**Production URL:** `https://func-fortianswer-gccvakhgayenbdak.canadacentral-01.azurewebsites.net`

---

## What It Does

| Capability | Description |
|---|---|
| **AI Q&A** | Answers security questions using Azure AI Search + Groq LLM |
| **Role-based access** | Customer / Agent / Admin roles each see different knowledge tiers |
| **Auto escalation** | Restricted / high-severity requests automatically create tickets |
| **Slot filling (US9)** | Guided multi-turn intake collects structured incident details before creating a ticket |
| **Web search fallback** | When the internal KB has no match, user can confirm a live web search |
| **Ticket management** | Create, view, and list tickets; linked to conversation sessions |
| **Conversation history** | Every chat turn is logged and retrievable per user |
| **Auth** | Username/password user accounts with role enforcement |
| **Document ingestion** | Upload PDF/DOCX to Blob Storage → auto-chunked, embedded, indexed |

---

## Architecture

```
Web UI / REST Client
        │
        ▼
Azure Functions (this repo)
        │
        ├── /api/auth/register + /api/auth/login   ← User accounts (Azure Table Storage)
        ├── /api/chat                               ← Main AI pipeline
        │       ├── Azure AI Search (retrieval)
        │       ├── Groq LLM (generation)
        │       ├── Slot Session Service (Azure Table Storage)
        │       └── Tickets Table Service (Azure Table Storage)
        ├── /api/tickets                            ← CRUD for escalation tickets
        ├── /api/conversations                      ← Conversation history
        └── /api/ingest                             ← Manual document ingestion trigger
                ├── Azure Blob Storage (document source)
                ├── OpenAI Embeddings
                └── Azure AI Search (indexing)
```

---

## API Endpoints

### Auth
| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/auth/register` | Create a new user account |
| POST | `/api/auth/login` | Authenticate and get user role |

### Chat
| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/chat` | Send a message — returns AI answer, escalation, or slot filling prompt |

### Tickets
| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/tickets` | Manually create a ticket |
| GET | `/api/tickets/{id}` | Get a ticket by ID |
| GET | `/api/tickets?username=` | List all tickets for a user |

### Conversations
| Method | Endpoint | Description |
|---|---|---|
| GET | `/api/conversations?username=` | Get chat history for a user |

### Ingestion
| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/ingest` | Trigger manual document ingestion |
| GET | `/api/health` | Health check |

> Full request/response schema, field descriptions, and worked examples are in [`docs/ui-integration-guide.md`](docs/ui-integration-guide.md).

---

## Chat Pipeline

```
POST /api/chat
        │
        ▼
1. Validate request (role, issueType, dataBoundary)
        │
        ▼
2. Is this a Restricted or high-severity request?
   ├── Admin    → Direct explanation, no ticket
   └── Others   → Does issueType have slot definitions?
                   ├── Yes → Slot filling (guided multi-turn intake)
                   │         After final answer → create ticket
                   └── No  → Create ticket immediately
        │
        ▼
3. Normal flow: Retrieve from Azure AI Search → Build prompt → Call Groq LLM
        │
        ▼
4. Web search fallback (if KB returns no results for Public queries)
        │
        ▼
5. Log conversation turn → Return structured response
```

### Role → Data Boundary

| Role | Max data boundary | Can see |
|---|---|---|
| Customer | Public | Public KB only |
| Agent | Internal | Public + Internal KB |
| Admin | Confidential | Public + Internal + Confidential |

### Issue Type → Priority

| Issue Type | Priority | Always Escalates? |
|---|---|---|
| Phishing, SuspiciousLogin, Severity | P1 Critical | Yes (SuspiciousLogin, Severity always force Restricted) |
| EndpointAlert, AccountLockout | P2 High | No |
| VPN, MFA, PasswordReset | P3 Medium | No |
| General | P4 Low | No |

### `next.action` Values (UI-facing)

| Value | Meaning |
|---|---|
| `none` | Normal answer — display response |
| `escalate` | Ticket created — show ticket banner with `ticketId` |
| `slot_filling` | Guided mode — show `slotFilling.nextQuestion` to user |

---

## Slot Filling (US9)

For high-priority issue types, the bot asks structured follow-up questions one at a time before creating the ticket. This ensures tickets are complete and actionable on arrival.

Questions per issue type:

| Issue Type | Questions |
|---|---|
| Phishing, SuspiciousLogin, VPN, MFA, EndpointAlert | 4 |
| AccountLockout, PasswordReset | 3 |
| General, Severity | 0 (no slot filling) |

Session state is stored in Azure Table Storage keyed by `conversationId`. The UI must send the **same `conversationId`** on every turn of a session.

---

## Local Development

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local)
- (Optional) [Azurite](https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azurite) for local storage emulation

### 1. Navigate to the Function App

```bash
cd src/orchestrator/FortiAnswer.Orchestrator
```

### 2. Configure local settings

```bash
cp local.settings.template.json local.settings.json
```

`local.settings.json` is git-ignored — do **not** commit it.

#### Stub mode (no secrets required)

By default the app runs in stub mode — no Azure credentials needed:

```json
{
  "Values": {
    "RETRIEVAL_MODE": "stub",
    "LLM_MODE": "stub"
  }
}
```

#### Full mode (real Azure services)

```json
{
  "Values": {
    "RETRIEVAL_MODE": "azureaisearch",
    "RETRIEVAL_ENDPOINT": "https://<your-search>.search.windows.net",
    "RETRIEVAL_API_KEY": "<key>",
    "RETRIEVAL_INDEX": "<index-name>",

    "LLM_MODE": "groq",
    "LLM_ENDPOINT": "https://api.groq.com/openai/v1",
    "LLM_API_KEY": "<groq-key>",
    "LLM_MODEL": "llama3-70b-8192",

    "OPENAI_API_KEY": "<openai-key-for-embeddings>",

    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "BLOB_CONNECTION_STRING": "<storage-connection-string>",
    "BLOB_CONTAINER_NAME": "<container>"
  }
}
```

### 3. Run

```bash
func start
```

### 4. Verify

```
GET http://localhost:7071/api/health
```

---

## Testing

### Local HTTP tests

Open [`tests/Localtest.http`](tests/Localtest.http) in VS Code (REST Client extension) — covers all endpoints including slot filling flows.

### Deployed environment tests

Open [`tests/requests.http`](tests/requests.http) — points to the production URL.

### Quick smoke test sequence (local)

```bash
# 1. Register
POST http://localhost:7071/api/auth/register
{ "username": "testuser", "password": "P@ssw0rd123", "role": "Customer" }

# 2. Normal chat
POST http://localhost:7071/api/chat
{ "message": "How do I reset my VPN password?", "issueType": "VPN", "userRole": "Customer", "username": "testuser", "conversationId": "conv-001" }

# 3. Slot filling (SuspiciousLogin — 5 turns)
POST http://localhost:7071/api/chat
{ "message": "Someone logged in from China at 3am.", "issueType": "SuspiciousLogin", "userRole": "Customer", "username": "testuser", "conversationId": "conv-002" }
# → next.action == "slot_filling" — answer the 4 follow-up questions with the same conv-002

# 4. List tickets
GET http://localhost:7071/api/tickets?username=testuser
```

---

## Document Ingestion

Documents are stored in Azure Blob Storage, chunked, embedded (OpenAI `text-embedding-3-small`), and indexed in Azure AI Search.

**Trigger options:**
- **Blob event** — upload a file to the configured container; an Event Grid trigger fires automatically
- **Manual** — `POST /api/ingest` with a payload specifying the blob path
- **Delete** — delete a blob; a separate Event Grid trigger removes the vectors from the index

**Supported formats:** PDF, DOCX

**Data boundary tagging:** Documents are tagged at ingestion time with a `dataBoundary` metadata field (`Public` / `Internal` / `Confidential`). Retrieval queries always filter by the requesting user's allowed boundary.

---

## Project Structure

```
fortianswer-orchestrator/
├── src/orchestrator/FortiAnswer.Orchestrator/
│   ├── ChatFunction.cs                  ← Main chat endpoint
│   ├── AuthRegisterFunction.cs
│   ├── AuthLoginFunction.cs
│   ├── TicketCreateFunction.cs
│   ├── TicketGetFunction.cs
│   ├── TicketListFunction.cs
│   ├── ConversationListFunction.cs
│   ├── IngestFunction.cs
│   ├── BlobIngestTriggerFunction.cs
│   ├── BlobDeletedEventGridTriggerFunction.cs
│   ├── HealthFunction.cs
│   ├── Models/
│   │   ├── ChatModels.cs                ← Request/response types
│   │   └── SlotModels.cs                ← Slot filling types
│   └── Services/
│       ├── RetrievalService.cs          ← Azure AI Search retrieval
│       ├── GroqClient.cs                ← LLM calls
│       ├── PromptBuilder.cs             ← Prompt construction
│       ├── SlotDefinitions.cs           ← Per-issueType question lists
│       ├── SlotSessionService.cs        ← Slot session state (Table Storage)
│       ├── TicketsTableService.cs       ← Ticket CRUD (Table Storage)
│       ├── TableStorageService.cs       ← Conversation logging
│       ├── UsersTableService.cs         ← User accounts
│       ├── WebSearchService.cs          ← Web search fallback
│       ├── IngestionOrchestrator.cs     ← Document ingestion pipeline
│       └── AzureAiSearchIngestService.cs
├── tests/
│   ├── Localtest.http                   ← Local dev REST tests
│   └── requests.http                    ← Production REST tests
└── docs/
    └── ui-integration-guide.md          ← Full API reference for UI team
```

---

## Sprint Delivery Status

| Sprint | User Story | Feature | Status |
|---|---|---|---|
| 1 | US1 | Azure Functions project scaffold | ✅ Done |
| 1 | US2 | Azure AI Search retrieval | ✅ Done |
| 1 | US3 | Groq LLM integration | ✅ Done |
| 1 | US4 | Role-based data boundary enforcement | ✅ Done |
| 1 | US5 | Auto escalation + ticket creation | ✅ Done |
| 2 | US6 | Auth (register / login) | ✅ Done |
| 2 | US7 | Ticket management (create / get / list) | ✅ Done |
| 2 | US8 | Conversation history | ✅ Done |
| 2 | US9 | Slot filling — guided incident intake | ✅ Done |
| 2 | US10 | Document ingestion pipeline (PDF/DOCX) | ✅ Done |
| 2 | –   | Web search fallback | ✅ Done |
