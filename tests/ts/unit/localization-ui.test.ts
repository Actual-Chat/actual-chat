import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('logging', () => ({ getLogs: () => ({}) }));

const values = new Map<string, string>();
const storage = {
    getItem: vi.fn((key: string) => values.get(key) ?? null),
    setItem: vi.fn((key: string, value: string) => values.set(key, value)),
    removeItem: vi.fn((key: string) => values.delete(key)),
};

beforeEach(() => {
    vi.resetModules();
    vi.clearAllMocks();
    values.clear();
    values.set('ui.language', 'ru');
    vi.stubGlobal('window', { localStorage: storage });
    vi.stubGlobal('location', { search: '' });
    vi.stubGlobal('navigator', { languages: ['ru-RU'] });
    vi.stubGlobal('document', { documentElement: { lang: '' } });
});

afterEach(() => vi.unstubAllGlobals());

describe('UI language persistence', () => {
    it.each(['de', 'ru'])('applies %s without persisting an override or fallback', async language => {
        // arrange
        const { LocalizationUI } = await import('../../../src/nodejs/src/localization-ui');

        // act
        LocalizationUI.setDocumentLanguage(language, false, language);

        // assert
        expect(document.documentElement.lang).toBe(language);
        expect(values.get('ui.language')).toBe('ru');
        expect(storage.setItem).not.toHaveBeenCalled();
        expect(storage.removeItem).not.toHaveBeenCalled();
    });

    it('persists the account selection for the next launch', async () => {
        // arrange
        const { LocalizationUI } = await import('../../../src/nodejs/src/localization-ui');

        // act
        LocalizationUI.setDocumentLanguage('en', true, 'en');
        LocalizationUI.init();

        // assert
        expect(document.documentElement.lang).toBe('en');
        expect(values.get('ui.language')).toBe('en');
        expect(LocalizationUI.languageInfo?.selected).toBe('en');
    });

    it('clears the selection for automatic language without persisting the detected language', async () => {
        // arrange
        const { LocalizationUI } = await import('../../../src/nodejs/src/localization-ui');

        // act
        LocalizationUI.setDocumentLanguage('ru', true, null);
        LocalizationUI.init();

        // assert
        expect(document.documentElement.lang).toBe('ru');
        expect(values.has('ui.language')).toBe(false);
        expect(LocalizationUI.languageInfo?.selected).toBeNull();
    });
});
