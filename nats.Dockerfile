ARG IMAGE_TAG=2.10.10-alpine3.19
FROM nats:${IMAGE_TAG}
ENTRYPOINT ["nats-server"]
CMD ["--jetstream", "--http_port", "8222"]
