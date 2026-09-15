import { Api } from 'api';
import { getLogs } from 'logging';

const { logScope, debugLog, warnLog } = getLogs('DubVoicePreview');

// Plays one voice preview at a time from /api/dub-voices/{id}/preview, driven by DubVoiceModal.
// The URL gets the session token a MAUI WebView can't send as a cookie; a plain HTMLAudioElement
// is enough for a short MP3 and needs no AudioContext.
export class DubVoicePreview {
    private static audio: HTMLAudioElement | null = null;
    private static blazorRef: DotNet.DotNetObject | null = null;

    /** Called by blazor */
    public static async play(url: string, blazorRef: DotNet.DotNetObject): Promise<void> {
        this.stop();
        this.blazorRef = blazorRef;
        const audio = new Audio(await this.withSessionToken(url));
        this.audio = audio;
        audio.onended = () => this.onEnded(audio);
        audio.onerror = () => {
            warnLog?.log(`${logScope}.play: failed to load`, url);
            this.onEnded(audio);
        };
        debugLog?.log(`${logScope}.play:`, url);
        try {
            await audio.play();
        } catch (e) {
            warnLog?.log(`${logScope}.play: play failed`, e);
            this.onEnded(audio);
        }
    }

    /** Called by blazor */
    public static stop(): void {
        const audio = this.audio;
        if (!audio)
            return;

        this.audio = null;
        this.blazorRef = null;
        audio.onended = null;
        audio.onerror = null;
        try {
            audio.pause();
            audio.src = '';
        } catch (e) {
            warnLog?.log(`${logScope}.stop: failed`, e);
        }
    }

    // Private methods

    private static onEnded(audio: HTMLAudioElement): void {
        if (this.audio !== audio)
            return;

        const blazorRef = this.blazorRef;
        this.audio = null;
        this.blazorRef = null;
        void blazorRef?.invokeMethodAsync('OnPreviewEnded');
    }

    private static async withSessionToken(url: string): Promise<string> {
        try {
            const sessionToken = await Api.getSessionToken();
            if (!sessionToken)
                return url;

            const parsed = new URL(url);
            parsed.searchParams.set('session', sessionToken);
            return parsed.toString();
        } catch (e) {
            warnLog?.log(`${logScope}.withSessionToken: failed`, e);
            return url;
        }
    }
}
