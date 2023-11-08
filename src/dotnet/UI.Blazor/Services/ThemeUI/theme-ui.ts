export class ThemeUI {
    public static replace(oldTheme: string, newTheme: string): void {
        document.body.classList.remove(oldTheme);
        document.body.classList.add(newTheme);
    }

    public static getBarColors() : string {
        const style = getComputedStyle(document.body);
        const headerColor = style.getPropertyValue('--background-01');
        const postPanelColor = style.getPropertyValue('--post-panel');
        return ThemeUI.normalizeColor(headerColor) + ";" + ThemeUI.normalizeColor(postPanelColor);
    }

    private static normalizeColor(hex : string) : string {
        if (hex && hex.length === 4)
            return '#' + hex[1] + hex[1] + hex[2] + hex[2] + hex[3] + hex[3];
        return hex;
    }
}
