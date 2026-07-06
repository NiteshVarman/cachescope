#!/usr/bin/env bash
# Provision (or update) all CacheScope infrastructure in one resource group.
# Usage:
#   RG=cachescope-rg LOCATION=centralindia \
#   SQL_PW='<strong-password>' GHCR_USER='<github-user>' GHCR_TOKEN='<pat>' \
#   IMAGE='ghcr.io/<user>/cachescope-host:latest' ./infra/deploy.sh
set -euo pipefail

RG="${RG:-cachescope-rg}"
LOCATION="${LOCATION:-centralindia}"
: "${SQL_PW:?set SQL_PW}"
: "${GHCR_USER:?set GHCR_USER}"
: "${GHCR_TOKEN:?set GHCR_TOKEN}"
: "${IMAGE:?set IMAGE}"

echo "Creating resource group $RG in $LOCATION..."
az group create -n "$RG" -l "$LOCATION" -o none

echo "Deploying infrastructure (Bicep)..."
az deployment group create \
  -g "$RG" \
  -f infra/main.bicep \
  -p sqlAdminPassword="$SQL_PW" ghcrUsername="$GHCR_USER" ghcrToken="$GHCR_TOKEN" containerImage="$IMAGE" \
  -o table

echo "Done. API FQDN:"
az deployment group show -g "$RG" -n main --query properties.outputs.apiFqdn.value -o tsv
