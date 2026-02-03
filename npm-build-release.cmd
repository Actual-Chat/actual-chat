:<<BATCH
    rem call "./npm-install.cmd"
    pushd src\nodejs
    call npm run build:Release
    popd

    exit /b
BATCH

#!/bin/sh
# "./npm-install.cmd"
pushd src/nodejs
npm run build:Release
popd
