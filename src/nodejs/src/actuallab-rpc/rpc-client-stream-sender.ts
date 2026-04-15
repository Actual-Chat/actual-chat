import Denque from "denque";
import { PromiseSource } from "../actuallab-core/index.js";
import type { RpcObjectId, IRpcObject } from "./rpc-object.js";
import { RpcObjectKind } from "./rpc-object.js";
import type { RpcPeer } from "./rpc-peer.js";

/** Default ack period for client-to-server streams. */
const DEFAULT_ACK_PERIOD = 256;
/** Default ack advance for client-to-server streams. */
const DEFAULT_ACK_ADVANCE = 128;
/** Default reconnect buffer capacity for non-realtime streams. */
const DEFAULT_RECONNECT_BUFFER_CAP = 3000;

/**
 * Client-side RPC stream producer — sends items to the server via
 * $sys.I / $sys.B / $sys.End system calls.
 *
 * This is the mirror of RpcStreamSender (which is server→client).
 * Used when a client method argument is RpcStream<T> — the client creates
 * a sender, passes its ref string as the argument, and pumps items to the server.
 *
 * Wire protocol (client perspective):
 * - Client passes the stream ref string as a method argument
 * - Server sends $sys.Ack(0, hostId) to start the stream
 * - Client sends $sys.I (single item) / $sys.B (batch) messages
 * - Server acks every ackPeriod items for flow control
 * - Client sends $sys.End to signal completion (with optional error)
 * - Server sends $sys.AckEnd to acknowledge completion
 */
export class RpcClientStreamSender<T> implements IRpcObject {
  readonly id: RpcObjectId;
  readonly kind = RpcObjectKind.Local;
  readonly allowReconnect: boolean;
  readonly isRealtime: boolean;
  readonly peer: RpcPeer;
  readonly ackPeriod: number;
  readonly ackAdvance: number;

  private _nextIndex = 0;
  private _ended = false;
  private _disconnectedByServer = false;
  private _started = new PromiseSource<void>();
  private readonly _reconnectBuffer: Denque<{ index: number; item: T }> | null;
  private readonly _reconnectBufferCap: number;

  constructor(
    peer: RpcPeer,
    ackPeriod = DEFAULT_ACK_PERIOD,
    ackAdvance = DEFAULT_ACK_ADVANCE,
    allowReconnect = false,
    isRealtime = false,
    reconnectBufferCap = DEFAULT_RECONNECT_BUFFER_CAP
  ) {
    const localId = peer.sharedObjects.nextId();
    this.id = { hostId: peer.hub.hubId, localId };
    this.allowReconnect = allowReconnect;
    this.isRealtime = isRealtime;
    this.peer = peer;
    this.ackPeriod = ackPeriod;
    this.ackAdvance = ackAdvance;
    this._reconnectBufferCap = reconnectBufferCap;
    this._reconnectBuffer = (!isRealtime && allowReconnect) ? new Denque<{ index: number; item: T }>() : null;

    // Register so system call handler can route $sys.Ack/$sys.AckEnd to us
    peer.sharedObjects.register(this);
  }

  /**
   * Returns the stream reference to pass as an RPC method argument. The server
   * deserializes this into `RpcStream<T>`, which is `[MessagePackObject(true)]`
   * on the wire — a MessagePack map with implicit PascalCase property keys. The
   * `SerializedId` field is itself an `RpcObjectId` value, which is
   * `[MessagePackObject]` with integer keys ⇒ a 2-element `[hostId, localId]`
   * array in MessagePack.
   *
   * Return type is `unknown` so callers can pass it as an RPC method argument
   * of any nominal type; the binary serializer treats it as an opaque object.
   */
  toRef(): unknown {
    return {
      SerializedId: [this.id.hostId, this.id.localId],
      AckPeriod: this.ackPeriod,
      AckAdvance: this.ackAdvance,
      AllowReconnect: this.allowReconnect,
    };
  }

  /** Whether this sender has been ended (completed, errored, or disconnected). */
  get isEnded(): boolean { return this._ended; }

  /** Whether this sender was disconnected by the server (e.g. pod restart). */
  get isDisconnectedByServer(): boolean { return this._disconnectedByServer; }

  /** Resolves when the server sends its initial $sys.Ack. */
  whenStarted(): Promise<void> {
    return this._started.promise;
  }

