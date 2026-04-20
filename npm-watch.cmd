:<<BATCH
    REM call "./npm-install.cmd"
    npm run watch

    exit /b
BATCH

#!/bin/sh
# "./npm-install.cmd" && npm run watch
npm run watch
