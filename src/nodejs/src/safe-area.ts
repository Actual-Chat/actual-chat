/** Overflow padding for a floating element, so it never lands under a display cutout or home
 *  indicator. Reads the CSS variables rather than env(), so debugUI.showSafeAreas applies here too. */
export function getSafeAreaPadding(gap = 0): { top: number, right: number, bottom: number, left: number } {
    const style = getComputedStyle(document.body);
    const inset = (name: string) => gap + (Number.parseFloat(style.getPropertyValue(name)) || 0);
    return {
        top: inset('--safe-area-top'),
        right: inset('--safe-area-right'),
        bottom: inset('--safe-area-bottom'),
        left: inset('--safe-area-left'),
    };
}
