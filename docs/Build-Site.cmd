:<<BATCH
    @echo off
    cd /d "%~dp0"

    if not exist node_modules (
        echo Installing dependencies...
        call npm install
    )

    echo Building documentation site...
    call npm run docs:build

    echo.
    echo Site built successfully!
    echo Output: %~dp0.vitepress\dist\

    exit /b
BATCH

#!/bin/sh
cd "$(dirname "$0")"

if [ ! -d "node_modules" ]; then
    echo "Installing dependencies..."
    npm install
fi

echo "Building documentation site..."
npm run docs:build

echo
echo "Site built successfully!"
echo "Output: $(pwd)/.vitepress/dist/"
