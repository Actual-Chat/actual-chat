import { DeviceInfo } from 'device-info';
import { Log } from 'logging';

const { debugLog } = Log.get('FontSizes');

const StorageKey = 'ui.font-size'
const AvailableSizes : { [title: string]: string } = {
    '14px': '14px',
    '16px': '16px',
    '18px': '18px',
    '20px': '20px',
    '24px': '24px',
}

export class FontSizes {
    public static init(): void {
        const size = load() ?? getDefault();
        this.set(size);
    }

    public static list() {
        return AvailableSizes;
    }

    public static get() : string {
        const root = document.querySelector(':root');
        const rootStyle = window.getComputedStyle(root);
        const size = rootStyle.getPropertyValue('--font-size');
        return getValidOrDefault(size);
    }

    public static set(size: string) : void {
        size = getValidOrDefault(size);
        const root = document.querySelector(':root');
        const rootStyle = window.getComputedStyle(root);
        const rootFontSize = rootStyle.getPropertyValue('--font-size');
        if (rootFontSize != size)
            (root as HTMLElement).style.setProperty('--font-size', size);
        save(size);
    }
}

function getDefault() {
    return DeviceInfo.isIos ? '18px' : '16px';
}

function isValid(size: string) : boolean {
    return !!AvailableSizes[size];
}

function getValidOrDefault(size: string) : string {
    return isValid(size) ? size : getDefault();
}

function load() : string | null {
    const storage = globalThis?.localStorage;
    if (!storage)
        return null;

    return storage.getItem(StorageKey);
}

function save(size: string) : void {
    const storage = globalThis?.localStorage;
    if (!storage)
        return null;

    return storage.setItem(StorageKey, size);
}

FontSizes.init();
