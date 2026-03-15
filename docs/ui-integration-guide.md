# FortiAnswer — UI Integration Guide
**For:** Web UI Developer (Li)
**Last updated:** 2026-03-14

**baseUrl:** = https://func-fortianswer-gccvakhgayenbdak.canadacentral-01.azurewebsites.net

---

## Overview

The backend exposes four groups of endpoints. As a UI developer, you mainly work with:

| Group | Endpoints | Purpose |
|---|---|---|
| Auth | `/api/auth/register`, `/api/auth/login` | User accounts |
| Chat | `/api/chat` | AI-powered Q&A with escalation |
| Tickets | `POST /api/tickets`, `GET /api/tickets/{id}`, `GET /api/tickets?username=` | Escalation tickets |
| Conversations | `GET /api/conversations?username=` | Conversation history per user |

---

## 1. Auth

### Register — `POST /api/auth/register`

**Request:**
```json
{
  "username": "pingchili",
  "password": "P@ssw0rd123",
  "email": "pingchili@edu.sait.ca",
  "company": "F12.net",
  "telephone": "1234567890",
  "role": "Admin"
}
```

**Rules:**
- `username`, `password` required
- Password must be **at least 10 characters**
- `role` values: `"Customer"` | `"Agent"` | `"Admin"` (defaults to `"Customer"` if omitted)

**Responses:**
| Status | Meaning |
|---|---|
| 201 Created | User created successfully |
| 400 Bad Request | Missing fields or weak password |
| 409 Conflict | Username already exists |

---

### Login — `POST /api/auth/login`

**Request:**
```json
{
  "username": "pingchili",
  "password": "P@ssw0rd123"
}
```

**Response 200:**
```json
{
  "authenticated": true,
  "username": "pingchili",
  "role": "Admin"
}
```

> **Important for UI:** Store the returned `username` and `role` in session/local state. You will need to pass `username` to the chat and ticket endpoints.

**Responses:**
| Status | Meaning |
|---|---|
| 200 OK | Login successful |
| 401 Unauthorized | Wrong password, unknown user, or disabled account |

---

## 2. Chat — `POST /api/chat`

This is the main endpoint. The UI sends a user message and receives an AI answer, citations, and an optional escalation ticket.

### Request Body

```json
{
  "message": "My VPN won't connect. What should I try?",
  "issueType": "VPN",
  "userRole": "Customer",
  "username": "pingchili",
  "dataBoundary": "Public",
  "conversationId": "conv-abc123"
}
```

| Field | Required | Notes |
|---|---|---|
| `message` | Yes | The user's question |
| `issueType` | Recommended | See issue type table below |
| `userRole` | Recommended | `"Customer"` / `"Agent"` / `"Admin"` — drives data access |
| `username` | Recommended | Logged-in username — used to attribute auto-created tickets |
| `dataBoundary` | Optional | Let the server decide based on role (see boundary table) |
| `conversationId` | Optional | Pass the same ID to group messages in one session. Server auto-generates if omitted |

---

### Role → Data Boundary Mapping

The UI should send `dataBoundary` matching the user's role. The server enforces the ceiling — a Customer can never access Internal content even if the UI sends it.

| Role | Send `dataBoundary` | What the user can access |
|---|---|---|
| `Customer` | `"Public"` | Public knowledge base only |
| `Agent` | `"Internal"` | Public + Internal knowledge base |
| `Admin` | `"Confidential"` | Public + Internal + Confidential |

> **Never send `"Restricted"` from the UI.** The server automatically forces Restricted when the `issueType` requires it (see below).

---

### Issue Types

Send `issueType` based on what the user selects in the UI. This controls tone, routing, and escalation.

| `issueType` value | User-facing label | Auto-forces Restricted? |
|---|---|---|
| `"VPN"` | VPN Issues | No |
| `"MFA"` | MFA / Two-Factor | No |
| `"PasswordReset"` | Password Reset | No |
| `"Phishing"` | Phishing / Suspicious Email | No |
| `"AccountLockout"` | Account Lockout | No |
| `"EndpointAlert"` | Endpoint / Device Alert | No |
| `"SuspiciousLogin"` | Suspicious Login | **Yes — always escalates** |
| `"Severity"` | Active Incident / Severity | **Yes — always escalates** |
| `"General"` | General / Other | No |

