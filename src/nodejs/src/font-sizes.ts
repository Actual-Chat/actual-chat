import { DeviceInfo } from 'device-info';
import { ScreenSize } from '../../dotnet/UI.Blazor/Services/ScreenSize/screen-size';

const storage = window.localStorage as Storage | null;
const storageKey = 'ui.font-size'
const availableSizes : Record<string, string> = {
    '14px': '14px',
    '16px': '16px',
    '18px': '18px',
    '20px': '20px',
    // '24px': '24px',
}

export class FontSizes {
    public static init(): void {
        const size = load() ?? getDefault();
        this.set(size);
    }

    public static list() {
        return availableSizes;
    }

    public static get(): string {
        if (!storage)
            return null;

        const root = document.querySelector(':root');
        const rootStyle = window.getComputedStyle(root);
        const size = rootStyle.getPropertyValue('--font-size');
        return getValidOrDefault(size);
    }

    public static set(size: string): void {
        if (!storage)
            return;

        size = getValidOrDefault(size);
        const root = document.querySelector(':root');
        const rootStyle = window.getComputedStyle(root);
        const rootFontSize = rootStyle.getPropertyValue('--font-size');
        if (rootFontSize != size) {
            (root as HTMLElement).style.setProperty('--font-size', size);
            ScreenSize.notifyChanged();
        }
        save(size);
    }
}

function getDefault() {
    return DeviceInfo.isIos ? '18px' : '16px';
}

function getValidOrDefault(size: string): string {
    return availableSizes[size] ?? getDefault();
}

function load(): string | null {
    return storage?.getItem(storageKey);
}

function save(size: string): void {
    storage?.setItem(storageKey, size);
}

FontSizes.init();
