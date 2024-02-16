ARG IMAGE_TAG=2.10.10-alpine3.19
FROM nats:${IMAGE_TAG}
CMD ["nats-server", "--config", "/etc/nats/nats-server.conf", "--jetstream"]
