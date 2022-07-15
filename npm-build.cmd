:<<BATCH
    rem call "./npm-install.cmd"
    pushd src\nodejs
    call npm ci
    call npx webpack --config webpack.config.js --mode development
    popd

    exit /b
BATCH

#!/bin/sh
# "./npm-install.cmd"
pushd src/nodejs
npm ci
npx webpack --config webpack.config.js --mode development
popd
