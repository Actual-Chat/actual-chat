const DB_NAME = "file-handles-database";
const STORE_NAME = "handles-storage";

export async function saveFileHandle(key: string, handle: FileSystemFileHandle): Promise<void> {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(DB_NAME, 1);

        request.onupgradeneeded = (event: IDBVersionChangeEvent) => {
            const db = (event.target as IDBOpenDBRequest).result;
            if (!db.objectStoreNames.contains(STORE_NAME)) {
                db.createObjectStore(STORE_NAME);
            }
        };

        request.onsuccess = (event: Event) => {
            const db = (event.target as IDBOpenDBRequest).result;
            const tx = db.transaction(STORE_NAME, "readwrite");
            const store = tx.objectStore(STORE_NAME);
            store.put(handle, key);

            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error);
        };

        request.onerror = () => reject(request.error);
    });
}

export async function getFileHandle(key: string): Promise<FileSystemFileHandle | null> {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(DB_NAME, 1);

        request.onsuccess = (event: Event) => {
            const db = (event.target as IDBOpenDBRequest).result;
            const tx = db.transaction(STORE_NAME, "readonly");
            const store = tx.objectStore(STORE_NAME);
            const getReq = store.get(key);

            getReq.onsuccess = () => {
                resolve((getReq.result as FileSystemFileHandle) ?? null);
            };
            getReq.onerror = () => reject(getReq.error);
        };

        request.onerror = () => reject(request.error);
    });
}

export async function deleteFileHandle(key: string): Promise<void> {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(DB_NAME, 1);

        request.onsuccess = (event: Event) => {
            const db = (event.target as IDBOpenDBRequest).result;
            const tx = db.transaction(STORE_NAME, "readwrite");
            const store = tx.objectStore(STORE_NAME);
            store.delete(key);

            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error);
        };

        request.onerror = () => reject(request.error);
    });
}
