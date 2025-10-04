:<<BATCH
    echo To install mkcert on Windows: choco install mkcert
    echo Details: https://chocolatey.org/packages/mkcert

    mkcert -install
    mkcert local.voxt.ai cdn.local.voxt.ai media.local.voxt.ai local.actual.chat cdn.local.actual.chat media.local.actual.chat
    move /Y local.voxt.ai+5.pem local.voxt.ai.crt
    move /Y local.voxt.ai+5-key.pem local.voxt.ai.key

    exit /b
BATCH

#!/bin/sh

echo To install mkcert: https://chocolatey.org/packages/mkcert

mkcert -install
mkcert local.voxt.ai cdn.local.voxt.ai media.local.voxt.ai local.actual.chat cdn.local.actual.chat media.local.actual.chat
mv -f local.voxt.ai+5.pem local.voxt.ai.crt
mv -f local.voxt.ai+5-key.pem local.voxt.ai.key
