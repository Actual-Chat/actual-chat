export class ThemeUI {
    public static replace(oldTheme: string, newTheme: string): void {
        document.body.classList.remove(oldTheme);
        document.body.classList.add(newTheme);
    }

    public static getBarColors() : string {
        const style = getComputedStyle(document.body);
        const headerColor = style.getPropertyValue('--background-01');
        const postPanelColor = style.getPropertyValue('--post-panel');
        return headerColor + ";" + postPanelColor;
    }
}
