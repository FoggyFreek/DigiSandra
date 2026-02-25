# DigiSandra – Azure AI Scheduling Agent

Intelligente planningsassistent voor Microsoft Teams die vergaderverzoeken interpreteert via natuurlijke taal, beschikbaarheid analyseert, conflicten proactief oplost en afspraken autonoom boekt in Outlook-agenda's.

## Architectuur

```
User (Teams)
   ↓
Teams Channel
   ↓
Azure Bot Service
   ↓
Azure Function (Orchestrator)
   ↓
Azure OpenAI (Intent + Reasoning)
   ↓
Microsoft Graph API
   ↓
Cosmos DB (State)
```

## Tech Stack

| Component | Technologie |
|-----------|------------|
| Runtime | .NET 8, Azure Functions v4 (isolated worker) |
| Bot | Microsoft Bot Framework SDK 4.x |
| AI | Azure OpenAI Service (GPT-4o) |
| API | Microsoft Graph SDK v5 |
| Database | Azure Cosmos DB (NoSQL, serverless) |
| Identity | Microsoft Entra ID (OAuth 2.0 OBO) |
| IaC | Bicep |
| CI/CD | GitHub Actions |

## Project Structure

```
src/SchedulingAgent/          # Azure Functions project
  Functions/                  # HTTP triggers, timer functions
  Bot/                        # Teams bot activity handlers
  Services/                   # Business logic (Graph, OpenAI, Cosmos DB, Conflict Resolution)
  Models/                     # Data models and DTOs
  Cards/                      # Adaptive Card builders
  Prompts/                    # OpenAI prompt templates
  Extensions/                 # DI registration
tests/SchedulingAgent.Tests/  # Unit tests (xUnit + Moq)
infra/                        # Bicep templates
.github/workflows/            # CI/CD pipelines
```

## Features

- **NLP Intent Parsing**: Vergaderverzoeken in natuurlijke taal via GPT-4o
- **Entra ID Validatie**: Gebruikers en groepen opzoeken via Microsoft Graph
- **Beschikbaarheid Analyse**: `findMeetingTimes` met `getSchedule` fallback
- **Smart Conflict Resolution**: AI-beslissingsmatrix voor conflictoplossing
- **Autonome 1-op-1 Interactie**: Directe chat met deelnemers bij conflicten
- **Adaptive Cards**: Rijke UI voor tijdslot selectie in Teams
- **Audit Trail**: Metadata-only logging via Cosmos DB
- **AVG/GDPR Compliant**: TTL policies, geen opslag van meeting-inhoud

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)
- Azure subscription with:
  - Azure OpenAI Service access
  - Microsoft Entra ID app registration
  - Bot Framework registration

## Quick Start

```bash
# Clone and build
git clone <repo-url>
cd DigiSandra
dotnet restore
dotnet build

# Configure local settings
cp src/SchedulingAgent/local.settings.json.example src/SchedulingAgent/local.settings.json
# Edit local.settings.json with your Azure credentials

# Run locally
cd src/SchedulingAgent
func start

# Run tests
cd ../..
dotnet test
```

## Required Graph API Permissions

| Permission | Type | Purpose |
|-----------|------|---------|
| Calendars.ReadWrite | Delegated | Agenda lezen en schrijven |
| Chat.Create | Delegated | 1:1 chats aanmaken |
| ChatMessage.Send | Delegated | Berichten versturen |
| User.Read.All | Application | Gebruikers opzoeken |
| Group.Read.All | Application | Groepen en leden opzoeken |

## Deployment

### Infrastructure

```bash
# Deploy to dev
az deployment group create \
  --resource-group rg-digisandra-dev \
  --template-file infra/main.bicep \
  --parameters environment=dev \
               botAppId=<app-id> \
               botAppPassword=<app-password> \
               graphClientId=<client-id> \
               graphClientSecret=<client-secret> \
               graphTenantId=<tenant-id>
```

### CI/CD

GitHub Actions workflows:
- **ci.yml**: Build, test, and Bicep validation on PR
- **deploy.yml**: Deploy to DEV/ACC/PROD environments

Required GitHub Secrets:
- `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`
- `AZURE_RESOURCE_GROUP`
- `BOT_APP_ID`, `BOT_APP_PASSWORD`
- `GRAPH_CLIENT_ID`, `GRAPH_CLIENT_SECRET`

## Usage Example

In Microsoft Teams, message the bot:

> "Plan een overleg van 1 uur met Jan en het Marketing-team voor volgende week over Project X."

The bot will:
1. Parse the intent (subject, duration, participants, time window)
2. Resolve participants via Entra ID
3. Find available time slots via Microsoft Graph
4. Resolve any conflicts using AI reasoning
5. Present options via an Adaptive Card
6. Book the meeting upon selection

## License

Proprietary – Internal use only.
