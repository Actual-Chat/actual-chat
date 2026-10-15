// The Cloud Run service that transcodes what Google Transcoder can't take. POST / with
// {"inputUrl", "outputUrl", "arguments"}: ffmpeg reads the source from the signed inputUrl, takes
// "arguments" as its output options, and the MP4 is PUT to the signed outputUrl before the response.
// GET / only prints the ffmpeg version, which is enough to time a cold start.
package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"log"
	"net/http"
	"net/url"
	"os"
	"os/exec"
	"time"
)

const outputContentType = "video/mp4"

type transcodeRequest struct {
	InputURL  string   `json:"inputUrl"`
	OutputURL string   `json:"outputUrl"`
	Arguments []string `json:"arguments"`
}

type transcodeResponse struct {
	Size            int64 `json:"size"`
	TranscodeMillis int64 `json:"transcodeMillis"`
	UploadMillis    int64 `json:"uploadMillis"`
}

func main() {
	port := os.Getenv("PORT")
	if port == "" {
		port = "8080"
	}
	server := &http.Server{
		Addr:              ":" + port,
		Handler:           http.HandlerFunc(handle),
		ReadHeaderTimeout: 10 * time.Second,
	}
	log.Printf("listening on %s", server.Addr)
	log.Fatal(server.ListenAndServe())
}

func handle(w http.ResponseWriter, r *http.Request) {
	switch r.Method {
	case http.MethodGet:
		version, err := exec.CommandContext(r.Context(), "ffmpeg", "-hide_banner", "-version").Output()
		if err != nil {
			http.Error(w, err.Error(), http.StatusInternalServerError)
			return
		}
		w.Write(bytes.SplitN(version, []byte("\n"), 2)[0])
	case http.MethodPost:
		transcode(w, r)
	default:
		http.Error(w, "method not allowed", http.StatusMethodNotAllowed)
	}
}

func transcode(w http.ResponseWriter, r *http.Request) {
	var request transcodeRequest
	if err := json.NewDecoder(io.LimitReader(r.Body, 1<<20)).Decode(&request); err != nil {
		http.Error(w, "invalid request: "+err.Error(), http.StatusBadRequest)
		return
	}
	if !isHttpURL(request.InputURL) || !isHttpURL(request.OutputURL) {
		http.Error(w, "inputUrl and outputUrl must be http(s) URLs", http.StatusBadRequest)
		return
	}

	output, err := os.CreateTemp("", "output-*.mp4")
	if err != nil {
		http.Error(w, err.Error(), http.StatusInternalServerError)
		return
	}
	output.Close()
	defer os.Remove(output.Name())

	startedAt := time.Now()
	args := []string{"-hide_banner", "-nostdin", "-loglevel", "warning", "-y", "-i", request.InputURL}
	args = append(append(args, request.Arguments...), output.Name())
	// The request's context kills ffmpeg when the caller gives up
	ffmpeg := exec.CommandContext(r.Context(), "ffmpeg", args...)
	var stderr bytes.Buffer
	ffmpeg.Stderr = io.MultiWriter(os.Stderr, &stderr)
	if err := ffmpeg.Run(); err != nil {
		log.Printf("ffmpeg failed after %v: %v", time.Since(startedAt), err)
		http.Error(w, fmt.Sprintf("ffmpeg failed: %v\n%s", err, tail(stderr.Bytes(), 4096)),
			http.StatusUnprocessableEntity)
		return
	}
	transcodeDuration := time.Since(startedAt)

	startedAt = time.Now()
	size, err := upload(r, output.Name(), request.OutputURL)
	if err != nil {
		log.Printf("upload failed: %v", err)
		http.Error(w, "upload failed: "+err.Error(), http.StatusBadGateway)
		return
	}
	uploadDuration := time.Since(startedAt)
	log.Printf("transcoded in %v, uploaded %d bytes in %v", transcodeDuration, size, uploadDuration)

	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(transcodeResponse{
		Size:            size,
		TranscodeMillis: transcodeDuration.Milliseconds(),
		UploadMillis:    uploadDuration.Milliseconds(),
	})
}

func upload(r *http.Request, path string, outputURL string) (int64, error) {
	var lastErr error
	for attempt := 0; attempt < 3; attempt++ {
		size, err := uploadOnce(r, path, outputURL)
		if err == nil {
			return size, nil
		}
		lastErr = err
		if r.Context().Err() != nil {
			break
		}
		time.Sleep(time.Duration(attempt+1) * time.Second)
	}
	return 0, lastErr
}

func uploadOnce(r *http.Request, path string, outputURL string) (int64, error) {
	file, err := os.Open(path)
	if err != nil {
		return 0, err
	}
	defer file.Close()
	info, err := file.Stat()
	if err != nil {
		return 0, err
	}

	request, err := http.NewRequestWithContext(r.Context(), http.MethodPut, outputURL, file)
	if err != nil {
		return 0, err
	}
	request.ContentLength = info.Size()
	request.Header.Set("Content-Type", outputContentType)
	response, err := http.DefaultClient.Do(request)
	if err != nil {
		return 0, err
	}
	defer response.Body.Close()
	if response.StatusCode/100 != 2 {
		body, _ := io.ReadAll(io.LimitReader(response.Body, 4096))
		return 0, fmt.Errorf("PUT returned %s: %s", response.Status, body)
	}
	return info.Size(), nil
}

func isHttpURL(value string) bool {
	parsed, err := url.Parse(value)
	return err == nil && (parsed.Scheme == "https" || parsed.Scheme == "http") && parsed.Host != ""
}

func tail(data []byte, maxLength int) []byte {
	if len(data) <= maxLength {
		return data
	}
	return data[len(data)-maxLength:]
}
