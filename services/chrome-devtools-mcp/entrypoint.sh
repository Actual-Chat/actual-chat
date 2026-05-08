#!/bin/sh
# Supervisor for supergateway + chrome-devtools-mcp.
#
# Layer 1 (main loop): wait for Chrome, then run supergateway. If it exits,
#   restart after a short backoff. Container-level `restart: unless-stopped`
#   handles the case where this script itself dies.
#
# Layer 2 (chrome watcher): polls Chrome's debug endpoint; after several
#   consecutive failures, kills supergateway so the main loop recycles it.
#   Needed because supergateway in --stateful mode keeps its HTTP listener
#   alive even when the inner stdio child (chrome-devtools-mcp) becomes
#   useless after Chrome restarts — a healthcheck on /healthz wouldn't catch
#   that.
set -u

PORT="${PORT:-8765}"
CHROME_DEBUG_PORT="${CHROME_DEBUG_PORT:-9222}"
WATCH_INTERVAL="${WATCH_INTERVAL:-3}"
WATCH_FAIL_THRESHOLD="${WATCH_FAIL_THRESHOLD:-3}"
# How often to re-log "waiting for Chrome..." while it stays down (seconds).
WAIT_LOG_INTERVAL="${WAIT_LOG_INTERVAL:-60}"
# Poll period of the main wait loop (seconds).
WAIT_POLL_INTERVAL="${WAIT_POLL_INTERVAL:-5}"

resolve_host_ip() {
  getent ahosts host.docker.internal | awk '$1 ~ /\./ { print $1; exit }'
}

chrome_ok() {
  wget -qO- --timeout=3 --tries=1 "http://${HOST_IP}:${CHROME_DEBUG_PORT}/json/version" >/dev/null 2>&1
}

log() { printf '[entrypoint] %s\n' "$*"; }

HOST_IP="$(resolve_host_ip)"
if [ -z "$HOST_IP" ]; then
  log "FATAL: could not resolve host.docker.internal"
  exit 1
fi
log "host.docker.internal -> ${HOST_IP}, target Chrome :${CHROME_DEBUG_PORT}, gateway :${PORT}"

# Watcher: recycle supergateway when Chrome flaps.
# Skips silently if supergateway isn't running — the main loop's wait phase
# already covers that case, no need to duplicate the noise.
(
  fails=0
  while true; do
    if chrome_ok; then
      fails=0
    else
      fails=$((fails + 1))
      if [ "$fails" -ge "$WATCH_FAIL_THRESHOLD" ]; then
        if pgrep -f 'supergateway' >/dev/null 2>&1; then
          log "watcher: Chrome unreachable ${fails}x, recycling supergateway"
          pkill -TERM -f 'supergateway' 2>/dev/null || true
        fi
        fails=0
      fi
    fi
    sleep "$WATCH_INTERVAL"
  done
) &

# Main loop.
while true; do
  waited=0
  logged_at=-1
  while ! chrome_ok; do
    if [ "$logged_at" -lt 0 ] || [ "$((waited - logged_at))" -ge "$WAIT_LOG_INTERVAL" ]; then
      log "waiting for Chrome at http://${HOST_IP}:${CHROME_DEBUG_PORT}..."
      logged_at="$waited"
    fi
    sleep "$WAIT_POLL_INTERVAL"
    waited=$((waited + WAIT_POLL_INTERVAL))
  done
  log "Chrome reachable; starting supergateway"
  supergateway \
    --stdio "chrome-devtools-mcp --browserUrl http://${HOST_IP}:${CHROME_DEBUG_PORT} --acceptInsecureCerts" \
    --outputTransport streamableHttp \
    --stateful \
    --port "$PORT" \
    --healthEndpoint /healthz \
    --logLevel info \
    || true
  log "supergateway exited; restarting in 2s"
  sleep 2
done
