:<<BATCH
    @echo off
    call npm run test:unit %*
    exit /b %ERRORLEVEL%
BATCH

#!/bin/sh
npm run test:unit "$@"
exit $?
