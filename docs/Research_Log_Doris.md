# Research Log — Doris An
**Project:** FortiAnswer (Capstone — Team 6)
**Role:** Backend / Orchestrator Developer

---

## Sprint 1 (Feb 5 – Feb 28, 2026)

---

### Week 1 — Azure Functions & LLM Integration

**Topic: Azure Functions (.NET 8 Isolated Worker)**
- Researched Azure Functions hosting models: in-process vs isolated worker
- Chose isolated worker for better dependency injection support and .NET 8 compatibility
- Studied cold start behavior and how to minimize latency for chat endpoints
- Source: Microsoft Azure Functions documentation

**Topic: Groq LLM API**
- Researched Groq's inference API for low-latency LLM generation
- Compared response speed vs OpenAI API for real-time chat use case
- Learned how to structure system prompts to ground answers in retrieved evidence (RAG pattern)
- Implemented PromptBuilder: evidence-in → grounded answer out, to reduce hallucination
- Source: Groq API docs, RAG pattern literature

**Topic: Retrieval-Augmented Generation (RAG)**
- Studied RAG architecture: how to combine retrieval (search) with LLM generation
- Learned why RAG is preferred over fine-tuning for enterprise KB use cases (freshness, auditability)
- Researched citation design: how to surface source references in the response
- Source: LangChain RAG documentation, academic overview papers

---

### Week 2 — Azure AI Search & RBAC

**Topic: Azure AI Search (Hybrid + Vector Retrieval)**
- Researched vector search vs keyword search vs hybrid search trade-offs
- Chose hybrid search (keyword + vector) for better recall on short IT support queries
- Studied index schema design: how to add a `classification` field for data boundary filtering
- Learned how to use OData filter expressions to enforce role-based retrieval at query time
- Source: Azure AI Search documentation, vector search best practices

**Topic: Role-Based Access Control (RBAC) for AI Retrieval**
- Researched how to enforce data boundaries in retrieval systems (not just UI-level access control)
- Designed three-tier boundary model: Public (Customer) → Internal (Agent) → Confidential (Admin)
- Learned that retrieval-level filtering is more reliable than post-retrieval filtering for compliance
- Source: Microsoft identity and access management docs, enterprise AI security patterns

**Topic: Knowledge Base Design**
- Researched how to structure enterprise FAQ content for vector indexing
- Organized KB entries by classification level (public/internal/confidential) for repeatable ingestion
- Studied chunking strategies for policy documents to maximize retrieval relevance

---

### Week 3 — Web Search, Slot Filling & Security

**Topic: Tavily Web Search API**
- Researched available web search APIs for AI applications (Tavily vs SerpAPI vs Bing Search)
- Chose Tavily for its AI-optimized result format and simple integration
- Designed gating policy: web search only permitted for Public boundary queries
- Studied risk of data leakage if internal queries are allowed to reach external search APIs
- Source: Tavily API documentation

**Topic: Slot Filling / Guided Intake Pattern**
- Researched conversational form-filling patterns (slot filling) used in task-oriented dialogue systems
- Studied how to manage multi-turn session state server-side (vs client-side)
- Designed slot session model: track current slot index, collected values, issue type per conversationId
- Implemented structured escalation: after all slots filled → auto-create ticket + agent summary
- Source: Rasa dialogue management docs, task-oriented dialogue research

**Topic: API Security & CORS**
- Researched CORS (Cross-Origin Resource Sharing) configuration for Azure Functions
- Debugged preflight request failures between frontend (different origin) and backend
- Learned how to configure allowed origins, headers, and methods correctly in Azure Functions host config
- Source: MDN CORS documentation, Azure Functions CORS guide

---

## Sprint 2 (March 1 – March 20, 2026)

---

### Week 1 — Slot Filling Bug Fix & Testing

**Topic: Debugging Duplicate Message Rendering**
- Identified root cause of slot question duplication: `answer` field and `slotFilling.nextQuestion` both containing the question text
- Researched API contract design best practices — separation of concerns between display text and structured data fields
- Fix: cleared `answer` field during slot continuation so question only appears in guided intake card

**Topic: End-to-End Testing for Conversational AI**
- Researched how to write HTTP-based integration tests for multi-turn conversational flows
- Studied session state testing: verifying that `conversationId` correctly maintains slot state across turns
- Designed 5 demo test cases covering: login, RAG retrieval, web search fallback, slot filling, and agent role access

---

## AI Tools Used

| Tool | Purpose | How I verified the output |
|---|---|---|
| ChatGPT | Troubleshoot CORS errors, draft backend logic ideas, API response design | Tested locally, checked logs, adjusted to fit project needs |
| ChatGPT | Help structure project documents (API contract, status report wording) | Reviewed and rewrote sections that didn't match our actual implementation |
| Claude Code | Code analysis, slot filling bug diagnosis, research log drafting | Cross-checked against actual codebase before accepting suggestions |

---

## Key Concepts Learned

| Concept | Why It Matters to This Project |
|---|---|
| RAG (Retrieval-Augmented Generation) | Ensures answers are grounded in approved KB, not hallucinated |
| Hybrid vector search | Better retrieval accuracy for short IT support queries |
| Retrieval-level RBAC | More reliable than UI-level access control for compliance |
| Slot filling (task-oriented dialogue) | Structures incident intake without free-form ambiguity |
| Web search gating | Prevents internal data from leaking to external APIs |
| Azure Functions isolated worker | Better .NET 8 support, cleaner dependency injection |
