---
name: deploy
description: Deploy DigiSandra to an environment via GitHub Actions
argument-hint: "[dev|acc|prod]"
disable-model-invocation: true
---

# Deploy DigiSandra

Deploy to environment: **$ARGUMENTS**

Valid environments: `dev`, `acc`, `prod`

## Steps

1. Verify target environment is valid (`dev`, `acc`, or `prod`)
2. Confirm all tests pass first by checking CI status
3. Trigger the `deploy.yml` GitHub Actions workflow for the target environment

## Deployment via GitHub Actions

```bash
# Trigger deploy workflow manually
gh workflow run deploy.yml --field environment=$ARGUMENTS
```

## Required GitHub Secrets

These must be configured in the repo before deployment works:

| Secret | Purpose |
|--------|---------|
| `AZURE_CLIENT_ID` | Service principal for OIDC auth |
| `AZURE_TENANT_ID` | Entra ID tenant |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription |
| `AZURE_RESOURCE_GROUP` | Target resource group |
| `BOT_APP_ID` | Bot Framework app registration |
| `BOT_APP_PASSWORD` | Bot Framework app secret |
| `GRAPH_CLIENT_ID` | Graph API app registration |
| `GRAPH_CLIENT_SECRET` | Graph API client secret |

## Local Development (not deployment)

```bash
# Prerequisites: .NET 8 SDK, Azure Functions Core Tools v4
dotnet build
cd src/SchedulingAgent && func start
```
