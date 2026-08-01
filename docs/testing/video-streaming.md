# Video Streaming Test Flow

This document describes how to test the video streaming feature (VideoPanel) using Playwright. It covers the full flow: sign in, start audio recording, open the video call modal, start the camera, and verify the local preview renders correctly.

## Prerequisites

- Host Chrome running with remote debugging: `ai chrome` (port 9222)
- Server running: `/server-start` or manually via `dotnet run`
- App accessible at `https://local.voxt.ai`

## Test Flow Overview

1. Connect to host Chrome via CDP
2. Navigate to a chat page
3. Sign in (if not already signed in)
4. Start audio recording (makes the video toggle button appear)
5. Click video toggle to open JoinVideoCallModal
6. Select camera and click Start
7. Verify VideoPanel renders live video on canvas
8. Verify "You" participant label appears

## Key Selectors

### Chat Audio Panel

| Element | Selector | Notes |
|---------|----------|-------|
| Audio panel | `.chat-audio-panel` | Main container |
| Recorder button | `.chat-audio-panel .recorder-wrapper .btn.btn-round` | Click to start/stop audio recording |
| Recording active | `.chat-audio-panel .recorder-wrapper.record-on` | Present when audio is recording |
| Video toggle | `.chat-audio-panel .video-wrapper .btn.btn-round` | Only visible while audio is recording |

### JoinVideoCallModal

| Element | Selector | Notes |
|---------|----------|-------|
| Modal container | `.join-video-call-modal` | Wait for this after clicking video toggle |
| Camera dropdown | `.camera-select` | `<select>` element listing available cameras |
| Start/Join button | `.btn-modal.btn-primary` | Primary action button in modal footer |

### VideoPanel

| Element | Selector | Notes |
|---------|----------|-------|
| Panel container | `.video-panel` | Main wrapper |
| Local video canvas | `.video-panel canvas.call-video` | Canvas element for local camera preview |
| "You" label | `.video-panel .video-frame .video-participant-label` | Shows "You" with camera icon when recording |
| Remote streams | `.video-panel .remote-streams` | Grid container for remote participant videos |
| Remote video | `.video-panel .remote-streams canvas.remote-video` | Individual remote stream canvases |
| Expand button | `.video-panel .expand-btn` | Fullscreen toggle |

### VideoPanel States

| Class | Meaning |
|-------|---------|
| `.video-panel.recording` | Local video is recording |
| `.video-panel.has-remote-streams` | Remote participants are streaming |
| `.video-panel.expanded` | Panel is in fullscreen mode |
| `.video-panel.first-time-open` | Opening animation in progress |
| `.video-panel.closing` | Closing animation in progress |

## Step-by-Step Implementation

### 1. Connect to Host Chrome

```typescript
import { chromium, type Page } from 'playwright';

const browser = await chromium.connectOverCDP('http://localhost:9222');

// Reuse an existing tab with the app loaded (avoids WASM cache issues)
let page: Page | null = null;
for (const ctx of browser.contexts()) {
    for (const p of ctx.pages()) {
        if (p.url().includes('local.voxt.ai')) {
            page = p;
            break;
        }
    }
    if (page) break;
}

// Fall back to opening a new page
if (!page) {
    const ctx = browser.contexts()[0] ?? await browser.newContext({ ignoreHTTPSErrors: true });
    page = await ctx.newPage();
}
```

### 2. Clear Browser Cache (After Rebuilds)

After rebuilding the server, the browser may cache stale WASM assets with fingerprinted URLs that no longer exist. Clear the cache via CDP:

```typescript
const cdpSession = await page.context().newCDPSession(page);
await cdpSession.send('Network.clearBrowserCache');
await cdpSession.send('Storage.clearDataForOrigin', {
    origin: 'https://local.voxt.ai',
    storageTypes: 'cache_storage,service_workers',
});
```

### 3. Navigate and Sign In

Navigate directly to the chat page (the sign-in footer appears on chat pages, not the landing page):

```typescript
await page.goto('https://local.voxt.ai/chat/the-actual-one', {
    waitUntil: 'load',
    timeout: 90_000,
});
// Blazor WASM needs time to fully boot after cache clear
await page.waitForTimeout(10000);
```

Check if already signed in by looking for the sign-in footer:

