/**
 * Stats Service
 * Aggregates and exposes metrics from all pipeline components
 */

import type { VideoPipeline } from './video-pipeline';

export interface PipelineMetrics {
  // Input stream
  inputResolution: string;
  inputFramerate: number;

  // Encoding
  encodedFrames: number;
  droppedFrames: number;
  keyFrames: number;
  encodingBitrate: number;
  encodingLatency: number;
  encoderHardwareAcceleration: string;

  // Segmentation (background blur)
  segmentationProcessedFrames?: number;
  segmentationAverageInferenceTime?: number;
  segmentationAverageBlurTime?: number;
  segmentationAverageTotalTime?: number;
  segmentationDroppedFrames?: number;
  segmentationBackend?: string;

  // Overall
  duration: number;
  totalLatency: number;
}

export class StatsService extends EventTarget {
    private updateInterval: number | null = null;
    private startTime = 0;
    private metrics: PipelineMetrics;
    private inputStream: MediaStream | null = null;

    constructor(
    private pipeline: VideoPipeline,
    inputStream?: MediaStream
    ) {
        super();
        this.inputStream = inputStream ?? null;
        this.metrics = this.getInitialMetrics();
    }

    setInputStream(stream: MediaStream): void {
        this.inputStream = stream;
        this.updateInputMetrics();
    }

    start(): void {
        this.startTime = performance.now();
        this.updateInputMetrics();

        this.updateInterval = window.setInterval(() => {
            this.updateMetrics();
        }, 500); // Update every 500ms
    }

    stop(): void {
        if (this.updateInterval) {
            clearInterval(this.updateInterval);
            this.updateInterval = null;
        }
    }

    private updateInputMetrics(): void {
        if (!this.inputStream) return;

        const videoTrack = this.inputStream.getVideoTracks()[0];
        const settings = videoTrack.getSettings();
        this.metrics.inputResolution = `${settings.width ?? 0}x${settings.height ?? 0}`;
        this.metrics.inputFramerate = settings.frameRate ?? 0;
    }

    private updateMetrics(): void {
        const encoderStats = this.pipeline.getEncoderStats();
        const segmentationStats = this.pipeline.getSegmentationStats();

        const duration = (performance.now() - this.startTime) / 1000;

        // Calculate encoding bitrate (kbps)
        const encodingBitrate = duration > 0
            ? (encoderStats.totalBytes * 8) / duration / 1000
            : 0;

        // Calculate total latency
        let totalLatency = encoderStats.averageEncodeTime;

        // Add segmentation latency if available
        if (segmentationStats) {
            totalLatency += segmentationStats.averageTotalTime;
        }

        this.metrics = {
            ...this.metrics,
            // Encoding
            encodedFrames: encoderStats.encodedFrames,
            droppedFrames: encoderStats.droppedFrames,
            keyFrames: encoderStats.keyFrames,
            encodingBitrate,
            encodingLatency: encoderStats.averageEncodeTime,
            encoderHardwareAcceleration: encoderStats.hardwareAcceleration,

            // Segmentation (if available)
            ...(segmentationStats && {
                segmentationProcessedFrames: segmentationStats.processedFrames,
                segmentationAverageInferenceTime: segmentationStats.averageInferenceTime,
                segmentationAverageBlurTime: segmentationStats.averageBlurTime,
                segmentationAverageTotalTime: segmentationStats.averageTotalTime,
                segmentationDroppedFrames: segmentationStats.droppedFrames,
                segmentationBackend: segmentationStats.backend
            }),

            // Overall
            duration,
            totalLatency
        };

        this.dispatchEvent(new CustomEvent('metrics-update', {
            detail: this.metrics
        }));
    }

    getMetrics(): PipelineMetrics {
        return { ...this.metrics };
    }

    private getInitialMetrics(): PipelineMetrics {
        return {
            inputResolution: 'N/A',
            inputFramerate: 0,
            encodedFrames: 0,
            droppedFrames: 0,
            keyFrames: 0,
            encodingBitrate: 0,
            encodingLatency: 0,
            encoderHardwareAcceleration: 'unknown',
            duration: 0,
            totalLatency: 0
        };
    }
}
