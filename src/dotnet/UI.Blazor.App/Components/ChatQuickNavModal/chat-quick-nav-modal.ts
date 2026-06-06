// Wires the quick-nav modal's keyboard navigation to Blazor and keeps the
// selected row visible. The list drops down below the input (unlike the
// mention picker, which pops up above the editor), so this is much simpler
// than mention-list.ts — no caret-relative measuring.
export class ChatQuickNavModal {
    private readonly root: HTMLElement;
    private readonly input: HTMLInputElement | null;
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly onKeyDown: (e: KeyboardEvent) => void;
    private scrollScheduled = false;

    static create(root: HTMLElement, blazorRef: DotNet.DotNetObject): ChatQuickNavModal {
        return new ChatQuickNavModal(root, blazorRef);
    }

    constructor(root: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.root = root;
        this.blazorRef = blazorRef;
        this.input = root.querySelector<HTMLInputElement>('.c-input');
        this.onKeyDown = (e: KeyboardEvent) => this.handleKeyDown(e);
        this.input?.addEventListener('keydown', this.onKeyDown);
        requestAnimationFrame(() => this.input?.focus());
    }

    public scrollSelectedIntoView(): void {
        if (this.scrollScheduled)
            return;

        this.scrollScheduled = true;
        requestAnimationFrame(() => {
            this.scrollScheduled = false;
            const list = this.root.querySelector<HTMLElement>('.c-list');
            const item = list?.querySelector<HTMLElement>('.c-item.selected');
            if (!list || !item)
                return;

            const rect = item.getBoundingClientRect();
            const listRect = list.getBoundingClientRect();
            if (rect.top < listRect.top)
                item.scrollIntoView({ behavior: 'smooth', block: 'start' });
            else if (rect.bottom > listRect.bottom)
                item.scrollIntoView({ behavior: 'smooth', block: 'end' });
        });
    }

    public dispose(): void {
        this.input?.removeEventListener('keydown', this.onKeyDown);
    }

    // Private methods

    private handleKeyDown(e: KeyboardEvent): void {
        switch (e.key) {
        case 'ArrowDown':
            e.preventDefault();
            void this.blazorRef.invokeMethodAsync('OnMoveSelection', 1);
            break;
        case 'ArrowUp':
            e.preventDefault();
            void this.blazorRef.invokeMethodAsync('OnMoveSelection', -1);
            break;
        case 'Enter':
            e.preventDefault();
            void this.blazorRef.invokeMethodAsync('OnSelectCurrent');
            break;
        }
    }
}
