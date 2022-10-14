const LogScope = 'FocusUI';
const debug = true;

export class FocusUI {
    public static focus(targetRef: HTMLElement) : void {
        if (debug)
            console.debug(`${LogScope}.focus: target =`, targetRef)
        targetRef.focus();
    }

    public static blur(t) : void {
        if (debug)
            console.debug(`${LogScope}.blur()`)
        const activeElement = document.activeElement as HTMLElement;
        if (activeElement != null && activeElement.blur != null)
            activeElement.blur();
    }
}
