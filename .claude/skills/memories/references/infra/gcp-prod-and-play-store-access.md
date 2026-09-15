Canonical reference: the user-level `/production-data-access` skill
(`C:\Users\Alex\.claude\skills\production-data-access\SKILL.md`) — invoke it instead of
relying on the summary below.

- Prod GCP project: `actual-chat-app-prod` (dev: `actual-chat-app-dev`, active gcloud config default). Account: `alex.yakunin@actual.chat`.
- Prod logs: `gcloud logging read 'severity>=ERROR' --project=actual-chat-app-prod --freshness=1h --limit=N`.
- Google Play Console reports bucket: `gs://pubsite_prod_6291022562349091998/` (developer account id 6291022562349091998, derived from the public Play listing's `dev?id=` link; app package `chat.actual.app`, dev flavor `chat.actual.dev.app`). Contains `reviews/` and `stats/{installs,crashes,ratings,store_performance}/`, monthly CSVs like `stats/installs/installs_chat.actual.app_YYYYMM_overview.csv`. Files are gzip-compressed UTF-16LE CSV despite the .csv name — download with `gcloud storage cp`, then gunzip + decode Unicode. Data lags ~2–4 days.
- No Play Developer API (androidpublisher) service account is set up; the GCS bucket is the only non-interactive route.
- gcloud auth tokens expire periodically (org reauth policy) with "Reauthentication failed: cannot prompt during non-interactive execution". Fix: run `gcloud auth login --launch-browser` as a background task — it opens the browser, the user completes sign-in, credentials land in the shared Windows store. WSL/Docker logins do NOT help the host gcloud ([[windows-ipv6-loopback-docker-hang]] is unrelated but same host/container split theme).
