import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'dom-helpers';
const debugLog = Log.get(LogScope, LogLevel.Debug);

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
