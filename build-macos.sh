#!/bin/bash
set -e

APP_NAME="OppoPodsManager"
BUNDLE_ID="com.oppo.podsmanager"
VERSION="1.1.5"
BUILD_DIR="bin/Release/net10.0/osx-arm64/publish"
APP_DIR="${BUILD_DIR}/${APP_NAME}.app"

echo "=== Building ${APP_NAME} for macOS ==="

# Step 1: Publish self-contained
echo "[1/3] Publishing self-contained build..."
dotnet publish -c Release -r osx-arm64 \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:PublishTrimmed=false \
  -o "${BUILD_DIR}"

# Step 2: Create .app bundle structure
echo "[2/3] Creating .app bundle..."
rm -rf "${APP_DIR}"
mkdir -p "${APP_DIR}/Contents/MacOS"
mkdir -p "${APP_DIR}/Contents/Resources"

# Copy executable and dependencies
cp "${BUILD_DIR}/${APP_NAME}" "${APP_DIR}/Contents/MacOS/"
cp "${BUILD_DIR}"/*.dylib "${APP_DIR}/Contents/MacOS/" 2>/dev/null || true

# Copy icon (convert ico to icns if needed, or use png)
if [ -f "Assets/tuopan.icns" ]; then
  cp "Assets/tuopan.icns" "${APP_DIR}/Contents/Resources/"
elif [ -f "Assets/tuopan.ico" ]; then
  # For now, just copy the ico - macOS can sometimes use it
  cp "Assets/tuopan.ico" "${APP_DIR}/Contents/Resources/"
fi

# Step 3: Create Info.plist
echo "[3/3] Creating Info.plist..."
cat > "${APP_DIR}/Contents/Info.plist" << 'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>OppoPodsManager</string>
    <key>CFBundleDisplayName</key>
    <string>OPPO Pods Manager</string>
    <key>CFBundleIdentifier</key>
    <string>com.oppo.podsmanager</string>
    <key>CFBundleVersion</key>
    <string>1.1.5</string>
    <key>CFBundleShortVersionString</key>
    <string>1.1.5</string>
    <key>CFBundleExecutable</key>
    <string>OppoPodsManager</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleSignature</key>
    <string>????</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSBluetoothAlwaysUsageDescription</key>
    <string>OPPO Pods Manager needs Bluetooth to connect to your earbuds.</string>
    <key>NSBluetoothPeripheralUsageDescription</key>
    <string>OPPO Pods Manager needs Bluetooth to connect to your earbuds.</string>
    <key>CFBundleIconFile</key>
    <string>tuopan</string>
</dict>
</plist>
PLIST

echo ""
echo "=== Build complete! ==="
echo "App bundle: ${APP_DIR}"
echo ""
echo "To run:"
echo "  open \"${APP_DIR}\""
echo ""
echo "To move to Applications:"
echo "  cp -R \"${APP_DIR}\" /Applications/"
