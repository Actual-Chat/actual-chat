import { throttle } from 'actuallab-core';
import { fromEvent, Subject, takeUntil } from 'rxjs';
import { DeviceInfo } from 'device-info';

export function getOrInheritData(target: unknown, dataName: string): [HTMLElement | SVGElement | null, string | null] {
    if (!(target instanceof HTMLElement) && !(target instanceof SVGElement))
        return [null, null];

    const rootElement = document.documentElement;
    let element = target;
    while (element !== rootElement) {
        const value = element.dataset[dataName];
        if (value)
            return [element, value];
        element = element.parentNode as (HTMLElement | SVGElement);
    }
    return [null, null];
}

export function getOrInheritAttribute(target: unknown, attributeName: string): [HTMLElement | SVGElement | null, unknown] {
    if (!(target instanceof HTMLElement) && !(target instanceof SVGElement))
        return [null, null];

    const rootElement = document.documentElement;
    let element = target;
    while (element !== rootElement) {
        const value = element[attributeName] as unknown;
        if (value !== undefined)
            return [element, value];
        element = element.parentNode as (HTMLElement | SVGElement);
    }
    return [null, null];
}

/**
 * The nearest ancestor of the live selection carrying `data-<dataName>`, and the selected
 * text clamped to it and whitespace-normalized. `[null, '']` when nothing is selected, the
 * selection has no such ancestor (e.g. it spans several owners), or the clamped text is empty.
 */
export function getSelectionOwner(dataName: string): [HTMLElement | SVGElement | null, string] {
    const selection = window.getSelection();
    if (!selection || selection.isCollapsed || selection.rangeCount === 0)
        return [null, ''];

    const range = selection.getRangeAt(0);
    let node: Node | null = range.commonAncestorContainer;
    if (node.nodeType !== Node.ELEMENT_NODE)
        node = node.parentElement;
    const [owner] = getOrInheritData(node, dataName);
    if (!owner)
        return [null, ''];

    const clamped = range.cloneRange();
    const bounds = document.createRange();
    bounds.selectNodeContents(owner);
    if (clamped.compareBoundaryPoints(Range.START_TO_START, bounds) < 0)
        clamped.setStart(bounds.startContainer, bounds.startOffset);
    if (clamped.compareBoundaryPoints(Range.END_TO_END, bounds) > 0)
        clamped.setEnd(bounds.endContainer, bounds.endOffset);
    const text = clamped.toString().replace(/\s+/g, ' ').trim();
    return text.length > 0 ? [owner, text] : [null, ''];
}

export function setOrRemoveAttribute(element: Element, name: string, value: string | undefined) {
    if (value === undefined)
        element.removeAttribute(name);
    else
        element.setAttribute(name, value);
}

export function exposeInputState(
    input: HTMLInputElement,
    wrapper: HTMLElement,
    prefix = 'input'
) : void {
    console.log('exposeInputState:', input, wrapper);
    const update = () => {
        setOrRemoveAttribute(wrapper, `data-${prefix}-checked`, input.checked ? 'true' : undefined);
        setOrRemoveAttribute(wrapper, `data-${prefix}-disabled`, input.disabled ? 'true' : undefined);
    };
    const updateThrottled = throttle(update, 10, 'delayHead');


    const observer = new MutationObserver(updateThrottled);
    observer.observe(input, { attributes: true });

    // The code below isn't useful - these events don't fire when changes
    // are triggered programmatically by Blazor, so when it happens, we
    // change input attributes to make sure above code catches them up.
    /*
    const listenerOptions = { capture: true, passive: true };
    ['input', 'change'].forEach(event => {
        input.addEventListener(event, updateThrottled, listenerOptions);
    });
    */

    update();
}

/**
 * Sets up mobile keyboard visibility handling for input elements.
 * On mobile, keeps the focused input visible when the keyboard appears/resizes.
 */
export function setupMobileKeyboardHandler(
    inputs: HTMLInputElement | HTMLInputElement[],
    disposed$: Subject<void>
): void {
    if (!DeviceInfo.isMobile || !window.visualViewport)
        return;

    const inputArray = Array.isArray(inputs) ? inputs : [inputs];
    let settled: ReturnType<typeof setTimeout>;

    const keepVisible = () => {
        const activeInput = inputArray.find(i => document.activeElement === i);
        if (activeInput) {
            activeInput.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }
    };

    fromEvent(window.visualViewport, 'resize')
        .pipe(takeUntil(disposed$))
        .subscribe(() => {
            clearTimeout(settled);
            settled = setTimeout(keepVisible, 150);
        });
}
