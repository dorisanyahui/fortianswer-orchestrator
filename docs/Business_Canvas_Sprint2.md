# Business Canvas — Team 6: FortiAnswer
**Updated for Sprint 2 review**

---

## Part One: The Template

---

### Problem Statement

As enterprise products and policies keep growing, support that relies only on humans becomes inconsistent, slower, and more expensive. A big reason is that company knowledge is spread across different places (policy docs, wikis, shared drives, past tickets), so different agents may give different answers to the same question. That inconsistency makes customers wait longer and lowers trust in the brand.

---

### Solution Statement

We built a proof-of-concept enterprise knowledge chatbot called **FortiAnswer** using Retrieval-Augmented Generation (RAG). The chatbot answers IT security support questions using approved internal knowledge — not guessing — and shows citations so users can see exactly where the answer came from. It enforces role-based access so each user only sees information they are allowed to see. When the bot cannot resolve an issue, it escalates by collecting required incident details through guided questions, creating a support ticket, and generating a summary for the agent to pick up quickly.

---

### End Users

- **Customers** — employees asking IT security questions and getting support
- **Service Agents / SOC Analysts** — handling escalations and reviewing tickets
- **IT / Security Admins** — managing role-based access and knowledge base content

---

### Other Stakeholders

- **Doris An** — Backend lead; responsible for orchestrator, API design, and Azure integration
- **Pingchi Li** — Frontend lead; responsible for web UI and user experience
- **Ayomide Anuoluwapo Adewunmi** — QA and testing; responsible for test cases and bug reporting
- **Muluken Tesfaye Keto** — QA and documentation; responsible for test execution and demo preparation
- **Shawn** (Course Instructor) — project oversight, sprint feedback, and final evaluation

---

### Competitive Analysis

| Competitor | What they do |
|---|---|
| Traditional chatbots (rule-based/FAQ bots) | Good for simple Q&A but hard to maintain; fail when questions change or fall outside the script |
| Generic LLM chat tools | Can generate fluent responses but do not use the company's real policies and cannot guarantee cited, traceable sources |
| ServiceNow Virtual Agent | Chatbot integrated with ITSM workflows and ticketing |
| Intercom Fin | AI support bot for customer service with help center integration |
| Microsoft Copilot Studio | Low-code chatbot builder with Microsoft 365 connectors |

---

### Differentiator

Our solution is different because it is built for **enterprise knowledge governance**, not just chat:

- Answers are grounded in internal approved content and always include **citations** so users can verify the source
- **Role-based access control (RBAC)** ensures each user only retrieves content at their permission level — enforced at the retrieval layer, not just the UI
- **Confidence-based escalation** — the bot does not guess; when it cannot resolve an issue, it escalates with a structured summary instead of giving an uncertain answer
- **Guided incident intake (slot filling)** collects all required details before creating a ticket, reducing back-and-forth between agents and customers

---

### Elevator Pitch

*"Tell me about what you did for capstone."*

> "For our capstone, we built an AI-powered IT security support chatbot called FortiAnswer. The problem we were solving is that in large organizations, support is often inconsistent — different people give different answers because company knowledge is scattered across documents, wikis, and past tickets.
>
> Our solution uses a technique called Retrieval-Augmented Generation, which means the chatbot answers questions by pulling from the company's own approved documents rather than making things up. Every answer includes a citation so users can see exactly where it came from.
>
> What makes it different from a regular AI chatbot is that it respects the company's security rules — different employees see different information depending on their role. And when the chatbot can't solve something, it walks the user through a set of structured questions to collect the incident details, then creates a support ticket automatically so a human agent can take over.
>
> It's a proof of concept, but it demonstrates the core idea: enterprise AI that is trustworthy, auditable, and safe."

---

### Assumptions

Since this is a **proof of concept**, we are not evaluating production readiness or user adoption. The following assumptions apply to our ability to build and demo the system:

1. Azure services needed for the project (Azure Functions, Azure AI Search, Azure Table Storage) are available and accessible to the team.
2. We can use an existing LLM API (Groq) — we are not training a new model from scratch.
3. Since this is a proof of concept with no real client, sample FAQ/policy content is created by the team for demo purposes, organized into public, internal, and confidential categories.
4. Basic authentication and role-based access control can be implemented at the API layer for the MVP.
5. We can define rule-based triggers for escalation (e.g., issue type = Suspicious Login always triggers guided intake).
6. Azure Table Storage is sufficient for storing tickets and conversation logs for the MVP demo scope.

---

## Part Two: Product Backlog

The backlog below reflects the full feature set. **High priority = MVP** (must be demonstrated). Medium and Low priority items are planned for later sprints or post-MVP.

**MVP is scoped to 7 High priority stories** based on instructor feedback to keep the MVP clear and achievable.

