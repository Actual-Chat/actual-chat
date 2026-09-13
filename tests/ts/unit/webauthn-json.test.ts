import { describe, it, expect } from 'vitest';
import {
    base64UrlToBytes,
    bytesToBase64Url,
    creationOptionsFromJson,
    requestOptionsFromJson,
    credentialToJson,
} from 'webauthn-json';

describe('webauthn-json', () => {
    it('round-trips base64url without padding', () => {
        const bytes = new Uint8Array([0, 1, 2, 250, 251, 252, 253, 254, 255]);
        const text = bytesToBase64Url(bytes);
        expect(text).not.toContain('=');
        expect(text).not.toContain('+');
        expect(text).not.toContain('/');
        expect(Array.from(base64UrlToBytes(text))).toEqual(Array.from(bytes));
    });

    it('converts creation options', () => {
        const options = creationOptionsFromJson({
            rp: { id: 'voxt.ai', name: 'Voxt' },
            user: { id: 'AQID', name: 'alice', displayName: 'Alice' },
            challenge: 'BAUG',
            pubKeyCredParams: [{ type: 'public-key', alg: -7 }],
            excludeCredentials: [{ type: 'public-key', id: 'BwgJ' }],
        });
        expect(Array.from(new Uint8Array(options.challenge as ArrayBuffer))).toEqual([4, 5, 6]);
        expect(Array.from(new Uint8Array(options.user.id as ArrayBuffer))).toEqual([1, 2, 3]);
        expect(Array.from(new Uint8Array(options.excludeCredentials![0].id as ArrayBuffer))).toEqual([7, 8, 9]);
        expect(options.user.name).toBe('alice');
    });

    it('converts request options', () => {
        const options = requestOptionsFromJson({
            challenge: 'BAUG',
            rpId: 'voxt.ai',
            allowCredentials: [],
            userVerification: 'required',
        });
        expect(Array.from(new Uint8Array(options.challenge as ArrayBuffer))).toEqual([4, 5, 6]);
        expect(options.rpId).toBe('voxt.ai');
    });

    it('serializes a credential without toJSON support', () => {
        const credential = {
            id: 'AQID',
            rawId: new Uint8Array([1, 2, 3]).buffer,
            type: 'public-key',
            authenticatorAttachment: 'platform',
            response: {
                clientDataJSON: new Uint8Array([4]).buffer,
                authenticatorData: new Uint8Array([5]).buffer,
                signature: new Uint8Array([6]).buffer,
                userHandle: new Uint8Array([7]).buffer,
            },
            getClientExtensionResults: () => ({}),
        } as unknown as PublicKeyCredential;
        const json = credentialToJson(credential) as Record<string, unknown>;
        expect(json.id).toBe('AQID');
        expect(json.rawId).toBe('AQID');
        const response = json.response as Record<string, unknown>;
        expect(response.clientDataJSON).toBe('BA');
        expect(response.signature).toBe('Bg');
        expect(response.userHandle).toBe('Bw');
    });
});
