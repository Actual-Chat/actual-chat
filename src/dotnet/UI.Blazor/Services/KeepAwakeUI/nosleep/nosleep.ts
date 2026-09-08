import { mp4, webm } from './media';
import { getLogs } from 'logging';
import { AC } from 'app-constants';

const { debugLog, errorLog } = getLogs('NoSleep');

export class NoSleep {
    private readonly noSleepVideo: HTMLVideoElement | null = null;
    private enabled = false;
    private wakeLock: WakeLockSentinel | null = null;

    // Detect native Wake Lock API support (Samsung Browser supports it but cannot use it)
    public get isNativeWakeLockSupported(): boolean {
        return 'wakeLock' in navigator && !navigator.userAgent.includes('Samsung');
    }

    public get isEnabled(): boolean {
        return this.enabled;
    }

    constructor() {
        if (this.isNativeWakeLockSupported) {
            // A wake lock is released whenever the page is hidden, so it has to be re-acquired on return.
            const handleVisibilityChange = () => {
                if (this.wakeLock !== null && document.visibilityState === 'visible')
                    void this.enable();
            };
            document.addEventListener('visibilitychange', handleVisibilityChange);
            document.addEventListener('fullscreenchange', handleVisibilityChange);
            return;
        }

        const noSleepVideo = document.createElement('video');
        this.noSleepVideo = noSleepVideo;
        noSleepVideo.setAttribute('title', AC.appName);
        noSleepVideo.setAttribute('playsinline', '');
        this.addSourceToVideo(noSleepVideo, 'webm', webm);
        this.addSourceToVideo(noSleepVideo, 'mp4', mp4);
        // For iOS >15 video needs to be on the document to work as a wake lock
        Object.assign(noSleepVideo.style, {
            position: 'absolute',
            left: '-100%',
            top: '-100%',
        });
        document.body.append(noSleepVideo);

        noSleepVideo.addEventListener('loadedmetadata', () => {
            if (noSleepVideo.duration <= 1) { // webm source
                noSleepVideo.setAttribute('loop', '');
                return;
            }

            // mp4 source
            noSleepVideo.addEventListener('timeupdate', () => {
                if (noSleepVideo.currentTime > 0.5)
                    noSleepVideo.currentTime = Math.random();
            });
        });
    }

    public async enable(): Promise<void> {
        try {
            if (this.isNativeWakeLockSupported) {
                const wakeLock = await navigator.wakeLock.request('screen');
                this.wakeLock = wakeLock;
                debugLog?.log('enable: wake lock is active');
                wakeLock.addEventListener('release', () => debugLog?.log('enable: wake lock is released'));
            }
            else
                await this.noSleepVideo!.play();
            this.enabled = true;
        }
        catch (e) {
            this.enabled = false;
            errorLog?.log('enable: error:', e);
            throw e;
        }
    }

    public async disable(): Promise<void> {
        if (this.isNativeWakeLockSupported) {
            if (this.wakeLock)
                await this.wakeLock.release();
            this.wakeLock = null;
        }
        else
            this.noSleepVideo!.pause();
        this.enabled = false;
    }

    private addSourceToVideo(element: HTMLVideoElement, type: string, dataURI: string): void {
        const source = document.createElement('source');
        source.src = dataURI;
        source.type = `video/${type}`;
        element.appendChild(source);
    }
}