> When `issueType` is `"SuspiciousLogin"` or `"Severity"`, the server **always** treats the request as Restricted and auto-creates a ticket — regardless of the user's role or `dataBoundary`. The UI does not need to do anything special; just send the issueType and handle the escalation response.

---

### Response Body

```json
{
  "requestId": "ccf3fed0b5d545b8ae5993ffab2a952e",
  "conversationId": "conv-ccf3fed0",
  "answer": "Here are the steps to troubleshoot your VPN...",
  "citations": [
    {
      "title": "VPN Troubleshooting Guide",
      "urlOrId": "public/faq-vpn.docx",
      "snippet": "score=0.91"
    }
  ],
  "needsWebConfirmation": false,
  "webSearchToken": null,
  "next": {
    "action": "none"
  },
  "escalation": {
    "shouldEscalate": false,
    "reason": ""
  },
  "mode": {
    "retrieval": "azureaisearch",
    "llm": "groq"
  }
}
```

---

### The `next` Field — What the UI Should Do

This is the key field for driving UI behaviour after a chat response.

| `next.action` | Meaning | What the UI should do |
|---|---|---|
| `"none"` | Normal answer | Display `answer` and `citations` |
| `"escalate"` | Ticket was auto-created | Display `answer`, show ticket banner with `next.ticketId` |
| `"slot_filling"` | Bot is collecting incident details | Enter guided mode — show `slotFilling.nextQuestion` as the next prompt |
| `"suggest_escalate"` | *(future)* AI suggests escalation | Show a soft prompt asking user to confirm |

**Escalation response example:**
```json
{
  "answer": "This request falls under Restricted content...",
  "next": {
    "action": "escalate",
    "ticketId": "cbc484ec1cd9"
  },
  "escalation": {
    "shouldEscalate": true,
    "reason": "Restricted content requires escalation."
  }
}
```

**Suggested UI for escalation:**
```
┌─────────────────────────────────────────────────────┐
│  ⚠ Your request has been escalated                  │
│  Ticket ID: cbc484ec1cd9                            │
│  An authorized responder will follow up with you.   │
│  [ View Ticket ]                                    │
└─────────────────────────────────────────────────────┘
```

---

### Web Search Confirmation Flow

When the internal knowledge base has no relevant results for a Public query, the server may ask the user to confirm a web search.

**Step 1 — Server asks for confirmation:**
```json
{
  "needsWebConfirmation": true,
  "webSearchToken": "eyJleH...",
  "answer": "Internal knowledge base did not return enough evidence. Would you like me to run a Web Search?"
}
```

**UI should show:** "Search the web for this question? [Yes] [No]"

**Step 2 — User confirms, resend with token:**
```json
{
  "message": "same message as before",
  "confirmWebSearch": true,
  "webSearchToken": "eyJleH..."
}
```

---

## 3. Slot Filling — Guided Incident Intake (US9)

When the user reports a high-priority issue (Phishing, SuspiciousLogin, VPN, MFA, EndpointAlert, AccountLockout, PasswordReset), the bot collects key details **one question at a time** before creating the ticket. This ensures the ticket contains complete, structured information so the security team can act immediately.

### When Does Slot Filling Trigger?

Slot filling replaces the **immediate auto-ticket** for non-Admin users when escalation is required:

| Trigger | Role | Result |
|---|---|---|
| `issueType = "SuspiciousLogin"` or `"Severity"` | Customer / Agent | Slot filling starts |
| `dataBoundary = "Restricted"` (explicit) | Customer / Agent | Slot filling starts (if issueType has slots) |
| `dataBoundary = "Confidential"` (non-Admin) | Customer / Agent | Slot filling starts (if issueType has slots) |
| Any of the above | **Admin** | No slot filling — Admin gets a direct explanation |
| `issueType = "General"` triggering escalation | Customer / Agent | No slot filling — ticket created immediately (General has no slots) |

> The UI **does not need to decide** when to start slot filling. Just check `next.action` on every response.

---

### Slot Filling Response Fields

When `next.action == "slot_filling"`, the response includes a `slotFilling` object:

