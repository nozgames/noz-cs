#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
NOZ_DIR="$SCRIPT_DIR/.."
REPO_DIR="$NOZ_DIR/.."
APP_PATH="$NOZ_DIR/editor/platform/ios/bin/Debug/net10.0-ios/ios-arm64/NoZ.Editor.iOS.app"

echo "=== Building Editor (unsigned) ==="
dotnet build "$NOZ_DIR/editor/platform/ios/NoZ.Editor.iOS.csproj" -r ios-arm64 -c Debug \
    /p:EnableCodeSigning=false /p:_RequireCodeSigning=false

echo ""
echo "=== Signing & Installing ==="
"$REPO_DIR/sign-ios.sh" "$APP_PATH" com.nozgames.editor
