import { Subject } from 'rxjs';
import { fastRaf } from 'fast-raf';

export class TabPanel {
    private readonly tabPanel: HTMLElement;
    private tabs: HTMLElement | null = null;
    private activeTab: Element | null;
    private hill: HTMLElement | null;
    private mutationObserver: MutationObserver;
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
            for (const mutation of mutations) {
                if (
                    mutation.type === 'attributes' &&
                    mutation.attributeName === 'class' &&
                    mutation.target instanceof HTMLElement &&
                    mutation.target.classList.contains('btn-group-container')
                ) {
                    const target = mutation.target;
                    if (target.classList.contains('selected-tab')) {
                        this.activeTab = target;
                        this.updateHillPosition();
                    }
                }
            }
        });

        this.mutationObserver.observe(tabPanel, {
            attributes: true,
            subtree: true,
            attributeFilter: ['class'],
        });
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
        this.mutationObserver?.disconnect();
    }

    // Public methods

    private updateHillPosition() {
        const rect = this.activeTab!.getBoundingClientRect();
        const parentRect = (this.activeTab!.parentElement as HTMLElement).getBoundingClientRect();

        const left = rect.left - parentRect.left;
        const width = rect.width;

        this.hill!.style.left = `${left + 4}px`;
        this.hill!.style.width = `${width - 8}px`;
    }
}