  /** Called by system call handler when $sys.Ack is received from the server. */
  onAck(_nextIndex: number, _hostId: string): void {
    if (!this._started.isCompleted) {
      this._started.resolve();
    }
    // .NET RpcSharedStream uses acks for flow control (sends up to
    // ackAdvance items beyond the acked position) and supports rewind
    // on mustReset. We intentionally skip both:
    // - Flow control: real-time media streams (audio/video) can't
    //   back-pressure — frames must be sent at capture rate or dropped.
    // - Rewind/reset: there's no send buffer to replay from; reconnect
    //   is not supported for client-to-server streams.
    // Previously, resetting _nextIndex on progress acks caused stream
    // corruption (re-sent indices with new data).
  }

  /** Called by system call handler when $sys.AckEnd is received from the server. */
  onAckEnd(_hostId: string): void {
    this._ended = true;
    this.peer.sharedObjects.unregister(this);
  }

  /** Send a single item to the server. */
  sendItem(item: T): void {
    if (this._ended)
      return;
    const conn = this.peer.connection;
    if (!conn) {
      if (this._reconnectBuffer) {
        this._reconnectBuffer.push({ index: this._nextIndex, item });
        this._nextIndex++;
        // Drop oldest items if over capacity
        while (this._reconnectBuffer.length > this._reconnectBufferCap) {
          this._reconnectBuffer.shift();
        }
      }
      return;
    }
    this.peer.hub.systemCallSender.item(conn, this.id.localId, this._nextIndex, item);
    this._nextIndex++;
  }

  /** Send a batch of items to the server. */
  sendBatch(items: T[]): void {
    if (this._ended || items.length === 0) return;
    const conn = this.peer.connection;
    if (!conn) {
      if (this._reconnectBuffer) {
        for (const item of items) {
          this._reconnectBuffer.push({ index: this._nextIndex, item });
          this._nextIndex++;
        }
        // Drop oldest items if over capacity
        while (this._reconnectBuffer.length > this._reconnectBufferCap) {
          this._reconnectBuffer.shift();
        }
      }
      return;
    }
    this.peer.hub.systemCallSender.batch(conn, this.id.localId, this._nextIndex, items);
    this._nextIndex += items.length;
  }

  /** Signal stream completion to the server. */
  sendEnd(error?: Error | null): void {
    if (this._ended) return;
    this._reconnectBuffer?.clear();
    this._ended = true;
    const conn = this.peer.connection;
    if (!conn) return;
    // .NET `ExceptionInfo` is a readonly struct (non-nullable value type).
    // Its MessagePack formatter can't deserialize MessagePack nil — it throws
    // `typecode is null, struct not supported`. The on-the-wire "no error"
    // shape is `default(ExceptionInfo)`, i.e. an empty map with PascalCase
    // keys: { TypeRef: "", Message: "" } where TypeRef is itself serialized
    // as its `AssemblyQualifiedName` string via `TypeRefMessagePackFormatter`.
    // Always send this shape — a null here aborts argument deserialization
    // on the server and tears down the whole $sys.End handler.
    const errorInfo = error
      ? { TypeRef: "System.Exception", Message: error.message }
      : { TypeRef: "", Message: "" };
    this.peer.hub.systemCallSender.end(conn, this.id.localId, this._nextIndex, errorInfo);
  }

  /**
   * Consume an AsyncIterable and send all items to the server.
   * Waits for the server's initial Ack before starting to pump items.
   */
  async writeFrom(source: AsyncIterable<T>): Promise<void> {
    await this._started.promise;

    try {
      for await (const item of source) {
        if (this._ended) return;
        this.sendItem(item);
      }
      if (!this._ended) {
        this.sendEnd();
      }
    } catch (e) {
      if (!this._ended) {
        this.sendEnd(e instanceof Error ? e : new Error(String(e)));
      }
    }
  }

  // -- IRpcObject --

  reconnect(): void {
    if (!this.allowReconnect) return;
    if (this.isRealtime) return; // No buffer to flush for realtime streams
    this._flushReconnectBuffer();
  }

  disconnect(): void {
    this._reconnectBuffer?.clear();
    this._ended = true;
    this._disconnectedByServer = true;
    if (!this._started.isCompleted) {
      this._started.resolve();
    }
    this.peer.sharedObjects.unregister(this);
  }

  // -- Private --

  private _flushReconnectBuffer(): void {
    if (!this._reconnectBuffer) return;
    const conn = this.peer.connection;
    if (!conn) return;
    while (this._reconnectBuffer.length > 0) {
      const entry = this._reconnectBuffer.shift()!;
      this.peer.hub.systemCallSender.item(conn, this.id.localId, entry.index, entry.item);
    }
  }
}
