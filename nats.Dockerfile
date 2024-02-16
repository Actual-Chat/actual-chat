ARG IMAGE_TAG
FROM nats:${MONITOR_TAG}
CMD ["nats-server", "--config", "/etc/nats/nats-server.conf", "--jetstream"]
