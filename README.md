# FortiAnswer Orchestrator

AI-powered security incident response assistant built with **C#**, **.NET 8**, and **Azure Functions**.
It accepts natural-language questions from service desk agents and end users, retrieves relevant knowledge from a secured knowledge base, and either answers directly or escalates the case into a structured support ticket with full context.

> This project demonstrates **backend engineering**, **cloud integration**, **AI orchestration**, and **workflow automation** in an enterprise support scenario.

---

## Overview

FortiAnswer Orchestrator is a cloud-based backend project designed for security support workflows.

It combines:

- **Natural-language question answering**
- **Role-based knowledge retrieval**
- **Automatic ticket escalation**
- **Guided incident intake**
- **Document ingestion and indexing**
- **Feedback analytics and conversation history**

The goal is to reduce manual triage effort, improve response consistency, and ensure that higher-risk or restricted issues are handled through the right escalation path.

---

## Why This Project Matters

In real support environments, users often ask questions in free-form language, while support teams still need:

- controlled access to sensitive information
- reliable retrieval from internal knowledge sources
- structured tickets for actionable incidents
- traceable conversation history
- consistent handling of urgent or restricted cases

FortiAnswer Orchestrator was built to address that gap by combining **LLM-assisted responses** with **retrieval**, **role enforcement**, and **workflow automation**.

---

## Key Highlights

- **AI-powered Q&A** using Azure AI Search retrieval plus Groq LLM generation
- **Role-based access control** across Public / Internal / Confidential knowledge tiers
- **Automatic escalation** for restricted or high-severity requests
- **Slot filling workflow** for guided multi-turn incident intake before ticket creation
- **Web search fallback** when no internal knowledge base match is available
- **Ticket management** with create, view, list, and admin overview flows
- **Conversation history logging** for traceability and user context
- **Document ingestion pipeline** for PDF and DOCX files
- **Feedback analytics** for response quality monitoring
- **77 xUnit unit tests** plus local and deployed HTTP endpoint testing

---

## Tech Stack

### Backend
- **C#**
- **.NET 8**
- **Azure Functions**

### Cloud & Storage
- **Azure Table Storage**
- **Azure Blob Storage**
- **Azure AI Search**

### AI / Search
- **Groq LLM**
- **Azure AI Search** for retrieval
- **OpenAI Embeddings** (`text-embedding-3-small`)
- **Tavily Web Search** for fallback lookup

### Dev Workflow
- **GitHub Actions**
- **xUnit**
- **REST Client / HTTP test files**

---

## Core Capabilities

| Capability | Description |
|---|---|
| **AI Q&A** | Answers security-related questions using retrieval + LLM generation |
| **Role-based access** | Customer / Agent / Admin roles see different knowledge boundaries |
| **Auto escalation** | High-severity or restricted requests create structured support tickets |
| **Slot filling** | Guided follow-up questions collect the details needed for better tickets |
| **Web search fallback** | Allows confirmed live web lookup when the internal KB has no match |
| **Ticket management** | Create, retrieve, list, and manage tickets |
| **Conversation history** | Logs each turn for auditability and continuity |
| **Authentication** | Username/password login with role enforcement |
| **Document ingestion** | Upload PDF/DOCX → chunk → embed → index into Azure AI Search |
| **Feedback analytics** | Collects user ratings and exposes admin reporting endpoints |

---

## Architecture

```text
Web UI / REST Client
        │
        │  x-api-key required on all requests
        ▼
Azure Functions (this repo)
        │
        ├── API Key Middleware
        │
        ├── /api/auth/register
        ├── /api/auth/login
        │
        ├── /api/chat
        │      ├── Azure AI Search (role-filtered retrieval)
        │      ├── Groq LLM (generation + multi-turn context)
        │      ├── Tavily Web Search (fallback)
        │      ├── Slot Session Service (Azure Table Storage)
        │      └── Tickets Table Service (Azure Table Storage)
        │
        ├── /api/tickets
        ├── /api/tickets/all
        ├── /api/feedback
        ├── /api/kb/documents
        ├── /api/conversations
        │
        └── /api/documents + /api/ingest
               ├── Azure Blob Storage
               ├── OpenAI Embeddings
               └── Azure AI Search Index
```

---

## How the Chat Pipeline Works

```text
POST /api/chat
        │
        ▼
1. Validate request
   - role
   - issueType
   - dataBoundary
        │
        ▼
2. Check whether request is restricted or high severity
   ├── Admin → explain directly
   └── Others
         ├── if slot definitions exist → guided slot filling
         └── otherwise → immediate ticket creation
        │
        ▼
3. Normal flow
   Retrieve from Azure AI Search
   → build prompt
   → call Groq LLM
        │
        ▼
4. Optional web search fallback
   if internal KB returns no relevant result
        │
        ▼
5. Log conversation turn
   and return structured response
```