```typescript
async function isSignedIn(page: Page): Promise<boolean> {
    const signinFooter = page.locator('div.signin-footer');
    const hasSigninFooter = await signinFooter.isVisible({ timeout: 2000 }).catch(() => false);
    if (hasSigninFooter) return false;

    const chatPanel = page.locator('.chat-audio-panel');
    return await chatPanel.isVisible({ timeout: 2000 }).catch(() => false);
}
```

Sign in using the sign-in footer button (not the header button):

```typescript
// Click sign-in button in the footer
await page.locator('div.signin-footer button').first().click();
await page.waitForTimeout(1500);

// Select email option if shown
const emailOpt = page.locator('button:has-text("Email")');
if (await emailOpt.isVisible({ timeout: 5000 }).catch(() => false)) {
    await emailOpt.click();
}

// Enter test email
await page.locator('input[type="email"], input[placeholder*="email" i]').first().fill('test-video@actual.chat');
await page.locator('button[type="submit"], button:has-text("Continue")').first().click();
await page.waitForTimeout(2000);

// Enter OTP (6 individual digit inputs)
const digitInputs = page.locator('input[maxlength="1"]');
for (let i = 0; i < 6; i++) {
    await digitInputs.nth(i).fill('1');
    await page.waitForTimeout(50);
}
await page.waitForTimeout(1000);

// Click verify/continue if visible
const verifyBtn = page.locator('button[type="submit"], button:has-text("Verify")').first();
if (await verifyBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
    await verifyBtn.click();
}
```

See [Login Flow](./login-flow.md) for the general login approach. Note that on chat pages the sign-in button is in `div.signin-footer`, not in the header.

### 4. Start Audio Recording

The video toggle button only appears while audio is recording:

```typescript
const recorderBtn = page.locator('.chat-audio-panel .recorder-wrapper .btn.btn-round').first();
await recorderBtn.waitFor({ timeout: 10_000 });

// Check if already recording
const alreadyRecording = await page.locator('.chat-audio-panel .recorder-wrapper.record-on')
    .isVisible({ timeout: 1000 }).catch(() => false);

if (!alreadyRecording) {
    await recorderBtn.click();
    await page.locator('.chat-audio-panel .recorder-wrapper.record-on')
        .waitFor({ timeout: 10_000 });
}
```

### 5. Open Video Call Modal

```typescript
const videoBtn = page.locator('.chat-audio-panel .video-wrapper .btn.btn-round').first();
await videoBtn.waitFor({ timeout: 15_000 });
await videoBtn.click();

// Wait for the modal
const modal = page.locator('.join-video-call-modal');
await modal.waitFor({ timeout: 10_000 });

// Give camera preview time to initialize
await page.waitForTimeout(3000);
```

### 6. Select Camera (Optional)

```typescript
const cameraSelect = modal.locator('select.camera-select');
if (await cameraSelect.isVisible({ timeout: 2000 }).catch(() => false)) {
    const options = await cameraSelect.locator('option').allTextContents();
    console.log('Available cameras:', options);

    // Select OBS Virtual Camera if available (useful for testing)
    const obsOption = options.find(o => /obs/i.test(o));
    if (obsOption) {
        await cameraSelect.selectOption({ label: obsOption });
        await page.waitForTimeout(1500);
    }
}
```

### 7. Click Start

```typescript
const startBtn = modal.locator('.btn-modal.btn-primary');
await startBtn.click();
await page.waitForTimeout(3000);
```

### 8. Verify Local Video Preview

After clicking Start, the VideoPanel should appear with a live camera preview rendered on a canvas:

```typescript
const videoPanel = page.locator('.video-panel');
await videoPanel.waitFor({ timeout: 15_000 });

const canvas = page.locator('.video-panel canvas.call-video');
await canvas.waitFor({ timeout: 10_000 });

// Allow time for the recording pipeline to render frames
await page.waitForTimeout(5000);

// Sample pixel data to confirm the canvas is not all-black
const result = await page.evaluate(() => {
    const c = document.querySelector('.video-panel canvas.call-video') as HTMLCanvasElement;
    if (!c) return { found: false, hasContent: false };

    const ctx = c.getContext('2d');
    if (!ctx) return { found: true, hasContent: false };

    const w = c.width, h = c.height;
    if (w === 0 || h === 0) return { found: true, hasContent: false };

    let nonBlack = 0;
    for (let gy = 0; gy < 3; gy++) {
        for (let gx = 0; gx < 3; gx++) {
            const x = Math.floor((gx + 0.5) * w / 3);
            const y = Math.floor((gy + 0.5) * h / 3);
            const [r, g, b, a] = ctx.getImageData(x, y, 1, 1).data;
            if (a > 0 && (r > 10 || g > 10 || b > 10)) nonBlack++;
        }
    }
    return { found: true, width: w, height: h, hasContent: nonBlack > 0, nonBlack };
});

console.log(`Canvas: ${result.width}x${result.height}, nonBlack=${result.nonBlack}/9`);
// Expected: 1280x720 (or camera resolution), nonBlack > 0
```

