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

    /** Reconnect objects with allowReconnect=true, disconnect others.
     *  disconnect() calls unregister() which removes the object from the map. */
    reconnectOrDisconnect(): void {
        for (const obj of this._objects.values()) {
            if (obj.allowReconnect) {
                obj.reconnect();
            } else {
                obj.disconnect();
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
