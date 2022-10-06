export interface Serialized {
    valueJson?: string;
    errorJson?: string;
}

export function serializedValue(value: unknown): Serialized {
    return { valueJson: JSON.stringify(value) }
}

export function serializedError(error: unknown): Serialized {
    return { errorJson: JSON.stringify(error) }
}

export async function serializedPromise(promise: PromiseLike<unknown>): Promise<Serialized> {
    try {
        const value = await promise;
        return serializedValue(value);
    }
    catch (error) {
        return serializedError(error);
    }
}
