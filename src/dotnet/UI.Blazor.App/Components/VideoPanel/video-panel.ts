import { fromEvent, Subject, takeUntil, filter } from 'rxjs';

export class VideoPanel {
    private blazorRef: DotNet.DotNetObject;
    private readonly videoPanel: HTMLElement;
    private readonly expandBtn: HTMLElement | null = null;
    private parentElement: HTMLElement | null = null;
    private disposed$: Subject<void> = new Subject<void>();

    static create(videoPanel: HTMLElement, blazorRef: DotNet.DotNetObject): VideoPanel {
        return new VideoPanel(videoPanel, blazorRef);
    }

    constructor(videoPanel: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.blazorRef = blazorRef;
        this.videoPanel = videoPanel;

        this.parentElement = this.videoPanel.parentElement;
        const needToShowElements = this.videoPanel.querySelectorAll('.show-with-delay');
        setTimeout(() => {
            needToShowElements.forEach(element => element.classList.add('show'));
            this.videoPanel.classList.remove('first-time-open');
        }, 1000);

        // Expand button
        this.expandBtn = this.videoPanel.querySelector('.expand-btn');
        if (this.expandBtn) {
            fromEvent(this.expandBtn, 'click')
                .pipe(takeUntil(this.disposed$))
                .subscribe(() => this.onExpandBtnClick());
        }

        // Escape key handler
        fromEvent<KeyboardEvent>(document, 'keydown')
            .pipe(
                takeUntil(this.disposed$),
                filter(e => e.key === 'Escape')
            )
            .subscribe(() => this.onEscPress());
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private onExpandBtnClick() {
        if (!this.videoPanel.classList.contains('expanded')) {
            this.videoPanel.classList.toggle('expanded');
            document.body.appendChild(this.videoPanel);
        } else {
            this.videoPanel.classList.toggle('expanded');
            this.parentElement?.appendChild(this.videoPanel);
        }
    }

    private onEscPress() {
        if (this.videoPanel.classList.contains('expanded')) {
            this.videoPanel.classList.remove('expanded');
            this.parentElement?.appendChild(this.videoPanel);
        }
    }

    public startClosing() {
        this.videoPanel.classList.remove('first-time-open');
        this.videoPanel.classList.add('closing');

        const content = this.videoPanel.querySelector('.c-content')!;
        let handled = false;
        const complete = () => {
            if (handled) return;
            handled = true;
            content.removeEventListener('animationend', complete);
            void this.blazorRef.invokeMethodAsync('CloseVideoPanel');
        };

        content.addEventListener('animationend', complete);
        setTimeout(complete, 500); // Safety fallback if animation doesn't fire
    }
}