| Story ID | As a… | I want to be able to… | So that… | Priority | Sprint | Status |
|---|---|---|---|---|---|---|
| US1 | Customer | get cybersecurity support answers 24/7 from the chatbot | I don't need to wait for business hours | **High** | Sprint 1 | Done |
| US2 | Customer | ask questions in natural language (not exact keywords) | I can explain my issue easily | **High** | Sprint 1 | Done |
| US3 | Customer | see the source/citation used in the answer | I can trust the guidance and follow the right steps | **High** | Sprint 1 | Done |
| US4 | Customer | choose a request type (phishing, suspicious login, VPN, MFA, endpoint alert) | the chatbot can guide me faster | **High** | Sprint 1 | Done |
| US9 | Service Agent / SOC Analyst | collect required incident details through guided questions (slot filling) | tickets are complete and easier to triage | **High** | Sprint 2 | Done |
| US11 | IT / Security Admin | enforce separation between enterprise data and public AI | sensitive data is protected | **High** | Sprint 1 | Done |
| US12 | IT / Security Admin | restrict internal KB access by role/group (RBAC) | users only see approved content for them | **High** | Sprint 1 | Done |
| US5 | Customer | report a phishing email by pasting sender/subject/URL indicators | the SOC can triage it quicker | Medium | Sprint 2 | Planned |
| US6 | Customer | create a security support ticket if my issue is not resolved | I still get help even when the bot can't solve it | Medium | Sprint 2 | Done |
| US7 | Service Agent / SOC Analyst | have repetitive FAQs handled by the chatbot | I can focus on investigation work | Medium | Sprint 1 | Done |
| US8 | Service Agent / SOC Analyst | receive an auto-summary when a chat escalates | I don't need to ask the same questions again | Medium | Sprint 2 | Done |
| US10 | SOC Analyst | tag the request with severity (low/medium/high) based on rules | high-risk cases get handled faster | Medium | Sprint 3 | Planned |
| US13 | Compliance Officer | require safe responses for restricted topics (credentials, sensitive incident details) | we reduce compliance and privacy risk | Medium | Sprint 2 | Done |
| US14 | Business Manager / Policy Owner | publish new or updated policies/playbooks to the knowledge base quickly | the chatbot stays aligned with the latest rules | Medium | Sprint 3 | Planned |
| US15 | Knowledge Base Owner | upload and organize runbooks/FAQs/playbooks for indexing | the bot has enough approved content to answer | Medium | Sprint 1 | Done |
| US16 | Knowledge Base Owner | refresh/re-index content on a schedule or after updates | answers don't become outdated | Medium | Sprint 3 | Planned |
| US17 | Customer | rate the answer (helpful / not helpful) and leave feedback | we can improve the bot over time | Medium | Sprint 3 | Planned |
| US18 | Business Manager | view basic analytics (top questions, deflection, escalation rate) | we can measure the value of the chatbot | Medium | Sprint 3 | Planned |
| US19 | IT / Security Admin | log conversations and outcomes (answered/escalated/ticket created) | we can audit and troubleshoot issues | Medium | Sprint 2 | Done |
| US20 | Service Agent Lead | maintain a set of "approved responses" for common security issues | answers stay consistent across the team | Low | Sprint 3 | Planned |
| US21 | Customer | upload a screenshot or error message during chat | troubleshooting becomes easier | Low | Sprint 3 | Planned |

---

## Major Milestones

1. Finalize scope, MVP user stories, and success criteria
2. Create and organize sample FAQ/policy content by classification (public/internal/confidential)
3. Build RAG prototype: search + answer + citations on Azure
4. Add governance features: data boundaries, RBAC, escalation workflow, slot filling
5. Testing, demo recording, and final documentation

---

## Technical Skills Required

1. RAG design (chunking, embeddings, hybrid retrieval)
2. Prompt design and response formatting (citations, safe responses)
3. Azure deployment (Functions, AI Search, Table Storage)
4. API design and integration
5. Role-based access control at the retrieval layer
6. Logging and telemetry (audit trail, outcome tracking)
7. Web UI integration (chat interface, guided intake, escalation display)

---

## Industry Knowledge Required

1. Cybersecurity support workflows (SOC, helpdesk, MDR) — how security issues are triaged and escalated
2. Common security incident types: phishing, suspicious login, VPN issues, MFA failures, endpoint alerts
3. Familiarity with security terminology: SIEM, EDR/XDR, MFA, VPN, IOCs
4. Security and privacy governance: data access control, least privilege, sensitive data handling
5. Knowledge base management: keeping playbooks and SOPs accurate

---

## Professional Skills Required

1. Team communication and task planning across 4 members
2. Scope control — MVP first, avoid feature creep
3. Documentation writing (API contract, integration guide, status reports)
4. Stakeholder communication (instructor feedback, sprint reviews)
5. Testing and validation mindset (accuracy, edge cases, bug reporting)
6. Presentation and demo storytelling for non-technical audiences
7. Time management within a fixed 9-week course schedule

---

## Constraints

1. Fixed course schedule: 9 weeks across 3 sprints
2. Limited budget: must use free-tier or low-cost Azure services
3. This is a proof of concept — not a production system; no real enterprise data is used
4. Model training is not realistic — using existing LLM APIs only
5. Team capacity limited to class hours plus ~4–7 hours per member per week outside class
6. Must not expose sensitive or real user data in responses or logs