```json
{
  "requestId": "...",
  "conversationId": "conv-abc123",
  "answer": "To help the security team handle your SuspiciousLogin report as quickly as possible, I need to collect a few details first.",
  "citations": [],
  "slotFilling": {
    "isActive": true,
    "currentStep": 1,
    "totalSteps": 4,
    "nextQuestion": "Which account was affected?",
    "slotKey": "affectedAccount",
    "hint": "e.g. your email address"
  },
  "next": {
    "action": "slot_filling"
  },
  "escalation": {
    "shouldEscalate": false,
    "reason": ""
  }
}
```

| Field | Type | Meaning |
|---|---|---|
| `slotFilling.isActive` | bool | `true` while collecting, `false` when done |
| `slotFilling.currentStep` | int | 1-based step number for the current question |
| `slotFilling.totalSteps` | int | Total questions for this issueType |
| `slotFilling.nextQuestion` | string | The question to show the user |
| `slotFilling.slotKey` | string | Machine-readable field name (for optional validation hints) |
| `slotFilling.hint` | string | Suggested input placeholder / example |

When all questions are answered, the final response has `isActive = false` and `next.action = "escalate"`:

```json
{
  "answer": "Thank you — I've collected all the details needed. A SuspiciousLogin ticket has been created and assigned to the security team.",
  "slotFilling": {
    "isActive": false
  },
  "next": {
    "action": "escalate",
    "ticketId": "a1b2c3d4e5f6"
  },
  "escalation": {
    "shouldEscalate": true,
    "reason": "Slot filling complete for SuspiciousLogin."
  }
}
```

---

### How to Send Answers

The user's answer is sent as the `message` field in the **next chat request**. No new fields needed — just keep the same `conversationId`.

```json
POST /api/chat
{
  "message": "alice@company.com",
  "conversationId": "conv-abc123",
  "username": "alice"
}
```

> **Critical:** The `conversationId` must be the same across all turns in a slot filling session. The backend uses it to look up the session state. If `conversationId` changes, the backend will start a new conversation and slot filling will restart.

---

### Number of Questions Per Issue Type

| `issueType` | Questions |
|---|---|
| `Phishing` | 4 |
| `SuspiciousLogin` | 4 |
| `VPN` | 4 |
| `MFA` | 4 |
| `EndpointAlert` | 4 |
| `AccountLockout` | 3 |
| `PasswordReset` | 3 |
| `General` / `Severity` | 0 — no slot filling |

---

### Complete Turn-by-Turn Example (SuspiciousLogin)

**Turn 1 — User reports the issue:**
```json
// Request
{ "message": "Someone logged in from China at 3am.", "issueType": "SuspiciousLogin", "userRole": "Customer", "username": "alice", "conversationId": "conv-abc123" }

// Response
{ "next": { "action": "slot_filling" }, "slotFilling": { "isActive": true, "currentStep": 1, "totalSteps": 4, "nextQuestion": "Which account was affected?", "hint": "e.g. your email address" } }
```

**Turn 2 — User answers question 1:**
```json
// Request
{ "message": "alice@company.com", "conversationId": "conv-abc123", "username": "alice" }

// Response
{ "next": { "action": "slot_filling" }, "slotFilling": { "isActive": true, "currentStep": 2, "totalSteps": 4, "nextQuestion": "When did you first notice this?", "hint": "e.g. 2 pm today" } }
```

**Turn 3 — User answers question 2:**
```json
// Request
{ "message": "I noticed it this morning around 8am", "conversationId": "conv-abc123", "username": "alice" }

// Response
{ "next": { "action": "slot_filling" }, "slotFilling": { "isActive": true, "currentStep": 3, "totalSteps": 4, "nextQuestion": "Where was the suspicious login from?", "hint": "e.g. China, unknown location" } }
```

**Turn 4 — User answers question 3:**
```json
// Request
{ "message": "Beijing, China", "conversationId": "conv-abc123", "username": "alice" }

// Response
{ "next": { "action": "slot_filling" }, "slotFilling": { "isActive": true, "currentStep": 4, "totalSteps": 4, "nextQuestion": "Was there an MFA prompt — and was it approved?", "hint": "yes / no / didn't receive one" } }
```

**Turn 5 — User answers question 4 (final):**
```json
// Request
{ "message": "No I did not approve any MFA prompt", "conversationId": "conv-abc123", "username": "alice" }

// Response — slot filling complete, ticket created
{ "answer": "Thank you — I've collected all the details needed...", "slotFilling": { "isActive": false }, "next": { "action": "escalate", "ticketId": "a1b2c3d4e5f6" }, "escalation": { "shouldEscalate": true } }
```

