## Local Setup (Azure Functions)

> **Important:** The Azure Function App lives here:  
> `src/orchestrator/FortiAnswer.Orchestrator` (same folder as `host.json`)

### Prerequisites
- Azure Functions Core Tools
- .NET SDK (match solution)
- (Optional) Azurite for local storage emulation

### 1) Navigate to the Function App folder
```bash
cd src/orchestrator/FortiAnswer.Orchestrator
```
### 2) Configure local settings

#### Copy the template:
```bash
cp local.settings.template.json local.settings.json
```

#### Edit local.settings.json only if needed.

local.settings.json is for local development only. Do NOT commit it.

#### Default mode (no secrets required)

By default the project runs in stub mode:
```bash
RETRIEVAL_MODE=stub

LLM_MODE=stub
```
In stub mode, these values can remain empty:

- RETRIEVAL_ENDPOINT, 
- RETRIEVAL_API_KEY, RETRIEVAL_INDEX

LLM_ENDPOINT, LLM_API_KEY, LLM_MODEL

### 3) Run the Function App
```bash
func start
```
### 4) Verify endpoints

- Health check
GET http://localhost:7071/api/health

- Chat endpoint
POST http://localhost:7071/api/chat


## Testing (Local)

1) Start the Azure Functions host:

```bash
cd src/orchestrator/FortiAnswer.Orchestrator
func start
