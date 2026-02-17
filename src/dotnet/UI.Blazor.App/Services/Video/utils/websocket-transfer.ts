/**
 * WebSocket Transfer Adapter
 * Replaces TransferSimulator with real WebSocket communication
 */

import type { EncodedChunkData } from '../webcodecs-encoder';
import { VideoWebSocketClient } from './websocket-client.js';
import type { TransferConfig, TransferStats } from './transfer-simulator.js';

export interface WebSocketTransferConfig extends TransferConfig {
  serverUrl: string;
  role: 'sender' | 'receiver' | 'bidirectional';
  reconnectInterval?: number;
  maxReconnectAttempts?: number;
}

export class WebSocketTransferAdapter {
    private client: VideoWebSocketClient;
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
    private connected = false;

    constructor(
    private config: WebSocketTransferConfig,
    private onChunkReceived: (chunk: EncodedChunkData) => void
    ) {
        this.startTime = performance.now();
        this.lastBitrateUpdate = this.startTime;

        // Create WebSocket client
        this.client = new VideoWebSocketClient({
            serverUrl: config.serverUrl,
            role: config.role,
            reconnectInterval: config.reconnectInterval,
            maxReconnectAttempts: config.maxReconnectAttempts
        });

        // Setup event handlers
        this.setupEventHandlers();
    }

    private setupEventHandlers(): void {
        this.client.onChunkReceived((chunk: EncodedChunkData) => {
            this.handleReceivedChunk(chunk);
        });

        this.client.onConnectionStateChange((state) => {
            this.handleConnectionStateChange(state);
        });
    }

    private handleReceivedChunk(chunk: EncodedChunkData): void {
    // Update stats
        this.stats.totalBytes += chunk.byteLength;
        this.stats.totalChunks++;
        this.stats.averageChunkSize = this.stats.totalBytes / this.stats.totalChunks;

        // Calculate latency (simulated since we don't have real network latency)
        const now = performance.now();
        const latency = 10; // Small constant latency for WebSocket overhead
        this.latencyHistory.push(latency);
        if (this.latencyHistory.length > 100) {
            this.latencyHistory.shift();
        }

        // Update bitrate calculation
        this.bytesInLastSecond += chunk.byteLength;

        // Call the callback
        this.onChunkReceived(chunk);
    }

    private handleConnectionStateChange(state: any): void {
        this.connected = state.connectionState === 'connected';

        if (state.connectionState === 'connected') {
            console.log('[WebSocketTransfer] Connected to WebSocket server');
        } else if (state.connectionState === 'disconnected') {
            console.log('[WebSocketTransfer] Disconnected from WebSocket server');
        }
    }

    public async sendChunk(chunk: EncodedChunkData): Promise<void> {
        if (!this.connected) {
            console.warn('[WebSocketTransfer] Cannot send chunk - not connected to WebSocket server');
            this.stats.packetsLost++;
            return;
        }

        // Update stats
        this.stats.totalBytes += chunk.byteLength;
        this.stats.totalChunks++;
        this.stats.averageChunkSize = this.stats.totalBytes / this.stats.totalChunks;

        // Calculate bitrate
        const now = performance.now();
        this.bytesInLastSecond += chunk.byteLength;

        // Send via WebSocket
        try {
            this.client.sendVideoChunk(chunk);
            console.log(`[WebSocketTransfer] Sent ${chunk.type} chunk (${(chunk.byteLength / 1024).toFixed(2)} KB)`);
        } catch (error) {
            console.error('[WebSocketTransfer] Error sending chunk via WebSocket:', error);
            this.stats.packetsLost++;
        }
    }

    public connect(): void {
        console.log(`[WebSocketTransfer] Connecting to WebSocket server: ${this.config.serverUrl}`);
        this.client.connect();
    }

    public disconnect(): void {
        console.log('[WebSocketTransfer] Disconnecting from WebSocket server');
        this.client.disconnect();
        this.connected = false;
    }

    public isConnected(): boolean {
        return this.connected;
    }

    public getStats(): TransferStats {
    // Calculate average latency
        this.stats.averageLatency = this.latencyHistory.length > 0
            ? this.latencyHistory.reduce((a, b) => a + b, 0) / this.latencyHistory.length
            : 0;

        // Calculate jitter (standard deviation of latency)
        if (this.latencyHistory.length > 1) {
            const mean = this.stats.averageLatency;
            const variance = this.latencyHistory.reduce(
                (sum, val) => sum + Math.pow(val - mean, 2),
                0
            ) / this.latencyHistory.length;
            this.stats.jitter = Math.sqrt(variance);
        }

        // Calculate throughput
        const elapsedSeconds = (performance.now() - this.startTime) / 1000;
        this.stats.throughput = this.stats.totalBytes / elapsedSeconds;

        // Calculate current bitrate (over last second)
        const now = performance.now();
        if (now - this.lastBitrateUpdate >= 1000) {
            this.stats.currentBitrate = (this.bytesInLastSecond * 8) / 1000; // kbps
            this.bytesInLastSecond = 0;
            this.lastBitrateUpdate = now;
        }

        return { ...this.stats };
    }

    public updateConfig(config: Partial<WebSocketTransferConfig>): void {
        this.config = { ...this.config, ...config };
    }

    public reset(): void {
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
    }
}
