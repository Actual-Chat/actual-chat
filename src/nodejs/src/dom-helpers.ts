import { Log, LogLevel } from 'logging';

const LogScope = 'dom-helpers';
const debugLog = Log.get(LogScope, LogLevel.Debug);

export function getOrInheritData(target: unknown, dataName: string): [HTMLElement | null, string | null] {
    if (!(target instanceof HTMLElement))
        return null;

    const rootElement = document.documentElement;
    let element = target;
    while (element && element !== rootElement) {
        const value = element.dataset[dataName];
        if (value)
            return [element, value];
        element = element.parentNode as HTMLElement;
    }
    return [null, null];
}
