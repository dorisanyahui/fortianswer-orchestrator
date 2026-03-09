# FortiAnswer — UI Integration Guide
**For:** Web UI Developer (Li)
**Last updated:** 2026-03-08

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

## 3. Tickets

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

## 4. Conversations — `GET /api/conversations?username={username}`

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

## 5. Ticket Priority Reference

The server automatically assigns priority based on `issueType`. The UI can use this to colour-code tickets.

| Priority | Colour suggestion | IssueTypes |
|---|---|---|
| P1 Critical | Red | `Phishing`, `SuspiciousLogin`, `Severity` |
| P2 High | Orange | `EndpointAlert`, `AccountLockout` |
| P3 Medium | Yellow | `VPN`, `MFA`, `PasswordReset` |
| P4 Low | Grey | `General` |

---

## 6. Escalation Logic — How It Works

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
Is boundary == "Restricted"?
  YES + role is Customer or Agent → Create ticket → return next.action="escalate" + ticketId
  YES + role is Admin             → No ticket     → return next.action="none" (explain only)
  NO  → Is Customer/Agent requesting Confidential?
          YES → Create ticket → return next.action="escalate" + ticketId
          NO  → Normal AI answer → return next.action="none"
```

---

### Real Examples with Actual Responses

**Example 1 — Customer asks about Suspicious Login**

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

Response:
```json
{
  "answer": "This request falls under Restricted content. I can't provide step-by-step playbook details in chat.\n\nA ticket has been created and will be assigned to an authorized responder.\n\nTo help them act quickly, please provide: who is affected, when it started, and whether MFA was approved or credentials were entered.",
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

**What the UI should show:** Display the `answer` text, then show a ticket banner with `ticketId = cbc484ec1cd9`.

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

## 7. Recommended UI Flow

```
User logs in (POST /api/auth/login)
  → Store username + role in session

User selects issue type from dropdown
  → Map role to dataBoundary (see table above)

User types message and submits
  → POST /api/chat with { message, issueType, userRole, username, dataBoundary }

Check response:

  next.action == "none"
    → Display answer + citations normally

  next.action == "escalate"
    → Display escalation banner with next.ticketId
    → Optionally call GET /api/tickets/{id} to show ticket details

  needsWebConfirmation == true
    → Show "Search web?" prompt
    → If yes: resend with confirmWebSearch=true + webSearchToken

User manually opens a ticket (optional)
  → POST /api/tickets with { username, summary, issueType }
  → Display returned ticketId
```

---

## 8. Notes for Li

- **No JWT yet.** Auth is username/password only. Store `username` in session after login and pass it in the `username` field of chat/ticket requests. Do not store the password.
- **`userRole` in chat is a hint.** The server enforces the real boundary. However you should still send the correct role so logs are accurate.
- **`conversationId` for threading.** Generate a UUID on the client at the start of each chat session and reuse it for all messages in that session. This groups messages together in the logs.
- **Correlation header.** The server returns `x-correlation-id` in every response header. Log this on the client side — it helps with debugging.
