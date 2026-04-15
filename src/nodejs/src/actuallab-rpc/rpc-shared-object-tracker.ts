import type { IRpcObject } from './rpc-object.js';

export class RpcSharedObjectTracker {
    private _nextId = 1;
    private _objects = new Map<number, IRpcObject>();

    nextId(): number {
        return this._nextId++;
    }

    register(obj: IRpcObject): void {
        this._objects.set(obj.id.localId, obj);
    }

    get(localId: number): IRpcObject | undefined {
        return this._objects.get(localId);
    }

    keys(): IterableIterator<number> {
        return this._objects.keys();
    }

    unregister(obj: IRpcObject): void {
        this._objects.delete(obj.id.localId);
    }

    /**
     * On same-peer WebSocket reconnect, selectively handle shared objects:
     * - allowReconnect=true (e.g. audio sender): call reconnect() to flush
     *   frames buffered during disconnect — preserves audio for transcription.
     * - allowReconnect=false (e.g. video sender): call disconnect() to end
     *   the sender — recovery will create a new stream.
     *
     * Without this, all senders would either be killed (losing buffered audio)
     * or left dangling (video senders pointing to dead server-side streams).
     */
    reconnectOrDisconnect(): void {
        for (const obj of this._objects.values()) {
            if (obj.allowReconnect) {
                obj.reconnect();
            } else {
                obj.disconnect(); // calls unregister() → removes from map
            }
        }
    }

    disconnectAll(): void {
        for (const obj of this._objects.values()) {
            obj.disconnect();
        }
        this._objects.clear();
    }
}
