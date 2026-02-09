:<<BATCH
    rem call "./npm-install.cmd"
    call npm run build:Release

    exit /b
BATCH

#!/bin/sh
# "./npm-install.cmd"
npm run build:Release
