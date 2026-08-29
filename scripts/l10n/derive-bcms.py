#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Derive the Montenegrin, Croatian and Serbian string catalogs from the Bosnian one.

Bosnian, Croatian, Montenegrin and Serbian are four standards of one language.
Translating them independently would produce four near-copies that drift apart
wherever a translator happened to word something differently, hiding the places
they genuinely differ. So Bosnian is the hand-written base (ijekavian, Latin) and
the other three are generated from it:

  cnr  Bosnian with Serbian-style month names and -šć- instead of -št-.
  hr   Same ijekavian reflexes, different lexicon and wholly different months.
  sr   ijekavian -> ekavian, then Latin -> Cyrillic.

Edit Strings.bs.json / Messages.bs.json and re-run this; never hand-edit the
derived files. `--check` regenerates into memory and fails if the files on disk
disagree, which is what keeps that rule honest.

Usage:
    scripts/derive-bcms.cmd              # rewrite the six derived catalogs
    scripts/derive-bcms.cmd --check      # verify they match the base, write nothing
"""
import argparse
import json
import os
import re
import sys

BASE_SUBTAG = "bs"
DERIVED = ("cnr", "hr", "sr")
KINDS = ("Strings", "Messages")
RESOURCES = os.path.join("src", "dotnet", "Localization", "Resources")
LETTER = "A-Za-zČĆŽŠĐčćžšđ"

# ---------------------------------------------------------------- substitution


def apply(rules, text, at_word_start=True):
    """Longest source first, so a longer form wins over its own prefix.

    at_word_start anchors a rule to a word start but not to its end, so a stem
    rule still catches every suffix ("uslov" -> "uvjet" also fixes "uslovi")
    while a short month name can't fire inside another word: "primajte" is not
    May. The ijekavian reflexes need the opposite - they occur mid-word
    ("proslijeđeno") - so those pass at_word_start=False.
    """
    for src, dst in sorted(rules, key=lambda r: -len(r[0])):
        if at_word_start:
            text = re.sub("(?<![" + LETTER + "])" + re.escape(src), dst.replace("\\", "\\\\"), text)
        else:
            text = text.replace(src, dst)
    return text


# Montenegrin: Bosnian, but jun/jul/avgust and korišćenje.
CNR = [
    ("juni", "jun"), ("juli", "jul"), ("august", "avgust"), ("aug", "avg"),
    ("korištenj", "korišćenj"), ("Korištenj", "Korišćenj"), ("korišten", "korišćen"),
]

CROATIAN = [
    # Months share no roots with the other three standards.
    ("januar", "siječanj"), ("februar", "veljača"), ("mart", "ožujak"),
    ("april", "travanj"), ("maj", "svibanj"), ("juni", "lipanj"),
    ("juli", "srpanj"), ("august", "kolovoz"), ("septembar", "rujan"),
    ("oktobar", "listopad"), ("novembar", "studeni"), ("decembar", "prosinac"),
    ("januara", "siječnja"), ("februara", "veljače"), ("marta", "ožujka"),
    ("aprila", "travnja"), ("maja", "svibnja"), ("juna", "lipnja"),
    ("jula", "srpnja"), ("augusta", "kolovoza"), ("septembra", "rujna"),
    ("oktobra", "listopada"), ("novembra", "studenoga"), ("decembra", "prosinca"),
    ("jan|feb|mar|apr|maj|jun|jul|aug|sep|okt|nov|dec",
     "sij|velj|ožu|tra|svi|lip|srp|kol|ruj|lis|stu|pro"),
    # Vocabulary.
    ("fajlova", "datoteka"), ("fajlovi", "datoteke"), ("Fajlovi", "Datoteke"),
    ("fajlove", "datoteke"), ("Fajlove", "Datoteke"),
    ("fajla", "datoteke"), ("fajl", "datoteka"), ("Fajl", "Datoteka"),
    ("uslov", "uvjet"), ("Uslov", "Uvjet"),
    ("interfejsa", "sučelja"), ("Interfejsa", "Sučelja"),
    ("interfejs", "sučelje"), ("Interfejs", "Sučelje"),
    ("dugme", "gumb"), ("Dugme", "Gumb"), ("dugmet", "gumb"),
    ("tastatur", "tipkovnic"), ("Tastatur", "Tipkovnic"),
    ("meni", "izbornik"), ("Meni", "Izbornik"),
    ("menij", "izbornic"), ("Menij", "Izbornic"),
    ("kompanije", "tvrtke"),
    ("opšte", "opće"), ("Opšte", "Opće"), ("opšt", "opć"),
    ("tačk", "točk"), ("Tačk", "Točk"),
    ("detalje", "pojedinosti"), ("Detalje", "Pojedinosti"),
    ("detalja", "pojedinosti"), ("detalji", "pojedinosti"), ("Detalji", "Pojedinosti"),
    ("obavještenjima", "obavijestima"), ("obavještenja", "obavijesti"),
    ("obavještenju", "obavijesti"), ("obavještenje", "obavijest"),
    ("Obavještenjima", "Obavijestima"), ("Obavještenja", "Obavijesti"),
    ("Obavještenje", "Obavijest"), ("obavještenj", "obavijest"),
    ("Obavještenj", "Obavijest"), ("obavještava", "obavješćuje"),
    ("transkrib", "transkribir"),
    ("generiši", "generiraj"), ("Generiši", "Generiraj"), ("generišem", "generiram"),
    ("registrov", "registrir"), ("Registruj", "Registriraj"), ("Registrovati", "Registrirati"),
    ("suspendovan", "suspendiran"),
    ("računar", "računalo"), ("Računar", "Računalo"), ("računaru", "računalu"),
    ("sistem", "sustav"), ("Sistem", "Sustav"),
    ("ekran", "zaslon"), ("Ekran", "Zaslon"),
    ("šta", "što"), ("Šta", "Što"),
    ("niko ", "nitko "), ("Niko ", "Nitko "),
    ("neko ", "netko "), ("Neko", "Netko"),
    ("svako ", "svatko "),
    ("ko otvori", "tko otvori"), ("ko je još", "tko je još"),
    # "prijenos" is masculine where "otpremanje" is neuter, so a predicate agreeing with it
    # needs its own whole-phrase entry above this line rather than a word swap.
    ("Otpremi", "Prenesi"), ("otpremanje", "prijenos"), ("Otpremanje", "Prijenos"),
    ("otpremite", "prenesite"), ("Otpremite", "Prenesite"),
    ("otpremljeno", "preneseno"), ("Otpremljeno", "Preneseno"),
    ("Kreiraj", "Stvori"), ("kreiraj", "stvori"), ("kreirate", "stvarate"),
    ("kreirao", "stvorio"), ("Kreirajte", "Stvorite"), ("kreiranje", "stvaranje"),
    ("sedmic", "tjedn"), ("desilo", "dogodilo"),
    ("dešavanjima", "događanjima"), ("hiljad", "tisuć"),
]

EKAVIAN = [
    ("Mjest", "Mest"), ("mjest", "mest"),
    ("prijenos", "prenos"), ("Prijenos", "Prenos"),
    ("prijevod", "prevod"), ("Prijevod", "Prevod"),
    ("obavještenj", "obaveštenj"), ("Obavještenj", "Obaveštenj"),
    ("obavještava", "obaveštava"), ("Obavještava", "Obaveštava"),
    ("obavijest", "obavest"), ("Obavijest", "Obavest"),
    ("sljedeć", "sledeć"), ("Sljedeć", "Sledeć"),
    ("prije", "pre"), ("Prije", "Pre"),
    ("dijel", "del"), ("Dijel", "Del"),
    ("podijel", "podel"), ("Podijel", "Podel"),
    ("nedjelj", "nedelj"), ("Nedjelj", "Nedelj"), ("ponedjeljak", "ponedeljak"),
    ("srijed", "sred"), ("|sri|", "|sre|"),
    ("rješ", "reš"), ("Rješ", "Reš"), ("riješ", "reš"), ("Riješ", "Reš"),
    ("uspješ", "uspeš"), ("Uspješ", "Uspeš"),
    ("uspjelo", "uspelo"), ("Uspjelo", "Uspelo"),
    ("svijetl", "svetl"), ("Svijetl", "Svetl"),
    ("vjeru", "veru"), ("Vjeru", "Veru"),
    ("vjerovatn", "verovatn"), ("Vjerovatn", "Verovatn"),
    ("bilješk", "belešk"), ("Bilješk", "Belešk"),
    ("posjet", "poset"), ("Posjet", "Poset"),
    ("cijel", "cel"), ("Cijel", "Cel"), ("Cijeli ekran", "Ceo ekran"),
    ("ovdje", "ovde"), ("Ovdje", "Ovde"), ("gdje", "gde"), ("Gdje", "Gde"),
    ("primijen", "primen"), ("Primijen", "Primen"),
    ("promijen", "promen"), ("Promijen", "Promen"),
    ("promjen", "promen"), ("Promjen", "Promen"),
    ("pomjer", "pomer"), ("Pomjer", "Pomer"),
    ("osvjež", "osvež"), ("Osvjež", "Osvež"),
    ("umjeren", "umeren"), ("Umjeren", "Umeren"),
    ("uvijek", "uvek"), ("Uvijek", "Uvek"),
    ("mijenja", "menja"), ("Mijenja", "Menja"),
    ("primijet", "primet"), ("Primijet", "Primet"),
    ("vrijede", "važe"), ("Vrijede", "Važe"),
    ("smije", "sme"), ("smiju", "smeju"),
    ("vidjet će", "videće"), ("šutite", "ćutite"),
    ("naprijed", "napred"), ("Naprijed", "Napred"),
    ("odjelj", "odelj"), ("Odjelj", "Odelj"),
    ("preglednik", "pregledač"), ("Preglednik", "Pregledač"),
    ("pregledniku", "pregledaču"),
    ("korištenj", "korišćenj"), ("Korištenj", "Korišćenj"), ("korišten", "korišćen"),
    ("jučer", "juče"), ("Jučer", "Juče"),
    ("djelimično", "delimično"), ("riječ", "reč"), ("Riječ", "Reč"),
    ("proslijeđ", "prosleđ"), ("Proslijeđ", "Prosleđ"),
    ("juni", "jun"), ("juli", "jul"), ("august", "avgust"), ("aug", "avg"),
    # Serbian Cyrillic text doesn't leave "chat" in Latin - "čet" is the usual form.
    ("chatovima", "četovima"), ("chatovi", "četovi"), ("chatova", "četova"),
    ("chatove", "četove"), ("chatom", "četom"), ("chatu", "četu"), ("chata", "četa"),
    ("chat", "čet"), ("Chatovima", "Četovima"), ("Chatovi", "Četovi"),
    ("Chatova", "Četova"), ("Chatu", "Četu"), ("Chata", "Četa"), ("Chat", "Čet"),
]

# Brands and protocol words a Serbian reader expects to stay in Latin. Matched
# together with any lowercase suffix glued on, so "Windowsu" survives whole.
KEEP_LATIN = [
    "WebAssembly", "reCAPTCHA", "Microsoft Edge", "Google Chrome", "Apple Safari",
    "macOS", "Windows", "Android", "iOS", "Google", "Safari", "Chrome", "Edge",
    "Telegram", "KLIPY", "GIF", "API", "URL", "SMS", "ID", "QR", "OK",
    "emoji", "Emoji", "Cookie", "cookie",
    "Live Activities", "AI", "txt", "MB", "&nbsp;", "&ndash;", "Welcome", "DELETE", "Voxt",
]

CYRILLIC = {
    "Lj": "Љ", "lj": "љ", "Nj": "Њ", "nj": "њ", "Dž": "Џ", "dž": "џ",
    "LJ": "Љ", "NJ": "Њ", "DŽ": "Џ",
    "A": "А", "B": "Б", "V": "В", "G": "Г", "D": "Д", "Đ": "Ђ", "E": "Е",
    "Ž": "Ж", "Z": "З", "I": "И", "J": "Ј", "K": "К", "L": "Л", "M": "М",
    "N": "Н", "O": "О", "P": "П", "R": "Р", "S": "С", "T": "Т", "Ć": "Ћ",
    "U": "У", "F": "Ф", "H": "Х", "C": "Ц", "Č": "Ч", "Š": "Ш",
    "a": "а", "b": "б", "v": "в", "g": "г", "d": "д", "đ": "ђ", "e": "е",
    "ž": "ж", "z": "з", "i": "и", "j": "ј", "k": "к", "l": "л", "m": "м",
    "n": "н", "o": "о", "p": "п", "r": "р", "s": "с", "t": "т", "ć": "ћ",
    "u": "у", "f": "ф", "h": "х", "c": "ц", "č": "ч", "š": "ш",
}


def to_cyrillic(text):
    holes = []

    def stash(match):
        holes.append(match.group(0))
        return "\x00%d\x00" % (len(holes) - 1)

    text = re.sub(r'\{[^}]*\}|&[a-z]+;', stash, text)
    for token in sorted(KEEP_LATIN, key=len, reverse=True):
        text = re.sub(r'(?<![' + LETTER + r'])' + re.escape(token) + r'[a-z]*', stash, text)

    out, i = [], 0
    while i < len(text):
        digraph = text[i:i + 2]
        if digraph in CYRILLIC:
            out.append(CYRILLIC[digraph])
            i += 2
            continue
        out.append(CYRILLIC.get(text[i], text[i]))
        i += 1
    return re.sub("\x00(\\d+)\x00", lambda m: holes[int(m.group(1))], "".join(out))


def derive(subtag, key, value):
    if subtag == "cnr":
        return apply(CNR, value)
    if subtag == "hr":
        return apply(CROATIAN, value)
    # Date_*Pattern values are DateTimeFormatInfo format specifiers, not prose:
    # transliterating "HH:mm" to "ХХ:мм" would make every timestamp unparseable.
    if key.endswith("Pattern"):
        return value
    return to_cyrillic(apply(EKAVIAN, value, at_word_start=False))


# ------------------------------------------------------------------- catalogs

KEY_LINE = re.compile(r'^(\s*)"([A-Za-z0-9_]+)"(\s*:\s*)(".*?")(,?)\s*$')


def read_lines(path):
    raw = open(path, "rb").read()
    text = raw.decode("utf-8-sig")
    return raw.startswith(b"\xef\xbb\xbf"), "\r\n" if "\r\n" in text else "\n", text


def render(base_path, subtag):
    """Rebuild the catalog with the base file's exact layout, so a diff between
    two of these lines up key for key."""
    bom, nl, text = read_lines(base_path)
    out = []
    for line in text.split(nl):
        m = KEY_LINE.match(line)
        if not m:
            out.append(line)
            continue
        key, value = m.group(2), json.loads(m.group(4))
        out.append(m.group(1) + '"' + key + '"' + m.group(3)
                   + json.dumps(derive(subtag, key, value), ensure_ascii=False) + m.group(5))
    return (b"\xef\xbb\xbf" if bom else b"") + nl.join(out).encode("utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true",
                        help="verify the derived catalogs match the Bosnian base; write nothing")
    args = parser.parse_args()

    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    resources = os.path.join(root, RESOURCES)
    if not os.path.isdir(resources):
        sys.exit("Not found: %s (run this from the repository)" % resources)

    stale = []
    for kind in KINDS:
        base = os.path.join(resources, "%s.%s.json" % (kind, BASE_SUBTAG))
        for subtag in DERIVED:
            target = os.path.join(resources, "%s.%s.json" % (kind, subtag))
            content = render(base, subtag)
            if args.check:
                current = open(target, "rb").read() if os.path.exists(target) else b""
                if current != content:
                    stale.append(os.path.relpath(target, root))
                continue
            open(target, "wb").write(content)
            print("wrote %s" % os.path.relpath(target, root))

    if args.check:
        if stale:
            print("Out of date with %s.%s.json:" % (KINDS[0], BASE_SUBTAG))
            for path in stale:
                print("  " + path)
            print("Run scripts/derive-bcms.cmd to regenerate.")
            return 1
        print("All derived BCMS catalogs match the Bosnian base.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
