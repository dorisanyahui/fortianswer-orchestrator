# Project Plan — Sprint 2
**Team:** Team 6 — FortiAnswer
**Members:** Doris An, Pingchi Li, Ayomide Anuoluwapo Adewunmi, Muluken Tesfaye Keto
**Sprint Dates:** March 1 – March 20, 2026

---

## 1. Revised MVP Backlog

Based on Sprint 1 feedback, we have narrowed the MVP to the core stories that define our product's unique value. Stories previously listed as MVP but not critical to the core experience have been moved to "Should Do."

### Must Do (MVP Core)

| User Story ID | As a… | I want to be able to… | So that… |
|---|---|---|---|
| US1 | Customer | get cybersecurity support answers 24/7 from the chatbot | I don't need to wait for business hours |
| US2 | Customer | ask questions in natural language | I can explain my issue easily |
| US4 | Customer | choose a request type (phishing, suspicious login, VPN, MFA, endpoint alert) | the chatbot can guide me faster |
| US9 | Service Agent / SOC Analyst | collect required incident details through guided questions (slot filling) | tickets are complete and easier to triage |
| US11 | IT / Security Admin | enforce separation between enterprise data and public AI | sensitive data is protected |
| US12 | IT / Security Admin | restrict internal KB access by role/group (RBAC) | users only see approved content |

### Should Do (Sprint 2 additions, if capacity allows)

| User Story ID | As a… | I want to be able to… | So that… |
|---|---|---|---|
| US6 | Customer | create a security support ticket if my issue is not resolved | I still get help even when the bot can't solve it |
| US10 | SOC Analyst | tag the request with severity (low/medium/high) based on rules | high-risk cases get handled faster |
| US13 | Compliance Officer | require safe responses for restricted topics | we reduce compliance and privacy risk |

### Could Do (Future sprints)

| User Story ID | Description |
|---|---|
| US8 | Auto-summary for agents when a chat escalates |
| US14 | Fast policy publishing workflow |
| US19 | Conversation logging for audit/troubleshooting |

---

## 2. Sprint 2 Goal (SMART)

| # | Goal | How We Measure Success |
|---|---|---|
| 1 | Ticket escalation working end-to-end | Customer can trigger escalation and receive a Ticket ID in the UI; verified by test with Customer role by March 10 |
| 2 | Slot filling complete and bug-free | Guided intake runs all 4 steps for "Suspicious Login" without duplicate questions; verified by demo recording by March 14 |
| 3 | All 4 team members have logged tasks and hours | Sprint tracker shows ≥ 20 hours per member by March 20 |
| 4 | Non-technical explanation ready | A 2-minute plain-English demo script completed, reviewed by a non-technical peer by March 18 |

---

## 3. Project Management Methodology

We follow **Agile Scrum** across 3 sprints. Below is how each Scrum ceremony maps to a specific tool and action.

### Tool Mapping

| Scrum Ceremony | Tool | Specific Action |
|---|---|---|
| **Product Backlog** | Sprint Tracking Spreadsheet (Excel) | Each User Story is one row with Priority, Owner, Status, and Estimated Hours |
| **Daily Standup** | Microsoft Teams (text message) | Each member posts 3 lines daily: (1) Done since last check-in, (2) Doing next, (3) Blockers |
| **Sprint Planning** | Teams video call + Excel | Select stories, break into tasks ≤ 3 hours, assign owners, estimate hours (target 25–30 hrs/member) |
| **Sprint Review / Demo** | Teams video call + screen recording | Demo completed stories against acceptance criteria; update Status = Done in spreadsheet |
| **Sprint Retrospective** | Teams video call | 20 min: What went well / What didn't / 2–3 action items for next sprint |
| **Technical Task Tracking** | GitHub Issues + Pull Requests | Each development task = one GitHub Issue; PR references the Issue when closed |
| **Document Version Control** | OneDrive | All documents saved with date suffix (e.g., `_20260310`); no local-only copies |

### Daily / Weekly / Sprint Rhythm

**Daily (10–15 min, Teams text standup):**
- What I completed since last check-in
- What I will do next
- Blockers or risks
- Update hours in the sprint spreadsheet

**Weekly (45–60 min, Teams video):**
- Demo current build or key artifact
- Review backlog: adjust priorities, confirm what is on track
- Review risk log
- Confirm deliverables due that week (docs, research log, showcase)

