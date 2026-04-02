import { fromEvent, Subject, takeUntil, filter } from 'rxjs';

export class VideoPanel {
    private blazorRef: DotNet.DotNetObject;
    private readonly videoPanel: HTMLElement;
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

        this.collapse();
        this.disposed$.next();
        this.disposed$.complete();
    }

    public toggleExpand(): void {
        if (!this.videoPanel.classList.contains('expanded')) {
            this.videoPanel.classList.add('expanded');
            document.body.appendChild(this.videoPanel);
            void this.blazorRef.invokeMethodAsync('OnExpanded');
        } else {
            this.collapse();
        }
    }

    public collapse() {
        if (!this.videoPanel.classList.contains('expanded'))
            return;

        this.videoPanel.classList.remove('expanded');
        this.parentElement?.appendChild(this.videoPanel);
        void this.blazorRef.invokeMethodAsync('OnCollapsed');
    }

    private onEscPress() {
        if (this.videoPanel.classList.contains('expanded'))
            this.collapse();
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
