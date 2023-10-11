import { Log } from 'logging';
import { DeviceInfo } from 'device-info';
import { ScreenSize } from '../../dotnet/UI.Blazor/Services/ScreenSize/screen-size';

const { debugLog } = Log.get('FontSizeInit');

const FontSize : {[title: string]: string} = {
    '14px': '14px',
    '16px': '16px',
    '18px': '18px',
    '20px': '20px',
    '24px': '24px',
}

export class FontSizeInit {
    private static fontSize = FontSize['16px'];

    public static init(): void {
        this.initInternal();
        ScreenSize.change$.subscribe(_ => this.initInternal());
    }

    private static initInternal() : void {
        debugLog?.log(`init`);

        const root = document.querySelector(':root');
        const rs = window.getComputedStyle(root);

        const rootFontSize = rs.getPropertyValue('--font-size');
        const localFontSize = localStorage.getItem('font-size');

        if (localFontSize == null) {
            this.setDefaultFontSize();
        } else {
            if (rootFontSize != localFontSize) {
                if (!this.isValidFontSizeValue(localFontSize))
                    this.setDefaultFontSize();
                else
                    this.fontSize = localFontSize;
            }
        }

        if (localFontSize != this.fontSize)
            localStorage.setItem('font-size', this.fontSize);
        if (rootFontSize != this.fontSize)
            (root as HTMLElement).style.setProperty('--font-size', this.fontSize);
    }

    private static setDefaultFontSize() {
        if (DeviceInfo.isIos)
            this.fontSize = FontSize['18px'];
        else
            this.fontSize = FontSize['16px'];
    }

    private static isValidFontSizeValue(fontValue: string) : boolean {
        let result = false;
        Object.values(FontSize).forEach(v => {
            if (v == fontValue)
                result = true;
        });
        return result;
    }

    public static getFontSizeValues() {
        return FontSize;
    }

    public static getRootFontSize() {
        const root = document.querySelector(':root');
        const rs = window.getComputedStyle(root);
        const fontSize = rs.getPropertyValue('--font-size');
        const reversed : {[size: string]: string}  = {};
        Object.entries(FontSize).forEach(([key, value]) => {
            reversed[value] = key;
        });
        return reversed[fontSize];
    }

    public static setRootFontSize(fontTitle: string) : boolean {
        if (fontTitle in FontSize && this.fontSize != FontSize[fontTitle]) {
            const size = FontSize[fontTitle];
            const root = document.querySelector(':root');
            (root as HTMLElement).style.setProperty('--font-size', size);
            localStorage.setItem('font-size', size);
            this.fontSize = size;
            return true;
        }
        return false;
    }
}

FontSizeInit.init();