**Sprint Ceremonies:**
- Sprint Planning (March 1): define goal, select stories, break into tasks
- Mid-Sprint Check (March 10): demo progress, flag risks early
- Sprint Review / Showcase (March 19): demonstrate completed stories
- Sprint Retrospective (March 20): lessons learned + action items

---

## 4. Sprint 2 Backlog

### User Stories Committed for Sprint 2

| User Story ID | As a… | I want to be able to… | So that… |
|---|---|---|---|
| US6 | Customer | create a security support ticket if my issue is not resolved | I still get help even when the bot can't solve it |
| US8 | Service Agent / SOC Analyst | receive an auto-summary when a chat escalates | I don't need to ask the same questions again |
| US9 | Service Agent / SOC Analyst | collect required incident details through guided questions (slot filling) | tickets are complete and easier to triage |
| US10 | SOC Analyst | tag the request with severity (low/medium/high) based on rules | high-risk cases get handled faster |
| US13 | Compliance Officer | require safe responses for restricted topics | we reduce compliance and privacy risk |
| US19 | IT / Security Admin | log conversations and outcomes | we can audit and troubleshoot issues |

---

## 5. Sprint 2 Resource Plan

Each team member is assigned stories and tasks with clear ownership. All tasks are broken into sub-tasks of ≤ 3 hours each. Target: **25–30 hours per member**.

**Role summary:**
- **Doris** — Backend / Orchestrator (API, business logic, data layer)
- **Li** — Frontend / Web UI (chat interface, guided intake UI, escalation display)
- **Ayo** — QA / Testing (test case design, manual testing, bug reporting)
- **Keto** — QA / Testing + Documentation (test execution, demo script, research log, status report)

---

### Doris An — Backend / Orchestrator

**Assigned Stories:** US6, US9, US13, US19

| Task | Est. Hours |
|---|---|
| US9 — Fix slot filling duplicate question bug (patch `answer` field in ChatFunction.cs; verify all 4 steps) | 3h |
| US6 — Implement `POST /api/tickets` endpoint (store conversationId, username, issueType, slotSummary) | 3h |
| US6 — Slot-to-ticket handoff (auto-create ticket after all slots collected; return ticketId in response) | 2h |
| US13 — Safe response enforcement (restricted-topic detection; return fallback for credentials/sensitive details) | 3h |
| US19 — Conversation logging (log outcome per requestId to Azure Table Storage: answered / escalated / ticket) | 3h |
| US19 — Audit log retrieval endpoint (`GET /api/logs?conversationId=`) | 2h |
| Update API contract doc to reflect new endpoints | 2h |
| Update research log with Sprint 2 technical topics | 2h |
| Sprint status report — backend section | 2h |
| **Total** | **~22h** |

---

### Pingchi Li — Frontend / Web UI

**Assigned Stories:** US6, US9, US8

| Task | Est. Hours |
|---|---|
| US9 — Fix duplicate question rendering (ensure `nextQuestion` only renders in guided card, not as extra chat bubble) | 3h |
| US9 — Guided intake UI polish (progress bar, hint placeholder text, Skip button) | 3h |
| US6 — Escalation banner (show Ticket ID + "Escalated to security team" message after slot filling completes) | 2h |
| US6 — Ticket detail view (simple view of ticketId, status, summary accessible from chat) | 3h |
| US8 — Agent summary card (display auto-summary when `next.action = escalate` for Agent role) | 2h |
| Integration with backend new endpoints (tickets API, logs API) | 3h |
| Sprint status report — frontend section | 2h |
| **Total** | **~18h** |

---

### Ayomide Anuoluwapo Adewunmi — QA / Testing

**Focus:** Design and execute test cases for all Sprint 2 features; report bugs to Doris and Li via GitHub Issues.

| Task | Est. Hours |
|---|---|
| Write test plan for Sprint 2 (scope, test types, pass/fail criteria) | 3h |
| US9 — Slot filling test cases: write 8 scenarios (all issue types, skip behavior, duplicate question check) | 3h |
| US6 — Ticket escalation test cases: write 6 scenarios (Customer triggers ticket, Agent views ticket, ticketId displayed in UI) | 3h |
| US13 — Restricted topic test cases: write 8 scenarios (credentials, PII, sensitive incident details; verify safe response returned) | 3h |
| Manual test execution — slot filling (run all test cases; log results; file GitHub Issues for any failures) | 3h |
| Manual test execution — ticket escalation and safe response (run all test cases; log results) | 3h |
| Research log — log 3 entries: software testing best practices, chatbot QA techniques, how to test AI response safety | 3h |
| Sprint status report — testing section (what was tested, pass/fail summary, bugs found) | 2h |
| **Total** | **~23h** |

