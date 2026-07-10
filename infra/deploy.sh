#!/usr/bin/env bash
# Provision (or update) all CacheScope infrastructure in one resource group.
# L4 is embedded SQLite in the API container, so there is NO database password.
# Usage:
#   RG=cachescope-rg LOCATION=centralindia \
#   GHCR_USER='<github-user>' GHCR_TOKEN='<pat>' \
#   IMAGE='ghcr.io/<user>/cachescope-host:latest' ./infra/deploy.sh
#   (GHCR_USER/GHCR_TOKEN are optional when the image is public.)
set -euo pipefail

RG="${RG:-cachescope-rg}"
LOCATION="${LOCATION:-centralindia}"
: "${IMAGE:?set IMAGE}"

echo "Creating resource group $RG in $LOCATION..."
az group create -n "$RG" -l "$LOCATION" -o none

echo "Deploying infrastructure (Bicep)..."
az deployment group create \
  -g "$RG" \
  -f infra/main.bicep \
  -p ghcrUsername="${GHCR_USER:-}" ghcrToken="${GHCR_TOKEN:-}" containerImage="$IMAGE" \
  -o table

echo "Done. API FQDN:"
az deployment group show -g "$RG" -n main --query properties.outputs.apiFqdn.value -o tsv
