/**
 * WebSocket Client for Video Chunk Transfer
 * Handles real-time video streaming between devices with session support
 */

import type { EncodedChunkData } from '../webcodecs-encoder';

// ============================================================================
// Types and Interfaces
// ============================================================================

export interface DeviceInfo {
  type: 'desktop' | 'mobile' | 'tablet' | 'unknown';
  userAgent: string;
  codecSupport: string[];
}

export interface SessionConfig {
  codec: 'h264' | 'av1';
  codecString: string;
  resolution: { width: number; height: number };
  bitrate: number;
  framerate: number;
  backgroundBlur?: boolean;
  av1DecoderBackend?: 'wasm' | 'builtin';
}

export interface ParticipantInfo {
  clientId: string;
  deviceType: string;
  role: string;
  joinedAt: number;
}

export interface SessionInfo {
  id: string;
  joinCode: string;
  joinUrl?: string;
  state: string;
  participants: ParticipantInfo[];
  config: SessionConfig;
  expiresAt: number;
}

export interface PeerInfo {
  clientId: string;
  deviceType: string;
  role: string;
  joinedAt: number;
}

export interface PerformanceMetrics {
  deviceType: string;
  role: 'sender' | 'receiver';
  codec: string;
  encodeTime?: number;
  encodeFps?: number;
  outputBitrate?: number;
  decodeTime?: number;
  decodeFps?: number;
  droppedFrames?: number;
  latency: number;
  jitter: number;
  cpuUsage?: number;
  memoryUsage?: number;
}

export interface WebSocketClientConfig {
  serverUrl: string;
  role?: 'sender' | 'receiver' | 'bidirectional'; // Optional, defaults to bidirectional
  reconnectInterval?: number;
  maxReconnectAttempts?: number;
  chunkBufferSize?: number;
  deviceInfo?: Partial<DeviceInfo>;
}

export interface WebSocketClientStats {
  connectionTime: number | null;
  lastActivity: number | null;
  bytesSent: number;
  bytesReceived: number;
  chunksSent: number;
  chunksReceived: number;
  connectionState: 'disconnected' | 'connecting' | 'connected' | 'error';
  reconnectAttempts: number;
}

// ============================================================================
// WebSocket Client Class
// ============================================================================

export class VideoWebSocketClient {
    private socket: WebSocket | null = null;
    private config: WebSocketClientConfig;
    private stats: WebSocketClientStats;
    private reconnectTimeout: number | null = null;
    private reconnectAttempts = 0;
    private connected = false;
    private messageQueue: string[] = [];

    // Client identification
    private clientId: string | null = null;
    private deviceInfo: DeviceInfo;

    // State
    private currentRole: 'sender' | 'receiver' | 'bidirectional' = 'bidirectional';

    // Session state
    private currentSession: SessionInfo | null = null;

    // Event callbacks
    private onChunkReceivedCallback: ((chunk: EncodedChunkData) => void) | null = null;
    private onControlMessageCallback: ((message: any) => void) | null = null;
    private onConnectionStateChangeCallback: ((state: WebSocketClientStats) => void) | null = null;

    // Session callbacks
    private onSessionJoinedCallback: ((session: SessionInfo) => void) | null = null;
    private onPeerJoinedCallback: ((peer: PeerInfo) => void) | null = null;
    private onPeerLeftCallback: ((clientId: string, reason: string) => void) | null = null;

    constructor(config: WebSocketClientConfig) {
        this.config = {
            serverUrl: config.serverUrl,
            role: config.role || 'bidirectional', // Default to bidirectional
            reconnectInterval: config.reconnectInterval || 3000,
            maxReconnectAttempts: config.maxReconnectAttempts || 10,
            chunkBufferSize: config.chunkBufferSize || 100
        };

        this.stats = {
            connectionTime: null,
            lastActivity: null,
            bytesSent: 0,
            bytesReceived: 0,
            chunksSent: 0,
            chunksReceived: 0,
            connectionState: 'disconnected',
            reconnectAttempts: 0
        };

        // Initialize device info
        this.deviceInfo = this.detectDeviceInfo(config.deviceInfo);
    }