---

### Muluken Tesfaye Keto — QA / Testing + Documentation

**Focus:** Test execution, non-technical demo preparation, sprint documentation.

| Task | Est. Hours |
|---|---|
| US10 — Severity tagging test cases: define low/medium/high rules per issue type; write 6 test scenarios | 3h |
| US19 — Logging test cases: verify conversation outcomes are logged correctly (answered / escalated / ticket); write 5 scenarios | 2h |
| Manual test execution — severity tagging and logging (run test cases; log results; file GitHub Issues) | 3h |
| End-to-end regression test (run full demo flow: login → chat → slot filling → escalation → ticket; verify no regressions) | 3h |
| Non-technical demo script (write 2-minute plain-English walkthrough of full chat flow for non-technical audience) | 3h |
| Demo rehearsal and peer review (review script with a non-technical peer; refine based on feedback by March 16) | 2h |
| Research log — log 3 entries: chatbot usability testing, ITSM ticketing concepts, IT security incident handling basics | 3h |
| Sprint status report — GAP analysis and non-technical lessons sections (STAR format) | 2h |
| **Total** | **~21h** |

---

## 6. GAP Analysis — Sprint 1 vs Plan

| Planned | Delivered | GAP | Corrective Action |
|---|---|---|---|
| All 4 members contributing equally | Doris and Li did majority of technical work; Ayo and Keto contributions not visible in logs | Hours imbalance | Sprint 2: Ayo and Keto have explicit tasks with hour targets; all members update tracker weekly |
| Research Log maintained by all members | Only Doris and Li in AI log; no research log submitted | Missing artifact | Sprint 2: Research log created (Doris), all members add entries by March 10 |
| Slot filling working end-to-end | Slot filling implemented but duplicate question bug in UI | Minor bug | Fixed in backend (ChatFunction.cs); frontend fix in progress |
| SCRUM ceremonies clearly documented | Tools listed but ceremony-to-tool mapping unclear | Documentation gap | Added explicit mapping table in this document (Section 3) |

---

## 7. Stakeholder Communication Plan

| Stakeholder | What They Need to Know | Frequency |
|---|---|---|
| Instructor | Sprint 2 progress, completed stories, demo readiness, blockers | Weekly update + end-of-sprint demo (March 19) |
| Product Owner (simulated) | Confirm escalation workflow meets needs; approve severity rules | Mid-sprint review (March 10) + end-of-sprint demo |
| Internal Team | Integration checkpoints (UI ↔ API ↔ KB), testing status, demo script readiness | 2–3x per week via Teams standup |
| Non-technical peer reviewer | Plain-English demo walkthrough for feedback on clarity | Once during sprint (target March 16) |

---

## 8. Non-Technical Lessons (Sprint 1)

*Using STAR format as recommended by instructor.*

**Situation:** Our Sprint 1 presentation was too technical — audience unfamiliar with AI/security had difficulty following.

**Task:** We needed to communicate the value of FortiAnswer to non-technical stakeholders (managers, compliance officers, end users).

**Action:** We identified that every technical feature needs a plain-English "so what" statement. For example, instead of "we implemented hybrid vector retrieval with RBAC filtering," we say: "the chatbot only shows each employee the information they are allowed to see — like a security badge for knowledge."

**Result:** For Sprint 2, we are preparing a 2-minute non-technical demo script and will test it with a peer who has no IT background before the showcase.

---

## 9. Risk Log

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Ayo / Keto tasks not completed again | Medium | High | Weekly check-in by March 8; if blocked, reassign by March 10 |
| Frontend slot bug not resolved in time for demo | Low | Medium | Backend fix already done; UI fix assigned to Li with March 10 deadline |
| Non-technical demo not ready | Medium | Medium | Assigned to Li; draft by March 14, review by March 16 |
| Azure service costs exceed free tier | Low | Low | Monitor usage; use stub mode for local dev |
