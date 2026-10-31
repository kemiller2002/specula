#!/usr/bin/env bash
# Builds the static Specula site into dist/ from a clean checkout:
#   npm ci && npm run build
set -euo pipefail
cd "$(dirname "$0")/.."

SHA="${SPECULA_SOURCE_SHA:-$(git rev-parse HEAD 2>/dev/null || echo unversioned-local-build)}"
BUILD_TIME="${SPECULA_BUILD_TIME:-$(date -u +%Y-%m-%dT%H:%M:%SZ)}"
SHORT="${SHA:0:12}"
LIMEN_VERSION="$(node -p "require('./node_modules/@echelon-foundry/limen/package.json').version")"
FORMA_VERSION="$(node -p "require('./node_modules/@echelon-foundry/design-system/package.json').version")"

echo "Building Specula ${SHORT} (Limen ${LIMEN_VERSION}, Forma ${FORMA_VERSION})"

dotnet publish src/Specula.Wasm -c Release -o .artifacts/wasm \
  -p:SpeculaSourceSha="${SHA}" -p:SpeculaBuildTime="${BUILD_TIME}" --nologo

rm -rf dist
mkdir -p dist
cp -R .artifacts/wasm/wwwroot/_framework dist/_framework

# Versioned directories keep a release's modules and styles together in
# browser caches (a Limen or Forma upgrade gets new URLs).
LIMEN_DIR="limen-${LIMEN_VERSION}"
FORMA_DIR="forma-${FORMA_VERSION}"
cp -R node_modules/@echelon-foundry/limen/dist "dist/${LIMEN_DIR}"
find "dist/${LIMEN_DIR}" \( -name '*.map' -o -name '*.d.ts' \) -delete
rm -rf "dist/${LIMEN_DIR}/tooling"
mkdir -p "dist/${FORMA_DIR}/brands"
for css in tokens foundations components; do
  cp "node_modules/@echelon-foundry/design-system/dist/${css}.css" "dist/${FORMA_DIR}/"
done
cp node_modules/@echelon-foundry/design-system/dist/brands/echelon.css "dist/${FORMA_DIR}/brands/"

sed -e "s#\./limen/#./${LIMEN_DIR}/#g" -e "s#_framework/dotnet.js#_framework/dotnet.js?v=${SHORT}#" -e "s#\./wasm-transport.js#./wasm-transport.js?v=${SHORT}#" web/main.js > dist/main.js
sed -e "s#\./limen/#./${LIMEN_DIR}/#g" web/wasm-transport.js > dist/wasm-transport.js
cp web/app.css dist/app.css
cp web/theme-bootstrap.js dist/theme-bootstrap.js
sed -e "s#__BUILD__#${SHORT}#g" -e "s#href=\"forma/#href=\"${FORMA_DIR}/#g" web/index.html > dist/index.html
cp dist/index.html dist/404.html
touch dist/.nojekyll

cat > dist/build.json <<JSON
{
  "application": "specula",
  "sourceSha": "${SHA}",
  "buildTime": "${BUILD_TIME}",
  "limen": "${LIMEN_VERSION}",
  "forma": "${FORMA_VERSION}"
}
JSON

echo "dist/ ready: $(du -sh dist | cut -f1)"
