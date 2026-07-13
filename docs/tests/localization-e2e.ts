/**
 * E2E test: App localization — language selection UI (feat/3721-app-localization)
 *
 * Model: UI language is a client-only preference stored in local settings (KVAS /
 * localStorage) — there is NO culture cookie and NO ?culture= URL param. Changing it
 * persists the choice and reloads so strings re-resolve.
 *
 * Covers:
 * 1. Language picker renders correctly (6 options, native names, values).
 * 2. English baseline — Settings strings are English.
 * 3. Switch to Spanish — Settings strings become Spanish; dropdown reflects it.
 * 4. Persist across reload (local settings, not cookie).
 * 5. Switch to Russian.
 * 6. Switch back to English (cleanup).
 *
 * Prerequisites:
 * - Server running (the branch build) at HostSettings__BaseUri.
 * - Chrome with remote debugging on :9222 (`ai chrome`), or falls back to headless.
 *
 * Usage:
 *   npx tsx docs/tests/localization-e2e.ts
 *
 * Screenshots: tmp/l10n-*.png
 */

import { chromium, type Page } from 'playwright';
import * as fs from 'fs';
import * as path from 'path';

// --- Configuration ---

function loadBaseUrl(): string {
    const envPath = path.resolve(process.cwd(), '.env');
    if (fs.existsSync(envPath)) {
        const match = fs.readFileSync(envPath, 'utf-8').match(/^HostSettings__BaseUri=(.+)$/m);
        if (match)
            return match[1].trim();
    }
    return 'https://local.voxt.ai';
}

const BASE_URL = loadBaseUrl();
const TEST_EMAIL = 'test-l10n@actual.chat';
const TEST_OTP = '111111';

const SUPPORTED: { code: string; name: string }[] = [
    { code: 'en', name: 'English' },
    { code: 'es', name: 'Español' },
    { code: 'fr', name: 'Français' },
    { code: 'it', name: 'Italiano' },
    { code: 'ru', name: 'Русский' },
    { code: 'de', name: 'Deutsch' },
];

const tmpDir = path.join(process.cwd(), 'tmp');
if (!fs.existsSync(tmpDir))
    fs.mkdirSync(tmpDir, { recursive: true });

function screenshotPath(name: string): string {
    return path.join(tmpDir, `l10n-${name}.png`);
}

// --- Expected translations (source of truth: the embedded JSON) ---

function loadStrings(lang: string): Record<string, string> {
    const filePath = path.join(process.cwd(), 'src/dotnet/UI.Blazor.App/Resources', `Strings.${lang}.json`);
    return JSON.parse(fs.readFileSync(filePath, 'utf-8'));
}

const enStrings = loadStrings('en');
const esStrings = loadStrings('es');
const ruStrings = loadStrings('ru');

// --- Test infrastructure ---

let passed = 0;
let failed = 0;
const failures: string[] = [];

function assert(condition: boolean, message: string) {
    if (condition) {
        passed++;
        console.log(`  PASS: ${message}`);
    }
    else {
        failed++;
        failures.push(message);
        console.error(`  FAIL: ${message}`);
    }
}

function assertContains(actual: string, expected: string, message: string) {
    assert(actual.includes(expected), `${message} — expected "${expected}" in "${actual.substring(0, 120)}"`);
}

// --- Helpers ---

async function dismissOverlays(page: Page) {
    const cookieBtn = page.locator('button.cookie-btn').first();
    if (await cookieBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
        await cookieBtn.click();
        await page.waitForTimeout(500);
    }
    await page.evaluate(() => {
        const d = (window as unknown as { debugUI?: { resetOnboarding(v: boolean): void; resetBubbles(v: boolean): void } }).debugUI;
        if (d) {
            d.resetOnboarding(false);
            d.resetBubbles(false);
        }
    });
    await page.waitForTimeout(300);
}

async function signIn(page: Page) {
    const accountBtn = page.locator('.account-btn');
    if (await accountBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
        console.log('  Already signed in');
        return;
    }
    console.log('  Signing in...');
    const signInBtn = page.locator('button.signin-button-group').first();
    await signInBtn.waitFor({ state: 'visible', timeout: 10000 });
    await signInBtn.click();
    await page.waitForTimeout(2000);

    const phoneEmailInput = page.locator('.sign-in-modal input[type="text"]').first();
    await phoneEmailInput.waitFor({ state: 'visible', timeout: 5000 });
    await phoneEmailInput.fill(TEST_EMAIL);
    await page.waitForTimeout(300);

    const registerToggle = page.locator('.sign-in-modal label.toggle');
    const submitBtn = page.locator('.sign-in-modal button[type="submit"]').first();
    await submitBtn.click();
    await page.waitForTimeout(3000);

    const accountNotFound = page.locator('text=Account not found');
    if (await accountNotFound.isVisible({ timeout: 2000 }).catch(() => false)) {
        console.log('  Account not found, enabling registration...');
        await registerToggle.click();
        await page.waitForTimeout(500);
        await submitBtn.click();
        await page.waitForTimeout(3000);
    }

    const digitInputs = page.locator('input.c-digit:visible');
    await digitInputs.first().waitFor({ state: 'visible', timeout: 5000 }).catch(() => {});
    if (await digitInputs.count() >= 6) {
        await digitInputs.first().click();
        await page.waitForTimeout(100);
        await page.keyboard.type(TEST_OTP, { delay: 80 });
        await page.waitForTimeout(2000);
    }

    const verifyBtn = page.locator('button[type="submit"]:visible').first();
    if (await verifyBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
        await verifyBtn.click();
        await page.waitForTimeout(3000);
    }
    await page.waitForTimeout(3000);
    await dismissOverlays(page);
    console.log('  Sign-in complete');
}

