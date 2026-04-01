:<<BATCH
    @echo off
    npm test %*
    exit /b %ERRORLEVEL%
BATCH

#!/bin/sh
npm test "$@"
exit $?
