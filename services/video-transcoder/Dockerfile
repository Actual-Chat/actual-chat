FROM alpine:3.22
RUN apk add --no-cache ffmpeg curl
COPY --chmod=755 transcode.sh /usr/local/bin/transcode.sh
ENTRYPOINT ["/usr/local/bin/transcode.sh"]
