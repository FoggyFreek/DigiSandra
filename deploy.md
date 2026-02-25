# DigiSandra – Deployment Guide

This guide walks you through deploying DigiSandra to an Azure tenant from scratch. It covers prerequisites, app registrations, secrets configuration, infrastructure deployment, and CI/CD setup.

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Azure Subscription & Resource Groups](#2-azure-subscription--resource-groups)
3. [Azure AD App Registrations](#3-azure-ad-app-registrations)
4. [Deploy Infrastructure (Bicep)](#4-deploy-infrastructure-bicep)
5. [Post-Deployment: Managed Identity Role Assignments](#5-post-deployment-managed-identity-role-assignments)
6. [CI/CD via GitHub Actions](#6-cicd-via-github-actions)
7. [Required GitHub Secrets](#7-required-github-secrets)
8. [Verify the Deployment](#8-verify-the-deployment)
9. [Azure Resources Reference](#9-azure-resources-reference)
10. [Configuration Reference](#10-configuration-reference)

---

## 1. Prerequisites

### Tools

Install these tools on your workstation before starting:

| Tool | Version | Installation |
|------|---------|-------------|
| Azure CLI | Latest | https://learn.microsoft.com/cli/azure/install-azure-cli |
| .NET SDK | 8.0.x | https://dotnet.microsoft.com/download |
| Azure Functions Core Tools | v4 | `npm install -g azure-functions-core-tools@4` |
| Bicep CLI | Latest | `az bicep install` |

### Permissions in Azure AD

You need at least one of the following in your target Azure AD tenant:

- **Application Administrator** role (to create and consent to app registrations)
- **Privileged Role Administrator** (to grant admin consent for Graph API permissions)

### Permissions in Azure Subscription

You need **Owner** or **Contributor + User Access Administrator** on the target subscription to:

- Create resource groups and all Azure resources
- Assign RBAC roles to the managed identity

---

## 2. Azure Subscription & Resource Groups

### 2.1 Identify your Azure Tenant and Subscription

```bash
az login
az account list --output table
az account set --subscription "<your-subscription-id>"
```

Note your **Tenant ID** and **Subscription ID** — you will need them later.

### 2.2 Create Resource Groups

Create one resource group per environment. Use any naming convention, but this guide uses:

```bash
az group create --name rg-digisandra-dev  --location westeurope
az group create --name rg-digisandra-acc  --location westeurope
az group create --name rg-digisandra-prod --location westeurope
```

> **Note**: Azure OpenAI is not available in all regions. Verify GPT-4o availability in your chosen region at https://learn.microsoft.com/azure/ai-services/openai/concepts/models before proceeding.

---

## 3. Azure AD App Registrations

You need **two** app registrations: one for the Bot and one for Microsoft Graph access. These are created once and shared across environments.

### 3.1 Bot Framework App Registration

The bot registration authenticates incoming Teams messages via Bot Framework.

```bash
# Create the app registration
az ad app create \
  --display-name "DigiSandra Bot" \
  --sign-in-audience AzureADMyOrg

# Note the appId from the output — this is BOT_APP_ID
```

Create a client secret:

```bash
az ad app credential reset \
  --id "<BOT_APP_ID>" \
  --append \
  --display-name "DigiSandra Bot Secret" \
  --years 2

# Note the 'password' from the output — this is BOT_APP_PASSWORD
```

Create a service principal (required for single-tenant bots):

```bash
az ad sp create --id "<BOT_APP_ID>"
```

### 3.2 Microsoft Graph App Registration

This registration gives DigiSandra service-to-service access to Microsoft Graph.

```bash
# Create the app registration
az ad app create \
  --display-name "DigiSandra Graph" \
  --sign-in-audience AzureADMyOrg

# Note the appId — this is GRAPH_CLIENT_ID
```

Create a client secret:

```bash
az ad app credential reset \
  --id "<GRAPH_CLIENT_ID>" \
  --append \
  --display-name "DigiSandra Graph Secret" \
  --years 2

# Note the 'password' — this is GRAPH_CLIENT_SECRET
```

Create a service principal:

```bash
az ad sp create --id "<GRAPH_CLIENT_ID>"
```

#### 3.2.1 Assign Graph API Application Permissions

These are **application permissions** (not delegated — no user sign-in required):

| Permission | Reason |
|-----------|--------|
| `User.Read.All` | Resolve participant names/emails via Entra ID |
| `Group.Read.All` | Expand group memberships to individual participants |
| `Calendars.ReadWrite` | Check availability and create calendar events |
| `Chat.Create` | Open 1:1 chats to request conflict resolution |
| `ChatMessage.Send` | Send messages in conflict resolution chats |

Add the permissions via Azure Portal:

1. Open **Azure Active Directory** → **App registrations** → **DigiSandra Graph**
2. Go to **API permissions** → **Add a permission** → **Microsoft Graph** → **Application permissions**
3. Search and add each permission listed above
4. Click **Grant admin consent for \<your tenant\>** — this requires Global Admin or Privileged Role Administrator

Or via CLI:

```bash
# Find Microsoft Graph service principal object ID
GRAPH_SP_ID=$(az ad sp list --display-name "Microsoft Graph" --query "[0].id" -o tsv)

# Required permission IDs (stable GUIDs, do not change)
# User.Read.All          = df021288-bdef-4463-88db-98f22de89214
# Group.Read.All         = 5b567255-7703-4780-807c-7be8301ae99b
# Calendars.ReadWrite    = ef54d2bf-783f-4e0f-bca1-3210c4d670c1
# Chat.Create            = d9c48af6-9ad9-47ad-82c3-63757137b9af
# ChatMessage.Send       = 116b7235-2fdd-4100-8639-4d3d3709b5dd

az ad app permission add \
  --id "<GRAPH_CLIENT_ID>" \
  --api "00000003-0000-0000-c000-000000000000" \
  --api-permissions \
    "df021288-bdef-4463-88db-98f22de89214=Role" \
    "5b567255-7703-4780-807c-7be8301ae99b=Role" \
    "ef54d2bf-783f-4e0f-bca1-3210c4d670c1=Role" \
    "d9c48af6-9ad9-47ad-82c3-63757137b9af=Role" \
    "116b7235-2fdd-4100-8639-4d3d3709b5dd=Role"

# Grant admin consent
az ad app permission admin-consent --id "<GRAPH_CLIENT_ID>"
```

### 3.3 CI/CD Service Principal

This service principal is used by GitHub Actions to authenticate with Azure via OIDC (no stored password).

```bash
az ad sp create-for-rbac \
  --name "DigiSandra CI/CD" \
  --role Contributor \
  --scopes /subscriptions/<SUBSCRIPTION_ID> \
  --sdk-auth

# Note appId (AZURE_CLIENT_ID) and tenant (AZURE_TENANT_ID) from output
```

If you prefer **least-privilege**, scope the Contributor role per resource group and add a separate `User Access Administrator` assignment needed for managed identity role assignments:

```bash
az role assignment create \
  --assignee "<CI_CD_CLIENT_ID>" \
  --role "User Access Administrator" \
  --scope /subscriptions/<SUBSCRIPTION_ID>/resourceGroups/rg-digisandra-dev
```

#### 3.3.1 Configure OIDC Federated Credentials (recommended over client secret)

```bash
# Allow GitHub Actions on the main branch to authenticate
az ad app federated-credential create \
  --id "<CI_CD_APP_ID>" \
  --parameters '{
    "name": "github-main",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:<your-github-org>/<your-repo>:ref:refs/heads/main",
    "audiences": ["api://AzureADTokenExchange"]
  }'

# Allow manual workflow dispatches for acc/prod
az ad app federated-credential create \
  --id "<CI_CD_APP_ID>" \
  --parameters '{
    "name": "github-environment-dev",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:<your-github-org>/<your-repo>:environment:dev",
    "audiences": ["api://AzureADTokenExchange"]
  }'
```

Repeat for `acc` and `prod` environments as needed.

---

## 4. Deploy Infrastructure (Bicep)

The Bicep templates in `infra/` deploy all Azure resources. You can run this manually or let GitHub Actions handle it (see Section 6).

### 4.1 Manual Deployment

```bash
az deployment group create \
  --resource-group rg-digisandra-dev \
  --template-file infra/main.bicep \
  --parameters \
    environment=dev \
    botAppId="<BOT_APP_ID>" \
    botAppPassword="<BOT_APP_PASSWORD>" \
    graphClientId="<GRAPH_CLIENT_ID>" \
    graphClientSecret="<GRAPH_CLIENT_SECRET>" \
    graphTenantId="<TENANT_ID>"
```

Replace `dev` with `acc` or `prod` and the corresponding resource group for other environments.

### 4.2 What Gets Deployed

| Resource | Name Pattern | SKU |
|---------|-------------|-----|
| Log Analytics Workspace | `log-digisandra-{env}` | PerGB2018, 30-day retention |
| Application Insights | `appi-digisandra-{env}` | Web, linked to Log Analytics |
| Cosmos DB Account | `cosmos-digisandra-{env}` | Standard Serverless, 7-day backup |
| Cosmos DB Database | `SchedulingAgent` | – |
| Cosmos DB Container | `Requests` | Partition: `/requestId`, TTL: 7 days |
| Azure OpenAI Account | `oai-digisandra-{env}` | S0 |
| GPT-4o Deployment | `gpt-4o` | Standard, 30K TPM |
| Storage Account | `st{digisandra}{env}` | Standard LRS |
| App Service Plan | `plan-digisandra-{env}` | Y1 Dynamic (Consumption) |
| Function App | `func-digisandra-{env}` | .NET 8 isolated, system-assigned MSI |
| Bot Service | `bot-digisandra-{env}` | S1 |
| Teams Channel | (linked to bot) | – |

### 4.3 Bicep Outputs

After deployment, capture the outputs for reference:

```bash
az deployment group show \
  --resource-group rg-digisandra-dev \
  --name main \
  --query properties.outputs
```

Key outputs:

| Output | Use |
|--------|-----|
| `functionAppName` | Target name for Function App code deployment |
| `functionAppEndpoint` | Messaging endpoint for the Bot Service |
| `cosmosDbEndpoint` | Application setting `CosmosDb__Endpoint` |
| `openAiEndpoint` | Application setting `AzureOpenAI__Endpoint` |

---

## 5. Post-Deployment: Managed Identity Role Assignments

The Function App has a **system-assigned managed identity**. After the Bicep deployment, assign the following RBAC roles so the app can access Azure services without credentials.

### 5.1 Get the Managed Identity Principal ID

```bash
PRINCIPAL_ID=$(az functionapp identity show \
  --name func-digisandra-dev \
  --resource-group rg-digisandra-dev \
  --query principalId -o tsv)
```

### 5.2 Cosmos DB — Data Contributor

```bash
COSMOS_ID=$(az cosmosdb show \
  --name cosmos-digisandra-dev \
  --resource-group rg-digisandra-dev \
  --query id -o tsv)

az cosmosdb sql role assignment create \
  --account-name cosmos-digisandra-dev \
  --resource-group rg-digisandra-dev \
  --role-definition-id "00000000-0000-0000-0000-000000000002" \
  --principal-id "$PRINCIPAL_ID" \
  --scope "$COSMOS_ID"
```

> `00000000-0000-0000-0000-000000000002` is the built-in **Cosmos DB Built-in Data Contributor** role.

### 5.3 Azure OpenAI — Cognitive Services User

```bash
OAI_ID=$(az cognitiveservices account show \
  --name oai-digisandra-dev \
  --resource-group rg-digisandra-dev \
  --query id -o tsv)

az role assignment create \
  --assignee "$PRINCIPAL_ID" \
  --role "Cognitive Services User" \
  --scope "$OAI_ID"
```

Repeat Steps 5.1–5.3 for each environment (`acc`, `prod`), substituting the resource group and resource names.

---

## 6. CI/CD via GitHub Actions

Two workflows are included:

| Workflow | File | Trigger |
|---------|------|---------|
| CI (build & test) | `.github/workflows/ci.yml` | Push to `main`/`develop`, PR to `main` |
| Deploy | `.github/workflows/deploy.yml` | Push to `main` (→ dev), manual dispatch (→ acc/prod) |

### 6.1 GitHub Environments

Create three environments in your GitHub repository (**Settings → Environments**):

- `dev`
- `acc`
- `prod`

For `acc` and `prod`, consider enabling **required reviewers** to add a manual approval gate before deployment.

### 6.2 Manual Deployment via workflow_dispatch

To deploy to `acc` or `prod`:

1. GitHub UI → **Actions** → **Deploy to Azure**
2. Click **Run workflow**
3. Select the target environment (`acc` or `prod`)
4. Click **Run workflow**

---

## 7. Required GitHub Secrets

Add these secrets in **Settings → Secrets and variables → Actions**. Secrets scoped to an environment override repository-level secrets.

### 7.1 Repository-Level Secrets

| Secret | Value | Description |
|--------|-------|-------------|
| `AZURE_CLIENT_ID` | CI/CD service principal `appId` | OIDC authentication to Azure |
| `AZURE_TENANT_ID` | Your Azure Tenant ID | OIDC authentication to Azure |
| `AZURE_SUBSCRIPTION_ID` | Your Azure Subscription ID | Target subscription for deployments |
| `AZURE_RESOURCE_GROUP` | e.g., `rg-digisandra-dev` | Resource group for deployments |
| `BOT_APP_ID` | Bot app registration `appId` | Bot Framework identity |
| `BOT_APP_PASSWORD` | Bot app registration client secret | Bot Framework authentication |
| `GRAPH_CLIENT_ID` | Graph app registration `appId` | Microsoft Graph service principal |
| `GRAPH_CLIENT_SECRET` | Graph app registration client secret | Microsoft Graph authentication |

> These secrets are passed as secure Bicep parameters and injected into Function App settings at deploy time. They are never logged.

### 7.2 Per-Environment Secrets (optional override)

If you use separate app registrations per environment, add environment-scoped secrets for each of `dev`, `acc`, `prod` with the same names listed above.

---

## 8. Verify the Deployment

### 8.1 Health Check

```bash
curl https://func-digisandra-dev.azurewebsites.net/api/health
# Expected: HTTP 200
```

### 8.2 Bot Messaging Endpoint

Verify the Bot Service is configured with the correct endpoint:

```bash
az bot show \
  --name bot-digisandra-dev \
  --resource-group rg-digisandra-dev \
  --query properties.endpoint
# Expected: https://func-digisandra-dev.azurewebsites.net/api/messages
```

### 8.3 Application Insights

Open Application Insights (`appi-digisandra-dev`) in the Azure Portal and verify:

- **Live Metrics** shows the Function App running
- No authentication or dependency errors in **Failures**

### 8.4 Teams Channel

In the Azure Portal, open the Bot Service → **Channels** → **Microsoft Teams**:

- Status should show **Running**
- Use the provided **Open in Teams** link to test the bot directly

---

## 9. Azure Resources Reference

### Resource Naming

All resources follow the pattern `{type-prefix}-digisandra-{environment}`, except:

- Storage accounts: `stdigisandra{env}` (no hyphens, max 24 chars)
- Cosmos DB container: always `Requests`
- Cosmos DB database: always `SchedulingAgent`

### Environments

| Environment | Purpose | Auto-deployed on push |
|------------|---------|----------------------|
| `dev` | Development / integration testing | Yes (push to `main`) |
| `acc` | Acceptance / UAT | Manual via `workflow_dispatch` |
| `prod` | Production | Manual via `workflow_dispatch` |

### Azure OpenAI Quota

The `gpt-4o` deployment is provisioned at **30K tokens per minute (Standard)**. Adjust the `capacity` parameter in `infra/modules/openAi.bicep` if your usage requires more throughput, subject to your subscription quota.

---

## 10. Configuration Reference

All configuration is injected into the Function App as application settings by the Bicep deployment. The following table shows every setting, its source, and which Bicep secure parameter or Bicep output populates it.

| Application Setting | Source | Notes |
|--------------------|--------|-------|
| `AzureWebJobsStorage` | Storage account connection string | Auto-generated by Bicep |
| `FUNCTIONS_EXTENSION_VERSION` | `~4` | Fixed |
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` | Fixed |
| `APPINSIGHTS_INSTRUMENTATIONKEY` | App Insights output | Auto-injected |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | App Insights output | Auto-injected |
| `CosmosDb__Endpoint` | Cosmos DB output | e.g., `https://cosmos-digisandra-dev.documents.azure.com:443/` |
| `CosmosDb__DatabaseName` | `SchedulingAgent` | Fixed |
| `CosmosDb__ContainerName` | `Requests` | Fixed |
| `CosmosDb__DefaultTtlSeconds` | `604800` | 7 days (GDPR) |
| `AzureOpenAI__Endpoint` | OpenAI output | e.g., `https://oai-digisandra-dev.openai.azure.com/` |
| `AzureOpenAI__DeploymentName` | `gpt-4o` | Fixed |
| `MicrosoftGraph__TenantId` | `graphTenantId` parameter | Your Azure Tenant ID |
| `MicrosoftGraph__ClientId` | `graphClientId` parameter (secure) | Graph app registration |
| `MicrosoftGraph__ClientSecret` | `graphClientSecret` parameter (secure) | Graph app registration |
| `Bot__MicrosoftAppId` | `botAppId` parameter (secure) | Bot app registration |
| `Bot__MicrosoftAppPassword` | `botAppPassword` parameter (secure) | Bot app registration |
| `ConflictResolution__TimeoutHours` | `4` | Hours before conflict escalation |
| `ConflictResolution__MaxRetries` | `3` | Retry attempts for Graph calls |

---

## Summary Checklist

- [ ] Azure CLI, .NET 8 SDK, Bicep CLI installed
- [ ] Resource groups created for each environment
- [ ] Bot Framework app registration created → `BOT_APP_ID`, `BOT_APP_PASSWORD` noted
- [ ] Graph app registration created → `GRAPH_CLIENT_ID`, `GRAPH_CLIENT_SECRET` noted
- [ ] Graph API application permissions added and admin consent granted
- [ ] CI/CD service principal created with OIDC federated credentials
- [ ] GitHub repository secrets configured (Section 7)
- [ ] GitHub Environments created (`dev`, `acc`, `prod`)
- [ ] Bicep infrastructure deployed (manual or via GitHub Actions push to `main`)
- [ ] Managed identity role assignments applied (Cosmos DB + OpenAI)
- [ ] Health check endpoint returns HTTP 200
- [ ] Bot Service Teams channel shows Running