    // ============================================================================
    // Device Detection
    // ============================================================================

    private detectDeviceInfo(override?: Partial<DeviceInfo>): DeviceInfo {
        const userAgent = typeof navigator !== 'undefined' ? navigator.userAgent : '';

        let type: DeviceInfo['type'] = 'desktop';
        if (/Mobile|Android|iPhone|iPad|iPod/i.test(userAgent)) {
            if (/iPad|Tablet/i.test(userAgent)) {
                type = 'tablet';
            } else {
                type = 'mobile';
            }
        }

        // Detect codec support
        const codecSupport: string[] = [];
        if (typeof VideoEncoder !== 'undefined') {
            // This is a simplified check - in production you'd do isConfigSupported
            codecSupport.push('avc1.640028', 'av01.0.08M.08');
        }

        return {
            type: override?.type || type,
            userAgent: override?.userAgent || userAgent,
            codecSupport: override?.codecSupport || codecSupport
        };
    }

    // ============================================================================
    // Connection Management
    // ============================================================================

    public connect(): void {
        if (this.connected) {
            console.log(`[WebSocketClient] Already connected to ${this.config.serverUrl}`);
            return;
        }

        this.updateConnectionState('connecting');
        console.log(`[WebSocketClient] Connecting to ${this.config.serverUrl} as ${this.config.role}...`);

        try {
            this.socket = new WebSocket(this.config.serverUrl);

            this.socket.onopen = () => {
                this.handleConnectionOpen();
            };

            this.socket.onmessage = (event) => {
                this.handleMessage(event.data);
            };

            this.socket.onclose = (event) => {
                this.handleConnectionClose(event);
            };

            this.socket.onerror = (error) => {
                this.handleConnectionError(error);
            };
        } catch (error) {
            console.error(`[WebSocketClient] Error creating WebSocket:`, error);
            this.updateConnectionState('error');
            this.scheduleReconnect();
        }
    }

    private handleConnectionOpen(): void {
        if (!this.socket) return;

        this.connected = true;
        this.reconnectAttempts = 0;
        this.stats.connectionTime = Date.now();
        this.stats.lastActivity = Date.now();
        this.updateConnectionState('connected');

        console.log(`[WebSocketClient] Connected to ${this.config.serverUrl}`);

        // Auto-join the default session
        this.autoJoinSession();

        // Process any queued messages
        this.processMessageQueue();
    }

    private handleMessage(data: string): void {
        if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
            return;
        }

        this.stats.lastActivity = Date.now();

