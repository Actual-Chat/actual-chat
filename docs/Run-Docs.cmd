:<<BATCH
    @echo off
    cd /d "%~dp0"

    if not exist node_modules (
        echo Installing dependencies...
        call npm install
    )

    echo Starting documentation server at http://localhost:7770 (https://docs.local.voxt.ai via nginx)
    echo Press Ctrl+C to stop.
    echo.
    call npm run docs:dev -- --open

    exit /b
BATCH

#!/bin/sh
cd "$(dirname "$0")"

if [ ! -d "node_modules" ]; then
    echo "Installing dependencies..."
    npm install
fi

echo "Starting documentation server at http://localhost:7770 (https://docs.local.voxt.ai via nginx)"
echo "Press Ctrl+C to stop."
echo
npm run docs:dev -- --open
