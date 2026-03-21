# Sprint Status Update — Sprint 2
**Team:** Team 6 — FortiAnswer
**Members:** Doris An, Pingchi Li, Ayomide Anuoluwapo Adewunmi, Muluken Tesfaye Keto
**Sprint Dates:** March 1 – March 20, 2026

---

## Summary of Activities

### Week 1 (March 1–7) — Slot Filling Bug Fix + Ticket API

**Doris (Backend):**
Identified and fixed the slot filling duplicate question bug: the `answer` field in the slot continuation response was mistakenly set to the question text, causing the UI to display it twice — once as a regular chat bubble and once inside the guided intake card. Fixed by clearing the `answer` field during slot continuation so the question only appears in the guided card. Implemented the `POST /api/tickets` endpoint to store ticket data (conversationId, username, issueType, slot answers) in Azure Table Storage. Implemented the slot-to-ticket handoff: after all slots are collected, a ticket is automatically created and the Ticket ID is returned in the escalation response.

**Li (Frontend):**
Investigated the duplicate question rendering on the frontend side. Identified that the UI was rendering `slotFilling.nextQuestion` both as a regular chat bubble and inside the guided intake card. Fixed the rendering logic so the question only appears in the guided card. Polished the guided intake UI: progress bar ("Step 2 / 4"), hint text in the input placeholder, and Skip button behavior.

**Ayo (QA / Testing):**
Wrote a Sprint 2 test plan covering scope, test types, and pass/fail criteria. Authored 8 test cases for slot filling (all issue types, skip behavior, duplicate question check) and 6 test cases for ticket escalation (Customer triggers ticket, Agent views ticket, Ticket ID displayed in UI).

**Keto (QA / Testing + Documentation):**
Wrote 6 test cases for severity tagging and 5 test cases for conversation logging. Set up the test execution log to track pass/fail results and bug reports per feature.

---

### Week 2 (March 8–14) — Safe Response + Logging + UI Escalation

**Doris (Backend):**
Implemented safe response enforcement for restricted topics: the backend detects when a query involves credentials, PII, or sensitive incident details, and returns a predefined safe fallback response instead of attempting retrieval. Implemented conversation logging: each request outcome (answered / escalated / ticket created) is written to Azure Table Storage with requestId and conversationId for audit purposes. Added `GET /api/logs?conversationId=` endpoint for agent and admin review.

**Li (Frontend):**
Built the escalation banner: after slot filling completes, the UI displays the Ticket ID and an "Escalated to security team" message. Built a basic ticket detail view accessible from the chat history. Integrated the frontend with the new tickets and logs API endpoints. Implemented the agent summary card: when `next.action = escalate`, an auto-summary card is displayed for Agent-role users.

**Ayo (QA / Testing):**
Authored 8 test cases for restricted topic / safe response behavior. Executed all slot filling and ticket escalation test cases. Filed 3 GitHub Issues for bugs found: (1) slot question still showing on first step after fix — confirmed and resolved by Li; (2) Ticket ID not displaying on mobile-width screen — fixed by Li; (3) log endpoint returning 500 for missing conversationId — fixed by Doris.

**Keto (QA / Testing + Documentation):**
Executed severity tagging and logging test cases. Drafted the non-technical demo script: a 2-minute plain-English walkthrough of the full chat flow, written for an audience with no IT background.

---

### Week 3 (March 15–20) — End-to-End Testing + Demo Preparation

**Doris (Backend):**
Updated API contract documentation to reflect new endpoints. Performed backend regression testing for all Sprint 2 features. Updated research log with Sprint 2 technical topics. Contributed backend section to this status report.

**Li (Frontend):**
Completed UI regression testing: full demo flow verified across Customer and Agent roles. Updated the UI integration guide. Contributed frontend section to this status report.

**Ayo (QA / Testing):**
Executed final round of restricted topic and severity tagging tests. Compiled test results summary (pass/fail per feature). Contributed testing section and "What we learned — non-technical" to this status report.

**Keto (QA / Testing + Documentation):**
Completed end-to-end regression test: full demo flow (login → chat → slot filling → escalation → ticket) verified with no regressions. Reviewed non-technical demo script with a peer who has no IT background; refined based on feedback. Contributed GAP analysis and STAR lessons sections to this status report.

---

## What Was Delivered

### Expectation vs. Reality

| Planned (Sprint 2 Goal) | Delivered | Status |
|---|---|---|
| Slot filling complete and bug-free (no duplicate questions) | Backend and frontend both fixed; verified across all issue types | ✅ Done |
| Ticket escalation end-to-end (Customer triggers ticket, Ticket ID returned) | `POST /api/tickets` implemented; escalation banner shows Ticket ID in UI | ✅ Done |
| Agent auto-summary card on escalation | Auto-summary card displayed for Agent role when `next.action = escalate` | ✅ Done |
| Safe response for restricted topics | Restricted-topic detection implemented; safe fallback returned for credentials/PII | ✅ Done |
| Conversation logging for audit | Outcomes logged per requestId; `GET /api/logs` endpoint available | ✅ Done |
| All 4 members contributing with logged hours | All members have tasks and hours recorded in sprint tracker | ✅ Done |
| Non-technical demo script | 2-minute plain-English walkthrough written and peer-reviewed | ✅ Done |
| Severity tagging (low/medium/high) | Test cases written; backend rule engine not yet implemented | ⚠️ Partial |

