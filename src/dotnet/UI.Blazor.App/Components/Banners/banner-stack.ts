import { Disposable } from 'disposable';
import { fromEvent, Subject, takeUntil } from 'rxjs';

const PeekOffsetPx = 8;
const MaxPeek = 2;
const ExpandedGapPx = 6;
const ScaleStep = 0.04;

export class BannerStack implements Disposable {
    private readonly disposed$: Subject<void> = new Subject<void>();
    private readonly mutationObserver: MutationObserver;
    private readonly resizeObserver: ResizeObserver;
    private readonly observed = new Set<HTMLElement>();
    private expanded = false;
    private rafHandle = 0;

    public static create(root: HTMLElement): BannerStack {
        return new BannerStack(root);
    }

    constructor(private readonly root: HTMLElement) {
        fromEvent<MouseEvent>(root, 'click')
            .pipe(takeUntil(this.disposed$))
            .subscribe(event => this.handleClick(event));
        this.mutationObserver = new MutationObserver(() => this.scheduleLayout());
        this.mutationObserver.observe(root, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: ['class'],
        });
        this.resizeObserver = new ResizeObserver(() => this.scheduleLayout());
        this.scheduleLayout();
    }

    public dispose(): void {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
        this.mutationObserver.disconnect();
        this.resizeObserver.disconnect();
        this.observed.clear();
        if (this.rafHandle)
            cancelAnimationFrame(this.rafHandle);
    }

    private handleClick(event: MouseEvent): void {
        const target = event.target as HTMLElement | null;
        // Buttons and links inside a banner keep acting; only a tap on the banner body toggles the stack.
        if (target?.closest('.banner-buttons, button, a, [role="button"]'))
            return;

        const banners = this.getBanners();
        if (banners.length <= 1 && !this.expanded)
            return;

        this.expanded = !this.expanded;
        this.layout();
    }

    private scheduleLayout(): void {
        if (this.disposed$.closed || this.rafHandle)
            return;

        this.rafHandle = requestAnimationFrame(() => {
            this.rafHandle = 0;
            this.layout();
        });
    }

    // A banner is only in the DOM while visible (Banner.razor drops it when hidden), so presence
    // is the visibility test. offsetHeight can't be used: an is-empty container is display:none,
    // which zeroes its children's height and would latch the stack empty forever.
    private getBanners(): HTMLElement[] {
        return Array.from(this.root.querySelectorAll<HTMLElement>('.banner'));
    }

    // Keep the ResizeObserver tracking exactly the live banners — unobserve ones that left the DOM,
    // otherwise detached banner nodes pile up in the observer and never get collected.
    private reconcileObserved(banners: HTMLElement[]): void {
        const current = new Set(banners);
        for (const banner of this.observed)
            if (!current.has(banner)) {
                this.resizeObserver.unobserve(banner);
                this.observed.delete(banner);
            }
        for (const banner of banners)
            if (!this.observed.has(banner)) {
                this.resizeObserver.observe(banner);
                this.observed.add(banner);
            }
    }

    private layout(): void {
        if (this.disposed$.closed)
            return;

        const banners = this.getBanners();
        const count = banners.length;
        const isExpanded = this.expanded && count > 1;
        this.root.classList.toggle('is-empty', count === 0);
        this.root.classList.toggle('expanded', isExpanded);
        this.reconcileObserved(banners);

        if (count === 0) {
            this.root.style.setProperty('--bs-height', '0px');
            return;
        }

        if (isExpanded) {
            let y = 0;
            banners.forEach((banner, i) => {
                banner.style.transform = `translateY(${y}px)`;
                banner.style.opacity = '1';
                banner.style.zIndex = `${count - i}`;
                banner.style.pointerEvents = 'auto';
                y += banner.offsetHeight + ExpandedGapPx;
            });
            this.root.style.setProperty('--bs-height', `${Math.max(0, y - ExpandedGapPx)}px`);
            return;
        }

        const topHeight = banners[0].offsetHeight;
        const peekCount = Math.min(count - 1, MaxPeek);
        banners.forEach((banner, i) => {
            if (i === 0) {
                banner.style.transform = 'translateY(0) scaleX(1)';
                banner.style.opacity = '1';
                banner.style.zIndex = `${count}`;
                banner.style.pointerEvents = 'auto';
            } else if (i <= MaxPeek) {
                // Bottom-align each peek so exactly i*PeekOffset sticks out below the top banner,
                // keeping its rounded corners visible regardless of the peek banner's own height.
                const y = topHeight - banner.offsetHeight + i * PeekOffsetPx;
                banner.style.transform = `translateY(${y}px) scaleX(${1 - i * ScaleStep})`;
                banner.style.opacity = '1';
                banner.style.zIndex = `${count - i}`;
                banner.style.pointerEvents = 'none';
            } else {
                banner.style.transform = `translateY(${topHeight + MaxPeek * PeekOffsetPx}px) scaleX(${1 - MaxPeek * ScaleStep})`;
                banner.style.opacity = '0';
                banner.style.zIndex = '0';
                banner.style.pointerEvents = 'none';
            }
        });
        this.root.style.setProperty('--bs-height', `${topHeight + peekCount * PeekOffsetPx}px`);
    }
}
