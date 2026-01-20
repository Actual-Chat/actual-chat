/**
 * Stats Service
 * Aggregates and exposes metrics from all pipeline components
 */

import type { VideoPipeline } from './video-pipeline';
// import type { AV1VideoPipeline } from './av1-video-pipeline';

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

  // Transfer simulation
  transferredBytes: number;
  transferredChunks: number;
  transferLatency: number;
  packetsLost: number;
  jitter: number;
  throughput: number;
  currentBitrate: number;

  // Decoding
  decodedFrames: number;
  decodingLatency: number;
  decoderHardwareAcceleration: string;
  decoderResolution: string;

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
    private pipeline: VideoPipeline /*| AV1VideoPipeline*/,
    inputStream?: MediaStream
  ) {
    super();
    this.inputStream = inputStream || null;
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
    if (videoTrack) {
      const settings = videoTrack.getSettings();
      this.metrics.inputResolution = `${settings.width || 0}x${settings.height || 0}`;
      this.metrics.inputFramerate = settings.frameRate || 0;
    }
  }

  private updateMetrics(): void {
    const encoderStats = this.pipeline.getEncoderStats();
    const transferStats = this.pipeline.getTransferStats();
    const decoderStats = /*'getAV1DecoderStats' in this.pipeline
      ? this.pipeline.getAV1DecoderStats()
      : */this.pipeline.getDecoderStats();
    const segmentationStats = this.pipeline.getSegmentationStats();

    const duration = (performance.now() - this.startTime) / 1000;

    // Calculate encoding bitrate (kbps)
    const encodingBitrate = duration > 0
      ? (encoderStats.totalBytes * 8) / duration / 1000
      : 0;

    // Calculate total latency (sum of encoding, transfer, decoding, and segmentation)
    let totalLatency =
      encoderStats.averageEncodeTime +
      transferStats.averageLatency +
      decoderStats.averageDecodeTime;

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

      // Transfer
      transferredBytes: transferStats.totalBytes,
      transferredChunks: transferStats.totalChunks,
      transferLatency: transferStats.averageLatency,
      packetsLost: transferStats.packetsLost,
      jitter: transferStats.jitter,
      throughput: transferStats.throughput,
      currentBitrate: transferStats.currentBitrate,

      // Decoding
      decodedFrames: decoderStats.decodedFrames,
      decodingLatency: decoderStats.averageDecodeTime,
      decoderHardwareAcceleration: decoderStats.hardwareAcceleration,
      decoderResolution: decoderStats.resolution,

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
      transferredBytes: 0,
      transferredChunks: 0,
      transferLatency: 0,
      packetsLost: 0,
      jitter: 0,
      throughput: 0,
      currentBitrate: 0,
      decodedFrames: 0,
      decodingLatency: 0,
      decoderHardwareAcceleration: 'unknown',
      decoderResolution: 'N/A',
      duration: 0,
      totalLatency: 0
    };
  }
}
