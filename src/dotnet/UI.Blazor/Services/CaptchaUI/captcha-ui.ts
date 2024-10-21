// noinspection JSUnusedGlobalSymbols
import { PromiseSource } from 'promises';

export class CaptchaUI {
    public static init(recaptchaUIBackendRef: DotNet.DotNetObject): void {
        const recaptchaScript = document.getElementById('recaptcha-head-script') as HTMLScriptElement;
        if (!recaptchaScript)
            return;

        if (!recaptchaScript.src)
            return;

        const match = recaptchaScript.src.match(/.+js\?render=(?<siteKey>[^&]+)/);
        if (!match.groups?.siteKey)
            return;
        const siteKey = match.groups.siteKey;
        void recaptchaUIBackendRef.invokeMethodAsync('OnInitialized', siteKey);
    }

    public static async getToken(siteKey: string, action: string) : Promise<string> {
        const resultPromise = new PromiseSource<string>();
        // @ts-ignore
        grecaptcha.enterprise.ready(async () => {
            // @ts-ignore
            const token = await grecaptcha.enterprise.execute(siteKey, {action: action}) as string;
            resultPromise.resolve(token);
        });
        return resultPromise;
    }
}
