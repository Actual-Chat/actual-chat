:<<BATCH
    echo To install mkcert on Windows: choco install mkcert
    echo Details: https://chocolatey.org/packages/mkcert

    mkcert -install
    mkcert local.voxt.ai "*.local.voxt.ai" local.actual.chat "*.local.actual.chat"
    move /Y local.voxt.ai+3.pem local.voxt.ai.crt
    move /Y local.voxt.ai+3-key.pem local.voxt.ai.key

    exit /b
BATCH

#!/bin/sh

# To install mkcert:
#   macOS: brew install mkcert
#   Windows: choco install mkcert
#   Linux: https://github.com/FiloSottile/mkcert#installation

mkcert -install
mkcert local.voxt.ai "*.local.voxt.ai" local.actual.chat "*.local.actual.chat"
mv -f local.voxt.ai+3.pem local.voxt.ai.crt
mv -f local.voxt.ai+3-key.pem local.voxt.ai.key
