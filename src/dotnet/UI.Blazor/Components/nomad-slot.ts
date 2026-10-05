// The script half of RenderIntoNomadSlot and RenderNomadSlot. The content is rendered once, where it is
// declared, and only its DOM moves - into the slot its target names, or back. All three are custom
// elements because their callbacks run within the DOM operation that adds or removes them: content
// taken out of the page together with its slot is back before the browser pauses its media.

const RenderIntoTagName = 'render-into-nomad-slot';
const ContentTagName = 'nomad-slot-content';
const SlotTagName = 'render-nomad-slot';
// On the content while it is in a slot; the value is that slot's key
const SlotAttribute = 'data-nomad-slot';
// Set by a script that knows the content must stay where it is declared for now, ahead of the render
const HoldAttribute = 'data-nomad-hold';

type MovableParent = HTMLElement & { moveBefore?(node: Node, child: Node | null): void };

const contents = new Set<NomadSlotContentElement>();

class RenderIntoNomadSlotElement extends HTMLElement {
    public content: NomadSlotContentElement | null = null;

    disconnectedCallback(): void {
        // The content belongs to where it is declared, wherever it is shown
        const content = this.content;
        if (content && content.parentElement !== this)
            content.remove();
    }
}

class NomadSlotContentElement extends HTMLElement {
    static observedAttributes = ['data-name', 'data-target', HoldAttribute];

    public declaredIn: RenderIntoNomadSlotElement | null = null;
    public isMoving = false;

    connectedCallback(): void {
        if (this.isMoving)
            return;

        if (!this.declaredIn && this.parentElement instanceof RenderIntoNomadSlotElement) {
            this.declaredIn = this.parentElement;
            this.declaredIn.content = this;
        }
        contents.add(this);
        NomadSlot.place(this);
    }

    // Called instead of the two callbacks around it when the move is a moveBefore
    connectedMoveCallback(): void {
        // Nothing to do: the content is where it was put
    }

    disconnectedCallback(): void {
        // Already put back in the same operation, by the callback of the slot it was removed with
        if (this.isMoving || this.isConnected)
            return;

        if (this.declaredIn?.isConnected)
            NomadSlot.place(this);
        else
            contents.delete(this);
    }

    attributeChangedCallback(): void {
        if (this.isConnected && !this.isMoving)
            NomadSlot.place(this);
    }
}

class RenderNomadSlotElement extends HTMLElement {
    // Blazor inserts an element first and sets its attributes after
    static observedAttributes = ['data-name', 'data-key'];

    connectedCallback(): void {
        NomadSlot.placeAll();
    }

    disconnectedCallback(): void {
        NomadSlot.placeAll();
    }

    attributeChangedCallback(): void {
        NomadSlot.placeAll();
    }
}

export class NomadSlot {
    static define(): void {
        if (typeof customElements === 'undefined' || customElements.get(SlotTagName))
            return;

        customElements.define(RenderIntoTagName, RenderIntoNomadSlotElement);
        customElements.define(ContentTagName, NomadSlotContentElement);
        customElements.define(SlotTagName, RenderNomadSlotElement);
    }

    static placeAll(): void {
        for (const content of contents)
            NomadSlot.place(content);
    }

    static place(content: NomadSlotContentElement): void {
        const declaredIn = content.declaredIn;
        if (!declaredIn?.isConnected)
            return;

        const name = content.dataset['name'];
        const key = content.dataset['target'];
        const slot = name && key !== undefined && !content.hasAttribute(HoldAttribute)
            ? NomadSlot.findSlot(name, key)
            : null;
        const parent = slot ?? declaredIn;
        if (content.parentElement !== parent)
            NomadSlot.move(content, parent);
        if (slot)
            content.setAttribute(SlotAttribute, key!);
        else
            content.removeAttribute(SlotAttribute);
    }

    // Private methods

    // Two slots with the same name and key: the first in document order wins
    private static findSlot(name: string, key: string): HTMLElement | null {
        for (const slot of document.querySelectorAll<HTMLElement>(SlotTagName)) {
            if (slot.dataset['name'] === name && slot.dataset['key'] === key)
                return slot;
        }
        return null;
    }

    private static move(content: NomadSlotContentElement, parent: MovableParent): void {
        content.isMoving = true;
        try {
            // An atomic move keeps media playing, focus and running animations; the fallback relies on
            // the move happening within the same task, which keeps media playing only
            if (parent.moveBefore && content.isConnected && parent.isConnected) {
                try {
                    parent.moveBefore(content, null);
                    return;
                }
                catch {
                    // Falls back to appendChild below
                }
            }
            parent.appendChild(content);
        }
        finally {
            content.isMoving = false;
        }
    }
}

NomadSlot.define();