async function openSettings(page: Page): Promise<boolean> {
    // The modal can already be open (e.g. restored after a language-change reload);
    // clicking the account button then would be intercepted by the modal overlay.
    if (await page.locator('.settings-modal').isVisible({ timeout: 1000 }).catch(() => false))
        return true;

    const settingsBtn = page.locator('.account-btn').first();
    await settingsBtn.waitFor({ state: 'visible', timeout: 10000 }).catch(() => {});
    if (await settingsBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
        await settingsBtn.click();
        await page.waitForTimeout(1500);
    }
    return page.locator('.settings-modal').isVisible({ timeout: 5000 }).catch(() => false);
}

async function getSettingsText(page: Page): Promise<string> {
    return (await page.locator('.settings-modal').textContent()) ?? '';
}

async function clickTab(page: Page, tabText: string): Promise<boolean> {
    const tab = page.locator(`.settings-modal .c-tab-item:has-text("${tabText}")`).first();
    if (await tab.isVisible({ timeout: 2000 }).catch(() => false)) {
        await tab.click();
        await page.waitForTimeout(1000);
        return true;
    }
    console.log(`  Tab "${tabText}" not found`);
    return false;
}

async function gotoUiTab(page: Page, uiTabLabel: string): Promise<boolean> {
    const opened = await openSettings(page);
    if (!opened)
        return false;

    await clickTab(page, uiTabLabel);
    return page.locator('.settings-modal select.c-language-select').isVisible({ timeout: 5000 }).catch(() => false);
}

async function switchLanguage(page: Page, langCode: string) {
    const langSelect = page.locator('.settings-modal select.c-language-select');
    await langSelect.waitFor({ state: 'visible', timeout: 5000 });
    await langSelect.selectOption(langCode);
    // Triggers History.ForceReload — wait for the reload + interactive app.
    await page.waitForLoadState('domcontentloaded');
    await page.waitForTimeout(5000);
    await page.locator('.account-btn').waitFor({ state: 'visible', timeout: 15000 }).catch(() => {});
    await dismissOverlays(page);
}

// --- Tests ---

async function testPickerUI(page: Page) {
    console.log('\n--- Test 1: Language picker renders correctly ---');
    const visible = await gotoUiTab(page, enStrings['Settings_UserInterface']);
    // The current language may not be English yet; the picker itself must still render.
    if (!visible) {
        // maybe currently non-English: try the Spanish/Russian label too
        for (const label of [esStrings['Settings_UserInterface'], ruStrings['Settings_UserInterface']]) {
            await clickTab(page, label);
            if (await page.locator('.settings-modal select.c-language-select').isVisible({ timeout: 2000 }).catch(() => false))
                break;
        }
    }
    const langSelect = page.locator('.settings-modal select.c-language-select');
    const pickerVisible = await langSelect.isVisible({ timeout: 3000 }).catch(() => false);
    assert(pickerVisible, 'Language picker dropdown is visible on the UI tab');
    await page.screenshot({ path: screenshotPath('01-picker') });
    if (!pickerVisible)
        return;

    const options = await langSelect.locator('option').all();
    assert(options.length === SUPPORTED.length, `Picker has ${SUPPORTED.length} options (got ${options.length})`);
    for (let i = 0; i < Math.min(options.length, SUPPORTED.length); i++) {
        const value = (await options[i].getAttribute('value')) ?? '';
        const text = ((await options[i].textContent()) ?? '').trim();
        assert(value === SUPPORTED[i].code, `Option ${i} value = "${SUPPORTED[i].code}" (got "${value}")`);
        assert(text === SUPPORTED[i].name, `Option ${i} label = "${SUPPORTED[i].name}" (got "${text}")`);
    }
}

async function testBaselineEnglish(page: Page) {
    console.log('\n--- Test 2: Set English baseline ---');
    await switchLanguage(page, 'en');
    const visible = await gotoUiTab(page, enStrings['Settings_UserInterface']);
    assert(visible, 'UI tab reachable in English');
    const langSelect = page.locator('.settings-modal select.c-language-select');
    assert((await langSelect.inputValue()) === 'en', 'Picker value is "en"');
    const text = await getSettingsText(page);
    assertContains(text, enStrings['Settings_YourAccount'], 'Tab: Your Account (EN)');
    assertContains(text, enStrings['Settings_Application'], 'Tab: Application (EN)');
    assertContains(text, enStrings['UserInterface_Theme'], 'Theme section (EN)');
    await page.screenshot({ path: screenshotPath('02-english') });
}

