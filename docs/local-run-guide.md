# FortiAnswer Orchestrator – Local Run Guide

## Purpose
This document explains how to run the FortiAnswer Orchestrator locally.
It is written for team members who are not familiar with Azure Functions.

No cloud resources are required for local development.

---

## Prerequisites

Please install the following tools before starting:

1. **Git**
   - Used to clone the project repository

2. **.NET 8 SDK**
   - Required to build and run the Azure Function
   - Download: https://dotnet.microsoft.com/download

3. **Azure Functions Core Tools (v4)**
   - Used to run Azure Functions locally
   - Install via npm or official installer

4. **Visual Studio Code** (recommended)
   - Any editor can be used, but VS Code is recommended

---

## Step 1: Clone the Repository


git clone https://github.com/dorisanyahui/fortianswer-orchestrator.git
cd fortianswer-orchestrator

## Step 2: Navigate to the Orchestrator Project

```bash
cd src/orchestrator/FortiAnswer.Orchestrator
```

This folder contains the Azure Function App.

## Step 3: Configure Local Settings

Create a local settings file for development.

```bash
cp local.settings.template.json local.settings.json
```

Important:

- `local.settings.json` is used only for local development  
- Do **NOT** commit this file to GitHub  
- No real secrets are required at this stage  

## Step 4: Start the Function App

Run the following command:

```bash
func start
```

If successful, you should see output similar to:

```text
Functions:
  health: [GET] http://localhost:7071/api/health
  chat:   [POST] http://localhost:7071/api/chat
```

## Step 5: Test the Health Endpoint

Open a terminal or browser and test:

```bash
curl http://localhost:7071/api/health
```

Expected response:

```json
{
  "status": "ok"
}
```

## Step 6: Test the Chat Endpoint

Example request using PowerShell:

```powershell
$body = @{
  message = "How do I reset my VPN client?"
  requestType = "troubleshooting"
  userRole = "customer"
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
  -Uri "http://localhost:7071/api/chat" `
  -ContentType "application/json" `
  -Body $body
```

Expected response (example):

```json
{
  "answer": "placeholder response",
  "requestId": "uuid",
  "escalation": {
    "shouldEscalate": false
  }
}
```

## Notes

- The current implementation uses placeholder logic
- Retrieval (RAG) and LLM integration will be added in later tasks
- This setup is sufficient for local development and demo preparation

## Troubleshooting

- If `func` is not recognized, restart the terminal or VS Code
- Ensure .NET 8 SDK is installed correctly
- Ensure you are running commands from the correct folder
