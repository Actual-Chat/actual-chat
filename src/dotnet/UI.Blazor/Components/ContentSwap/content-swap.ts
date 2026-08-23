// The JS half of ContentSwap. A holding swap area renders `data-swap-hold` on its host, and the CSS
// pauses every layer animation while that attribute is there - so displaying the incoming content is
// just removing it, with no state to keep here and no interop round-trip in the way.
//
// A Blazor re-render can't put the attribute back: Blazor writes one only when its own value for it
// changed, and ContentSwap makes that value unique per swap.
//
// Blazor is told after the fact, because the hand-off of the registrations a layer made into shared
// areas - RenderIntoSlot, RenderIntoStack, SettingsTab - happens on the .NET side and has to line up
// with this exact moment.

import { DotNet } from '@microsoft/dotnet-js-interop';
import { MutationProcessor } from 'mutation-processor';

const HostSelector = '.content-swap';
const HoldAttribute = 'data-swap-hold';
const NameAttribute = 'data-swap-name';
const PropagateAttribute = 'data-swap-propagate';

const swaps = new WeakMap<Element, ContentSwap>();
// A display can land before Blazor gets around to creating the host's ContentSwap - the render script
// runs on the same batch that first rendered the host. Remembering it here replays it on creation.
const pendingDisplays = new WeakSet<Element>();

export class ContentSwap {
    public static create(host: HTMLElement, backendRef: DotNet.DotNetObject): ContentSwap {
        return new ContentSwap(host, backendRef);
    }

    // `origin` is either an element inside the content that just appeared, or a ContentSwap's Name.
    public static display(origin: HTMLElement | string): void {
        if (typeof origin === 'string') {
            for (const host of document.querySelectorAll<HTMLElement>(`${HostSelector}[${NameAttribute}]`))
                if (host.getAttribute(NameAttribute) === origin)
                    ContentSwap.displayHost(host);

            return;
        }

        // The enclosing area, and no further unless that area says its content is also what the area
        // around it is waiting for. Walking the whole chain by default would release an outer area
        // that is still waiting for something later.
        let host = origin.closest<HTMLElement>(HostSelector);
        while (host !== null) {
            const mustPropagate = host.hasAttribute(PropagateAttribute);
            ContentSwap.displayHost(host);
            if (!mustPropagate)
                return;

            host = host.parentElement?.closest<HTMLElement>(HostSelector) ?? null;
        }
    }

    private static displayHost(host: HTMLElement): void {
        if (!host.hasAttribute(HoldAttribute))
            return;

        host.removeAttribute(HoldAttribute);
        const swap = swaps.get(host);
        if (swap)
            swap.notifyDisplayed();
        else
            pendingDisplays.add(host);
    }

    public constructor(
        private readonly host: HTMLElement,
        private readonly backendRef: DotNet.DotNetObject,
    ) {
        swaps.set(host, this);
        if (pendingDisplays.delete(host))
            this.notifyDisplayed();
    }

    public dispose(): void {
        swaps.delete(this.host);
    }

    // Private methods

    private notifyDisplayed(): void {
        void this.backendRef.invokeMethodAsync('OnDisplayed');
    }
}

// Registered at import time, like every other render script: the attribute can be in the very first
// render, and the callback runs before paint, so a hand-off here is one the user never sees as a delay.
MutationProcessor.registerRenderScript('content-swap-display', element => ContentSwap.display(element));
