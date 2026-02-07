#!/bin/bash
# deploy.sh - Deploy mod to local Old World mods folder for testing
#
# Prerequisites:
#   .env file with OLDWORLD_MODS_PATH set
#
# Usage: ./scripts/deploy.sh

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
cd "$PROJECT_DIR"

# Load .env
if [ -f ".env" ]; then
    source ".env"
else
    echo "Error: .env file not found"
    echo "Create a .env file with OLDWORLD_MODS_PATH set"
    exit 1
fi

if [ -z "$OLDWORLD_MODS_PATH" ]; then
    echo "Error: OLDWORLD_MODS_PATH not set in .env"
    exit 1
fi

MOD_FOLDER="$OLDWORLD_MODS_PATH/Aristocratic Republic"

echo "=== Deploying to mods folder ==="
echo "Target: $MOD_FOLDER"

mkdir -p "$MOD_FOLDER"

cp ModInfo.xml "$MOD_FOLDER/"
cp logo-512.png "$MOD_FOLDER/"
cp -r Infos "$MOD_FOLDER/"

echo ""
echo "=== Deployment complete ==="
echo "Deployed files:"
ls -la "$MOD_FOLDER/"