async function testSwitchToSpanish(page: Page) {
    console.log('\n--- Test 3: Switch to Spanish ---');
    await switchLanguage(page, 'es');
    const visible = await gotoUiTab(page, esStrings['Settings_UserInterface']);
    assert(visible, 'UI tab reachable in Spanish');
    const langSelect = page.locator('.settings-modal select.c-language-select');
    assert((await langSelect.inputValue()) === 'es', 'Picker value is "es"');
    const text = await getSettingsText(page);
    assertContains(text, esStrings['Settings_YourAccount'], 'Tab: Your Account (ES)');
    assertContains(text, esStrings['Settings_Application'], 'Tab: Application (ES)');
    assertContains(text, esStrings['UserInterface_Theme'], 'Theme section (ES)');
    await page.screenshot({ path: screenshotPath('03-spanish') });
}

async function testPersistAfterReload(page: Page) {
    console.log('\n--- Test 4: Language persists after reload (local settings) ---');
    await page.keyboard.press('Escape');
    await page.waitForTimeout(500);
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(5000);
    await dismissOverlays(page);
    const modalOpen = await openSettings(page);
    assert(modalOpen, 'Settings modal visible after reload');
    if (!modalOpen)
        return;

    const text = await getSettingsText(page);
    assertContains(text, esStrings['Settings_YourAccount'], 'Still Spanish after reload: Your Account');
    assertContains(text, esStrings['Settings_Application'], 'Still Spanish after reload: Application');
    await page.screenshot({ path: screenshotPath('04-persist-spanish') });
}

async function testSwitchToRussian(page: Page) {
    console.log('\n--- Test 5: Switch to Russian ---');
    await clickTab(page, esStrings['Settings_UserInterface']);
    await switchLanguage(page, 'ru');
    const visible = await gotoUiTab(page, ruStrings['Settings_UserInterface']);
    assert(visible, 'UI tab reachable in Russian');
    const text = await getSettingsText(page);
    assertContains(text, ruStrings['Settings_YourAccount'], 'Tab: Your Account (RU)');
    assertContains(text, ruStrings['Settings_Application'], 'Tab: Application (RU)');
    await page.screenshot({ path: screenshotPath('05-russian') });
}

async function testSwitchBackToEnglish(page: Page) {
    console.log('\n--- Test 6: Switch back to English (cleanup) ---');
    await clickTab(page, ruStrings['Settings_UserInterface']);
    await switchLanguage(page, 'en');
    const modalOpen = await openSettings(page);
    assert(modalOpen, 'Settings modal visible after switching back to English');
    if (!modalOpen)
        return;

    const text = await getSettingsText(page);
    assertContains(text, enStrings['Settings_YourAccount'], 'Back to EN: Your Account');
    await page.screenshot({ path: screenshotPath('06-english-again') });
}

// --- Main ---

async function main() {
    console.log('\n=== Localization E2E (language selection UI) ===');
    console.log(`Base URL: ${BASE_URL}`);

    let browser;
    let usingHostChrome = false;
    try {
        browser = await chromium.connectOverCDP('http://localhost:9222');
        usingHostChrome = true;
        console.log('Connected to host Chrome');
    }
    catch {
        console.log('Host Chrome not available, launching headless Chromium...');
        browser = await chromium.launch({ headless: true, args: ['--ignore-certificate-errors'] });
    }

    const context = usingHostChrome
        ? (browser.contexts()[0] ?? await browser.newContext({ ignoreHTTPSErrors: true }))
        : await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 720 } });
    const page = await context.newPage();

    try {
        await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
        await page.waitForTimeout(4000);
        await dismissOverlays(page);
        await signIn(page);
        await page.waitForTimeout(2000);
        await dismissOverlays(page);

        await testPickerUI(page);
        await testBaselineEnglish(page);
        await testSwitchToSpanish(page);
        await testPersistAfterReload(page);
        await testSwitchToRussian(page);
        await testSwitchBackToEnglish(page);
    }
    catch (error) {
        console.error('\nFatal error:', error);
        await page.screenshot({ path: screenshotPath('fatal-error') }).catch(() => {});
    }
    finally {
        await page.close();
        if (!usingHostChrome)
            await browser.close();
    }

    console.log(`\n=== Results: ${passed} passed, ${failed} failed ===`);
    if (failures.length > 0) {
        console.log('\nFailures:');
        failures.forEach(f => console.log(`  FAIL: ${f}`));
    }
    console.log(`\nScreenshots: ${tmpDir}/l10n-*.png`);
    process.exit(failed > 0 ? 1 : 0);
}

main().catch(err => {
    console.error('Fatal:', err);
    process.exit(1);
});