---

### UI Implementation Checklist for Slot Filling

**State to track in the UI:**

| Variable | Where to get it | Purpose |
|---|---|---|
| `isSlotFilling` | `response.slotFilling?.isActive === true` | Toggle guided mode |
| `currentStep` | `response.slotFilling.currentStep` | Show progress bar |
| `totalSteps` | `response.slotFilling.totalSteps` | Show progress bar |
| `nextQuestion` | `response.slotFilling.nextQuestion` | Display in chat bubble |
| `inputHint` | `response.slotFilling.hint` | Input placeholder text |

**On receiving `next.action == "slot_filling"`:**
1. Render `slotFilling.nextQuestion` as a bot message (styled differently from a normal answer — e.g. a light blue background to signal "guided mode")
2. Show a progress indicator: `Step 1 / 4`
3. Set the input box placeholder to `slotFilling.hint`
4. Keep the send button enabled — user types their answer normally

**On sending the user's answer:**
- Send exactly as a normal chat message: `{ "message": userInput, "conversationId": same_id, "username": ... }`
- Do **not** re-send `issueType` or `dataBoundary` — the backend reads those from the saved session

**On receiving `next.action == "escalate"` after slot filling:**
- `slotFilling.isActive` will be `false`
- Show the ticket confirmation banner with `next.ticketId` (same as a normal escalation)
- Exit guided mode

**Suggested UI mockup:**

```
┌─────────────────────────────────────────────────────────────────┐
│  Bot  [Step 1 / 4]                                              │
│  ─────────────────────────────────────────────────────────────  │
│  To help the security team handle your SuspiciousLogin          │
│  report, I need a few details.                                  │
│                                                                 │
│  Which account was affected?                                    │
│                                                                 │
│  [ alice@company.com_________________ ]  [Send]                │
│    e.g. your email address                                      │
└─────────────────────────────────────────────────────────────────┘
```

---

## 4. Tickets

### Create Ticket (Manual) — `POST /api/tickets`

For when the user wants to open a ticket without going through chat.

**Request:**
```json
{
  "username": "pingchili",
  "summary": "VPN not connecting after password reset",
  "issueType": "VPN",
  "dataBoundary": "Public",
  "conversationId": "conv-abc123"
}
```

| Field | Required | Notes |
|---|---|---|
| `username` | Yes | Must match a valid, active user in the system |
| `summary` | Yes | Short description of the issue |
| `issueType` | Optional | Defaults to `"General"` |
| `dataBoundary` | Optional | Defaults to `"Public"` |
| `conversationId` | Optional | Links ticket to a previous chat session |

**Response 201:**
```json
{
  "ticketId": "a1b2c3d4e5f6",
  "status": "Open",
  "priority": "P3",
  "issueType": "VPN",
  "dataBoundary": "Public",
  "createdByUser": "pingchili",
  "conversationId": "conv-abc123",
  "createdUtc": "2026-03-07T03:00:00Z"
}
```

**Responses:**
| Status | Meaning |
|---|---|
| 201 Created | Ticket created |
| 400 Bad Request | Missing `summary` or invalid JSON |
| 401 Unauthorized | Unknown or disabled user |

---

### Get Ticket — `GET /api/tickets/{id}`

**Example:** `GET /api/tickets/a1b2c3d4e5f6`

**Response 200:**
```json
{
  "ticketId": "a1b2c3d4e5f6",
  "conversationId": "conv-abc123",
  "status": "Open",
  "priority": "P3",
  "issueType": "VPN",
  "dataBoundary": "Public",
  "createdByUser": "pingchili",
  "assignedTo": null,
  "summary": "VPN not connecting after password reset",
  "escalationReason": "Manual ticket created by user.",
  "source": "manual",
  "createdUtc": "2026-03-07T03:00:00Z",
  "updatedUtc": "2026-03-07T03:00:00Z"
}
```

| `source` value | Meaning |
|---|---|
| `"manual"` | User created via `POST /api/tickets` |
| `"auto"` | System created from a chat escalation |

