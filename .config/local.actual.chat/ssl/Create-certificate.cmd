# Self-signed CAs is required for Android
# https://support.hcltechsw.com/csm?id=kb_article&sysparm_article=KB0028834
# https://android.stackexchange.com/questions/237141/how-to-get-android-11-to-trust-a-user-root-ca-without-a-private-key

"c:\Program Files\Git\usr\bin\openssl" req -x509 -nodes -days 825 -newkey rsa:2048 -keyout local.actual.chat.key -out local.actual.chat.crt -reqexts v3_req -extensions v3_ca -subj "/C=/ST=/L=/O=actual.chat/CN=local.actual.chat" -addext "subjectAltName = DNS:local.actual.chat, DNS:cdn.local.actual.chat, DNS:media.local.actual.chat"

# check certificate props "c:\Program Files\Git\usr\bin\openssl" x509 -in local.actual.chat.crt -text -noout
