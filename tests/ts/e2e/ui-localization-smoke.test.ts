/**
 * E2E smoke test: with each supported UI language selected, walk a tour of the app and
 * assert that nothing on the way is still rendered in English.
 *
 * Detection is catalog-driven: any visible text — or title/aria-label/placeholder/data-tooltip —
 * that equals an English catalog value whose translation differs is an unlocalized string,
 * reported with the key that produced it. The reverse check runs too: each screen must show some
 * translated text, so a language that failed to apply fails the test instead of passing vacuously.
 *
 * Run (all 13 non-English languages, ~30 s each):
 *   npx vitest run tests/ts/e2e/ui-localization-smoke.test.ts --config vitest.config.e2e.ts
 * Run for a subset:
 *   AC_E2E_LANGUAGES=ru,ja npx vitest run tests/ts/e2e/ui-localization-smoke.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Page } from 'playwright';
import {
    BASE_URL, connectBrowser, ensureSignedIn, screenshot, setUILanguage, skipOnboarding,
    waitForAppReady, type BrowserConnection,
} from './helpers';
import {
    ENGLISH, UI_LANGUAGES, findProbeMatches, getEnglishProbes, getTranslatedValues,
    type EnglishProbe, type UILanguage,
} from './localization-catalog';

const shot = (name: string) => screenshot('e2e-l10n', name);

// A screen showing fewer localized strings than this is a screen the language didn't reach.
const MinTranslatedPerScreen = 3;
// This many unreachable screens in a row means the page is wedged, not that one screen is missing.
const MaxEmptySteps = 2;
const NavigationTimeout = 20_000;

// Subtrees the scan skips: content rather than UI. The legal documents are written in English
// and shipped as markup, so the words they share with the catalog say nothing about localization.
const IgnoredContainers = ['.docs-content'];

// Keys whose English text legitimately shows up in a localized UI, and why. A text is excused
// only when every key that produces it is listed here.
const KnownEnglish: Record<string, string> = {
    Navbar_Notes: 'the built-in Notes chat is created server-side with an English title the user can rename',
};

interface TourStep {
    name: string;
    run: (page: Page) => Promise<void>;
    // A screen that depends on account state this test doesn't create - a chat with members,
    // a Place. Unreachable is a skip, not a failure; what it renders is still scanned.
    isOptional?: boolean;
}

interface ScannedText {
    text: string;
    where: string;
}

interface Finding {
    text: string;
    keys: string[];
    screens: string[];
    where: string;
}

interface TourResult {
    findings: Map<string, Finding>;
    problems: string[];
    isComplete: boolean;
}

const TOUR: TourStep[] = [
    { name: 'chat-list', run: page => goto(page, '/chat') },
    { name: 'chat-list-sort-menu', run: page => openMenu(page, '.chat-list-sort-btn') },
    { name: 'create-menu', run: page => openMenu(page, '.chat-list-fab') },
    {
        name: 'chat-view',
        // The last chat rather than the first: Notes is pinned first and has no members, so the
        // right panel would open on its Threads tab and never render a member list.
        run: async page => {
            await closePopups(page);
            await page.locator('.chat-list .navbar-item-content[data-href^="/chat/"]').last()
                .click({ force: true, timeout: 15_000 });
            await page.locator('.chat-view, .chat-message-editor').first()
                .waitFor({ state: 'visible', timeout: 20_000 });
        },
    },
    { name: 'message-editor-menu', run: page => openMenu(page, '.chat-message-editor .attach-btn') },
    { name: 'chat-menu', run: page => openMenu(page, '.chat-list .navbar-item[data-menu]', 'right') },
    {
        name: 'chat-side-panel',
        run: async page => {
            await closePopups(page);
            // The panel's open state is remembered per user, and its toggle is rendered only
            // while the panel is closed — so open it just when it isn't already open.
            const panel = page.locator('.chat-side-panel').first();
            if (!await panel.isVisible().catch(() => false))
                await click(page, '.chat-header-control-panel button:has(i.icon-layout)');

            await panel.waitFor({ state: 'visible', timeout: 10_000 });
            // Members is where presence text renders. Only a chat that has members offers the
            // tab, so this is best-effort — which chat the list opens on isn't ours to pick.
            const members = panel.locator('[data-tab-id="members"]').first();
            if (await members.isVisible().catch(() => false)) {
                await members.click({ force: true });
                await page.waitForTimeout(500);
            }
        },
    },
    // The panel's own tabs: each is a screen of its own, and which of them a chat offers
    // depends on what has been posted in it.
    ...['media', 'files', 'links'].map(tab => ({
        name: `chat-side-panel/${tab}`,
        run: (page: Page) => openSidePanelTab(page, tab),
        isOptional: true,
    })),
    // The settings tabs are reached by clicking, not by a /settings/<tab> navigation each: nine
    // more full page loads is nine more chances for one to hang, and clicking is what a user does.
    { name: 'settings', run: page => openSettings(page) },
    ...['account', 'userInterface', 'transcription', 'app', 'sessions', 'apiKeys', 'privacy', 'documents', 'devTools']
        .map(tab => ({ name: `settings/${tab}`, run: (page: Page) => openSettingsTab(page, tab) })),
    // Screens off the main flow. Each of these re-enters from /chat rather than continuing
    // where the previous step left off, so one that fails to open can't cascade into the next.
    { name: 'search-panel', run: page => openSearch(page) },
    { name: 'search-filter-menu', run: page => openSearchFilterMenu(page) },
    { name: 'search-filter-badges', run: page => applySearchFilters(page) },
    { name: 'mention-list', run: page => openMentionList(page), isOptional: true },
    { name: 'chat-unavailable', run: page => goto(page, `/chat/${UnknownChatSid}`) },
];

// A well-formed group-chat id that no chat has, so the page renders its unavailable state
// instead of 404ing out of the app.
const UnknownChatSid = 's-0000000000-0000000000';

describe('UI localization smoke', () => {
    let conn: BrowserConnection;
    let originalLanguage = ENGLISH.code;

    beforeAll(async () => {
        conn = await connectBrowser();
        const page = await conn.context.newPage();
        await ensureSignedIn(page);

        // This test explicitly reloads after changing the language. Use server render mode because
        // reloading under WASM re-boots MONO against whatever the service worker cached, which
        // double-faults after a server rebuild.
        await goto(page, '/fusion/renderMode/s');
        await page.close();
    }, 180_000);

    afterAll(async () => {
        const page = await conn.context.newPage();
        try {
            await setUILanguage(page, originalLanguage);
        } catch (e) {
            console.log('Failed to restore UI language:', e instanceof Error ? e.message : String(e));
        }
        await page.close();
        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    }, 120_000);

    for (const language of getLanguagesUnderTest()) {
        it(`renders every screen in ${language.nativeName} (${language.code})`, async () => {
            // arrange
            const probes = getEnglishProbes(language);
            const translatedValues = getTranslatedValues(language);

            // act
            // A page can wedge — one navigation hangs and every later one on that page times out
            // too. It is a browser-side state (the service worker is the prime suspect), so a
            // fresh page recovers; the tour restarts on one.
            let tour: TourResult | undefined;
            let lastError: unknown;
            for (let attempt = 0; attempt < 2 && !tour?.isComplete; attempt++) {
                if (attempt > 0)
                    console.log(`[${language.code}] the page stopped navigating — retrying on a fresh one`);

                const page = await conn.context.newPage();
                page.setDefaultNavigationTimeout(NavigationTimeout);
                try {
                    const previous = await setUILanguage(page, language.code);
                    if (originalLanguage === ENGLISH.code)
                        originalLanguage = previous;

                    tour = await runTour(page, language, probes, translatedValues);
                } catch (e) {
                    lastError = e;
                    console.log(`[${language.code}] tour failed: ${e instanceof Error ? e.message : String(e)}`);
                } finally {
                    await page.close().catch(() => { /* ignore */ });
                }
            }
            if (!tour)
                throw lastError;

            // assert
            const problems = [...tour.problems, ...describeFindings(tour.findings).map(x => `[english] ${x}`)];
            expect(problems, `${language.code}: screens the language did not reach, and English text on those it did`)
                .toEqual([]);
        }, 420_000);
    }
});

