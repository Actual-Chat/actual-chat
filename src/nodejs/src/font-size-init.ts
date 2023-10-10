import { Log } from 'logging';
import { DeviceInfo } from 'device-info';
import { ScreenSize } from '../../dotnet/UI.Blazor/Services/ScreenSize/screen-size';

const { debugLog } = Log.get('FontSizeInit');

const FontSize : {[title: string]: string} = {
    'Small': '14px',
    'Medium': '16px',
    'Large': '18px',
    'XL': '20px',
    'XXL': '24px',
}

export class FontSizeInit {
    private static fontSize= FontSize['Medium'];

    public static init(): void {
        debugLog?.log(`init`);

        const root = document.querySelector(':root');
        const rs = window.getComputedStyle(root);

        const rootFontSize = rs.getPropertyValue('--font-size');
        const localFontSize = localStorage.getItem('font-size');

        if (localFontSize == null) {
            this.isLocalFontSizeNull();
        } else {
            if (rootFontSize != localFontSize) {
                if (!this.checkLocalFontSizeValues(localFontSize)) {
                    // get default
                    this.isLocalFontSizeNull();
                } else {
                    this.fontSize = localFontSize;
                }
            }
        }

        if (localFontSize != this.fontSize) {
            localStorage.setItem('font-size', this.fontSize);
        }
        if (rootFontSize != this.fontSize) {
            (root as HTMLElement).style.setProperty('--font-size', this.fontSize);
        }

        ScreenSize.change$.subscribe(_ => this.init());
    }

    private static isLocalFontSizeNull() {
        if (DeviceInfo.isIos) {
            this.fontSize = FontSize['Large'];
        } else {
            this.fontSize = FontSize['Medium'];
        }
    }

    private static checkLocalFontSizeValues(fontValue: string) : boolean {
        let result = false;
        Object.values(FontSize).forEach(v => {
            console.log('v: ', v);
            if (v == fontValue)
                result = true;
        });
        return result;
    }

    private static reverseFontSize() {
        const result : {[size: string]: string}  = {};
        Object.entries(FontSize).forEach(([key, value]) => {
            result[value] = key;
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
        return this.reverseFontSize()[fontSize];
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
