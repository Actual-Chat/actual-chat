ARG MONITOR_TAG
FROM golang:1.20.1-alpine as builder

WORKDIR /build
ARG GCSFUSE_GIT_TAG
RUN apk --update --no-cache add fuse fuse-dev \
    && wget https://github.com/GoogleCloudPlatform/gcsfuse/archive/refs/tags/${GCSFUSE_GIT_TAG}.tar.gz -O - | tar -xz --strip-components=1 \
    && go install ./tools/build_gcsfuse \
    && build_gcsfuse . /tmp ${GCSFUSE_GIT_TAG}

FROM mcr.microsoft.com/dotnet/nightly/monitor:${MONITOR_TAG}
RUN apk add --update --no-cache ca-certificates fuse \
    && mkdir -p /mnt/gcs-blobs

COPY --from=builder /tmp/bin/gcsfuse /usr/local/bin/gcsfuse
COPY --from=builder /tmp/sbin/mount.gcsfuse /usr/sbin/mount.gcsfuse
