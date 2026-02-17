/**
 * Transfer Simulator
 * Simulates network transfer with configurable bandwidth, latency, jitter, and packet loss
 */

import type { EncodedChunkData } from '../webcodecs-encoder';

export interface TransferConfig {
  bandwidth: number; // bytes per second
  latency: number; // milliseconds
  jitter: number; // milliseconds variance
  packetLoss: number; // 0-1 percentage
}

export interface TransferStats {
  totalBytes: number;
  totalChunks: number;
  averageChunkSize: number;
  averageLatency: number;
  packetsLost: number;
  throughput: number; // bytes/sec
  jitter: number; // standard deviation of latency
  currentBitrate: number; // kbps
}

export class TransferSimulator {
    private stats: TransferStats = {
        totalBytes: 0,
        totalChunks: 0,
        averageChunkSize: 0,
        averageLatency: 0,
        packetsLost: 0,
        throughput: 0,
        jitter: 0,
        currentBitrate: 0
    };

    private latencyHistory: number[] = [];
    private startTime = 0;
    private lastBitrateUpdate = 0;
    private bytesInLastSecond = 0;

    // FIFO queue to maintain chunk ordering despite variable network delays
    private deliveryQueue: { chunk: EncodedChunkData; deliveryTime: number }[] = [];
    private isProcessingQueue = false;

    constructor(
    private config: TransferConfig,
    private onChunkReceived: (chunk: EncodedChunkData) => void
    ) {
        this.startTime = performance.now();
        this.lastBitrateUpdate = this.startTime;
    }

    // eslint-disable-next-line
    async sendChunk(chunk: EncodedChunkData): Promise<void> {
    // Track chunk type for bandwidth monitoring
        if (chunk.type === 'key') {
            console.log(`[TransferSimulator] 🔑 Sending keyframe chunk: ${(chunk.byteLength / 1024).toFixed(2)} KB`);
        }

        // Simulate packet loss
        if (Math.random() < this.config.packetLoss) {
            this.stats.packetsLost++;
            console.warn('Packet lost (simulated)');
            return; // Drop packet
        }

        // Calculate transfer time based on bandwidth
        const transferTime = (chunk.byteLength / this.config.bandwidth) * 1000;

        // Add jitter to latency (random variation)
        const jitter = (Math.random() - 0.5) * this.config.jitter * 2;
        const totalLatency = this.config.latency + jitter + transferTime;

        // Track latency
        this.latencyHistory.push(totalLatency);
        if (this.latencyHistory.length > 100) {
            this.latencyHistory.shift();
        }

        // Calculate when this chunk should be delivered
        const deliveryTime = performance.now() + totalLatency;

        // Add to FIFO queue to maintain ordering
        this.deliveryQueue.push({ chunk, deliveryTime });

        // Update stats immediately (chunk is "sent")
        this.updateStats(chunk.byteLength, totalLatency);

        // Start processing queue if not already running
        if (!this.isProcessingQueue) {
            void this.processDeliveryQueue();
        }
    }

    /**
   * Process delivery queue in FIFO order, respecting delivery times
   * This ensures chunks are delivered in order they were sent, despite variable delays
   */
    private async processDeliveryQueue(): Promise<void> {
        this.isProcessingQueue = true;

        while (this.deliveryQueue.length > 0) {
            const queueItem = this.deliveryQueue[0];
            const now = performance.now();

            // Wait until this chunk's delivery time
            if (now < queueItem.deliveryTime) {
                const waitTime = queueItem.deliveryTime - now;
                await new Promise(resolve => setTimeout(resolve, waitTime));
            }

            // Remove from queue and deliver
            this.deliveryQueue.shift();
            this.onChunkReceived(queueItem.chunk);
        }

        this.isProcessingQueue = false;
    }

    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    private updateStats(bytes: number, _latency: number): void {
        this.stats.totalBytes += bytes;
        this.stats.totalChunks++;
        this.stats.averageChunkSize = this.stats.totalBytes / this.stats.totalChunks;

        // Calculate average latency
        this.stats.averageLatency =
      this.latencyHistory.reduce((a, b) => a + b, 0) / this.latencyHistory.length;

        // Calculate overall throughput
        const elapsedSeconds = (performance.now() - this.startTime) / 1000;
        this.stats.throughput = this.stats.totalBytes / elapsedSeconds;

        // Calculate jitter (standard deviation of latency)
        const mean = this.stats.averageLatency;
        const variance = this.latencyHistory.reduce(
            (sum, val) => sum + Math.pow(val - mean, 2),
            0
        ) / this.latencyHistory.length;
        this.stats.jitter = Math.sqrt(variance);

        // Calculate current bitrate (over last second)
        const now = performance.now();
        const bitrateKbps = this.stats.currentBitrate / 1000;

        // Log bandwidth stats periodically (every second)
        if (this.stats.totalChunks % 30 === 0 && this.stats.totalChunks > 0) {
            console.log(`[TransferSimulator] 📊 Current bandwidth: ${bitrateKbps.toFixed(1)} Kbps, avg chunk: ${(this.stats.averageChunkSize / 1024).toFixed(2)} KB`);
        }
        if (now - this.lastBitrateUpdate >= 1000) {
            this.stats.currentBitrate = (this.bytesInLastSecond * 8) / 1000; // kbps
            this.bytesInLastSecond = 0;
            this.lastBitrateUpdate = now;
        }
        this.bytesInLastSecond += bytes;
    }

    getStats(): TransferStats {
        return { ...this.stats };
    }

    updateConfig(config: Partial<TransferConfig>): void {
        this.config = { ...this.config, ...config };
    }

    reset(): void {
        this.stats = {
            totalBytes: 0,
            totalChunks: 0,
            averageChunkSize: 0,
            averageLatency: 0,
            packetsLost: 0,
            throughput: 0,
            jitter: 0,
            currentBitrate: 0
        };
        this.latencyHistory = [];
        this.startTime = performance.now();
        this.lastBitrateUpdate = this.startTime;
        this.bytesInLastSecond = 0;
        this.deliveryQueue = [];
        this.isProcessingQueue = false;
    }
}
