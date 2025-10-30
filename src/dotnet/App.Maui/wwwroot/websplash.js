(async () => {
    try {
        const overlay = document.getElementById('web-splash');
        if (overlay) {
            await window.App.whenBundleReady;
            const trueLiteral = 'AAEBAQ==';
            const isSignedInData = await ui.localSettings.kvas.get('AccountUI.IsSignedIn');
            const isSignedIn = isSignedInData === trueLiteral;
            // do not show skeletons splash screen if the user was not signed in
            if (!isSignedIn)
                return;

            const isVisibleData = await ui.localSettings.kvas.get('RightPanelUI.RightPanel.IsVisible');
            const isRightPanelVisible = isVisibleData === trueLiteral;
            overlay.innerHTML = `
                <splash-page-skeleton isRightPanelVisible="${isRightPanelVisible}"/>
            `;
        }
    } catch (err) {
        console.error('Splash script error:', err);
    }
})();