**Responses:**
| Status | Meaning |
|---|---|
| 200 OK | Ticket found |
| 404 Not Found | No ticket with that ID |

---

### List Tickets by User — `GET /api/tickets?username={username}`

Returns all tickets belonging to the logged-in user, newest first.

**Example:** `GET /api/tickets?username=pingchili`

**Response 200:**
```json
[
  {
    "ticketId": "a1b2c3d4e5f6",
    "conversationId": "conv-abc123",
    "status": "Open",
    "priority": "P3",
    "issueType": "VPN",
    "dataBoundary": "Public",
    "createdByUser": "pingchili",
    "assignedTo": null,
    "summary": "VPN not connecting after password reset",
    "escalationReason": "Manual ticket created by user.",
    "source": "manual",
    "createdUtc": "2026-03-07T03:00:00Z",
    "updatedUtc": "2026-03-07T03:00:00Z"
  }
]
```

| Status | Meaning |
|---|---|
| 200 OK | Returns array (empty array if no tickets) |
| 400 Bad Request | `username` query param missing |

---

## 5. Conversations — `GET /api/conversations?username={username}`

Returns the logged-in user's conversation history, newest first. Each entry is one chat turn.

**Example:** `GET /api/conversations?username=pingchili`

**Response 200:**
```json
[
  {
    "requestId": "ccf3fed0b5d545b8ae5993ffab2a952e",
    "conversationId": "conv-ccf3fed0",
    "username": "pingchili",
    "outcome": "answered",
    "issueType": "VPN",
    "ticketId": null,
    "createdAtUtc": "2026-03-08T10:00:00Z"
  },
  {
    "requestId": "abc123...",
    "conversationId": "conv-abc123",
    "username": "pingchili",
    "outcome": "escalated",
    "issueType": "SuspiciousLogin",
    "ticketId": "cbc484ec1cd9",
    "createdAtUtc": "2026-03-08T09:00:00Z"
  }
]
```

| Field | Meaning |
|---|---|
| `requestId` | Unique ID for this chat turn |
| `conversationId` | Groups turns from the same session |
| `outcome` | `answered` \| `escalated` \| `needs_web_confirmation` \| `error` |
| `issueType` | Issue category for this turn |
| `ticketId` | Set only when this turn triggered an escalation |

| Status | Meaning |
|---|---|
| 200 OK | Returns array (empty array if no history) |
| 400 Bad Request | `username` query param missing |

---

## 6. Ticket Priority Reference

The server automatically assigns priority based on `issueType`. The UI can use this to colour-code tickets.

| Priority | Colour suggestion | IssueTypes |
|---|---|---|
| P1 Critical | Red | `Phishing`, `SuspiciousLogin`, `Severity` |
| P2 High | Orange | `EndpointAlert`, `AccountLockout` |
| P3 Medium | Yellow | `VPN`, `MFA`, `PasswordReset` |
| P4 Low | Grey | `General` |

---

## 7. Escalation Logic — How It Works

This section explains exactly when and why escalation triggers, so you know what to expect in the UI.

### Two Trigger Paths

**Path A — Automatic from Chat (server decides)**

The server escalates automatically in two situations. The UI does not need to detect this — just check `next.action` in every chat response.

| Situation | Who gets a ticket? | Example |
|---|---|---|
| User sends `issueType: "SuspiciousLogin"` | Customer, Agent | Login from unknown country |
| User sends `issueType: "Severity"` | Customer, Agent | Active breach reported |
| Customer/Agent sends `dataBoundary: "Restricted"` | Customer, Agent | Direct API call with Restricted |
| Customer/Agent requests Confidential content | Customer, Agent | Non-Admin asks for Confidential data |
| Admin triggers any of the above | Nobody — no ticket | Admin gets a direct explanation instead |

> The web UI never needs to send `dataBoundary: "Restricted"`. Just send the `issueType` correctly and the server handles the rest.

**Path B — Manual (user creates ticket directly)**

Any logged-in user can open a ticket at any time via `POST /api/tickets`, without going through chat.

---

### Escalation Decision Tree

