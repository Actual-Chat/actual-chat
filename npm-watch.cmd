:<<BATCH
    call "./npm-install.cmd"
    npm run watch --prefix src/nodejs

    exit /b
BATCH

#!/bin/sh
"./npm-install.cmd" && npm run watch --prefix src/nodejs
