Alex's Chrome on port 9222 is frequently behind another window, which makes
`document.visibilityState` `hidden` and stops `requestAnimationFrame` entirely.
Any rAF-driven probe then hangs and any per-frame recording comes back empty.
`Page.bringToFront` and `Browser.setWindowBounds` do not fix it.

**Why:** every frame-accurate UI measurement in this repo (virtual list scroll
physics, animation smoothness) samples on rAF, so a throttled tab silently
produces zeros rather than an error.

**How to apply:** launch a dedicated instance instead —
`chrome.exe --headless=new --remote-debugging-port=9333 --user-data-dir=<tmp>
--disable-backgrounding-occluded-windows --ignore-certificate-errors` — and
carry the session over with `Storage.getCookies` on :9222 followed by
`Network.setCookies` on the new one. That is enough to reach admin-only pages
such as `/test/virtual-list`. Don't navigate the user's own tabs for tests.

For real gestures use the attached Android phone
(`adb forward tcp:9444 localabstract:chrome_devtools_remote`) and
`adb shell input swipe` — CDP's `Input.dispatchTouchEvent` arrives too unevenly
over adb to register as a fling. See [[virtual-list-doc-must-track-code]] for
where the resulting measurements belong.