---

## GAP Analysis — What We Planned vs. What We Delivered

| Area | Gap | Root Cause | Corrective Action |
|---|---|---|---|
| Severity tagging (US10) | Test cases ready but backend not implemented | Underestimated complexity of rules engine; deprioritized to avoid blocking other features | Move to Sprint 3 as first priority; Doris to implement in Week 1 |
| Equal contribution (Sprint 1 gap) | All 4 members contributed in Sprint 2; hours logged for everyone | Sprint 1 had no explicit tasks for Ayo and Keto | Resolved: explicit task assignment with hour targets worked |
| Non-technical communication (Sprint 1 gap) | Demo script written and peer-reviewed | Sprint 1 had no non-technical explanation prepared | Resolved: Keto owned the demo script; reviewed before showcase |

---

## What We Learned

### Technical Lessons (STAR Format)

**Lesson 1 — Duplicate rendering can come from both backend and frontend**

- **Situation:** After fixing the backend `answer` field, the slot question still appeared duplicated in the UI during initial testing.
- **Task:** Determine whether the remaining duplication was a frontend rendering issue or a backend problem.
- **Action:** Li traced the frontend rendering logic and found that `slotFilling.nextQuestion` was being added to the message list as a standalone chat bubble in addition to being shown inside the guided intake card. Fixed the rendering condition so it only renders in the card.
- **Result:** Slot questions now display exactly once. This taught us that API contract bugs and UI rendering bugs can produce identical symptoms — both layers need to be investigated independently.

**Lesson 2 — Writing test cases before coding reveals missing edge cases**

- **Situation:** Ayo wrote slot filling test cases in Week 1, before Li had finished the UI fix.
- **Task:** Document expected behavior for all slot types and edge cases (skip, first step, last step).
- **Action:** The test cases identified 2 edge cases that were not in the original fix: (1) the first step still showed duplication because the fix only applied to continuation steps, and (2) the Skip button did not advance the slot index correctly.
- **Result:** Both issues were caught before the demo. Writing test cases ahead of implementation is now a standard practice for our team.

---

### Non-Technical Lessons (STAR Format)

**Lesson 3 — Clearer role boundaries reduced blocking and confusion**

- **Situation:** In Sprint 1, Ayo and Keto did not have clearly defined tasks, which meant they were unsure what to contribute and often waited for Doris and Li to finish before they could start.
- **Task:** Restructure Sprint 2 so all four members have independent tasks from day one.
- **Action:** We assigned Ayo and Keto dedicated QA roles with explicit deliverables (test plans, test cases, bug reports, demo script). Their work did not depend on the backend or frontend being complete — they could write test cases from the API contract and demo script from the user story descriptions.
- **Result:** All four members started contributing from Week 1. Two bugs that would have been missed were caught by Ayo's testing before the sprint showcase.

**Lesson 4 — Explaining the product in plain language requires practice**

- **Situation:** Sprint 1 feedback noted that our presentation was too technical for non-technical stakeholders.
- **Task:** Prepare a version of the demo that anyone could understand, regardless of IT background.
- **Action:** Keto wrote a 2-minute demo script using everyday language (e.g., "the chatbot only shows you information you're allowed to see — like a security badge for knowledge"). The script was reviewed by a peer with no IT background, who asked clarifying questions that helped us improve it further.
- **Result:** The Sprint 2 showcase included a non-technical intro that was well-received. We will apply the same approach to the Sprint 3 final presentation.

---

## What Went Well

- **Bug fix collaboration was efficient.** The slot filling duplicate question bug was identified, fixed on the backend, and confirmed still present on the frontend — all within 2 days. Having Ayo run test cases immediately after each fix shortened the feedback loop.
- **All four members contributed visibly.** For the first time, all members have logged hours, filed or resolved GitHub Issues, and contributed named sections to the status report.
- **QA caught real bugs before the demo.** Ayo found 3 bugs through test execution that would have been visible during the showcase. Filing them as GitHub Issues made them easy to track and resolve.
- **Non-technical demo script landed well.** Keto's plain-English walkthrough was the clearest explanation of the product we have given so far.

---

## What We Will Improve in Sprint 3

| Improvement | Why | SMART Target |
|---|---|---|
| Implement severity tagging (US10) | Was planned for Sprint 2 but not completed | Doris implements backend rule engine by March 28; Ayo writes and executes 6 test cases by March 30 |
| Add user feedback collection (US17) | Sprint 3 goal; helps measure chatbot value | Li adds thumbs up/down rating to UI by April 3; Doris stores feedback by April 3 |
| KB re-index workflow (US16) | Knowledge base can become stale without it | Keto documents re-index steps by April 4; Doris implements schedule trigger by April 6 |
| Maintain non-technical communication standard | Sprint 1 and 2 feedback consistently highlighted this | Every sprint deliverable includes a one-sentence plain-English summary; reviewed by Keto before submission |
| Keep all 4 members contributing equally | Now working — must continue | All members log hours weekly in sprint tracker; reviewed at each standup |

---

## Hours Summary (Sprint 2)

| Team Member | Role | Estimated Hours |
|---|---|---|
| Doris An | Backend / Orchestrator | 22h |
| Pingchi Li | Frontend / Web UI | 18h |
| Ayomide Anuoluwapo Adewunmi | QA / Testing | 23h |
| Muluken Tesfaye Keto | QA / Testing + Documentation | 21h |
