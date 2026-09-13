Until 2026-09-11 the `prod` branch of flux-team-core set `ChatSettings__OpenAIModel=gpt-4.1`,
`ChatSettings__Translation__OpenAIModel=gpt-4.1` and `ChatSettings__Translation__RealtimeOpenAIModel=gpt-4.1-nano`
(added 2025-08-12, commit 1632ce7a). Dev has no such overrides and runs the code defaults.

App commit 69cb4bdb97 (v2.20) switched the defaults to gpt-5.6-terra/luna and made every OpenAI call send
`ReasoningEffort = None`. gpt-4.1 rejects that with `HTTP 400 Unrecognized request argument supplied:
reasoning_effort`, so every translation on prod failed while dev worked. Fixed by deleting the three overrides
(flux-team-core dc250b64, rolled out 18:51 UTC). `ChatSettings__LanguageDetection__RealtimeOpenAIModel` is still
set on prod but binds to no property, so it does nothing.

**Why:** a model change validated on dev does not prove anything about prod when prod pins a different model.
**How to apply:** before shipping an OpenAI model or request-parameter change, diff the model env vars in
`origin/prod` vs `origin/dev` of flux-team-core. Prod exception details are in `labels."exception.message"`,
not `textPayload`; successful translations log only at Debug. See [[dev-env-vars-via-flux-team-core]].

**Resolved 2026-09-12.** The code-side guard — `OpenAIModels.GetLowestReasoningEffort` (PR #4503, cherry-picked
to release/v2.20 as `97b0f5e42a`) — ships in prod image `2.20.130`, so a pin to an older model no longer 400s.
Prod and dev both run the code defaults now, with zero translation errors in the first 24h on gpt-5.6-terra.
Decision: do **not** re-add the pins — that would downgrade prod and restore the dev/prod divergence.
Open question: gpt-5.6-terra looked ~3x slower than gpt-4.1 (p50 1.26s vs 0.38s), but from ~11 sampled
Cloud Trace spans each; Cloud Trace free read quota rate-limits bigger pulls.
