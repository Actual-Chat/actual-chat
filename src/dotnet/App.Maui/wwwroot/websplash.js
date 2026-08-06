// Applies the stored theme before anything renders. Without this the first painted frame is
// white for ~40ms: `:root, .theme-light` share a block in colors.css, so --background-01 is
// var(--white) until theme.ts puts theme-<x> on <body>, and the app shell paints that.
// Measured on iPhone 13 Pro, Release: #EDEDED full-screen at 214-240ms, gone with this.
// theme.ts is the owner of this class - it re-applies the same value on init, so this only
// moves the first application earlier. Keep the storage key in sync with theme.ts.
(() => {
    try {
        const theme = localStorage.getItem('ui.theme')
            ?? (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
        document.body.classList.add(`theme-${theme}`);
    } catch {
        // Intended: a missing/blocked localStorage must not stop the splash below
    }
})();

(async () => {
    try {
        const overlay = document.getElementById('web-splash');
        if (overlay) {
            const isSignedInData = localStorage.getItem('AccountUI.IsSignedIn');
            const isSignedIn = isSignedInData === '1';
            // do not show skeletons splash screen if the user was not signed in
            if (!isSignedIn)
                return;

            const isRightPanelVisibleData = localStorage.getItem('RightPanelUI.RightPanel.IsVisible');
            const isRightPanelVisible = isRightPanelVisibleData === '1';
            overlay.innerHTML = `
                <splash-page-skeleton isRightPanelVisible="${isRightPanelVisible}"/>
            `;
        }
    } catch (err) {
        console.error('Splash script error:', err);
    }
})();
