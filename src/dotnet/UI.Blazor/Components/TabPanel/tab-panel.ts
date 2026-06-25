// TODO: Fix ESLint errors
/* eslint-disable @typescript-eslint/no-unnecessary-condition */
import { Subject,
    takeUntil,
    tap,
    map,
    switchMap,
    fromEvent,
    merge } from 'rxjs';
import { fastRaf } from 'fast-raf';

export class TabPanel {
    private readonly tabPanel: HTMLElement;
    private tabs: HTMLElement | null = null;
    private scrollContainer: HTMLElement | null = null;
    private activeTab: Element | null;
    private hill: HTMLElement | null;
    private mutationObserver: MutationObserver;
    private resizeObserver: ResizeObserver;
    private readonly disposed$: Subject<void> = new Subject<void>();

    static create(tabPanel: HTMLDivElement): TabPanel {
        return new TabPanel(tabPanel);
    }

    constructor(tabPanel: HTMLDivElement) {
        this.tabPanel = tabPanel;
        if (!this.tabPanel)
            return;

        this.tabs = this.tabPanel.querySelector('.tab-panel-tabs');
        if (!this.tabs)
            return;

        this.scrollContainer = this.tabs.querySelector('.btn-group');
        if (!this.scrollContainer)
            return;

        this.setupDragScroll();

        this.hill = this.tabPanel.querySelector('.bottom-hill');
        if (!this.hill)
            return;

        this.activeTab = this.tabs.querySelector('.btn-group-container.selected-tab');
        if (this.activeTab) {
            fastRaf(() => {
                fastRaf(() => this.updateHillPosition());
            });
        }

        this.mutationObserver = new MutationObserver((mutations) => {
            let needsUpdate = false;
            for (const mutation of mutations) {
                if (
                    mutation.type === 'attributes' &&
                    mutation.target instanceof HTMLElement &&
                    mutation.target.classList.contains('btn-group-container')
                ) {
                    needsUpdate = true;
                }
                else if (mutation.type === 'childList') {
                    // A tab button was inserted/removed (e.g. the Call tab appearing): a node added
                    // already-selected fires no attribute mutation, so track its size and reposition here.
                    mutation.addedNodes.forEach(node => {
                        if (node instanceof HTMLElement && node.classList.contains('btn-group-container'))
                            this.resizeObserver?.observe(node);
                    });
                    needsUpdate = true;
                }
            }
            if (needsUpdate) {
                const active = this.tabs?.querySelector('.btn-group-container.selected-tab');
                if (active) {
                    this.activeTab = active;
                    fastRaf(() => this.updateHillPosition());
                }
            }
        });

        this.mutationObserver.observe(tabPanel, {
            attributes: true,
            childList: true,
            subtree: true,
            attributeFilter: ['class'],
        });

        this.resizeObserver = new ResizeObserver(() => {
            fastRaf(() => this.updateHillPosition());
        });

        this.tabs.querySelectorAll('.btn-group-container')
            .forEach(tab => this.resizeObserver.observe(tab));

        fromEvent(this.scrollContainer, 'scroll')
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.updateHillPosition());
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
        this.mutationObserver?.disconnect();
        this.resizeObserver?.disconnect();
    }

    // Public methods

    private updateHillPosition() {
        if (!this.activeTab || !this.hill)
            return;

        const rect = this.activeTab.getBoundingClientRect();
        const parentRect = this.activeTab.parentElement!.getBoundingClientRect();

        const left = rect.left - parentRect.left;
        const width = rect.width;

        this.hill.style.left = `${left + 4}px`;
        this.hill.style.width = `${width - 8}px`;
    }

    private setupDragScroll(): void {
        const mouseDown$ = fromEvent<MouseEvent>(this.scrollContainer!, 'mousedown');
        const mouseMove$ = fromEvent<MouseEvent>(this.scrollContainer!, 'mousemove');
        const mouseUp$ = fromEvent<MouseEvent>(this.scrollContainer!, 'mouseup');
        const mouseLeave$ = fromEvent<MouseEvent>(this.scrollContainer!, 'mouseleave');

        const dragEnd$ = merge(mouseUp$, mouseLeave$);

        mouseDown$.pipe(
            tap(() => {
                this.scrollContainer!.style.cursor = 'grabbing';
                this.scrollContainer!.style.userSelect = 'none';
            }),
            map((event) => ({
                startX: event.pageX,
                startScrollLeft: this.scrollContainer!.scrollLeft
            })),
            switchMap(({ startX, startScrollLeft }) =>
                mouseMove$.pipe(
                    takeUntil(dragEnd$),
                    map((moveEvent) => ({
                        scrollLeft: startScrollLeft - (moveEvent.pageX - startX)
                    })),
                    tap(({ scrollLeft }) => {
                        this.scrollContainer!.scrollLeft = scrollLeft;
                    })
                )
            ),
            takeUntil(this.disposed$)
        ).subscribe();

        dragEnd$.pipe(takeUntil(this.disposed$)).subscribe(() => {
            this.scrollContainer!.style.cursor = 'grab';
            this.scrollContainer!.style.userSelect = '';
        });
    }
}
