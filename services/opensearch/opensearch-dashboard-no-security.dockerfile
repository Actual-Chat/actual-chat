ARG OPENSEARCH_VERSION
FROM opensearchproject/opensearch-dashboards:${OPENSEARCH_VERSION}
RUN /usr/share/opensearch-dashboards/bin/opensearch-dashboards-plugin remove securityDashboards
COPY --chown=opensearch-dashboards:opensearch-dashboards opensearch_dashboards.no-security.yaml /usr/share/opensearch-dashboards/config/opensearch_dashboards.yml
