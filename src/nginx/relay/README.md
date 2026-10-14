# RPC relay

An nginx that forwards RPC traffic to the origin. Clients reach it as `<relayId>.edge.<origin>`.

- **443** passes TLS through to `RELAY_ORIGIN`. The relay never decrypts anything, so the origin
  sees the relay's address, not the client's.
- **8443** answers every connection with one line and closes it:
  `v1|<relayId>|<client ip>|<unix seconds>|<signature>`. The signature is the base64url
  HMAC-SHA256 of everything before it, keyed by the relay's key (the key string itself, not
  its decoded bytes). The client sends the line to the origin as the `relayAttestation` query
  parameter of its RPC connection, and the origin takes the client's IP from it.

## Settings

| Variable | Meaning |
|---|---|
| `RELAY_ID` | The relay's name, the part before `.edge.` |
| `RELAY_ORIGIN` | `host:port` the traffic goes to |
| `RELAY_KEY` | Random, at least 32 bytes: `openssl rand -base64 32`. Never committed. |

The origin needs the same keys in `CoreServerSettings.RpcRelayKeys` (for example the
`CoreServer__RpcRelayKeys` environment variable): `kz1=key1;kz1=key2;tr1=key3`. A relay can have
several keys at once. Rotate by adding the new key at the origin, switching the relay, then
removing the old one.

`docker compose up` refuses to start when `RELAY_ID`, `RELAY_ORIGIN` or `RELAY_KEY` is empty.
An empty key is never valid: the origin ignores `relay=` entries without a key, because anyone can
compute an HMAC made with an empty key.

## When the attestation doesn't verify

The origin doesn't reject the connection. A wrong signature, an unknown relay, an expired line (older than 60 s
or more than 10 s in the future) or a missing one all mean the same: the origin takes the client's IP from
`CF-Connecting-IP` or `X-Forwarded-For`, as it does without a relay. Behind a relay that is the relay's
address, so IP-based decisions (the country gate, rate limits) see the relay. The client isn't told and
doesn't retry; it asks the relay for a fresh attestation every 20 s anyway.

## Running

```
cp .env.example .env   # fill RELAY_KEY
docker compose up -d
```

Check the attestation: `nc <relay host> 8443` prints one line.

An attestation is valid for 60 seconds. Anyone who sees one on the network can send it to the origin
within that time and have their connection attributed to its IP. They can't pick another IP.
