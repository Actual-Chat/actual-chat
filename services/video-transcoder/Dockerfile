FROM golang:1.24-alpine AS build
WORKDIR /src
COPY go.mod main.go ./
RUN CGO_ENABLED=0 go build -trimpath -ldflags="-s -w" -o /video-transcoder .

FROM alpine:3.22
RUN apk add --no-cache ffmpeg
COPY --from=build /video-transcoder /usr/local/bin/video-transcoder
ENTRYPOINT ["/usr/local/bin/video-transcoder"]
