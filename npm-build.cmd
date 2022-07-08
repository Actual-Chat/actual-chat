:<<BATCH
    rem call "./npm-install.cmd"
    pushd src\nodejs
    call npx webpack --config webpack.config.js --mode development
    popd

    exit /b
BATCH

#!/bin/sh
# "./npm-install.cmd"
pushd src/nodejs
npm run build --prefix src/nodejs
popd
