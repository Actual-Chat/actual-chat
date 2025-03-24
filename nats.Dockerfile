ARG IMAGE_TAG=2.11.0-alpine3.21
FROM nats:${IMAGE_TAG}
ENTRYPOINT ["nats-server"]
CMD ["--jetstream", "--http_port", "8222"]
