/**
 * Reads the embedded UI string catalogs (`Strings.<subtag>.json`, `Messages.<subtag>.json`)
 * straight from the source tree and turns them into probes the localization smoke test
 * matches against the rendered DOM:
 * - `englishProbes` — text that must NOT appear while a non-English UI language is on
 * - `translatedValues` — text that proves the language actually took effect.
 *
 * The catalogs are JSONC (comments + trailing commas), so they need the tolerant reader below
 * rather than JSON.parse.
 */

import * as fs from 'fs';
import * as path from 'path';

export const RESOURCES_DIR = path.resolve(process.cwd(), 'src/dotnet/Localization/Resources');

// Languages.AllUI, in its declaration order.
export const UI_LANGUAGES: UILanguage[] = [
    { code: 'en-US', subtag: 'en', nativeName: 'English' },
    { code: 'es-ES', subtag: 'es', nativeName: 'Español' },
    { code: 'fr-FR', subtag: 'fr', nativeName: 'Français' },
    { code: 'it-IT', subtag: 'it', nativeName: 'Italiano' },
    { code: 'ru-RU', subtag: 'ru', nativeName: 'Русский' },
    { code: 'de-DE', subtag: 'de', nativeName: 'Deutsch' },
    { code: 'zh-CN', subtag: 'zh', nativeName: '中文' },
    { code: 'hi-IN', subtag: 'hi', nativeName: 'हिन्दी' },
    { code: 'ja-JP', subtag: 'ja', nativeName: '日本語' },
    { code: 'ko-KR', subtag: 'ko', nativeName: '한국어' },
    { code: 'pt-PT', subtag: 'pt', nativeName: 'Português' },
    { code: 'tr-TR', subtag: 'tr', nativeName: 'Türkçe' },
    { code: 'uk-UA', subtag: 'uk', nativeName: 'Українська' },
    { code: 'vi-VN', subtag: 'vi', nativeName: 'Tiếng Việt' },
    { code: 'pl-PL', subtag: 'pl', nativeName: 'Polski' },
    { code: 'id-ID', subtag: 'id', nativeName: 'Bahasa Indonesia' },
    { code: 'cs-CZ', subtag: 'cs', nativeName: 'Čeština' },
    { code: 'cs-CZ', subtag: 'cs', nativeName: 'Čeština' },
    { code: 'bg-BG', subtag: 'bg', nativeName: 'Български' },
    { code: 'bs-BA', subtag: 'bs', nativeName: 'Bosanski' },
    { code: 'hr-HR', subtag: 'hr', nativeName: 'Hrvatski' },
    { code: 'cnr-ME', subtag: 'cnr', nativeName: 'Crnogorski' },
    { code: 'sr-SR', subtag: 'sr', nativeName: 'Српски' },
];

export const ENGLISH = UI_LANGUAGES[0];

export interface UILanguage {
    code: string;
    subtag: string;
    nativeName: string;
}

export type Catalog = Record<string, string>;

/** An English string that must not survive a language switch, and the keys that produce it. */
export interface EnglishProbe {
    keys: string[];
    text: string;
    // Set for templates ("Delete {0} chats?"): matches the text with the arguments filled in.
    pattern?: RegExp;
}

// A value shorter than this is too likely to collide with user content ("Name", "Chat").
const MinProbeLength = 5;
// A template's literal parts must carry this much text before it can be matched as a pattern.
const MinTemplateLiteralLength = 10;

export function loadCatalog(kind: 'Strings' | 'Messages', subtag: string): Catalog {
    const file = path.join(RESOURCES_DIR, `${kind}.${subtag}.json`);
    return parseJsonc(fs.readFileSync(file, 'utf-8'), file);
}

// Strings + Messages of one language, merged the way AppStringLocalizer merges them.
export function loadStrings(subtag: string): Catalog {
    return { ...loadCatalog('Strings', subtag), ...loadCatalog('Messages', subtag) };
}