        try {
            const message = JSON.parse(data);
            console.log(`[WebSocketClient] Received message:`, message.type);

            switch (message.type) {
            // Connection messages
            case 'welcome':
                this.clientId = message.clientId;
                console.log(`[WebSocketClient] Welcome from server, clientId: ${this.clientId}`);
                break;

            case 'session_joined':
                this.handleSessionJoined(message);
                break;

                // Peer events
            case 'peer_joined':
                this.handlePeerJoined(message);
                break;

            case 'peer_left':
                this.handlePeerLeft(message);
                break;

                // Video messages
            case 'video_chunk':
                this.handleVideoChunk(message);
                break;

            case 'control':
                this.handleControlMessage(message);
                break;

            case 'pong':
                // Handle ping response
                break;

            default:
                console.warn(`[WebSocketClient] Unknown message type:`, message.type);
            }
        } catch (error) {
            console.error(`[WebSocketClient] Error processing message:`, error);
        }
    }


    // ============================================================================
    // Session Message Handlers
    // ============================================================================

    private handleSessionJoined(message: any): void {
        this.currentSession = {
            id: message.sessionId,
            joinCode: '', // Not used in auto-join mode
            state: 'active',
            participants: message.participants || [],
            config: message.sessionConfig || {} as SessionConfig,
            expiresAt: 0
        };
        this.currentRole = message.role || 'bidirectional';

        console.log(`[WebSocketClient] Joined session as ${message.role}`);
        this.onSessionJoinedCallback?.(this.currentSession);
    }

    private handlePeerJoined(message: any): void {
        const peer: PeerInfo = message.peer;
        console.log(`[WebSocketClient] Peer joined: ${peer.clientId} (${peer.deviceType})`);

        // Update participants list if we have a current session
        if (this.currentSession && peer) {
            const participant: ParticipantInfo = {
                clientId: peer.clientId,
                deviceType: peer.deviceType,
                role: peer.role,
                joinedAt: peer.joinedAt
            };
            // Add if not already in list
            const existing = this.currentSession.participants.find(p => p.clientId === peer.clientId);
            if (!existing) {
                this.currentSession.participants.push(participant);
            }
        }

        this.onPeerJoinedCallback?.(peer);
    }

    private handlePeerLeft(message: any): void {
        const clientId = message.clientId;
        const reason = message.reason || 'unknown';
        console.log(`[WebSocketClient] Peer left: ${clientId} (${reason})`);

        // Remove from participants list if we have a current session
        if (this.currentSession) {
            this.currentSession.participants = this.currentSession.participants.filter(
                p => p.clientId !== clientId
            );
        }

        this.onPeerLeftCallback?.(clientId, reason);
    }

    // ============================================================================
    // Video Message Handlers
    // ============================================================================

    private handleVideoChunk(message: any): void {
        if (!message.chunk) {
            console.warn(`[WebSocketClient] Invalid video chunk message:`, message);
            return;
        }

        const chunk: EncodedChunkData = message.chunk;
        this.stats.chunksReceived++;
        this.stats.bytesReceived += chunk.byteLength;
        this.stats.lastActivity = Date.now();

        console.log(`[WebSocketClient] Received ${chunk.type} chunk (${(chunk.byteLength / 1024).toFixed(2)} KB) from ${message.senderId}`);

        if (this.onChunkReceivedCallback) {
            this.onChunkReceivedCallback(chunk);
        }
    }

    private handleControlMessage(message: any): void {
        console.log(`[WebSocketClient] Control message:`, message.action);

        if (this.onControlMessageCallback) {
            this.onControlMessageCallback(message);
        }
    }

    // ============================================================================
    // Connection Lifecycle
    // ============================================================================

    private handleConnectionClose(event: CloseEvent): void {
        console.log(`[WebSocketClient] Connection closed:`, event.code, event.reason);

        this.connected = false;
        this.updateConnectionState('disconnected');
        this.scheduleReconnect();
    }

    private handleConnectionError(error: Event): void {
        console.error(`[WebSocketClient] Connection error:`, error);

        this.connected = false;
        this.updateConnectionState('error');
        this.scheduleReconnect();
    }

    private scheduleReconnect(): void {
        if (this.reconnectAttempts >= (this.config.maxReconnectAttempts || 10)) {
            console.log(`[WebSocketClient] Max reconnect attempts reached (${this.reconnectAttempts})`);
            return;
        }

        this.reconnectAttempts++;
        this.stats.reconnectAttempts = this.reconnectAttempts;

        const delay = this.config.reconnectInterval || 3000;
        console.log(`[WebSocketClient] Scheduling reconnect attempt ${this.reconnectAttempts} in ${delay}ms...`);

        this.reconnectTimeout = window.setTimeout(() => {
            this.connect();
        }, delay);
    }

    private processMessageQueue(): void {
        if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
            return;
        }

        while (this.messageQueue.length > 0) {
            const message = this.messageQueue.shift();
            if (message) {
                this.sendRawMessage(message);
            }
        }
    }


    // ============================================================================
    // Legacy API (for backward compatibility)
    // ============================================================================

    /**
   * Auto-join the default session (called automatically on connect)
   */
    public autoJoinSession(): void {
    // Send auto-join message
        this.sendMessage({
            type: 'auto_join_session',
            deviceInfo: this.deviceInfo,
            timestamp: Date.now()
        });
    }

    /**
   * Register as bidirectional client (legacy method for backward compatibility)
   */
    public register(): void {
    // No registration needed in bidirectional mode - clients are automatically bidirectional
        this.currentRole = 'bidirectional';
    }

    public sendVideoChunk(chunk: EncodedChunkData): void {
        if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
            console.warn(`[WebSocketClient] Cannot send video chunk - not connected`);
            return;
        }

        const message = {
            type: 'video_chunk',
            chunk: chunk,
            timestamp: Date.now()
        };

        this.sendMessage(message);
        this.stats.chunksSent++;
        this.stats.bytesSent += chunk.byteLength;
    }

    public sendControlMessage(action: string, data?: any): void {
        const message = {
            type: 'control',
            action: action,
            data: data,
            timestamp: Date.now()
        };

        this.sendMessage(message);
    }

    // ============================================================================
    // Message Sending
    // ============================================================================

    public sendMessage(message: any): void {
        const messageStr = JSON.stringify(message);

        if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
            // Queue message if not connected
            this.messageQueue.push(messageStr);
            console.log(`[WebSocketClient] Queued message (not connected):`, message.type);
            return;
        }

        this.sendRawMessage(messageStr);
    }

    private sendRawMessage(message: string): void {
        if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
            return;
        }

        try {
            this.socket.send(message);
            this.stats.bytesSent += message.length;
            this.stats.lastActivity = Date.now();

            // Update stats based on message type
            const parsed = JSON.parse(message);
            if (parsed.type === 'video_chunk') {
                this.stats.chunksSent++;
            }

            console.log(`[WebSocketClient] Sent message:`, parsed.type);
        } catch (error) {
            console.error(`[WebSocketClient] Error sending message:`, error);
        }
    }

    public sendPing(): void {
        this.sendMessage({
            type: 'ping',
            timestamp: Date.now()
        });
    }

    // ============================================================================
    // Connection API
    // ============================================================================

    public disconnect(): void {
        if (this.reconnectTimeout) {
            window.clearTimeout(this.reconnectTimeout);
            this.reconnectTimeout = null;
        }

        if (this.socket) {
            try {
                this.socket.close(1000, 'Client disconnect');
            } catch (error) {
                console.error(`[WebSocketClient] Error disconnecting:`, error);
            }
            this.socket = null;
        }

        this.connected = false;
        this.currentSession = null;
        this.currentRole = 'bidirectional';
        this.updateConnectionState('disconnected');
        console.log(`[WebSocketClient] Disconnected from ${this.config.serverUrl}`);
    }

    public isConnected(): boolean {
        return this.connected;
    }

    // ============================================================================
    // State Getters
    // ============================================================================

    public getStats(): WebSocketClientStats {
        return { ...this.stats };
    }

    public getClientId(): string | null {
        return this.clientId;
    }

    public getCurrentSession(): SessionInfo | null {
        return this.currentSession ? { ...this.currentSession } : null;
    }

    public getCurrentRole(): 'sender' | 'receiver' | 'bidirectional' {
        return this.currentRole;
    }

    public getDeviceInfo(): DeviceInfo {
        return { ...this.deviceInfo };
    }

    public isInSession(): boolean {
        return this.currentSession !== null;
    }

    // ============================================================================
    // Event Callbacks
    // ============================================================================

    public onChunkReceived(callback: (chunk: EncodedChunkData) => void): void {
        this.onChunkReceivedCallback = callback;
    }

    public onControlMessage(callback: (message: any) => void): void {
        this.onControlMessageCallback = callback;
    }

    public onConnectionStateChange(callback: (state: WebSocketClientStats) => void): void {
        this.onConnectionStateChangeCallback = callback;
    }

    // Session callbacks
    public onSessionJoined(callback: (session: SessionInfo) => void): void {
        this.onSessionJoinedCallback = callback;
    }

    public onPeerJoined(callback: (peer: PeerInfo) => void): void {
        this.onPeerJoinedCallback = callback;
    }

    public onPeerLeft(callback: (clientId: string, reason: string) => void): void {
        this.onPeerLeftCallback = callback;
    }


    private updateConnectionState(state: WebSocketClientStats['connectionState']): void {
        this.stats.connectionState = state;

        if (this.onConnectionStateChangeCallback) {
            this.onConnectionStateChangeCallback({ ...this.stats });
        }
    }
}