```
User sends chat request
        |
        v
Is issueType == "SuspiciousLogin" or "Severity"?
  YES → Force Restricted boundary (server overrides)
  NO  → Use role-based boundary (Customer=Public, Agent=Internal, Admin=Confidential)
        |
        v
Is boundary == "Restricted" or Customer/Agent requesting Confidential?
  NO  → Normal AI answer → next.action="none"
  YES + role is Admin → No ticket, explain only → next.action="none"
  YES + role is Customer or Agent
        |
        v
Does issueType have slot definitions? (Phishing/SuspiciousLogin/VPN/MFA/EndpointAlert/AccountLockout/PasswordReset)
  YES → Start slot filling → next.action="slot_filling"
        User answers questions one by one (same conversationId each turn)
        After final answer → Create ticket → next.action="escalate" + ticketId
  NO  → Create ticket immediately → next.action="escalate" + ticketId
        (issueType="General" or "Severity" — no slot definitions)
```

---

### Real Examples with Actual Responses

**Example 1 — Customer asks about Suspicious Login (slot filling starts)**

Request:
```json
POST /api/chat
{
  "message": "Someone logged in from an unknown country and approved MFA. What do I do?",
  "issueType": "SuspiciousLogin",
  "userRole": "Customer",
  "username": "pingchili",
  "dataBoundary": "Public"
}
```

Response (Turn 1 — slot filling begins):
```json
{
  "answer": "To help the security team handle your SuspiciousLogin report as quickly as possible, I need to collect a few details first.",
  "slotFilling": {
    "isActive": true,
    "currentStep": 1,
    "totalSteps": 4,
    "nextQuestion": "Which account was affected?",
    "slotKey": "affectedAccount",
    "hint": "e.g. your email address"
  },
  "next": { "action": "slot_filling" },
  "escalation": { "shouldEscalate": false, "reason": "" }
}
```

**What the UI should show:** Enter guided mode. Show `slotFilling.nextQuestion` as the bot's message with a `Step 1 / 4` indicator. The user types their answer and sends it with the same `conversationId`.

After the user answers all 4 questions, the final response will be:
```json
{
  "answer": "Thank you — I've collected all the details needed. A SuspiciousLogin ticket has been created and assigned to the security team.",
  "slotFilling": { "isActive": false },
  "next": { "action": "escalate", "ticketId": "cbc484ec1cd9" },
  "escalation": { "shouldEscalate": true, "reason": "Slot filling complete for SuspiciousLogin." }
}
```

**What the UI should show then:** Exit guided mode, show the ticket banner with `ticketId = cbc484ec1cd9`.

---

**Example 2 — Admin asks about Suspicious Login**

Request:
```json
POST /api/chat
{
  "message": "Suspicious login detected on exec account. What are our options?",
  "issueType": "SuspiciousLogin",
  "userRole": "Admin",
  "username": "pingchili",
  "dataBoundary": "Confidential"
}
```

Response:
```json
{
  "answer": "This request involves Restricted content, which cannot be served through the chat interface — even for Admins.\n\nRestricted playbooks and procedures must be accessed through the secure out-of-band channel (e.g., your IR runbook vault or incident bridge).\n\nIf this is an active incident, initiate the incident response process directly and coordinate with your security team.",
  "next": {
    "action": "none"
  },
  "escalation": {
    "shouldEscalate": true,
    "reason": "Restricted content requires escalation."
  }
}
```

**What the UI should show:** Display the `answer` text only. No ticket banner — `next.action` is `"none"` and there is no `ticketId`.

---

**Example 3 — Normal chat, no escalation**

Request:
```json
POST /api/chat
{
  "message": "My VPN keeps timing out. What should I try?",
  "issueType": "VPN",
  "userRole": "Customer",
  "username": "pingchili",
  "dataBoundary": "Public"
}
```

Response:
```json
{
  "answer": "Here are steps to troubleshoot your VPN...",
  "citations": [ { "title": "VPN Guide", "urlOrId": "public/faq-vpn.docx", "snippet": "score=0.91" } ],
  "next": { "action": "none" },
  "escalation": { "shouldEscalate": false, "reason": "" }
}
```

**What the UI should show:** Normal answer + citations. No banner.

---

### How to Get a Ticket After Escalation

When `next.action == "escalate"`, the `ticketId` is already in the response. Use it to fetch the full ticket:

```
GET /api/tickets/cbc484ec1cd9
```

