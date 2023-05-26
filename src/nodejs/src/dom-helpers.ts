import { throttle } from 'promises';

export function getOrInheritData(target: unknown, dataName: string): [HTMLElement | SVGElement | null, string | null] {
    if (!(target instanceof HTMLElement) && !(target instanceof SVGElement))
        return [null, null];

    const rootElement = document.documentElement;
    let element = target;
    while (element && element !== rootElement) {
        const value = element.dataset[dataName];
        if (value)
            return [element, value];
        element = element.parentNode as (HTMLElement | SVGElement);
    }
    return [null, null];
}

export function getOrInheritAttribute(target: unknown, attributeName: string): [HTMLElement | SVGElement | null, unknown | null] {
    if (!(target instanceof HTMLElement) && !(target instanceof SVGElement))
        return [null, null];

    const rootElement = document.documentElement;
    let element = target;
    while (element && element !== rootElement) {
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

    // eslint-disable-next-line @typescript-eslint/no-unused-vars
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
