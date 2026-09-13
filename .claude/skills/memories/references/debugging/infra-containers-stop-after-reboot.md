On 2026-09-11 every `actual-chat-infra-*` container (redis, postgres, nats, nginx, imageproxy, seq, opensearch, smtp4dev,
dns-forwarder) was `Exited (255)` hours after a reboot. server-loop kept cycling: Step 3 starts, `Failed to connect to Redis`,
watchdog misses, restart. The seq MCP also fails to connect in that state.

**Why:** it looks like a code/build problem from the loop log alone.

**How to apply:** on a loop that never reaches `Watchdog: started`, check `docker ps -a --filter name=actual-chat-infra`
and `docker start` the exited ones (existing containers, no compose needed). Git Bash `curl https://local.voxt.ai` fails
TLS (exit 35) even when nginx is fine — probe with PowerShell `Invoke-WebRequest` or the browser. Related: [[server-loop-owns-builds]].
