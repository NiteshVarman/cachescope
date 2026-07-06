#!/usr/bin/env bash
# Delete the entire CacheScope resource group so idle cost returns to ~zero.
# The serverless SQL database and scale-to-zero container app already cost ~nothing
# when idle, but a full teardown guarantees it. Re-run deploy.sh to bring it back.
# Usage:  RG=cachescope-rg ./infra/teardown.sh
set -euo pipefail

RG="${RG:-cachescope-rg}"
echo "Deleting resource group $RG (this removes ALL CacheScope resources)..."
az group delete -n "$RG" --yes --no-wait
echo "Deletion started. Redeploy any time with: ./infra/deploy.sh"
