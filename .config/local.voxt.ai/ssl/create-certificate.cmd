:<<BATCH
    REM Self-signed CAs is required for Android
    REM https://support.hcltechsw.com/csm?id=kb_article&sysparm_article=KB0028834
    REM https://android.stackexchange.com/questions/237141/how-to-get-android-11-to-trust-a-user-root-ca-without-a-private-key

    "c:\Program Files\Git\usr\bin\openssl" req -x509 -nodes -days 825 -newkey rsa:2048 -keyout local.voxt.ai.key -out local.voxt.ai.crt -reqexts v3_req -extensions v3_ca -subj "/C=/ST=/L=/O=voxt.ai/CN=local.voxt.ai" -addext "subjectAltName = DNS:local.voxt.ai, DNS:cdn.local.voxt.ai, DNS:media.local.voxt.ai, DNS:local.actual.chat, DNS:cdn.local.actual.chat, DNS:media.local.actual.chat"

    REM check certificate props "c:\Program Files\Git\usr\bin\openssl" x509 -in local.voxt.ai.crt -text -noout

    exit /b
BATCH

#!/bin/sh

# Self-signed CAs is required for Android
# https://support.hcltechsw.com/csm?id=kb_article&sysparm_article=KB0028834
# https://android.stackexchange.com/questions/237141/how-to-get-android-11-to-trust-a-user-root-ca-without-a-private-key

openssl req -x509 -nodes -days 825 -newkey rsa:2048 -keyout local.voxt.ai.key -out local.voxt.ai.crt -reqexts v3_req -extensions v3_ca -subj "/C=/ST=/L=/O=voxt.ai/CN=local.voxt.ai" -addext "subjectAltName = DNS:local.voxt.ai, DNS:cdn.local.voxt.ai, DNS:media.local.voxt.ai, DNS:local.actual.chat, DNS:cdn.local.actual.chat, DNS:media.local.actual.chat"

# check certificate props "openssl x509 -in local.voxt.ai.crt -text -noout"
