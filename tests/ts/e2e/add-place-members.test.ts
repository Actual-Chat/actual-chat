/**
 * E2E regression for #3864: clicking "Add members" on a Place throws
 * `System.ArgumentNullException: (Parameter 'jsObjectReference')` from
 * CopyTrigger.OnAfterRenderAsync — a race where AddMemberModalPage's
 * OnInitializedAsync re-renders the page before CopyTrigger's first-render
 * JS interop has assigned `_jsRef`.
 *
 * Run:
 *   ./node_modules/.bin/vitest run tests/ts/e2e/add-place-members.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Page, ConsoleMessage } from 'playwright';
import {
    BASE_URL, connectBrowser, ensureSignedIn, skipOnboarding,
    screenshot, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('add-place-members', name);

// The exact failure mode we are guarding against. Keep both anchors so we
// don't accidentally match unrelated `jsObjectReference` mentions or
// unrelated CopyTrigger render frames.
const COPY_TRIGGER_NRE = 'CopyTrigger.OnAfterRenderAsync';
const JS_REF_NULL = /Parameter 'jsObjectReference'|ArgumentNullException.*jsObjectReference/;

interface CapturedError {
    kind: 'pageerror' | 'console';
    text: string;
}

function attachErrorListeners(page: Page): CapturedError[] {
    const errors: CapturedError[] = [];
    page.on('pageerror', err => {
        errors.push({ kind: 'pageerror', text: err.stack ?? err.message });
    });
    page.on('console', (msg: ConsoleMessage) => {
        if (msg.type() !== 'error') return;
        // Blazor serializes server-side exceptions through console.error;
        // the message text contains the full stack from the server.
        const text = msg.text();
        errors.push({ kind: 'console', text });
    });
    return errors;
}

async function ensurePlaceExists(page: Page, title: string): Promise<void> {
    // `.place-plus-btn` only renders for the currently-selected Place. Wait
    // generously (Blazor SPA boot + Fusion's "restore last selected place"
    // round-trip can easily exceed 5 s on a fresh tab); only fall through to
    // creation when no place is genuinely available.
    if (await page.locator('.place-plus-btn').first().isVisible({ timeout: 10_000 }).catch(() => false))
        return;
    const existingPlace = page.locator('.navbar-place-buttons .navbar-button').first();
    if (await existingPlace.isVisible({ timeout: 2_000 }).catch(() => false)) {
        await existingPlace.click({ force: true });
        await page.locator('.place-plus-btn').first()
            .waitFor({ state: 'visible', timeout: 15_000 });
        return;
    }

    // Open the navbar "+" → CreateMenu. The menu-host listens for
    // document-level click events with a matching `data-menu` ancestor;
    // dispatch a synthetic click on the inner <button> so we don't depend on
    // pixel-precise hit-testing (the wrapper div + onboarding overlay can
    // both intercept Playwright's real click).
    await page.locator('[data-menu*="CreateMenu"] button').first()
        .waitFor({ state: 'visible', timeout: 15_000 });
    await page.evaluate(() => {
        const btn = document.querySelector<HTMLButtonElement>('[data-menu*="CreateMenu"] button');
        btn?.click();
    });

    const newPlaceEntry = page.locator('li.ac-menu-item:has-text("New Place")').first();
    await newPlaceEntry.waitFor({ state: 'visible', timeout: 10_000 });
    await newPlaceEntry.click();

    const modal = page.locator('.new-place-modal');
    await modal.waitFor({ state: 'visible', timeout: 10_000 });

    const titleInput = modal.locator('input[id*="title" i]').first();
    await titleInput.waitFor({ state: 'visible', timeout: 5_000 });
    await titleInput.click();
    // pressSequentially: TextBox attaches its debounced input listener in
    // OnAfterRenderAsync, so fill() can land before the listener exists.
    await titleInput.pressSequentially(title, { delay: 30 });
    await titleInput.press('Tab');

    const createBtn = modal.locator('button:has-text("Create")').first();
    await createBtn.waitFor({ state: 'visible', timeout: 5_000 });
    await createBtn.click();

    // Place creation auto-navigates to the welcome chat and shows
    // `.place-plus-btn` in LeftPanelPlaceContentHeader. Wait for that landmark
    // instead of the modal disappearing — the modal may close while the
    // navigation is still in-flight, and clicking too early misses the place.
    await page.locator('.place-plus-btn').first()
        .waitFor({ state: 'visible', timeout: 30_000 });
}

async function openPlaceAndAddMembers(page: Page): Promise<void> {
    await skipOnboarding(page);

    // Two entry points open AddMemberModal for a Place:
    //  - LeftPanelPlaceContentHeader's `.add-members-btn` (visible when the
    //    place has <2 members — our freshly-created case).
    //  - PlaceMenuButton's `.place-plus-btn` → "Add members" menu entry.
    // Prefer the direct button when present; fall back to the menu.
    //
    // Both are Blazor `@onclick` divs / menu-host triggers — neither has a
    // hit-target surface that Playwright's real click reliably lands on
    // (the div bounding box overlaps siblings, and the menu-host resolves
    // `data-menu` against the click's bubbled target). Dispatch a synthetic
    // click via evaluate so the handler fires deterministically.
    const directVisible = await page.locator('.add-members-btn').first()
        .isVisible({ timeout: 3_000 }).catch(() => false);
    if (directVisible) {
        await page.evaluate(() => {
            document.querySelector<HTMLElement>('.add-members-btn')?.click();
        });
        return;
    }

    await page.locator('.place-plus-btn button, .place-plus-btn').first()
        .waitFor({ state: 'visible', timeout: 10_000 });
    await page.evaluate(() => {
        const btn = document.querySelector<HTMLButtonElement>('.place-plus-btn button')
            ?? document.querySelector<HTMLElement>('.place-plus-btn');
        btn?.click();
    });

    const addMembersEntry = page.locator(
        'li.ac-menu-item:has-text("Add members"), li.ac-menu-item:has-text("Invite members")',
    ).first();
    await addMembersEntry.waitFor({ state: 'visible', timeout: 10_000 });
    await addMembersEntry.click();
}

describe('Add members to a Place — CopyTrigger jsObjectReference NRE (#3864)', () => {
    let conn: BrowserConnection;
    let page: Page;
    let errors: CapturedError[];

    beforeAll(async () => {
        conn = await connectBrowser();
        page = await conn.context.newPage();
        errors = attachErrorListeners(page);

        await ensureSignedIn(page);
        await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
        await skipOnboarding(page);

        await ensurePlaceExists(page, `E2E #3864 ${Date.now().toString(36)}`);
        await skipOnboarding(page);
    }, 120_000);

    afterAll(async () => {
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition -- page may be unset if beforeAll fails
        if (page) await page.close();
        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    });

    it('opens AddMember modal without throwing CopyTrigger NRE', async () => {
        // arrange — discard anything captured during sign-in / place creation;
        // we only care about errors triggered by the Add Members flow.
        errors.length = 0;

        // act
        await openPlaceAndAddMembers(page);

        // Wait until the modal's body is on screen. The CopyTrigger for the
        // place invite link sits inside the `.share-block` block; if it
        // renders at all, the buggy OnAfterRenderAsync path has run.
        const modalBody = page.locator('.add-member-modal-content, .add-member-modal').first();
        await modalBody.waitFor({ state: 'visible', timeout: 15_000 });

        // Give Blazor enough time to (a) complete OnInitializedAsync's
        // server round-trip, which re-renders AddMemberModalPage, and
        // (b) for OnAfterRenderAsync(firstRender: false) on CopyTrigger
        // to fire and throw. ~2s is generous on local infra.
        await page.waitForTimeout(2_500);
        await page.screenshot({ path: shot('after-open') });

        // assert
        const matches = errors.filter(e =>
            e.text.includes(COPY_TRIGGER_NRE) && JS_REF_NULL.test(e.text));

        if (matches.length > 0) {
            const preview = matches.slice(0, 2)
                .map((e, i) => `[${i}] (${e.kind}) ${e.text.substring(0, 600)}`)
                .join('\n---\n');
            throw new Error(
                `CopyTrigger.OnAfterRenderAsync threw ArgumentNullException for ` +
                `jsObjectReference (#3864). Captured ${matches.length} matching ` +
                `error(s):\n${preview}`,
            );
        }

        expect(matches.length).toBe(0);
    }, 60_000);
});
