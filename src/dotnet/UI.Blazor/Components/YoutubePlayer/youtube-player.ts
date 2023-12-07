import { Subject, BehaviorSubject, takeUntil, filter } from 'rxjs';

const apiReady$: BehaviorSubject<boolean> = new BehaviorSubject<boolean | null>(null);

export class YoutubePlayer {
    private readonly disposed$: Subject<void> = new Subject<void>();

    static create(frame: HTMLElement, blazorRef: DotNet.DotNetObject): YoutubePlayer {
        return new YoutubePlayer(frame, blazorRef);
    }

    constructor(
        private readonly frame: HTMLElement,
        private readonly blazorRef: DotNet.DotNetObject
    ) {
        loadYouTubeIframeAPI();
        apiReady$
            .pipe(
                takeUntil(this.disposed$),
                filter((x: boolean | null) => x === true),
            )
            .subscribe(() => {
                new YT.Player(frame, {
                    events: {
                        onStateChange(stateChangeEvent: YT.OnStateChangeEvent) {
                            const event = new CustomEvent("youtubeplayeronstatechange", {
                                bubbles: true,
                                detail: {...stateChangeEvent},
                            });
                            frame.dispatchEvent(event);
                        },
                    }
                });
            });
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }
}

function loadYouTubeIframeAPI(): void {
    if (apiReady$.value !== null)
        return;

    if (window.YT) {
        apiReady$.next(true);
        return;
    }

    apiReady$.next(false);

    const tag = document.createElement('script');
    tag.id = 'youtube-iframe-api';
    tag.src = 'https://www.youtube.com/iframe_api';
    const firstScriptTag = document.getElementsByTagName('script')[0];
    firstScriptTag.parentNode.insertBefore(tag, firstScriptTag);
}

(window as any).onYouTubeIframeAPIReady = function(): void {
    apiReady$.next(true);
}
