Two ways an iPhone reaches `local.voxt.ai` on the dev Mac, and one that never works. The name
has no public record, so the phone must ask the Mac's `dns-forwarder` (dnsmasq, wildcard
`*.local.voxt.ai` → `LOCAL_IP`), and the TLS cert is signed by the shared
`.config/local.voxt.ai/ssl/rootCA.crt` (Alex's mkcert root), which the phone must carry with full
trust (Settings › General › About › Certificate Trust Settings). Confirmed on iPhone, 2026-09-16.

**Same Wi-Fi:** set the Wi-Fi network's DNS on the phone to the Mac's LAN IP by hand. Nothing else.

**Hotspot (Mac tethered to the phone):** the phone cannot take a manual DNS on cellular, so the
only route is an encrypted-DNS profile. DNS-over-TLS on port 853 works; DNS-over-HTTPS on a
non-443 port was installed, showed as "Неизвестно" and never got a query. Front dnsmasq with
[dnsproxy](https://github.com/AdguardTeam/dnsproxy) (a single binary; keep it in `tmp/dnsproxy/`):

```sh
LOCAL_IP="$(sed -n 's/^LOCAL_IP=//p' .env)"
dnsproxy --listen=0.0.0.0 --port=0 --tls-port=853 \
  --tls-crt=.config/local.voxt.ai/ssl/local.voxt.ai.crt \
  --tls-key=.config/local.voxt.ai/ssl/local.voxt.ai.key \
  --upstream="$LOCAL_IP:53"
```

Then install this profile on the phone (`ServerAddresses` = the Mac's hotspot IP, usually
`172.20.10.x`) and select it under Settings › General › VPN & Device Management › DNS. The
`SupplementalMatchDomains` keep every other name on the carrier resolver:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>PayloadContent</key><array><dict>
    <key>PayloadType</key><string>com.apple.dnsSettings.managed</string>
    <key>PayloadVersion</key><integer>1</integer>
    <key>PayloadIdentifier</key><string>ai.voxt.local.dns.dot.settings</string>
    <key>PayloadUUID</key><string>7D1B7A2E-3C7E-4B6B-9C1D-5B2F0E8A1A03</string>
    <key>PayloadDisplayName</key><string>local.voxt.ai DNS (DoT)</string>
    <key>DNSSettings</key><dict>
      <key>DNSProtocol</key><string>TLS</string>
      <key>ServerName</key><string>local.voxt.ai</string>
      <key>ServerAddresses</key><array><string>172.20.10.12</string></array>
      <key>SupplementalMatchDomains</key><array>
        <string>local.voxt.ai</string><string>local.actual.chat</string>
      </array>
    </dict>
  </dict></array>
  <key>PayloadDisplayName</key><string>Voxt local dev DNS (DoT, hotspot)</string>
  <key>PayloadIdentifier</key><string>ai.voxt.local.dns.dot</string>
  <key>PayloadType</key><string>Configuration</string>
  <key>PayloadUUID</key><string>7D1B7A2E-3C7E-4B6B-9C1D-5B2F0E8A1A04</string>
  <key>PayloadVersion</key><integer>1</integer>
</dict></plist>
```

To get the file onto the phone without DNS, serve it over plain HTTP on the hotspot IP with the
right MIME type and open the URL in Safari — a `.mobileconfig` served as `application/octet-stream`
is downloaded as a file, not offered as a profile:

```sh
python3 -c "import http.server as h, mimetypes as m; m.add_type('application/x-apple-aspen-config', '.mobileconfig'); h.ThreadingHTTPServer(('$LOCAL_IP', 8099), h.SimpleHTTPRequestHandler).serve_forever()"
```

**Never works:** phone on LTE, Mac on Wi-Fi — there is no route between them at all.

**Why:** three things bake the Mac's IP in at start and go stale silently when it changes (Wi-Fi ↔
hotspot): the `dns-forwarder` container (binds `LOCAL_IP` at creation), dnsproxy (its `--upstream`),
and the profile (`ServerAddresses`). A stale forwarder still answers, with the *old* address, so
the phone loads nothing and every log looks healthy.

**How to apply:** after any IP change on the Mac, update `LOCAL_IP` in `.env`, run
`docker compose up -d --force-recreate dns-forwarder`, restart dnsproxy, and re-check with
`dig @$LOCAL_IP local.voxt.ai`. Deselect the profile when back on Wi-Fi, otherwise it keeps
sending `local.voxt.ai` lookups to the hotspot address and the manual Wi-Fi DNS appears broken.
Docker Desktop's nginx log shows a relay source IP, not the phone's — grep the iPhone user agent.
See [[infra-containers-stop-after-reboot]] for the forwarder not running at all.
