# FortiAnswer Orchestrator – API Contract (v0.1)

## Overview
The Orchestrator exposes two HTTP endpoints:
- `GET /api/health` – health check for deployment validation
- `POST /api/chat` – main orchestration entrypoint (RAG + LLM will be added later)

Local dev base URL:
- `http://localhost:7071`

> Note: Azure Functions adds the `/api` prefix by default.

---

## GET /api/health

### Purpose
Confirm the service is running.

### Request
- Method: `GET`
- Route: `/api/health`

### Response (200)
```json
{"status":"ok"}
