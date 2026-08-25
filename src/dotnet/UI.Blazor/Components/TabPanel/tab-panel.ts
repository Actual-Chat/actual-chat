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
    private static readonly scrollTolerancePx = 1;
    private readonly tabPanel: HTMLElement;
    private tabs: HTMLElement | null = null;
    private scrollContainer: HTMLElement | null = null;
    private activeTab: Element | null;
    private hill: HTMLElement | null;
    private mutationObserver: MutationObserver;
    private resizeObserver: ResizeObserver;
    private isInitialized = false;
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
        fastRaf(() => {
            fastRaf(() => {
                this.update();
                this.scrollActiveTabIntoView('auto');
                this.isInitialized = true;
            });
        });

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
                    const isSelectionChanged = active !== this.activeTab;
                    this.activeTab = active;
                    fastRaf(() => {
                        this.update();
                        if (isSelectionChanged)
                            this.scrollActiveTabIntoView(this.isInitialized ? 'smooth' : 'auto');
                    });
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
            fastRaf(() => this.update());
        });

        this.resizeObserver.observe(this.scrollContainer);
        this.tabs.querySelectorAll('.btn-group-container')
            .forEach(tab => this.resizeObserver.observe(tab));

        fromEvent(this.scrollContainer, 'scroll')
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.update());
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
        this.mutationObserver?.disconnect();
        this.resizeObserver?.disconnect();
    }

    // Private methods

    private update() {
        this.updateHillPosition();
        this.updateOverflow();
    }

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

    // Read off the tabs, not scrollWidth: that counts the container's own horizontal padding.
    private updateOverflow() {
        const scrollContainer = this.scrollContainer;
        const tabs = scrollContainer?.children;
        if (!tabs?.length)
            return;

        const tolerance = TabPanel.scrollTolerancePx;
        const box = scrollContainer!.getBoundingClientRect();
        const first = tabs[0].getBoundingClientRect();
        const last = tabs[tabs.length - 1].getBoundingClientRect();
        scrollContainer!.classList.toggle('fade-left', first.left < box.left - tolerance);
        scrollContainer!.classList.toggle('fade-right', last.right > box.right + tolerance);
    }

    private scrollActiveTabIntoView(behavior: ScrollBehavior) {
        this.activeTab?.scrollIntoView({ inline: 'nearest', block: 'nearest', behavior });
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
