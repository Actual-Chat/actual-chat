/**
 * MediaRecorder-based video recorder
 * Records the decoded output stream using MediaRecorder for proper muxing
 */

import { Log } from 'logging';

const { infoLog, warnLog, errorLog } = Log.get('VideoPipeline');

export class MediaStreamRecorder {
    private mediaRecorder: MediaRecorder | null = null;
    private recordedChunks: Blob[] = [];

    constructor(private mimeType = 'video/webm;codecs=vp9') {}

    /**
   * Start recording from a MediaStream
   */
    start(stream: MediaStream): void {
        this.recordedChunks = [];

        try {
            // Try to create MediaRecorder with specified MIME type
            const options: MediaRecorderOptions = {
                mimeType: this.mimeType,
                videoBitsPerSecond: 5000000 // 5 Mbps
            };

            // Test if MIME type is supported
            if (!MediaRecorder.isTypeSupported(this.mimeType)) {
                warnLog?.log('MIME type', this.mimeType, 'not supported, using default');
                this.mediaRecorder = new MediaRecorder(stream);
            } else {
                this.mediaRecorder = new MediaRecorder(stream, options);
            }

            this.mediaRecorder.ondataavailable = (event) => {
                if (event.data.size > 0) {
                    this.recordedChunks.push(event.data);
                }
            };

            this.mediaRecorder.start(100); // Collect data every 100ms
            infoLog?.log('MediaRecorder started with MIME type:', this.mediaRecorder.mimeType);
        } catch (error) {
            errorLog?.log('MediaRecorder error starting recording:', error);
            throw error;
        }
    }

    /**
   * Stop recording and return the final video blob
   */
    async stop(): Promise<Blob> {
        return new Promise((resolve, reject) => {
            if (!this.mediaRecorder) {
                reject(new Error('MediaRecorder not initialized'));
                return;
            }

            this.mediaRecorder.onstop = () => {
                const mimeType = this.mediaRecorder?.mimeType ?? this.mimeType;
                const blob = new Blob(this.recordedChunks, { type: mimeType });
                infoLog?.log('MediaRecorder stopped. Blob size:', (blob.size / 1024 / 1024).toFixed(2), 'MB, MIME:', mimeType);
                resolve(blob);
            };

            this.mediaRecorder.onerror = (event: Event) => {
                errorLog?.log('MediaRecorder error:', event);
                reject(new Error('MediaRecorder error'));
            };

            this.mediaRecorder.stop();
        });
    }

    /**
   * Get current state
   */
    getState(): RecordingState | undefined {
        return this.mediaRecorder?.state;
    }
}