### 9. Verify "You" Label

```typescript
const label = page.locator('.video-panel .video-frame .video-participant-label');
const visible = await label.isVisible({ timeout: 5000 }).catch(() => false);
const text = (await label.textContent())?.trim() ?? '';
const hasIcon = await label.locator('i.icon-video').isVisible().catch(() => false);

console.log(`Label: "${text}", icon: ${hasIcon}`);
// Expected: text contains "You", icon visible
```

## Debugging Tips

### Capture Browser Console Logs

Filter for VideoPanel and RecordingService messages:

```typescript
page.on('console', msg => {
    const text = msg.text();
    if (msg.type() === 'error')
        console.log(`[browser:error] ${text.slice(0, 200)}`);
    if (text.includes('VideoPanel') || text.includes('RecordingService') || text.includes('Recording'))
        console.log(`[browser:log] ${text.slice(0, 300)}`);
});
```

### Inspect Hidden Video Elements

The recording pipeline creates hidden `<video>` elements. Check their state:

```typescript
const debug = await page.evaluate(() => {
    const videos = document.querySelectorAll('video');
    return Array.from(videos).map(v => ({
        srcObject: v.srcObject ? 'MediaStream' : 'null',
        readyState: v.readyState,
        videoWidth: v.videoWidth,
        videoHeight: v.videoHeight,
        paused: v.paused,
    }));
});
console.log('Video elements:', JSON.stringify(debug));
```

### Canvas Stays Black (300x150)

A 300x150 canvas with all-black pixels indicates the render loop is not drawing frames. Common causes:

1. **`startRecording()` never called**: Check browser console for `[VideoPanel] Starting video recording...`. If absent, the Blazor auto-start logic may not be triggering. Verify the JoinVideoCallModal dispatches the correct state.

2. **Preview stream dead**: The recording pipeline's `MediaStreamTrackProcessor` exclusively consumes the camera track. The preview must use a separate `getUserMedia()` stream, acquired before the pipeline starts.

3. **Render loop timing**: The `requestAnimationFrame` render loop checks `isRecording` on each frame. If `isRecording` is still `false` when the first frame fires (during an async `await`), the loop exits permanently.

### Stale WASM After Rebuild

After rebuilding the server, the browser may request old fingerprinted `.wasm` and `.dll` URLs that return 404/502. Clear the cache via CDP (see step 2 above) or hard-refresh the tab.

## Example Test Script

A complete example is available at [`tmp/test-video-preview.ts`](../../tmp/test-video-preview.ts).

```bash
cd /proj/ActualChat && npx tsx tmp/test-video-preview.ts
```

## Architecture Notes

### Local Preview Pipeline

The local camera preview uses a **separate** `getUserMedia()` stream from the recording pipeline. This is necessary because `MediaStreamTrackProcessor` (used by the WebCodecs encoding pipeline) exclusively consumes frames from its track — cloning the track after the processor starts produces a dead track in Chromium.

Flow:
1. Acquire preview stream via `getUserMedia()`
2. Start recording pipeline (consumes its own camera stream internally)
3. Set `isRecording = true`
4. Start `requestAnimationFrame` loop drawing preview stream to canvas

### Remote Stream Rendering

Remote video streams are rendered by `VideoTrackPlayer` components inside `.remote-streams`. Each remote stream gets its own `<canvas class="remote-video">` element with an author name label. When remote streams are present, the local preview shrinks to a picture-in-picture view in the bottom-right corner (`.has-remote-streams .video-frame`).

### Component Hierarchy

```
VideoPanel.razor
├── .video-frame
│   ├── canvas.call-video          (local camera preview)
│   └── .video-participant-label   ("You" label, shown when recording)
├── .remote-streams
│   └── VideoTrackPlayer.razor     (one per remote participant)
│       └── .remote-video-container
│           ├── canvas.remote-video
│           └── .video-participant-label  (author name)
├── .expand-btn                    (fullscreen toggle)
└── .video-error                   (error message, shown on failure)
```
