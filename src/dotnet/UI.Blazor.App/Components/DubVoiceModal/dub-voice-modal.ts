import { getLogs } from 'logging';

const { logScope, debugLog, warnLog } = getLogs('DubVoicePreview');

// Plays one voice preview at a time from the MP3 bytes DubVoiceModal fetches over RPC.
// A plain HTMLAudioElement is enough for a short clip and needs no AudioContext.
export class DubVoicePreview {
    private static audio: HTMLAudioElement | null = null;
    private static objectUrl: string | null = null;
    private static blazorRef: DotNet.DotNetObject | null = null;

    /**
     * Called by blazor. Returns once playback starts loading: the modal shows a spinner
     * from the click until `onplaying` calls back OnPreviewStarted, then a stop button
     * until OnPreviewEnded (which is also the answer to a load or playback failure).
     */
    public static play(bytes: Uint8Array<ArrayBuffer>, voiceId: string, blazorRef: DotNet.DotNetObject): void {
        this.stop();
        this.blazorRef = blazorRef;
        const objectUrl = URL.createObjectURL(new Blob([bytes], { type: 'audio/mpeg' }));
        this.objectUrl = objectUrl;
        const audio = new Audio(objectUrl);
        this.audio = audio;
        audio.onplaying = () => {
            if (this.audio === audio)
                void blazorRef.invokeMethodAsync('OnPreviewStarted', voiceId);
        };
        audio.onended = () => this.onEnded(audio, voiceId);
        audio.onerror = () => {
            warnLog?.log(`${logScope}.play: failed to load`, voiceId);
            this.onEnded(audio, voiceId);
        };
        debugLog?.log(`${logScope}.play:`, voiceId);
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
        this.revokeObjectUrl();
    }

    // Private methods

    private static onEnded(audio: HTMLAudioElement, voiceId: string): void {
        if (this.audio !== audio)
            return;

        const blazorRef = this.blazorRef;
        this.audio = null;
        this.blazorRef = null;
        this.revokeObjectUrl();
        void blazorRef?.invokeMethodAsync('OnPreviewEnded', voiceId);
    }

    private static revokeObjectUrl(): void {
        if (!this.objectUrl)
            return;

        URL.revokeObjectURL(this.objectUrl);
        this.objectUrl = null;
    }
}
