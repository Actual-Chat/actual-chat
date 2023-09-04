import { fromEvent, Subject, takeUntil } from 'rxjs';
import { ScreenSize } from '../../../../UI.Blazor/Services/ScreenSize/screen-size';

import { Log } from 'logging';

const { debugLog } = Log.get('Landing');

export class NewLanding {
    private readonly disposed$ = new Subject<void>();
    private readonly header: HTMLElement;
    private readonly links = new Array<HTMLElement>();
    private readonly pages = new Array<HTMLElement>();

    static create(landing: HTMLElement): NewLanding {
        return new NewLanding(landing);
    }

    constructor(
        private readonly landing: HTMLElement,
    ) {
        this.header = landing.querySelector('.landing-header');
        landing.querySelectorAll('.landing-links').forEach(e => this.links.push(e as HTMLElement));
        landing.querySelectorAll('.scrollable').forEach(e => this.pages.push(e as HTMLElement));

        this.onScreenSizeChange();
        ScreenSize.event$
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.onScreenSizeChange());

        fromEvent(document, 'scroll', { capture: true, passive: true })
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.onScroll());

        const plug = this.landing.querySelector('.landing-video-plug') as HTMLImageElement;
        const video = this.landing.querySelector('.landing-video') as HTMLVideoElement;
        if (video != null) {
            video.play().then(() => {
                plug.classList.remove('flex');
                plug.hidden = true;
                video.hidden = false;
            });
        }

    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    // Event handlers

    private onScreenSizeChange() {
        const h = window.innerHeight;
        const w = window.innerWidth;
        const hwRatio = h / w;
        let useFullScreenPages = ScreenSize.isNarrow() ? (hwRatio >= 1.8 && hwRatio <= 2.5) : (h >= 700);
        if (useFullScreenPages)
            this.landing.classList.remove('no-full-screen-pages');
        else
            this.landing.classList.add('no-full-screen-pages');
    }

    private onScroll(): void {
        const page1 = this.landing.querySelector('.page-1');
        let isNotFirstPage = page1.getBoundingClientRect().bottom <= 0;
        if (isNotFirstPage) {
            this.header.classList.add('filled');
        } else {
            this.header.classList.remove('filled');
        }
    }
}
