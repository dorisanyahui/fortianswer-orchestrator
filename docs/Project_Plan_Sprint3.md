# Project Plan — Sprint 3
**Team:** Team 6 — FortiAnswer
**Members:** Doris An, Pingchi Li, Ayomide Anuoluwapo Adewunmi, Muluken Tesfaye Keto
**Sprint Dates:** March 21 – April 10, 2026

---

## Sprint 3 Goal

**Close out the MVP.** Every user story that was committed across Sprint 1 and 2 must be verifiable by the end of this sprint. No new features — only finish what was planned and fix what is broken.

---

## 1. MVP Completion Checklist

These are the stories that still need work going into Sprint 3.

| User Story | Description | Backend Status | UI Status | Sprint 3 Owner |
|---|---|---|---|---|
| US10 | Severity tagging (P1–P4 colour labels on tickets) | ✅ Done — `DerivePriority()` already implemented | ❌ UI not showing priority colours | Li |
| US17 | User feedback (thumbs up / thumbs down on answers) | ❌ No endpoint | ❌ No UI | Doris (endpoint) + Li (UI) |
| US16 | KB re-index when documents are updated | 🔶 Manual trigger exists (`POST /api/ingest`) | ❌ No admin UI trigger | Doris (verify) + Li (button) |
| Agent ticket dashboard | Agent/Admin can see all tickets, assign, change status | ✅ Done (`GET /api/admin/tickets`, `PATCH /api/tickets/{id}`) | ❌ No UI | Li |

---

## 2. Sprint 3 Backlog

### Doris — Backend

| Task | Story | Est. Hours |
|---|---|---|
| Add `POST /api/feedback` endpoint — store thumbs up/down + optional comment per `requestId` to Table Storage | US17 | 3h |
| Verify `BlobIngestTriggerFunction` works end-to-end with a real document upload; document any gaps | US16 | 2h |
| Regression test all endpoints before final demo | All | 2h |
| Update API contract + ui-integration-guide with feedback endpoint | US17 | 1h |
| Sprint 3 status report — backend section | — | 1h |
| **Total** | | **~9h** |

### Li — Frontend / Web UI

| Task | Story | Est. Hours |
|---|---|---|
| Add priority colour badge to ticket cards (P1=Red, P2=Orange, P3=Yellow, P4=Grey) | US10 | 2h |
| Add thumbs up / thumbs down button to each chat answer; call `POST /api/feedback` on click | US17 | 3h |
| Build Agent/Admin ticket dashboard — simple list view with filter by status/priority; call `GET /api/admin/tickets` | Agent dashboard | 4h |
| Add "Assign to me" + status dropdown to ticket detail view; call `PATCH /api/tickets/{id}` | Agent dashboard | 3h |
| Add "Re-index KB" button in Admin panel; call `POST /api/ingest` | US16 | 1h |
| Sprint 3 status report — frontend section | — | 1h |
| **Total** | | **~14h** |

### Ayo — QA / Testing

| Task | Story | Est. Hours |
|---|---|---|
| Write 4 test cases for priority colour display (each P level shows correct colour) | US10 | 2h |
| Write 4 test cases for feedback endpoint (thumbs up, thumbs down, missing requestId, duplicate feedback) | US17 | 2h |
| Write 4 test cases for agent ticket dashboard (filter by status, filter by priority, assign ticket, close ticket) | Agent dashboard | 2h |
| Execute all Sprint 3 test cases; file GitHub Issues for failures | All | 3h |
| End-to-end regression test — full demo flow: login → chat → slot filling → escalation → ticket → agent assigns | All | 2h |
| Sprint 3 status report — testing section | — | 1h |
| **Total** | | **~12h** |

### Keto — QA / Testing + Documentation

| Task | Story | Est. Hours |
|---|---|---|
| Execute KB re-index test (upload new doc → trigger ingest → verify it appears in chat answers) | US16 | 2h |
| Update non-technical demo script for Sprint 3 final showcase (add agent dashboard and feedback sections) | — | 2h |
| GAP analysis — Sprint 2 vs plan; Sprint 3 corrective actions | — | 2h |
| Research log — 2 entries: ITSM ticket workflow, user feedback loops in chatbots | — | 2h |
| Sprint 3 status report — GAP analysis + non-technical lessons sections | — | 2h |
| **Total** | | **~10h** |

---

## 3. What We Are NOT Doing in Sprint 3

| Item | Reason |
|---|---|
| JWT / real token auth | Scope too large; demo environment does not require it |
| US14 — Fast policy publishing workflow | Out of scope for MVP |
| Advanced analytics / dashboards | Not in original backlog |
| DataBoundary server-side derivation | Manual ticket creation is demo-only; not production risk |

---

## 4. Feedback Endpoint — Quick Spec for Li

**`POST /api/feedback`**

```json
{
  "requestId": "ccf3fed0b5d545b8ae5993ffab2a952e",
  "username": "alice",
  "rating": "up"
}
```

| Field | Required | Values |
|---|---|---|
| `requestId` | Yes | The `requestId` from the chat response |
| `username` | Yes | Logged-in username |
| `rating` | Yes | `"up"` or `"down"` |

Response `201 Created`:
```json
{ "recorded": true }
```

---

## 5. Risk Log

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Li has too many UI tasks | Medium | Medium | Agent dashboard is the priority; feedback UI is simpler, do it second |
| KB re-index not working in prod environment | Low | Medium | Doris verifies with a real upload in Week 1; if broken, fix before Li builds the button |
| Sprint 3 demo preparation left too late | Low | High | Demo script updated by Keto in Week 2; dry run by April 8 |

---

## 6. Sprint 3 Timeline

| Week | Dates | Focus |
|---|---|---|
| Week 1 | March 21–27 | Doris: feedback endpoint + KB verify. Li: priority colours + start ticket dashboard |
| Week 2 | March 28 – April 3 | Li: finish dashboard + feedback UI. Ayo: test case execution. Keto: demo script update |
| Week 3 | April 4–10 | Regression testing, bug fixes, final demo prep, status report |