// English text that, seen while `language` is on, means something wasn't localized. A value is
// skipped when the translation repeats it verbatim (brand names, "OK", date patterns), or when it
// occurs anywhere in the target catalog: a language may borrow an English word, and a borrowed one
// is indistinguishable from an untranslated one.
export function getEnglishProbes(language: UILanguage): EnglishProbe[] {
    const en = loadStrings(ENGLISH.subtag);
    const translated = loadStrings(language.subtag);
    const translatedTexts = new Set(Object.values(translated).flatMap(splitPluralForms));
    const byText = new Map<string, EnglishProbe>();
    for (const [key, value] of Object.entries(en)) {
        if (translated[key] === value)
            continue;

        for (const form of splitPluralForms(value)) {
            const probe = toProbe(key, form);
            if (!probe || translatedTexts.has(form))
                continue;

            const existing = byText.get(probe.text);
            if (existing)
                existing.keys.push(key);
            else
                byText.set(probe.text, probe);
        }
    }
    return [...byText.values()];
}

// Translated text long enough to prove, when seen on screen, that the language is in effect.
export function getTranslatedValues(language: UILanguage): Set<string> {
    const en = loadStrings(ENGLISH.subtag);
    const translated = loadStrings(language.subtag);
    const result = new Set<string>();
    for (const [key, value] of Object.entries(translated)) {
        if (en[key] === value)
            continue;

        for (const form of splitPluralForms(value)) {
            const text = form.trim();
            // CJK says in two characters what Latin script needs five for.
            const minLength = /^[\x00-\x7F]*$/.test(text) ? MinProbeLength : 2;
            if (text.length >= minLength && !text.includes('{'))
                result.add(text);
        }
    }
    return result;
}

// Matches the scanned texts against the probes: exact values first, templates by pattern.
export function findProbeMatches(probes: EnglishProbe[], texts: string[]): Map<string, EnglishProbe> {
    const exact = new Map<string, EnglishProbe>();
    const patterns: EnglishProbe[] = [];
    for (const probe of probes) {
        if (probe.pattern)
            patterns.push(probe);
        else
            exact.set(probe.text, probe);
    }

    const result = new Map<string, EnglishProbe>();
    for (const text of texts) {
        const hit = exact.get(text) ?? patterns.find(p => p.pattern!.test(text));
        if (hit)
            result.set(text, hit);
    }
    return result;
}

// Private methods

function toProbe(key: string, value: string): EnglishProbe | null {
    const text = value.trim();
    if (text.length < MinProbeLength)
        return null;

    if (!text.includes('{'))
        return { keys: [key], text };

    // A template is matched by its literal parts; too little literal text and it would
    // match arbitrary user content ("{0} members" vs. a chat actually named that).
    const literals = text.split(/\{[^}]*\}/g);
    const literalLength = literals.reduce((sum, x) => sum + x.trim().length, 0);
    if (literalLength < MinTemplateLiteralLength || !literals.some(x => x.trim().length >= MinProbeLength))
        return null;

    const source = literals.map(escapeRegex).join('(.+?)');
    return { keys: [key], text, pattern: new RegExp(`^${source}$`) };
}

// The plural forms of one key are listed in a single value, separated by '|' — each is
// a text the UI can render on its own.
function splitPluralForms(value: string): string[] {
    return value.includes('|') ? value.split('|') : [value];
}

function escapeRegex(value: string): string {
    return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function parseJsonc(content: string, file: string): Catalog {
    let result = '';
    let isInString = false;
    for (let i = 0; i < content.length; i++) {
        const c = content[i];
        if (isInString) {
            result += c;
            if (c === '\\') {
                result += content[++i];
                continue;
            }
            if (c === '"')
                isInString = false;
            continue;
        }
        if (c === '"') {
            isInString = true;
            result += c;
            continue;
        }
        if (c === '/' && content[i + 1] === '/') {
            while (i < content.length && content[i] !== '\n')
                i++;
            result += '\n';
            continue;
        }
        if (c === '/' && content[i + 1] === '*') {
            const end = content.indexOf('*/', i + 2);
            i = end < 0 ? content.length : end + 1;
            continue;
        }
        result += c;
    }
    try {
        return JSON.parse(result.replace(/,(\s*[}\]])/g, '$1')) as Catalog;
    } catch (e) {
        throw new Error(`Failed to parse ${file}: ${e instanceof Error ? e.message : String(e)}`);
    }
}
