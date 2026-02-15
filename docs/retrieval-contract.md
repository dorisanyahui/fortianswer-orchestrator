
# FortiAnswer Retrieval API Contract (v0.1)

This document defines the contract between the Orchestrator (`/api/chat`) and the Retrieval service.
Goal: enable plug-and-play integration with consistent tracing via `correlationId`.

---

## 1. Endpoint

**POST** `<RETRIEVAL_ENDPOINT>`

### Authentication (choose ONE and keep consistent)
Option A:
- Header: `x-api-key: <key>`

Option B:
- Header: `Authorization: Bearer <token>`

> Local testing may allow no-auth, but production must enforce one selected method.

---

## 2. Request Schema (JSON)

```json
{
  "query": "string",
  "topK": 5,
  "filters": {
    "requestType": "Public",
    "userRole": "User",
    "userGroup": "AnyGroup",
    "dataBoundary": "Public"
  },
  "conversationId": "conv-001",
  "correlationId": "doris-test-001"
}
````

### Rules

* `query` is required (maps to `ChatRequest.message`).
* `topK` optional, default `5`.
* `filters` optional but must accept the keys:

  * `requestType`, `userRole`, `userGroup`, `dataBoundary`
* `correlationId` must be logged and echoed back in response.

---

## 3. Response Schema (JSON)

```json
{
  "correlationId": "doris-test-001",
  "items": [
    {
      "title": "string",
      "urlOrId": "string",
      "snippet": "string",
      "score": 0.87
    }
  ]
}
```

### Rules

* `items` must always be an array (empty array allowed).
* Each item must include: `title`, `urlOrId`, `snippet`.
* `score` optional (recommended for ranking/threshold later).
* `correlationId` must match the request correlationId.

---

## 4. Error Handling

Use meaningful HTTP status codes:

* `400` Bad Request: invalid payload (missing query, invalid JSON)
* `401/403`: authentication/authorization failure
* `408` or `504`: timeout
* `500`: internal server error

Recommended error body:

```json
{
  "correlationId": "doris-test-001",
  "error": { "code": "InvalidQuery", "message": "..." }
}
```

---

## 5. Performance / Limits (baseline)

* Target P95 latency (topK=5): < 2 seconds
* `snippet` length recommendation: <= 500 chars
* No result should return: `items: []` (not an error)

---

## 6. Acceptance Criteria (Integration Readiness)

Retrieval is considered ready when:

1. CorrelationId is traceable end-to-end (request -> retrieval logs -> response).
2. Valid query returns `items[]` that can be mapped to Orchestrator `Citation`.
3. Missing query returns HTTP 400.
4. Error scenarios return stable HTTP status codes (no crashes).

```



