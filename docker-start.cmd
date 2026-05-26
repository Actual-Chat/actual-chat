pwsh -NoProfile -c ". ./scripts/Common.ps1; Update-LocalIP | Out-Null"
docker compose up -d --build --pull always --wait