// Private methods

async function runTour(
    page: Page,
    language: UILanguage,
    probes: EnglishProbe[],
    translatedValues: Set<string>
): Promise<TourResult> {
    const findings = new Map<string, Finding>();
    const problems: string[] = [];
    let emptyStepCount = 0;
    for (const step of TOUR) {
        const texts = await runStep(page, step);
        if (!texts.length) {
            if (!step.isOptional)
                problems.push(`[unreached] ${step.name} (nothing visible)`);
            if (++emptyStepCount >= MaxEmptySteps)
                return { findings, problems, isComplete: false };

            continue;
        }

        emptyStepCount = 0;
        const matches = findProbeMatches(probes, texts.map(x => x.text));
        for (const scanned of texts) {
            const probe = matches.get(scanned.text);
            if (probe)
                addFinding(findings, probe, scanned, step.name);
        }
        const translatedCount = texts.filter(x => translatedValues.has(x.text)).length;
        if (translatedCount < MinTranslatedPerScreen && !step.isOptional)
            problems.push(`[unreached] ${step.name} (${translatedCount} localized strings)`);
    }
    await page.screenshot({ path: shot(`${language.subtag}-final`) }).catch(() => { /* ignore */ });
    return { findings, problems, isComplete: true };
}

function getLanguagesUnderTest(): UILanguage[] {
    const filter = process.env.AC_E2E_LANGUAGES?.trim();
    const languages = UI_LANGUAGES.filter(x => x !== ENGLISH);
    if (!filter || filter.toLowerCase() === 'all')
        return languages;

    const wanted = filter.split(',').map(x => x.trim().toLowerCase()).filter(x => x.length > 0);
    const selected = languages.filter(x => wanted.includes(x.subtag) || wanted.includes(x.code.toLowerCase()));
    if (!selected.length)
        throw new Error(`AC_E2E_LANGUAGES="${filter}" matches no supported UI language`);

    return selected;
}

