#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
NOZ_DIR="$SCRIPT_DIR/.."
APP_PATH="$NOZ_DIR/editor/platform/ios/bin/Debug/net10.0-ios/ios-arm64/NoZ.Editor.iOS.app"
DEVICE_ID="78EE01F8-2E37-5100-A454-CD62E48AECFC"
ENTITLEMENTS="$NOZ_DIR/editor/platform/ios/editor.entitlements"
PROVISION_PROFILE="$HOME/Library/Developer/Xcode/UserData/Provisioning Profiles/a20bd87f-7408-4283-9a39-1faed13effa9.mobileprovision"

echo "=== Building Editor (unsigned) ==="
dotnet build "$NOZ_DIR/editor/platform/ios/NoZ.Editor.iOS.csproj" -r ios-arm64 -c Debug \
    /p:EnableCodeSigning=false /p:_RequireCodeSigning=false

# Embed provisioning profile
echo "Embedding provisioning profile..."
cp "$PROVISION_PROFILE" "$APP_PATH/embedded.mobileprovision"

# Sign frameworks first
if [ -d "$APP_PATH/Frameworks" ]; then
    for fw in "$APP_PATH/Frameworks"/*; do
        echo "Signing $(basename "$fw")..."
        /usr/bin/codesign --force --sign "Apple Development" --timestamp=none "$fw"
    done
fi

# Sign the app with entitlements
echo "Signing NoZ.Editor.iOS.app..."
/usr/bin/codesign --force --sign "Apple Development" --timestamp=none --entitlements "$ENTITLEMENTS" "$APP_PATH"

echo "Installing to device..."
xcrun devicectl device install app --device "$DEVICE_ID" "$APP_PATH"

echo "Done."
