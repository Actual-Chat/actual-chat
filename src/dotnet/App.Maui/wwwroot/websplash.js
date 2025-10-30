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
