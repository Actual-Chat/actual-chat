import { getLogs } from 'logging';
import { creationOptionsFromJson, requestOptionsFromJson, credentialToJson } from 'webauthn-json';

const { warnLog } = getLogs('Passkeys');

export interface PasskeyResult {
    json?: string;
    error?: string;
}

export class Passkeys {
    public static readonly cancelledError = 'cancelled';

    public static async isAvailable(): Promise<boolean> {
        try {
            if (typeof PublicKeyCredential === 'undefined' || !window.isSecureContext)
                return false;

            return await PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable();
        }
        catch (e) {
            warnLog?.log('isAvailable: failed', e);
            return false;
        }
    }

    public static create(optionsJson: string): Promise<PasskeyResult> {
        return this.run(async () => {
            const json = JSON.parse(optionsJson) as Record<string, unknown>;
            const publicKey = creationOptionsFromJson(json);
            const credential = await navigator.credentials.create({ publicKey });
            return credential as PublicKeyCredential | null;
        });
    }

    public static get(optionsJson: string): Promise<PasskeyResult> {
        return this.run(async () => {
            const json = JSON.parse(optionsJson) as Record<string, unknown>;
            const publicKey = requestOptionsFromJson(json);
            const credential = await navigator.credentials.get({ publicKey });
            return credential as PublicKeyCredential | null;
        });
    }

    // Private methods

    private static async run(ceremony: () => Promise<PublicKeyCredential | null>): Promise<PasskeyResult> {
        try {
            const credential = await ceremony();
            if (!credential)
                return { error: this.cancelledError };

            return { json: JSON.stringify(credentialToJson(credential)) };
        }
        catch (e) {
            const name = (e as { name?: string }).name;
            if (name === 'NotAllowedError' || name === 'AbortError')
                return { error: this.cancelledError };

            warnLog?.log('run: failed', e);
            const message = e instanceof Error ? e.message : String(e);
            return { error: `${name ?? 'Error'}: ${message}` };
        }
    }
}
