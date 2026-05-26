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
