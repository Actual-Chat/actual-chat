// noinspection JSUnusedGlobalSymbols
import { PromiseSource } from 'actuallab-core';

let fakeCaptchaMustFail = false;

export class CaptchaUI {
    public static init(): string {
        // Non-production: use fake captcha with pass/fail toggle
        if (document.getElementById('recaptcha-fake'))
            return 'fake';

        // Production: use real reCAPTCHA
        const recaptchaScript = document.getElementById('recaptcha-head-script') as HTMLScriptElement;
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (recaptchaScript?.src) {
            const match = /.+js\?render=(?<siteKey>[^&]+)/.exec(recaptchaScript.src);
            if (match?.groups?.siteKey)
                return match.groups.siteKey;
        }

        return '';
    }

    public static async getToken(siteKey: string, _action: string) : Promise<string> {
        if (siteKey === 'fake')
            return fakeCaptchaMustFail ? 'fake-fail' : 'fake-pass';

        const resultPromise = new PromiseSource<string>();
        // @ts-expect-error intentional
        // eslint-disable-next-line @typescript-eslint/no-unsafe-call,@typescript-eslint/no-unsafe-member-access
        grecaptcha.enterprise.ready(async () => {
            // @ts-expect-error intentional
            // eslint-disable-next-line @typescript-eslint/no-unsafe-call,@typescript-eslint/no-unsafe-member-access
            const token = await grecaptcha.enterprise.execute(siteKey, { action: _action }) as string;
            resultPromise.resolve(token);
        });
        return resultPromise;
    }

    public static setFakeCaptchaFail(shouldFail: boolean): void {
        fakeCaptchaMustFail = shouldFail;
    }
}
