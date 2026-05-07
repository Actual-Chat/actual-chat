# Bridges stdio chrome-devtools-mcp to streamable HTTP via supergateway.
# Packages are pre-installed so container start is fast and offline-safe;
# bumping versions = rebuild image.
FROM node:20-alpine

RUN apk add --no-cache wget tini \
 && npm install -g --omit=dev \
        supergateway@3.4.0 \
        chrome-devtools-mcp@0.20.0 \
 && npm cache clean --force

COPY entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh

EXPOSE 8765
ENTRYPOINT ["/sbin/tini", "--", "/usr/local/bin/entrypoint.sh"]
