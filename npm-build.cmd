:<<BATCH
    rem call "./npm-install.cmd"
    pushd src\nodejs
    call npm ci
    call npm run build:Debug
    popd

    exit /b
BATCH

#!/bin/sh
# "./npm-install.cmd"
pushd src/nodejs
npm ci
npm run build:Debug
popd
