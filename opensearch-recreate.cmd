docker compose rm -s -v -f opensearch-node opensearch-config opensearch-dashboards
docker volume prune -a -f
./docker-start.cmd
