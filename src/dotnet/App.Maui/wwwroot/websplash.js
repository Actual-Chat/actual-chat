(async () => {
    try {
        const overlay = document.getElementById('web-splash');
        if (overlay) {
            await window.App.whenBundleReady;
            const isVisibleData = await ui.localSettings.kvas.get('RightPanelUI.RightPanel.IsVisible');
            const isRightPanelVisible = isVisibleData === 'AAEBAQ==';
            document.body.dataset.rpVisible = isRightPanelVisible ? "true" : "false";
            overlay.innerHTML = `
                <splash-page-skeleton isRightPanelVisible="${isRightPanelVisible}"/>
            `;
        }
    } catch (err) {
        console.error('Splash script error:', err);
    }
})();