---

## Role and Data Boundary Model

| Role | Max Data Boundary | Access Scope |
|---|---|---|
| **Customer** | Public | Public KB only |
| **Agent** | Internal | Public + Internal KB |
| **Admin** | Confidential | Public + Internal + Confidential KB |

This ensures that retrieval results are filtered according to the requesting user's permitted knowledge tier.

---

## Issue Priority Model

| Issue Type | Priority | Always Escalates? |
|---|---|---|
| **Phishing** | P1 Critical | Yes |
| **SuspiciousLogin** | P1 Critical | Yes |
| **Severity** | P1 Critical | Yes |
| **EndpointAlert** | P2 High | No |
| **AccountLockout** | P2 High | No |
| **VPN** | P3 Medium | No |
| **MFA** | P3 Medium | No |
| **PasswordReset** | P3 Medium | No |
| **General** | P4 Low | No |

---

## Slot Filling Workflow

For selected issue types, the system does not immediately create a ticket.
Instead, it asks a guided sequence of follow-up questions to collect complete and actionable incident details.

### Current question counts

| Issue Type | Questions |
|---|---|
| **Phishing** | 4 |
| **SuspiciousLogin** | 4 |
| **VPN** | 4 |
| **MFA** | 4 |
| **EndpointAlert** | 4 |
| **AccountLockout** | 3 |
| **PasswordReset** | 3 |
| **General** | 0 |
| **Severity** | 0 |

Session state is stored in **Azure Table Storage** and keyed by `conversationId`.
The client must reuse the same `conversationId` across the same guided session.

---

## API Overview

### Auth

| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/auth/register` | Create a new user account |
| POST | `/api/auth/login` | Authenticate and return user role |

### Chat

| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/chat` | Submit a message and receive an answer, escalation, or slot-filling prompt |

### Tickets

| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/tickets` | Manually create a ticket |
| GET | `/api/tickets/{id}` | Get ticket by ID |
| GET | `/api/tickets?username=` | List tickets for a user |
| GET | `/api/tickets/all?role=` | Full overview for agent/admin users |
| PATCH | `/api/tickets/{id}?role=` | Update status, assignee, or priority |

### Feedback

| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/feedback` | Submit thumbs up/down feedback |
| GET | `/api/feedback/summary?role=admin` | View satisfaction metrics |
| GET | `/api/feedback/flagged?role=admin` | View flagged low-rated responses |
| PATCH | `/api/feedback/{requestId}/dismiss?role=admin` | Mark flagged response as reviewed |

### Knowledge Base

| Method | Endpoint | Description |
|---|---|---|
| GET | `/api/kb/documents?role=` | List indexed documents |
| POST | `/api/documents/upload?role=admin` | Upload document and trigger ingestion |
| DELETE | `/api/documents/delete?role=admin` | Delete document from storage and search index |

### Conversations

| Method | Endpoint | Description |
|---|---|---|
| GET | `/api/conversations?username=` | Retrieve chat history for a user |

### Ingestion & Ops

| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/ingest` | Trigger manual ingestion |
| GET | `/api/health` | Health check for core dependencies |

> Full request and response details can be documented separately in `docs/ui-integration-guide.md`.

---

## Example Use Cases

### 1. Self-service knowledge question
A customer asks how to reset VPN access.
The system retrieves role-allowed documentation, generates an answer, and returns a normal response.

### 2. High-risk incident report
A customer reports a suspicious login.
The system identifies a high-priority issue, begins guided slot filling, and creates a ticket once intake is complete.

### 3. Missing internal KB result
A user asks a question not covered in the internal knowledge base.
The system can offer a confirmed web search fallback before returning a final answer.

---

## Document Ingestion Pipeline

Documents are stored in **Azure Blob Storage**, chunked, embedded, and indexed into **Azure AI Search**.

### Supported formats
- PDF
- DOCX

### Ingestion flow
1. Upload document
2. Store in Blob Storage
3. Chunk text content
4. Generate embeddings
5. Index vectors and metadata in Azure AI Search

### Trigger options
- **Blob event trigger** after upload
- **Manual ingestion** through `POST /api/ingest`
- **Delete trigger** to remove indexed vectors when a file is removed

### Data boundary tagging
Each document is tagged with a `dataBoundary` value:
- `Public`
- `Internal`
- `Confidential`

Retrieval always filters results by the requesting user's allowed access level.

---

## My Contributions

This project was built to demonstrate my ability to design and implement a practical cloud backend for AI-assisted support workflows.

My contributions include:

- Designing the overall backend architecture and workflow boundaries
- Building Azure Functions-based API endpoints in C#
- Implementing role-based retrieval and escalation logic
- Adding guided slot-filling flows for structured incident intake
- Building ticket, feedback, conversation, and document management endpoints
- Integrating Azure AI Search, Blob Storage, Table Storage, and LLM services
- Writing and organizing tests, local HTTP test flows, and project documentation

---

## Project Structure

```text
fortianswer-orchestrator/
├── src/orchestrator/FortiAnswer.Orchestrator/
│   ├── ChatFunction.cs
│   ├── AuthRegisterFunction.cs
│   ├── AuthLoginFunction.cs
│   ├── TicketCreateFunction.cs
│   ├── TicketGetFunction.cs
│   ├── TicketListFunction.cs
│   ├── TicketAdminListFunction.cs
│   ├── TicketUpdateFunction.cs
│   ├── FeedbackFunction.cs
│   ├── FeedbackQueryFunction.cs
│   ├── KbDocumentsFunction.cs
│   ├── AdminDocumentUploadFunction.cs
│   ├── AdminDocumentDeleteFunction.cs
│   ├── ConversationListFunction.cs
│   ├── IngestFunction.cs
│   ├── BlobIngestTriggerFunction.cs
│   ├── BlobDeletedEventGridTriggerFunction.cs
│   ├── SlotSessionCleanupFunction.cs
│   ├── HealthFunction.cs
│   ├── Middleware/
│   ├── Models/
│   └── Services/
├── tests/
│   ├── FortiAnswer.Orchestrator.Tests/
│   ├── Localtest.http
│   └── requests.http
└── docs/
    ├── ui-integration-guide.md
    ├── sprint3-backend-changes-for-li.md
    └── demo-ppt-outline.md
```

---

## Local Development

### Prerequisites

- **.NET 8 SDK**
- **Azure Functions Core Tools v4**
- **Azurite** (optional, for local storage emulation)

### Run locally

```bash
cd src/orchestrator/FortiAnswer.Orchestrator
cp local.settings.template.json local.settings.json
func start
```

### Health check

```text
GET http://localhost:7071/api/health
```

### Stub mode

Use stub mode when running without real Azure or LLM credentials:

```json
{
  "Values": {
    "RETRIEVAL_MODE": "stub",
    "LLM_MODE": "stub"
  }
}
```

### Full mode

Use full mode when connecting to real services:

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

---

## Testing

### Unit tests
- **77 xUnit unit tests** are included in the test project

### HTTP endpoint testing
- `tests/Localtest.http` for local testing
- `tests/requests.http` for deployed environment testing

### Sample smoke test flow

```text
1. Register a user
2. Submit a normal chat request
3. Submit a high-priority issue that triggers slot filling
4. Verify ticket creation and listing
```

---

## Future Improvements

Potential next steps for the project include:

- stronger authentication and authorization flow
- RBAC integration with enterprise identity providers
- richer observability and telemetry dashboards
- Teams or Power Platform integration
- admin-facing workflow dashboard
- more advanced prompt and citation control
- expanded document classification and governance features

---

## Sprint Delivery Summary

| Sprint | Feature | Status |
|---|---|---|
| Sprint 1 | Azure Functions scaffold | Done |
| Sprint 1 | Azure AI Search retrieval | Done |
| Sprint 1 | Groq LLM integration | Done |
| Sprint 1 | Role-based data boundary enforcement | Done |
| Sprint 1 | Auto escalation and ticket creation | Done |
| Sprint 2 | Authentication | Done |
| Sprint 2 | Ticket management | Done |
| Sprint 2 | Conversation history | Done |
| Sprint 2 | Slot filling | Done |
| Sprint 2 | Document ingestion pipeline | Done |
| Sprint 2 | Web search fallback | Done |
| Sprint 3 | Feedback analytics | Done |
| Sprint 3 | Agent/Admin ticket dashboard | Done |
| Sprint 3 | Multi-turn conversation memory | Done |
| Sprint 3 | KB document list, upload, delete | Done |
| Sprint 3 | API key protection and rate limiting | Done |
| Sprint 3 | Health check and dependency validation | Done |
| Sprint 3 | Slot session cleanup | Done |

---

## Notes

For portfolio presentation, this README focuses on:

- business context
- architecture and engineering decisions
- technical scope
- implementation depth
- maintainability and testing

A deeper endpoint-by-endpoint reference can continue to live under the `docs/` folder.
