import { DeviceInfo } from 'device-info';
import { ScreenSize } from '../../dotnet/UI.Blazor/Services/ScreenSize/screen-size';
import { Log } from 'logging';

const { debugLog } = Log.get('FontSizes');

const StorageKey = 'ui.font-size'
const AvailableSizes : { [title: string]: string } = {
    '14px': '14px',
    '16px': '16px',
    '18px': '18px',
    '20px': '20px',
    // '24px': '24px',
}
const Storage = globalThis?.localStorage;
const IsEnabled = window != null && Storage != null;

export class FontSizes {
    public static init(): void {
        const size = load() ?? getDefault();
        this.set(size);
    }

    public static list() {
        return AvailableSizes;
    }

    public static get(): string {
        if (!IsEnabled)
            return null;

        const root = document.querySelector(':root');
        const rootStyle = window.getComputedStyle(root);
        const size = rootStyle.getPropertyValue('--font-size');
        return getValidOrDefault(size);
    }

    public static set(size: string): void {
        if (!IsEnabled)
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
    return AvailableSizes[size] ?? getDefault();
}

function load(): string | null {
    return IsEnabled ? Storage.getItem(StorageKey) : null;
}

function save(size: string): void {
    if (IsEnabled)
        Storage.setItem(StorageKey, size);
}

FontSizes.init();
