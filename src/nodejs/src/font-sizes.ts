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
        const root = document?.querySelector(':root');
        const rootStyle = window?.getComputedStyle(root);
        if (!rootStyle)
            return; // Nothing to do: there is no UI

        const size = rootStyle.getPropertyValue('--font-size');
        return getValidOrDefault(size);
    }

    public static set(size: string) : void {
        const root = document?.querySelector(':root');
        const rootStyle = window?.getComputedStyle(root);
        if (!rootStyle)
            return; // Nothing to do: there is no UI

        size = getValidOrDefault(size);
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

function getValidOrDefault(size: string) : string {
    return AvailableSizes[size] ?? getDefault();
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
