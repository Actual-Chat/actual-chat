:<<BATCH
    @echo off
    npm run watch
    exit /b
BATCH

#!/bin/sh
# "./npm-install.cmd" && npm run watch
npm run watch
