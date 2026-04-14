import { PromiseSource } from "../actuallab-core/index.js";
import type { RpcObjectId, IRpcObject } from "./rpc-object.js";
import { RpcObjectKind } from "./rpc-object.js";
import type { RpcPeer } from "./rpc-peer.js";

/** Default ack period for client-to-server streams. */
const DEFAULT_ACK_PERIOD = 256;
/** Default ack advance for client-to-server streams. */
const DEFAULT_ACK_ADVANCE = 128;

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
  readonly peer: RpcPeer;
  readonly ackPeriod: number;
  readonly ackAdvance: number;

  private _nextIndex = 0;
  private _ended = false;
  private _disconnectedByServer = false;
  private _started = new PromiseSource<void>();

  constructor(
    peer: RpcPeer,
    ackPeriod = DEFAULT_ACK_PERIOD,
    ackAdvance = DEFAULT_ACK_ADVANCE,
    allowReconnect = false
  ) {
    const localId = peer.sharedObjects.nextId();
    this.id = { hostId: peer.hub.hubId, localId };
    this.allowReconnect = allowReconnect;
    this.peer = peer;
    this.ackPeriod = ackPeriod;
    this.ackAdvance = ackAdvance;

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
    // The server sends periodic Ack(nextIndex) as a progress acknowledgment —
    // it does NOT mean "reset to nextIndex". Only reset if the server
    // explicitly requests it via a non-default hostId (mustReset=true on the
    // server side). Progress acks have hostId=default (empty Guid).
    //
    // Previously, this code unconditionally reset _nextIndex when
    // nextIndex < _nextIndex, causing the sender to re-send items from
    // the acked position with NEW data — corrupting the stream and
    // eventually triggering a server-side stream end.
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
    if (!conn)
      return;
    this.peer.hub.systemCallSender.item(conn, this.id.localId, this._nextIndex, item);
    this._nextIndex++;
  }

  /** Send a batch of items to the server. */
  sendBatch(items: T[]): void {
    if (this._ended || items.length === 0) return;
    const conn = this.peer.connection;
    if (!conn) return;
    this.peer.hub.systemCallSender.batch(conn, this.id.localId, this._nextIndex, items);
    this._nextIndex += items.length;
  }

  /** Signal stream completion to the server. */
  sendEnd(error?: Error | null): void {
    if (this._ended) return;
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
    // Client-to-server streams don't support reconnection
  }

  disconnect(): void {
    this._ended = true;
    this._disconnectedByServer = true;
    if (!this._started.isCompleted) {
      this._started.resolve();
    }
    this.peer.sharedObjects.unregister(this);
  }
}