Response:
```json
{
  "ticketId": "cbc484ec1cd9",
  "status": "Open",
  "priority": "P1",
  "issueType": "SuspiciousLogin",
  "dataBoundary": "Restricted",
  "createdByUser": "pingchili",
  "assignedTo": null,
  "summary": "Auto-escalated: Restricted content request. Message: Someone logged in from...",
  "escalationReason": "Restricted content requires escalation.",
  "source": "auto",
  "createdUtc": "2026-03-07T03:15:00Z"
}
```

You can call this endpoint immediately after receiving an escalation response to show the user their ticket details — status, priority, who it's assigned to.

---

### Quick Test Checklist for Li

Use these requests to verify escalation is working end-to-end. Run them with any REST client (e.g. VS Code REST Client, Postman, curl).

**Test 1 — Escalation triggered by issueType (Customer)**
```http
POST http://localhost:7071/api/chat
Content-Type: application/json

{
  "message": "Someone logged in from an unknown country and approved MFA.",
  "issueType": "SuspiciousLogin",
  "userRole": "Customer",
  "username": "pingchili",
  "dataBoundary": "Public"
}
```
Expected: `next.action = "escalate"`, `next.ticketId` is set, `escalation.shouldEscalate = true`

---

**Test 2 — Admin gets explanation only, no ticket**
```http
POST http://localhost:7071/api/chat
Content-Type: application/json

{
  "message": "We have a confirmed breach. What is the escalation path?",
  "issueType": "Severity",
  "userRole": "Admin",
  "username": "pingchili",
  "dataBoundary": "Confidential"
}
```
Expected: `next.action = "none"`, no `ticketId`, answer explains to use out-of-band channel

---

**Test 3 — Normal chat, no escalation**
```http
POST http://localhost:7071/api/chat
Content-Type: application/json

{
  "message": "How do I reset my VPN password?",
  "issueType": "VPN",
  "userRole": "Customer",
  "username": "pingchili",
  "dataBoundary": "Public"
}
```
Expected: `next.action = "none"`, `escalation.shouldEscalate = false`, answer + citations returned

---

**Test 4 — Manual ticket creation**
```http
POST http://localhost:7071/api/tickets
Content-Type: application/json

{
  "username": "pingchili",
  "summary": "VPN not connecting after password reset",
  "issueType": "VPN"
}
```
Expected: `201 Created`, response contains `ticketId`

---

**Test 5 — Fetch the ticket from Test 4**
```http
GET http://localhost:7071/api/tickets/{ticketId from Test 4}
```
Expected: `200 OK`, full ticket object with `status = "Open"`

---

**Test 6 — List all tickets for a user**
```http
GET http://localhost:7071/api/tickets?username=pingchili
```
Expected: `200 OK`, array of tickets where every item has `createdByUser = "pingchili"`. Should include the ticket from Test 4 and any auto-escalated tickets from Tests 1 and 3.

---

**Test 7 — List conversation history for a user**
```http
GET http://localhost:7071/api/conversations?username=pingchili
```
Expected: `200 OK`, array of conversation entries, newest first. Entries from Tests 1–3 should appear. Check that `ticketId` is populated on the entry from Test 1 (escalation) and `null` on Tests 2 and 3.

---

**Test 8 — Missing username returns 400**
```http
GET http://localhost:7071/api/tickets
```
Expected: `400 Bad Request`, `error.code = "ValidationError"`

```http
GET http://localhost:7071/api/conversations
```
Expected: `400 Bad Request`, `error.code = "ValidationError"`

---

**Test 9 — Slot filling: SuspiciousLogin triggers guided mode (Turn 1)**
```http
POST http://localhost:7071/api/chat
Content-Type: application/json

{
  "message": "Someone logged in from China at 3am and I think they approved MFA.",
  "issueType": "SuspiciousLogin",
  "userRole": "Customer",
  "username": "pingchili",
  "conversationId": "conv-test-slotfill-01"
}
```
Expected: `next.action = "slot_filling"`, `slotFilling.isActive = true`, `slotFilling.currentStep = 1`, `slotFilling.totalSteps = 4`, `slotFilling.nextQuestion` is about affected account.

---

**Test 10 — Slot filling: answer turn 2 (same conversationId)**
```http
POST http://localhost:7071/api/chat
Content-Type: application/json

{
  "message": "pingchili@company.com",
  "conversationId": "conv-test-slotfill-01",
  "username": "pingchili"
}
```
Expected: `next.action = "slot_filling"`, `slotFilling.currentStep = 2`, `slotFilling.nextQuestion` is about when the login was noticed.