async function runStep(page: Page, step: TourStep): Promise<ScannedText[]> {
    try {
        await step.run(page);
    } catch (e) {
        console.log(`Step "${step.name}" failed: ${e instanceof Error ? e.message : String(e)}`);
        return [];
    }
    await page.waitForTimeout(500);
    return scanVisibleText(page);
}

async function goto(page: Page, route: string) {
    // Both failures a retry fixes: ERR_ABORTED from overlapping browser navigation, and the
    // occasional load that just hangs.
    let lastError: unknown;
    for (let attempt = 0; attempt < 2; attempt++) {
        const isNavigated = await page.goto(`${BASE_URL}${route}`, { waitUntil: 'domcontentloaded' })
            .then(() => true, (e: unknown) => {
                lastError = e;
                return false;
            });
        if (isNavigated) {
            await waitForAppReady(page);
            await skipOnboarding(page);
            return;
        }
        await page.waitForTimeout(1_000);
    }
    throw lastError;
}

async function openSettings(page: Page) {
    await closePopups(page);
    await goto(page, '/settings');
    await page.locator('.settings-modal').first().waitFor({ state: 'visible', timeout: 15_000 });
}

async function openSidePanelTab(page: Page, tabId: string) {
    await click(page, `.chat-side-panel [data-tab-id="${tabId}"]`);
    await page.waitForTimeout(500);
}

async function openSearch(page: Page) {
    await goto(page, '/chat');
    await click(page, '.left-chat-search-input input');
    // Typed text is what keeps the panel open: an empty input closes it again on blur,
    // and every later search step needs it to stay open.
    await page.keyboard.type('a');
    await page.locator('.left-chat-search-input.open').first().waitFor({ state: 'visible', timeout: 10_000 });
}

async function openSearchFilterMenu(page: Page) {
    await click(page, '.left-chat-search-input .c-filter-btn');
    await page.locator('.search-filter-menu').first().waitFor({ state: 'visible', timeout: 10_000 });
}

// The badges render the filters the menu sets, and they are a separate set of strings from
// the menu's own - narrower ones that have to fit a chip.
async function applySearchFilters(page: Page) {
    await pickFilter(page, 0);
    await openSearchFilterMenu(page);
    await pickFilter(page, 1);
    await page.locator('.search-filter-badge').first().waitFor({ state: 'visible', timeout: 10_000 });
}

