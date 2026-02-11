// noinspection JSUnusedGlobalSymbols
import { PromiseSource } from 'promises';

export class CaptchaUI {
    public static init(recaptchaUIBackendRef: DotNet.DotNetObject): void {
        const recaptchaScript = document.getElementById('recaptcha-head-script') as HTMLScriptElement;
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (!recaptchaScript)
            return;

        if (!recaptchaScript.src)
            return;

        const match = /.+js\?render=(?<siteKey>[^&]+)/.exec(recaptchaScript.src);
        // @ts-expect-error TODO(AK): fix ignored error
        if (!match.groups?.siteKey)
            return;
        // @ts-expect-error TODO(AK): fix ignored error
        const siteKey = match.groups.siteKey;
        void recaptchaUIBackendRef.invokeMethodAsync('OnInitialized', siteKey);
    }

    public static async getToken(siteKey: string, action: string) : Promise<string> {
        const resultPromise = new PromiseSource<string>();
        // @ts-expect-error intentional
        // eslint-disable-next-line @typescript-eslint/no-unsafe-call,@typescript-eslint/no-unsafe-member-access
        grecaptcha.enterprise.ready(async () => {
            // @ts-expect-error intentional
            // eslint-disable-next-line @typescript-eslint/no-unsafe-call,@typescript-eslint/no-unsafe-member-access
            const token = await grecaptcha.enterprise.execute(siteKey, { action: action }) as string;
            resultPromise.resolve(token);
        });
        return resultPromise;
    }
}
