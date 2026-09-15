import { Api } from 'api';
import { getLogs } from 'logging';

const { logScope, debugLog, warnLog } = getLogs('DubVoicePreview');

const MaxWarmUpsInFlight = 3;

// Plays one voice preview at a time from /api/dub-voices/{id}/preview, driven by DubVoiceModal.
// The URL gets the session token a MAUI WebView can't send as a cookie; a plain HTMLAudioElement
// is enough for a short MP3 and needs no AudioContext.
export class DubVoicePreview {
    private static audio: HTMLAudioElement | null = null;
    private static blazorRef: DotNet.DotNetObject | null = null;
    private static readonly warmedUrls = new Set<string>();
    private static readonly warmUpQueue: string[] = [];
    private static warmUpsInFlight = 0;

    /**
     * Called by blazor. Returns once the fetch is started: a cache miss costs the server ~2 s of
     * synthesis, so the modal shows a spinner until OnPreviewStarted, then a stop button until
     * OnPreviewEnded (which is also the answer to a load or playback failure).
     */
    public static async play(url: string, voiceId: string, blazorRef: DotNet.DotNetObject): Promise<void> {
        this.stop();
        this.blazorRef = blazorRef;
        const audio = new Audio(await this.withSessionToken(url));
        this.audio = audio;
        audio.onplaying = () => {
            if (this.audio === audio)
                void blazorRef.invokeMethodAsync('OnPreviewStarted', voiceId);
        };
        audio.onended = () => this.onEnded(audio, voiceId);
        audio.onerror = () => {
            warnLog?.log(`${logScope}.play: failed to load`, url);
            this.onEnded(audio, voiceId);
        };
        debugLog?.log(`${logScope}.play:`, url);
        audio.play().catch((e: unknown) => {
            warnLog?.log(`${logScope}.play: play failed`, e);
            this.onEnded(audio, voiceId);
        });
    }

    /** Called by blazor */
    public static stop(): void {
        const audio = this.audio;
        if (!audio)
            return;

        this.audio = null;
        this.blazorRef = null;
        audio.onplaying = null;
        audio.onended = null;
        audio.onerror = null;
        try {
            audio.pause();
            audio.src = '';
        } catch (e) {
            warnLog?.log(`${logScope}.stop: failed`, e);
        }
    }

    /**
     * Called by blazor. Fetches previews the user is likely to play next so the server's per-voice
     * cache is filled before the click; the latest request goes first, at most MaxWarmUpsInFlight
     * at a time, each URL once.
     */
    public static warmUp(urls: string[]): void {
        const newUrls = urls.filter(url => !this.warmedUrls.has(url));
        for (const url of newUrls)
            this.warmedUrls.add(url);
        this.warmUpQueue.unshift(...newUrls);
        this.pumpWarmUps();
    }

    // Private methods

    private static onEnded(audio: HTMLAudioElement, voiceId: string): void {
        if (this.audio !== audio)
            return;

        const blazorRef = this.blazorRef;
        this.audio = null;
        this.blazorRef = null;
        void blazorRef?.invokeMethodAsync('OnPreviewEnded', voiceId);
    }

    private static pumpWarmUps(): void {
        while (this.warmUpsInFlight < MaxWarmUpsInFlight) {
            const url = this.warmUpQueue.shift();
            if (url === undefined)
                break;

            this.warmUpsInFlight++;
            void this.fetchWarmUp(url).finally(() => {
                this.warmUpsInFlight--;
                this.pumpWarmUps();
            });
        }
    }

    private static async fetchWarmUp(url: string): Promise<void> {
        try {
            const init: RequestInit & { priority: 'low' } = { priority: 'low' };
            const response = await fetch(await this.withSessionToken(url), init);
            if (!response.ok) {
                warnLog?.log(`${logScope}.fetchWarmUp: ${response.status} for`, url);
                this.warmedUrls.delete(url);
                return;
            }

            // Read to the end: the browser caches a response only once it's fully received
            await response.arrayBuffer();
            debugLog?.log(`${logScope}.fetchWarmUp: done`, url);
        } catch (e) {
            warnLog?.log(`${logScope}.fetchWarmUp: failed`, url, e);
            this.warmedUrls.delete(url);
        }
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
