#!/bin/sh
# LG webOS paketini (.ipk) üretir: ortak web/ dosyaları + webos/appinfo.json ve simgeler.
# Gerekli: npm install -g @webos-tools/cli
set -e
cd "$(dirname "$0")"
rm -rf build/webos && mkdir -p build/webos
cp web/index.html web/app.js build/webos/
cp webos/appinfo.json webos/icon.png webos/largeIcon.png build/webos/
ares-package build/webos -o .
