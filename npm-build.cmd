:<<BATCH
    rem call "./npm-install.cmd"
    call npm ci
    call npm run build:Debug

    exit /b
BATCH

#!/bin/sh
# "./npm-install.cmd"
npm ci
npm run build:Debug