---

**Test 11 — Slot filling: answer turn 3**
```http
POST http://localhost:7071/api/chat
Content-Type: application/json

{
  "message": "I noticed it this morning around 8am",
  "conversationId": "conv-test-slotfill-01",
  "username": "pingchili"
}
```
Expected: `next.action = "slot_filling"`, `slotFilling.currentStep = 3`.

---

**Test 12 — Slot filling: answer turn 4**
```http
POST http://localhost:7071/api/chat
Content-Type: application/json

{
  "message": "Beijing, China",
  "conversationId": "conv-test-slotfill-01",
  "username": "pingchili"
}
```
Expected: `next.action = "slot_filling"`, `slotFilling.currentStep = 4` (last question).

---

**Test 13 — Slot filling: final answer → ticket created**
```http
POST http://localhost:7071/api/chat
Content-Type: application/json

{
  "message": "No I did not approve any MFA prompt",
  "conversationId": "conv-test-slotfill-01",
  "username": "pingchili"
}
```
Expected: `next.action = "escalate"`, `next.ticketId` is set, `slotFilling.isActive = false`, `escalation.shouldEscalate = true`.

---

**Test 14 — Slot filling: verify ticket was created with full details**
```http
GET http://localhost:7071/api/tickets/{ticketId from Test 13}
```
Expected: `200 OK`, `source = "slot_filling"`, `priority = "P1"`, `summary` contains all 4 collected answers.

---

**Test 15 — Slot filling does NOT trigger for Admin**
```http
POST http://localhost:7071/api/chat
Content-Type: application/json

{
  "message": "Someone logged in from an unknown location.",
  "issueType": "SuspiciousLogin",
  "userRole": "Admin",
  "username": "pingchili",
  "conversationId": "conv-test-admin-01"
}
```
Expected: `next.action = "none"`, **no** `slotFilling` field, `escalation.shouldEscalate = true`.

---

## 8. Recommended UI Flow

```
User logs in (POST /api/auth/login)
  → Store username + role in session

User selects issue type from dropdown
  → Map role to dataBoundary (see table above)
  → Generate a conversationId (UUID) for this session — reuse it for ALL turns

User types message and submits
  → POST /api/chat with { message, issueType, userRole, username, dataBoundary, conversationId }

Check response:

  next.action == "none"
    → Display answer + citations normally

  next.action == "escalate"
    → Display escalation banner with next.ticketId
    → Optionally call GET /api/tickets/{id} to show ticket details

  next.action == "slot_filling"
    → Enter guided mode:
        Show slotFilling.nextQuestion as bot message
        Show progress: "Step N / M"
        Set input placeholder to slotFilling.hint
        User types answer → resend with { message: answer, conversationId, username }
        Repeat until next.action == "escalate"
    → On final escalate: show ticket banner, exit guided mode

  needsWebConfirmation == true
    → Show "Search web?" prompt
    → If yes: resend with confirmWebSearch=true + webSearchToken

User manually opens a ticket (optional)
  → POST /api/tickets with { username, summary, issueType }
  → Display returned ticketId
```

---

## 9. Notes for Li

- **No JWT yet.** Auth is username/password only. Store `username` in session after login and pass it in the `username` field of chat/ticket requests. Do not store the password.
- **`userRole` in chat is a hint.** The server enforces the real boundary. However you should still send the correct role so logs are accurate.
- **`conversationId` is now critical — not optional.** Generate a UUID on the client at the start of each chat session and reuse it for every message in that session. Without a consistent `conversationId`, slot filling will break (the backend cannot find the session state between turns). Use something like `"conv-" + crypto.randomUUID()`.
- **Slot filling does not require UI state about which slot you're on.** The server tracks that. The UI only needs to check `next.action` and render `slotFilling.nextQuestion`. No hardcoded question lists on the frontend.
- **`slotFilling` field may be absent.** When the response is a normal answer or a direct escalation (no slot filling), the `slotFilling` key will not be present. Always use optional chaining: `response.slotFilling?.isActive`.
- **Correlation header.** The server returns `x-correlation-id` in every response header. Log this on the client side — it helps with debugging.
