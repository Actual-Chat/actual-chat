# Playwright (AI)

AI agents can use Playwright for browser automation tasks, either connecting to a host browser for visual debugging or using headless Chromium for automated testing.

## Browser Options

### Option 1: Host Browser (Visual)

Connect to Chrome running on the host machine for visual debugging. This allows you to watch automation steps in real-time.

**Start Chrome with remote debugging:**

```bash
ai chrome
```

This launches Chrome with:
- Remote debugging on port 9222
- `--remote-debugging-address=0.0.0.0` for network accessibility
- A separate user profile to avoid conflicts

On Windows, it also creates a firewall rule to allow connections from WSL/Docker. If administrator privileges are required, you'll be prompted with the exact command to run.

**Connect from the AI agent's environment:**

```typescript
import { chromium } from 'playwright';

// Connect to host Chrome
// Note: The IP depends on your network setup
const browser = await chromium.connectOverCDP('http://<host-ip>:9222');
const contexts = browser.contexts();
const context = contexts.length > 0 ? contexts[0] : await browser.newContext();
const page = await context.newPage();

await page.goto('https://local.voxt.ai');
```

**Finding the host IP:**

The IP address varies by environment. Common approaches:
- Docker with `--network host`: Use `localhost`
- WSL to Windows: Resolve `host.docker.internal` and use the resulting IPv4 address
- Check `/etc/resolv.conf` nameserver or default gateway

**Use cases:**
- Visual debugging - watch automation steps execute
- Debugging failures - see exactly what the browser shows
- Developing new tests - easier to write selectors when you can see the page
- Demos - show stakeholders automated workflows

### Option 2: Headless Chromium (Automated)

Use the pre-installed headless Chromium for automated testing without visual output.

Chromium is pre-installed in the Docker environment (see `claude.Dockerfile`):
- Playwright and browser dependencies are installed
- Chromium browser is pre-downloaded (~280MB)

**Launch headless browser:**

```typescript
import { chromium } from 'playwright';

const browser = await chromium.launch({
    headless: true,
});
const context = await browser.newContext();
const page = await context.newPage();

await page.goto('https://local.voxt.ai');
// ... perform automated tests
await browser.close();
```

**Use cases:**
- Automated testing without visual output
- CI/CD pipelines
- Batch operations
- Screenshot/PDF generation

## Prerequisites

Before running Playwright scripts:

1. **For host browser:** Ensure Chrome is running with `ai chrome`

2. **For headless in Docker:** No additional setup needed — Playwright and Chromium are pre-installed in the Docker image. Both the global installation (`/usr/lib/node_modules/playwright`) and the project's `node_modules/playwright` are available.

3. **For headless outside Docker:** Install the Playwright package if not already available:
   ```bash
   npm install playwright
   ```

## Running Scripts

Scripts using `require('playwright')` or `import ... from 'playwright'` must be able to resolve the module. Two approaches:

```bash
# Option 1: Run from the project root (node_modules/playwright is found automatically)
node path/to/script.js

# Option 2: Set NODE_PATH for scripts located outside the project tree
NODE_PATH=/usr/lib/node_modules node /path/to/external/script.js

# Option 3: Run TypeScript scripts via npx tsx
npx tsx path/to/script.ts
```

## Temporary Files

Store screenshots and other outputs in the `tmp/` folder:

```typescript
import * as path from 'path';

const tmpDir = path.join(process.cwd(), 'tmp');
await page.screenshot({ path: path.join(tmpDir, 'screenshot.png') });
```

## Debug UI Methods

After the page loads, you can use debug methods to control the UI:

```typescript
await page.evaluate(() => {
    const debugUI = (window as any).debugUI;
    if (debugUI) {
        // Skip onboarding dialogs
        debugUI.resetOnboarding(false);
        // Skip tutorial bubbles
        debugUI.resetBubbles(false);
    }
});
```

See [Login Flow](./login-flow.md) for complete examples.

## Common Patterns

### Waiting for Elements

```typescript
// Wait for element to be visible
await page.locator('.my-element').waitFor({ timeout: 5000 });

// Check if element is visible without throwing
const isVisible = await page.locator('.my-element')
    .isVisible({ timeout: 2000 })
    .catch(() => false);
```

### Handling Dynamic Content

```typescript
// Wait for network to settle
await page.goto(url, { waitUntil: 'networkidle' });

// Wait for specific condition
await page.waitForFunction(() => {
    return document.querySelectorAll('.chat-message').length > 0;
});
```

### Taking Screenshots

```typescript
// Full page screenshot
await page.screenshot({ path: 'tmp/full.png', fullPage: true });

// Element screenshot
await page.locator('.chat-view').screenshot({ path: 'tmp/chat.png' });
```

## Example Scripts

See [`docs/tests/`](../tests/) for example scripts:
- [`signin-and-message.ts`](../tests/signin-and-message.ts) - Complete login and messaging flow
