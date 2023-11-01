export class ThemeUI {
    public static replace(oldTheme: string, newTheme: string): void {
        document.body.classList.remove(oldTheme);
        document.body.classList.add(newTheme);
    }

    public static getPostPanelColor() : string {
        return getComputedStyle(document.body).getPropertyValue('--post-panel');
    }
}
