#!/usr/bin/env bash
set -euo pipefail

PROJECT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_FILE="${PROJECT_DIR}/GalaxyBudsClient.iOS.csproj"

dotnet publish "${PROJECT_FILE}" \
  -c Release \
  -f net10.0-ios \
  -r ios-arm64 \
  /p:BuildIpa=true \
  /p:ArchiveOnBuild=true \
  /p:EnableCodeSigning=false

echo "Done. Check the publish output under:"
echo "${PROJECT_DIR}/bin/Release/net10.0-ios/ios-arm64/publish/"
