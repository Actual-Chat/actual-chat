(async () => {
    try {
        const overlay = document.getElementById('web-splash');
        if (overlay) {
            await window.App.whenBundleReady;
            const isVisibleData = await ui.localSettings.kvas.get('RightPanelUI.RightPanel.IsVisible');
            const isRightPanelVisible = isVisibleData === 'AAEBAQ==';
            const body = document.body;
            const closedCls = "rp-closed";
            const openCls = "rp-open";
            if (isRightPanelVisible) {
                body.classList.remove(closedCls);
                body.classList.add(openCls);
            } else {
                body.classList.remove(openCls);
                body.classList.add(closedCls);
            }
            overlay.innerHTML = `
                <splash-page-skeleton isRightPanelVisible="${isRightPanelVisible}"/>
            `;
        }
    } catch (err) {
        console.error('Splash script error:', err);
    }
})();
