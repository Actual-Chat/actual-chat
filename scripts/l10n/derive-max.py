#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Derive the hidden Max UI catalog from every shipped localization.

For each ordinary resource, Max keeps the visually widest translation. Plural
forms compete independently, so the generated value has one stable form for
every count. Date resources are copied as a coherent set from the locale with
the widest month and day names.

Widths use the checked-in TT Commons Pro glyph advances. Characters absent
from that font use deterministic Unicode-aware estimates, keeping generation
portable while accounting for wide CJK glyphs and zero-width combining marks.

Usage:
    scripts/derive-max.cmd              # rewrite the two Max catalogs
    scripts/derive-max.cmd --check      # verify them, write nothing
"""
import argparse
import glob
import html
import json
import os
import re
import struct
import sys
import unicodedata

KINDS = ("Strings", "Messages")
MAX_SUBTAG = "max"
RESOURCES = os.path.join("src", "dotnet", "Localization", "Resources")
FONT = os.path.join("src", "nodejs", "fonts", "TT-Commons-Pro-Regular.ttf")
DATE_PREFIX = "Date_"
DATE_NAME_KEYS = (
    "Date_MonthNames",
    "Date_MonthGenitiveNames",
    "Date_ShortMonthNames",
    "Date_DayNames",
    "Date_ShortDayNames",
)

KEY_LINE = re.compile(r'^(\s*)"([A-Za-z0-9_]+)"(\s*:\s*)(".*?")(,?)\s*$')
PLACEHOLDER = re.compile(r"\{[^{}]+\}")
TAG = re.compile(r"<[^>]*>")
BREAK_TAG = re.compile(r"<\s*br\s*/?\s*>", re.IGNORECASE)
WORD_BREAK = re.compile(r"[\s\u00a0\-/\u2010-\u2015]+")


def u16(data, offset):
    return struct.unpack_from(">H", data, offset)[0]


def i16(data, offset):
    return struct.unpack_from(">h", data, offset)[0]


def u32(data, offset):
    return struct.unpack_from(">I", data, offset)[0]


class FontMetrics:
    def __init__(self, path):
        with open(path, "rb") as file:
            self.data = file.read()
        self.tables = self._read_tables()
        self.units_per_em = u16(self.data, self.tables["head"] + 18)
        self.advances = self._read_advances()
        self.cmaps = self._read_cmaps()

    def width(self, text):
        return sum(self._character_width(c) for c in text)

    def _read_tables(self):
        result = {}
        for i in range(u16(self.data, 4)):
            record = 12 + i * 16
            tag = self.data[record:record + 4].decode("ascii")
            result[tag] = u32(self.data, record + 8)
        for required in ("cmap", "head", "hhea", "hmtx"):
            if required not in result:
                raise ValueError("Font has no %s table" % required)
        return result

    def _read_advances(self):
        count = u16(self.data, self.tables["hhea"] + 34)
        hmtx = self.tables["hmtx"]
        return [u16(self.data, hmtx + i * 4) for i in range(count)]

    def _read_cmaps(self):
        cmap = self.tables["cmap"]
        records = []
        for i in range(u16(self.data, cmap + 2)):
            record = cmap + 4 + i * 8
            platform = u16(self.data, record)
            encoding = u16(self.data, record + 2)
            offset = cmap + u32(self.data, record + 4)
            fmt = u16(self.data, offset)
            if fmt in (4, 12):
                priority = (
                    fmt == 12,
                    platform == 3 and encoding == 10,
                    platform == 0,
                    platform == 3 and encoding == 1,
                )
                records.append((priority, fmt, offset))
        if not records:
            raise ValueError("Font has no supported cmap")
        return [(fmt, offset) for _, fmt, offset in sorted(records, reverse=True)]

    def _character_width(self, character):
        if character in "\r\n":
            return 0
        if unicodedata.combining(character) or unicodedata.category(character) in ("Mn", "Me", "Cf"):
            return 0

        glyph = self._glyph_id(ord(character))
        if glyph:
            index = min(glyph, len(self.advances) - 1)
            return self.advances[index]
        return self._fallback_width(character)

    def _glyph_id(self, codepoint):
        for fmt, offset in self.cmaps:
            glyph = self._glyph_id_12(offset, codepoint) if fmt == 12 else self._glyph_id_4(offset, codepoint)
            if glyph:
                return glyph
        return 0

    def _glyph_id_12(self, offset, codepoint):
        low = 0
        high = u32(self.data, offset + 12) - 1
        groups = offset + 16
        while low <= high:
            middle = (low + high) // 2
            group = groups + middle * 12
            start = u32(self.data, group)
            end = u32(self.data, group + 4)
            if codepoint < start:
                high = middle - 1
            elif codepoint > end:
                low = middle + 1
            else:
                return u32(self.data, group + 8) + codepoint - start
        return 0

    def _glyph_id_4(self, offset, codepoint):
        if codepoint > 0xFFFF:
            return 0
        segment_count = u16(self.data, offset + 6) // 2
        end_codes = offset + 14
        start_codes = end_codes + segment_count * 2 + 2
        deltas = start_codes + segment_count * 2
        range_offsets = deltas + segment_count * 2
        for i in range(segment_count):
            end = u16(self.data, end_codes + i * 2)
            if codepoint > end:
                continue
            start = u16(self.data, start_codes + i * 2)
            if codepoint < start:
                return 0
            delta = i16(self.data, deltas + i * 2)
            range_offset_address = range_offsets + i * 2
            range_offset = u16(self.data, range_offset_address)
            if range_offset == 0:
                return (codepoint + delta) & 0xFFFF
            glyph_address = range_offset_address + range_offset + (codepoint - start) * 2
            glyph = u16(self.data, glyph_address)
            return (glyph + delta) & 0xFFFF if glyph else 0
        return 0

    def _fallback_width(self, character):
        category = unicodedata.category(character)
        if character.isspace():
            ratio = 0.25
        elif unicodedata.east_asian_width(character) in ("W", "F"):
            ratio = 1.0
        elif category.startswith("P"):
            ratio = 0.4
        elif category.startswith("N"):
            ratio = 0.55
        elif category.startswith("S"):
            ratio = 1.0
        else:
            ratio = 0.6
        return round(self.units_per_em * ratio)


def read_text(path):
    with open(path, "rb") as file:
        raw = file.read()
    text = raw.decode("utf-8-sig")
    return raw.startswith(b"\xef\xbb\xbf"), "\r\n" if "\r\n" in text else "\n", text


def load_catalog(path):
    _, _, text = read_text(path)
    json_text = "\n".join(line for line in text.splitlines() if not line.lstrip().startswith("//"))
    return json.loads(json_text)


def load_catalogs(resources, kind):
    result = {}
    pattern = os.path.join(resources, "%s.*.json" % kind)
    for path in sorted(glob.glob(pattern)):
        subtag = os.path.basename(path)[len(kind) + 1:-5]
        if subtag != MAX_SUBTAG:
            result[subtag] = load_catalog(path)
    if "en" not in result:
        raise ValueError("No %s.en.json catalog" % kind)
    return result


def visible_text(value):
    value = BREAK_TAG.sub("\n", value)
    value = TAG.sub("", value)
    value = PLACEHOLDER.sub("", value)
    return html.unescape(value)


def width_score(metrics, value):
    text = visible_text(value)
    line_width = max((metrics.width(line) for line in text.splitlines()), default=0)
    word_width = max((metrics.width(word) for word in WORD_BREAK.split(text)), default=0)
    return line_width, word_width


def select_widest(metrics, candidates):
    best_value = ""
    best_score = (-1, -1)
    for value in candidates:
        score = width_score(metrics, value)
        if score > best_score:
            best_value = value
            best_score = score
    return best_value


def select_date_source(metrics, catalogs):
    best_subtag = ""
    best_score = -1
    for subtag, catalog in catalogs.items():
        score = 0
        for key in DATE_NAME_KEYS:
            score += max(metrics.width(value) for value in catalog[key].split("|"))
        if score > best_score:
            best_subtag = subtag
            best_score = score
    return best_subtag


def validate_catalogs(kind, catalogs):
    english_keys = set(catalogs["en"])
    for subtag, catalog in catalogs.items():
        keys = set(catalog)
        if keys != english_keys:
            missing = sorted(english_keys - keys)
            extra = sorted(keys - english_keys)
            raise ValueError(
                "%s.%s.json key mismatch; missing=%s extra=%s" % (kind, subtag, missing, extra))


def derive_values(metrics, catalogs, date_source):
    result = {}
    for key in catalogs["en"]:
        if key.startswith(DATE_PREFIX):
            result[key] = catalogs[date_source][key]
            continue

        values = [catalog[key] for catalog in catalogs.values()]
        candidates = [form for value in values for form in value.split("|")]
        result[key] = select_widest(metrics, candidates)
    return result


def render(base_path, values):
    bom, newline, text = read_text(base_path)
    output = []
    for line in text.split(newline):
        match = KEY_LINE.match(line)
        if not match:
            output.append(line)
            continue
        key = match.group(2)
        value = json.dumps(values[key], ensure_ascii=False)
        output.append(match.group(1) + '"' + key + '"' + match.group(3) + value + match.group(5))
    prefix = b"\xef\xbb\xbf" if bom else b""
    return prefix + newline.join(output).encode("utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true", help="verify the Max catalogs; write nothing")
    args = parser.parse_args()

    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    resources = os.path.join(root, RESOURCES)
    metrics = FontMetrics(os.path.join(root, FONT))
    strings = load_catalogs(resources, "Strings")
    date_source = select_date_source(metrics, strings)
    stale = []

    for kind in KINDS:
        catalogs = strings if kind == "Strings" else load_catalogs(resources, kind)
        validate_catalogs(kind, catalogs)
        values = derive_values(metrics, catalogs, date_source)
        base = os.path.join(resources, "%s.en.json" % kind)
        target = os.path.join(resources, "%s.%s.json" % (kind, MAX_SUBTAG))
        content = render(base, values)
        relative = os.path.relpath(target, root)
        if args.check:
            if os.path.exists(target):
                with open(target, "rb") as file:
                    current = file.read()
            else:
                current = b""
            if current != content:
                stale.append(relative)
        else:
            with open(target, "wb") as file:
                file.write(content)
            print("wrote %s" % relative)

    if args.check:
        if stale:
            print("Out-of-date Max catalogs:")
            for path in stale:
                print("  " + path)
            print("Run scripts/derive-max.cmd to regenerate.")
            return 1
        print("All Max catalogs match the shipped localizations.")
    print("Max date resources use the '%s' catalog." % date_source)
    return 0


if __name__ == "__main__":
    sys.exit(main())
