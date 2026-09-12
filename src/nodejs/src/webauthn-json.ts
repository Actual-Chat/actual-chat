/**
 * WebAuthn Level 3 JSON (base64url-encoded byte fields) ↔ the ArrayBuffer-based browser API.
 * Uses `parseCreationOptionsFromJSON`/`parseRequestOptionsFromJSON`/`toJSON()` when the
 * browser has them, falling back to a manual conversion otherwise.
 */

type Json = Record<string, unknown>;

interface PublicKeyCredentialStatic {
    parseCreationOptionsFromJSON?: (json: Json) => PublicKeyCredentialCreationOptions;
    parseRequestOptionsFromJSON?: (json: Json) => PublicKeyCredentialRequestOptions;
}

interface CredentialWithToJson {
    toJSON?: () => Json;
}

interface CredentialResponseFields {
    clientDataJSON: ArrayBuffer;
    attestationObject?: ArrayBuffer;
    authenticatorData?: ArrayBuffer;
    signature?: ArrayBuffer;
    userHandle?: ArrayBuffer | null;
    getTransports?: () => string[];
}

export function base64UrlToBytes(text: string): Uint8Array {
    const base64 = text.replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64 + '='.repeat((4 - (base64.length % 4)) % 4);
    const binary = atob(padded);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++)
        bytes[i] = binary.charCodeAt(i);

    return bytes;
}

export function bytesToBase64Url(data: ArrayBuffer | Uint8Array): string {
    const bytes = data instanceof Uint8Array ? data : new Uint8Array(data);
    let binary = '';
    for (const byte of bytes)
        binary += String.fromCharCode(byte);

    return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

export function creationOptionsFromJson(json: Json): PublicKeyCredentialCreationOptions {
    const native = publicKeyCredentialStatic()?.parseCreationOptionsFromJSON;
    if (native)
        return native(json);

    const user = json.user as Json;
    return {
        ...json,
        challenge: toBuffer(json.challenge as string),
        user: { ...user, id: toBuffer(user.id as string) } as PublicKeyCredentialUserEntity,
        excludeCredentials: descriptorsFromJson(json.excludeCredentials),
    } as PublicKeyCredentialCreationOptions;
}

export function requestOptionsFromJson(json: Json): PublicKeyCredentialRequestOptions {
    const native = publicKeyCredentialStatic()?.parseRequestOptionsFromJSON;
    if (native)
        return native(json);

    return {
        ...json,
        challenge: toBuffer(json.challenge as string),
        allowCredentials: descriptorsFromJson(json.allowCredentials),
    } as PublicKeyCredentialRequestOptions;
}

export function credentialToJson(credential: PublicKeyCredential): Json {
    const credentialWithToJson = credential as unknown as CredentialWithToJson;
    if (typeof credentialWithToJson.toJSON === 'function')
        return credentialWithToJson.toJSON();

    const response = credential.response as unknown as CredentialResponseFields;
    const json: Json = {
        id: credential.id,
        rawId: bytesToBase64Url(credential.rawId),
        type: credential.type,
        authenticatorAttachment: credential.authenticatorAttachment ?? undefined,
        clientExtensionResults: credential.getClientExtensionResults(),
        response: {
            clientDataJSON: bytesToBase64Url(response.clientDataJSON),
        },
    };
    const responseJson = json.response as Json;
    if (response.attestationObject) {
        responseJson.attestationObject = bytesToBase64Url(response.attestationObject);
        if (typeof response.getTransports === 'function')
            responseJson.transports = response.getTransports();
    }
    if (response.authenticatorData)
        responseJson.authenticatorData = bytesToBase64Url(response.authenticatorData);
    if (response.signature)
        responseJson.signature = bytesToBase64Url(response.signature);
    if (response.userHandle)
        responseJson.userHandle = bytesToBase64Url(response.userHandle);

    return json;
}

// Private methods

function publicKeyCredentialStatic(): PublicKeyCredentialStatic | null {
    return typeof PublicKeyCredential === 'undefined'
        ? null
        : PublicKeyCredential as unknown as PublicKeyCredentialStatic;
}

function descriptorsFromJson(value: unknown): PublicKeyCredentialDescriptor[] | undefined {
    if (!Array.isArray(value))
        return undefined;

    return value.map(x =>
        ({ ...(x as Json), id: toBuffer((x as Json).id as string) }) as PublicKeyCredentialDescriptor);
}

function toBuffer(text: string): ArrayBuffer {
    return base64UrlToBytes(text).buffer as ArrayBuffer;
}
