/**
 * E2E test: Verify avatar editing works correctly.
 *
 * Covers the diff-based avatar update flow (fix for #3736):
 * - Editing avatar name and bio via the OwnAvatarEditorModal
 * - Creating a new avatar
 * - Verifying that edits persist after page reload
 *
 * Run:
 *   npx vitest run tests/ts/e2e/avatar-edit.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Page } from 'playwright';
import {
    BASE_URL, connectBrowser, dismissCookieConsent, skipOnboarding,
    isSignedIn, signIn, screenshot, useServerRenderMode, waitForAppReady,
    type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e', name);

// Repeated /settings goto across tests races with the previous modal's close animation,
// an OnboardingModal re-render, or the avatar-saved bubble tutorial — retry until the
// settings modal itself is visible. Use waitFor (not isVisible, which doesn't poll).
async function openSettings(page: Page) {
    const settingsModal = page.locator('.settings-modal');
    for (let attempt = 0; attempt < 3; attempt++) {
        await page.goto(`${BASE_URL}/settings`, { waitUntil: 'domcontentloaded' });
        await skipOnboarding(page);
        const visible = await settingsModal.waitFor({ state: 'visible', timeout: 15_000 })
            .then(() => true).catch(() => false);
        if (visible) return;
    }
    throw new Error('settings-modal did not become visible after 3 goto attempts');
}

// Retry click with interleaved skipOnboarding — onboarding stepper can re-mount
// between opening a child modal and pressing a button inside it.
async function clickResilient(
    page: Page,
    locator: ReturnType<Page['locator']>,
    attempts = 4,
) {
    for (let i = 0; i < attempts; i++) {
        try {
            await locator.click({ timeout: 5_000 });
            return;
        } catch (e) {
            if (i === attempts - 1) throw e;
            await skipOnboarding(page);
            await page.waitForTimeout(300);
        }
    }
}

describe('avatar editing', () => {
    let conn: BrowserConnection;
    let page: Page;
    const uniqueSuffix = Date.now().toString(36);

    beforeAll(async () => {
        conn = await connectBrowser();
        page = await conn.context.newPage();

        await useServerRenderMode(page);
        await page.goto(BASE_URL, { waitUntil: 'domcontentloaded', timeout: 15000 });
        await waitForAppReady(page);
        await dismissCookieConsent(page);

        if (!await isSignedIn(page))
            await signIn(page);
        await skipOnboarding(page);
        console.log('Signed in successfully');
    }, 90_000);

    afterAll(async () => {
        await page.close();
        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    });

    it('should create a new avatar and set name and bio', async () => {
        // arrange
        await openSettings(page);

        const accountTab = page.locator('text=Your Account').first();
        if (await accountTab.isVisible({ timeout: 3000 }).catch(() => false)) {
            await clickResilient(page, accountTab);
            await page.waitForTimeout(1000);
        }

        // act - click "+ Add avatar"
        const addBtn = page.locator('text=Add avatar').first();
        await addBtn.waitFor({ state: 'visible', timeout: 10000 });
        await page.screenshot({ path: shot('avatar-before-create') });
        await clickResilient(page, addBtn);
        await page.waitForTimeout(2000);

        // assert - editor modal opens
        const avatarModal = page.locator('.edit-avatar-modal');
        await avatarModal.waitFor({ state: 'visible', timeout: 5000 });
        await page.screenshot({ path: shot('avatar-created') });

        // act - set name and bio
        const nameInput = avatarModal.locator('input#avatar-editor-name, input[id*="name" i]').first();
        await nameInput.waitFor({ state: 'visible', timeout: 5000 });
        await nameInput.fill(`Avatar ${uniqueSuffix}`);
        await page.waitForTimeout(300);

        const bioInput = avatarModal.locator('textarea, input[id*="bio" i]').first();
        if (await bioInput.isVisible({ timeout: 3000 }).catch(() => false)) {
            await bioInput.fill(`Bio ${uniqueSuffix}`);
            await page.waitForTimeout(300);
        }

        // act - save
        const saveButton = avatarModal.locator('button:has-text("Save")').first();
        await saveButton.waitFor({ state: 'visible', timeout: 3000 });
        await clickResilient(page, saveButton);

        // assert - modal closed
        await avatarModal.waitFor({ state: 'hidden', timeout: 10_000 });
        await page.screenshot({ path: shot('avatar-saved') });
    }, 60_000);

    it('should edit an existing avatar name', async () => {
        // arrange - navigate to settings and open avatar editor
        await openSettings(page);

        const accountTab = page.locator('text=Your Account').first();
        if (await accountTab.isVisible({ timeout: 3000 }).catch(() => false)) {
            await clickResilient(page, accountTab);
            await page.waitForTimeout(1000);
        }

        // dismiss any tooltip bubbles
        const okBtn = page.locator('button:has-text("Ok")');
        if (await okBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
            await clickResilient(page, okBtn);
            await page.waitForTimeout(500);
        }

        // act - open the avatar editor via the edit button on an avatar tile
        // Avatar tiles have both star and edit buttons; the Information section only has edit
        const avatarTile = page.locator(':has(> button:has(i.icon-star))').first();
        const avatarEditBtn = avatarTile.locator('button:has(i.icon-edit)');
        await avatarEditBtn.waitFor({ state: 'visible', timeout: 10000 });
        await page.screenshot({ path: shot('avatar-before-edit') });
        await clickResilient(page, avatarEditBtn);
        await page.waitForTimeout(1500);

        const avatarModal = page.locator('.edit-avatar-modal');
        await avatarModal.waitFor({ state: 'visible', timeout: 5000 });

        // act - change name. Ctrl+A via locator.press doesn't reliably select in this
        // input (observed: only the first char gets deleted, new text gets inserted
        // before the leftover tail). selectText() calls input.select() directly, then
        // typing replaces the selection. Tab → blur fires Blazor's @onchange.
        const nameInput = avatarModal.locator('input#avatar-editor-name, input[id*="name" i]').first();
        await nameInput.waitFor({ state: 'visible', timeout: 5000 });
        const newName = `Edited ${uniqueSuffix}`;
        await nameInput.click();
        await nameInput.selectText();
        await nameInput.pressSequentially(newName, { delay: 20 });
        await nameInput.press('Tab');
        await page.waitForTimeout(300);
        await page.screenshot({ path: shot('avatar-editor-filled') });

        // act - save
        const saveButton = avatarModal.locator('button:has-text("Save")').first();
        await clickResilient(page, saveButton);
        // Wait for the modal to actually close — the next click on the tile
        // edit button can otherwise be intercepted by the lingering modal
        // overlay while its close animation runs.
        await avatarModal.waitFor({ state: 'hidden', timeout: 10_000 });

        // assert - re-open and verify name persisted
        await avatarEditBtn.waitFor({ state: 'visible', timeout: 10000 });
        await clickResilient(page, avatarEditBtn);
        await avatarModal.waitFor({ state: 'visible', timeout: 5000 });

        const savedName = await nameInput.inputValue();
        expect(savedName).toBe(newName);
        await page.screenshot({ path: shot('avatar-editor-verified') });

        // cleanup - close modal
        await page.keyboard.press('Escape');
        await page.waitForTimeout(500);
    }, 60_000);

    it('should persist avatar changes after page reload', async () => {
        // arrange - open editor and change name
        await openSettings(page);

        const accountTab = page.locator('text=Your Account').first();
        if (await accountTab.isVisible({ timeout: 3000 }).catch(() => false)) {
            await clickResilient(page, accountTab);
            await page.waitForTimeout(1000);
        }

        // dismiss any tooltip bubbles
        const okBtn = page.locator('button:has-text("Ok")');
        if (await okBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
            await clickResilient(page, okBtn);
            await page.waitForTimeout(500);
        }

        const avatarTile = page.locator(':has(> button:has(i.icon-star))').first();
        const avatarEditBtn = avatarTile.locator('button:has(i.icon-edit)');
        await avatarEditBtn.waitFor({ state: 'visible', timeout: 10000 });
        await clickResilient(page, avatarEditBtn);
        await page.waitForTimeout(1500);

        const avatarModal = page.locator('.edit-avatar-modal');
        await avatarModal.waitFor({ state: 'visible', timeout: 5000 });

        const nameInput = avatarModal.locator('input#avatar-editor-name, input[id*="name" i]').first();
        await nameInput.waitFor({ state: 'visible', timeout: 5000 });

        // act - set a unique name and save
        const persistName = `Persist ${uniqueSuffix}`;
        await nameInput.fill(persistName);
        await page.waitForTimeout(300);

        const saveButton = avatarModal.locator('button:has-text("Save")').first();
        await clickResilient(page, saveButton);
        await avatarModal.waitFor({ state: 'hidden', timeout: 10_000 });

        // act - reload the page
        await openSettings(page);
        const _settingsModal = page.locator('.settings-modal');

        if (await accountTab.isVisible({ timeout: 3000 }).catch(() => false)) {
            await clickResilient(page, accountTab);
            await page.waitForTimeout(1000);
        }

        // dismiss tooltip again after reload
        if (await okBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
            await clickResilient(page, okBtn);
            await page.waitForTimeout(500);
        }

        // assert - re-open editor and verify name survived reload
        await avatarEditBtn.waitFor({ state: 'visible', timeout: 10000 });
        await clickResilient(page, avatarEditBtn);
        await page.waitForTimeout(1500);
        await avatarModal.waitFor({ state: 'visible', timeout: 5000 });

        const reloadedName = await nameInput.inputValue();
        expect(reloadedName).toBe(persistName);
        await page.screenshot({ path: shot('avatar-persisted') });
    }, 60_000);
});
