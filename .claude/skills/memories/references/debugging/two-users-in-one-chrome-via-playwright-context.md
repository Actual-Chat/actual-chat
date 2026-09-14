On 2026-09-03 the `mcp__chrome1__*` bridge and a direct Playwright `connectOverCDP` to `:9222`
both timed out (`Network.enable` never answered) because that instance is Alex's real
default-profile Chrome with many extension/omnibox targets. `:9223` (chrome2) was fine.

**Why:** the debug-ui skill assumes two Chromes for two logins, but the first one is not
reliably automatable.

**How to apply:** for a two-user test, keep the user under test in chrome2's default context
(via `mcp__chrome2__*`) and host the second user in `browser.newContext()` created by
Playwright over CDP on `:9223` — it gets its own cookie jar, unlike the MCP `isolatedContext`.
A tiny file-polling command agent (`tmp/pw-agent.mjs` + `tmp/pw-send.sh`) keeps that context
alive across steps. Test phones: `+1 555 555 5550` = `4An15V`, `+1 555 555 5551` = `7GI0pt`.
Related: [[server-loop-owns-builds]], [[headless-chrome-for-ui-measurement]].
