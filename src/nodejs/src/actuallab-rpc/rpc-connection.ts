// .NET counterparts:
//   RpcConnection — thin wrapper around RpcTransport + PropertyBag.  RpcTransport
//     is an abstract IAsyncEnumerable<RpcInboundMessage> + Send() that can be
//     WebSocket-based or in-memory.  The transport also handles frame batching and
//     back-pressure via a Channel<T> send queue.
//
// Omitted from .NET:
//   - PropertyBag on connection — used in .NET for per-connection metadata (e.g.
//     authentication info attached by middleware).  TS has no middleware pipeline,
//     so no need for an extensible property bag.
//   - IsLocal flag — .NET distinguishes local loopback connections (same-process
//     server) from remote ones.  TS is always a remote client.
//   - RpcTransport as IAsyncEnumerable — .NET reads inbound messages via
//     async iteration with cancellation.  TS uses event-based delivery
//     (messageReceived handler) because browser WebSocket API is event-driven.
//   - Channel<RpcOutboundMessage> send queue with backpressure — .NET buffers
//     outbound messages in an async channel that the transport drains.  TS sends
//     synchronously on the WebSocket; if the socket isn't ready, messages are
//     buffered in _sendBuffer (CONNECTING) or silently dropped (CLOSING/CLOSED).
//     JS is single-threaded so there's no contention, and WebSocket.send() itself
//     handles buffering at the OS level.
//   - IAsyncDisposable — .NET transports are disposable resources.  TS connections
//     are closed via close() and garbage-collected.

import { PromiseSource, EventHandlerSet } from "../actuallab-core/index.js";
import {
  splitFrame, serializeFrame,
  splitBinaryFrame, serializeBinaryFrame,
} from "./rpc-serialization.js";
import type { RpcMessage } from "./rpc-message.js";

/** Abstract WebSocket interface — works with both browser WebSocket and Node.js ws. */
export interface WebSocketLike {
  readonly readyState: number;
  binaryType?: string;
  send(data: string | ArrayBufferLike | Uint8Array | ArrayBufferView): void;
  close(code?: number, reason?: string): void;
  onopen: ((ev: unknown) => void) | null;
  onmessage: ((ev: { data: unknown }) => void) | null;
  onclose: ((ev: { code: number; reason: string }) => void) | null;
  onerror: ((ev: unknown) => void) | null;
}

export const WebSocketState = {
  CONNECTING: 0,
  OPEN: 1,
  CLOSING: 2,
  CLOSED: 3,
} as const;

/** Received message — either text (string) or binary (already parsed). */
export type RpcReceivedMessage =
  | { kind: "text"; raw: string }
  | { kind: "binary"; message: RpcMessage; args: unknown[] };

/** Abstract RPC connection — transport-agnostic interface for sending/receiving messages. */
export interface RpcConnection {
  readonly isOpen: boolean;
  readonly binaryMode: boolean;
  readonly whenConnected: Promise<void>;
  readonly messageReceived: EventHandlerSet<RpcReceivedMessage>;
  readonly closed: EventHandlerSet<{ code: number; reason: string }>;
  send(serializedMessage: string): void;
  sendBinary(data: Uint8Array): void;
  close(code?: number, reason?: string): void;
}

/** WebSocket-based RpcConnection — handles frame splitting, binary/text modes, and message queueing. */
export class RpcWebSocketConnection implements RpcConnection {
  private _ws: WebSocketLike;
  private _sendBuffer: Array<string | Uint8Array> = [];
  private _connected = new PromiseSource<void>();

  readonly binaryMode: boolean;
  readonly messageReceived = new EventHandlerSet<RpcReceivedMessage>();
  readonly closed = new EventHandlerSet<{ code: number; reason: string }>();
  readonly error = new EventHandlerSet<unknown>();

  constructor(ws: WebSocketLike, binaryMode = false) {
    this._ws = ws;
    this.binaryMode = binaryMode;

    if (binaryMode && ws.binaryType !== undefined)
      ws.binaryType = "arraybuffer";

    if (ws.readyState === WebSocketState.OPEN) {
      this._connected.resolve();
      this._flush();
    }

    ws.onopen = () => {
      console.log('[RpcConnection] WebSocket opened, binaryMode:', binaryMode);
      this._connected.resolve();
      this._flush();
    };

    ws.onmessage = (ev) => {
      if (ev.data instanceof ArrayBuffer) {
        // Binary frame — V5 size-prefixed messages
        const frame = new Uint8Array(ev.data);
        console.log('[RpcConnection] Binary message received, size:', frame.length, 'first bytes:', Array.from(frame.slice(0, 20)));
        try {
          const messages = splitBinaryFrame(frame);
          console.log('[RpcConnection] Parsed', messages.length, 'binary messages');
          for (const { message, args } of messages) {
            console.log('[RpcConnection] Binary msg:', message.Method, 'RelatedId:', message.RelatedId, 'args:', args.length);
            this.messageReceived.trigger({ kind: "binary", message, args });
          }
        } catch (e) {
          console.error('[RpcConnection] Failed to parse binary frame:', e);
        }
      } else {
        // Text frame — JSON delimited messages
        const data = typeof ev.data === "string" ? ev.data : String(ev.data);
        const messages = splitFrame(data);
        for (const msg of messages) {
          if (msg.length > 0) this.messageReceived.trigger({ kind: "text", raw: msg });
        }
      }
    };

    ws.onclose = (ev) => {
      this.closed.trigger({ code: ev.code, reason: ev.reason });
    };

    ws.onerror = (ev) => {
      this.error.trigger(ev);
    };
  }

  get isOpen(): boolean {
    return this._ws.readyState === WebSocketState.OPEN;
  }

  get whenConnected(): Promise<void> {
    return this._connected.promise;
  }

  send(serializedMessage: string): void {
    this._sendRaw(serializedMessage);
  }

  sendBinary(data: Uint8Array): void {
    this._sendRaw(data);
  }

  sendTextBatch(messages: string[]): void {
    this._sendRaw(serializeFrame(messages));
  }

  sendBinaryBatch(messages: Uint8Array[]): void {
    this._sendRaw(serializeBinaryFrame(messages));
  }

  close(code?: number, reason?: string): void {
    this._ws.close(code, reason);
  }

  private _sendRaw(data: string | Uint8Array): void {
    try {
      if (this._ws.readyState === WebSocketState.OPEN) {
        if (data instanceof Uint8Array) {
          console.log('[RpcConnection] Sending binary, size:', data.length, 'first bytes:', Array.from(data.slice(0, 20)));
        }
        this._ws.send(data);
      } else if (this._ws.readyState === WebSocketState.CONNECTING)
        this._sendBuffer.push(data);
      // CLOSING/CLOSED: silently drop
    } catch {
      // Swallow — disconnect event handles cleanup
    }
  }

  private _flush(): void {
    if (this._sendBuffer.length === 0) return;
    const buffer = this._sendBuffer;
    this._sendBuffer = [];
    try {
      for (const item of buffer)
        this._ws.send(item);
    } catch {
      // Swallow — disconnect event handles cleanup
    }
  }
}
