# FortiAnswer Orchestrator – API Contract (v0.1)

## 1. Overview

The Orchestrator exposes two HTTP endpoints:

- **GET `/api/health`**  
  Used for service health check and deployment validation.

- **POST `/api/chat`**  
  Main orchestration entrypoint.  
  This endpoint will later integrate Retrieval (RAG) and LLM generation.

> Note: Azure Functions automatically adds the `/api` prefix.

---

## 2. Base URL (Local Development)

http://localhost:7071


---

## 3. GET /api/health

### Purpose
Confirm that the Orchestrator service is running and reachable.

### Request
- Method: `GET`
- Route: `/api/health`
- Authentication: None

### Response (200 OK)

```json
{
  "status": "ok"
}


## 4. POST /api/chat
### Purpose

Accept a user question and route it through the orchestration pipeline
(Retrieval → LLM → Structured Response).

### Request

 - Method: 'POST'
 - Route: '/api/chat'
 - Content-Type: 'application/json'

Request Body
{
  "message": "How do I reset my VPN client?",
  "requestType": "troubleshooting",
  "userRole": "customer"
}

### Fields
Field	Type	Required	Description
message	string	Yes	User input question
requestType	string	Yes	Request category (e.g. troubleshooting, policy, how-to)
userRole	string	Yes	User role (e.g. customer, agent, admin)

Response (200 OK)

{
  "answer": "Steps to reset your VPN client...",
  "citations": {
    "kb-001": "Internal VPN Reset Guide"
  },
  "actionHints": {
    "next": "integrate_retrieval"
  },
  "requestId": "uuid-string",
  "escalation": {
    "shouldEscalate": false,
    "reason": ""
  }
}


### Response Fields
Field	Type	Description
answer	string	Final response text
citations	object	Knowledge sources used (placeholder for now)
actionHints	object	Internal orchestration hints
requestId	string	Correlation ID for tracing
escalation	object	Escalation decision result



## 5. Versioning Notes

### Current version: v0.1

Retrieval, RBAC filtering, and LLM integration are not yet active

Response structure is stable and safe for downstream integration

