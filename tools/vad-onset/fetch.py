"""Downloads the recordings listed in recordings.tsv and prepares them for VoicePreRollTrimFittingTest.

The audio comes from private chats, so it never enters the repo: each message's audio is fetched through
the admin-only /api/audio-blob endpoint, which needs your session and access to the chat.

Usage:
    set ActualChat_SessionId=<value of the FusionAuth.SessionId cookie of an admin session>
    python tools/vad-onset/fetch.py [--base-url https://voxt.ai]

Output (git-ignored), under tmp/vad-onset/:
    webm/      the downloaded audio, one file per message
    pcm/       "long" recordings as 16 kHz mono s16le
    pcm-soft/  "soft" clips cut from start_s to end_s, named <chat>_<local>@<start>@<letter>.pcm
Requires ffmpeg on PATH. Already downloaded files are skipped, so it is safe to rerun.
"""
import argparse
import csv
import os
import subprocess
import sys
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

SAMPLE_RATE = 16000
ROOT = Path(__file__).resolve().parents[2]
DATA = ROOT / "tmp" / "vad-onset"
TRANSLIT = {"ш": "sh", "щ": "shch", "ж": "zh", "х": "kh", "ф": "f", "ц": "ts", "ч": "ch", "з": "z", "с": "s"}


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--base-url", default=os.environ.get("ActualChat_BaseUrl", "https://voxt.ai"))
    args = parser.parse_args()
    session_id = os.environ.get("ActualChat_SessionId")
    if not session_id:
        sys.exit("Set ActualChat_SessionId to the FusionAuth.SessionId cookie of an admin session.")

    with open(Path(__file__).with_name("recordings.tsv"), encoding="utf-8") as f:
        rows = list(csv.DictReader(f, delimiter="\t"))
    for name in ("webm", "pcm", "pcm-soft"):
        (DATA / name).mkdir(parents=True, exist_ok=True)

    messages = sorted({(r["chat_id"], r["local_id"]) for r in rows})
    with ThreadPoolExecutor(8) as pool:
        failures = [m for m, ok in zip(messages, pool.map(lambda m: download(args.base_url, session_id, *m), messages))
                    if not ok]
    for row in rows:
        webm = DATA / "webm" / f"{row['chat_id']}_{row['local_id']}.webm"
        if webm.exists():
            export(row, webm)
    print(f"{len(messages) - len(failures)} of {len(messages)} messages available in {DATA}")
    if failures:
        print("Not downloaded: " + ", ".join(f"{c}:{l}" for c, l in failures))


def download(base_url, session_id, chat_id, local_id):
    target = DATA / "webm" / f"{chat_id}_{local_id}.webm"
    if target.exists():
        return True

    request = urllib.request.Request(f"{base_url}/api/audio-blob/{chat_id}/{local_id}",
                                     headers={"Cookie": f"FusionAuth.SessionId={session_id}"})
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            data = response.read()
    except urllib.error.HTTPError as e:
        print(f"{chat_id}:{local_id}: HTTP {e.code} {e.read()[:200]!r}", file=sys.stderr)
        return False

    target.write_bytes(data)
    return True


def export(row, webm):
    name = f"{row['chat_id']}_{row['local_id']}"
    if row["kind"] == "long":
        target = DATA / "pcm" / f"{name}.pcm"
        window = []
    else:
        letter = TRANSLIT.get(row["first_letter"], row["first_letter"])
        target = DATA / "pcm-soft" / f"{name}@{row['start_s']}@{letter}.pcm"
        window = ["-ss", row["start_s"], "-to", row["end_s"]]
    if target.exists():
        return

    # Decoding first and trimming the decoded stream keeps the cut sample-exact
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(webm), *window,
                    "-ac", "1", "-ar", str(SAMPLE_RATE), "-f", "s16le", str(target)], check=True)


if __name__ == "__main__":
    main()