async function pickFilter(page: Page, sectionIndex: number) {
    // The second option of each section: the first is the "no filter" default, which
    // renders no badge.
    await page.locator('.search-filter-menu .c-section').nth(sectionIndex)
        .locator('.c-option').nth(1).click({ force: true, timeout: 10_000 });
    await page.waitForTimeout(500);
}

async function openMentionList(page: Page) {
    await goto(page, '/chat');
    await page.locator('.chat-list .navbar-item-content[data-href^="/chat/"]').last()
        .click({ force: true, timeout: 15_000 });
    await page.locator('.chat-message-editor').first().waitFor({ state: 'visible', timeout: 20_000 });
    await page.locator('.chat-message-editor [contenteditable]').first().click({ force: true });
    await page.keyboard.type('@');
    await page.locator('.mention-list').first().waitFor({ state: 'visible', timeout: 10_000 });
}

async function openSettingsTab(page: Page, tabId: string) {
    await click(page, `.settings-modal button[data-tab-id="${tabId}"]`);
    await page.locator('.settings-tab-content').first().waitFor({ state: 'visible', timeout: 15_000 });
}

async function click(page: Page, selector: string, button: 'left' | 'right' = 'left') {
    const target = page.locator(selector).first();
    await target.waitFor({ state: 'visible', timeout: 15_000 });
    await target.click({ force: true, button });
}

async function openMenu(page: Page, selector: string, button: 'left' | 'right' = 'left') {
    await closePopups(page);
    await click(page, selector, button);
    await page.locator('.ac-menu-host .ac-menu').first().waitFor({ state: 'visible', timeout: 10_000 });
}

async function closePopups(page: Page) {
    for (let attempt = 0; attempt < 3; attempt++) {
        const popup = page.locator('.ac-menu-host .ac-menu').first();
        if (!await popup.isVisible().catch(() => false))
            return;

        await page.keyboard.press('Escape');
        await page.waitForTimeout(300);
    }
}

function addFinding(findings: Map<string, Finding>, probe: EnglishProbe, scanned: ScannedText, screen: string) {
    const existing = findings.get(scanned.text);
    if (!existing) {
        findings.set(scanned.text, { text: scanned.text, keys: probe.keys, screens: [screen], where: scanned.where });
        return;
    }

    if (!existing.screens.includes(screen))
        existing.screens.push(screen);
}

function describeFindings(findings: Map<string, Finding>): string[] {
    return [...findings.values()]
        .filter(x => !x.keys.every(key => key in KnownEnglish))
        .map(x => `"${x.text}" [${x.keys.join(', ')}] at ${x.where} on ${x.screens.join(', ')}`);
}

// Every visible text node and localizable attribute, each with a short description of where it sits.
async function scanVisibleText(page: Page): Promise<ScannedText[]> {
    return page.evaluate((ignoredContainers: string[]) => {
        const results: { text: string; where: string }[] = [];
        const seen = new Set<string>();
        const describe = (el: Element): string => {
            const cls = (el.getAttribute('class') ?? '').split(' ').find(x => x.length > 0);
            const tag = el.tagName.toLowerCase();
            return cls ? `${tag}.${cls}` : tag;
        };
        const add = (raw: string | null, el: Element) => {
            const text = (raw ?? '').replace(/\s+/g, ' ').trim();
            if (!text)
                return;

            const where = describe(el);
            const id = `${text} ${where}`;
            if (seen.has(id))
                return;

            seen.add(id);
            results.push({ text, where });
        };
        const isScanned = (el: Element) => el.getClientRects().length > 0
            && getComputedStyle(el).visibility !== 'hidden'
            && !ignoredContainers.some(x => el.closest(x));

        const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
        for (let node = walker.nextNode(); node; node = walker.nextNode()) {
            const el = node.parentElement;
            if (!el || ['SCRIPT', 'STYLE', 'NOSCRIPT', 'TITLE'].includes(el.tagName) || !isScanned(el))
                continue;

            add(node.nodeValue, el);
        }
        const attributed = '[title], [aria-label], [placeholder], [alt], [data-tooltip]';
        for (const el of document.body.querySelectorAll(attributed)) {
            if (!isScanned(el))
                continue;

            for (const attr of ['title', 'aria-label', 'placeholder', 'alt', 'data-tooltip'])
                add(el.getAttribute(attr), el);
        }
        return results;
    }, IgnoredContainers);
}
