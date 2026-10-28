"""Builds tmp/vad-onset/labels.html: every case of the last VoicePreRollTrimFittingTest run with its audio,
a spectrogram, the labelled speech onset and the VAD trigger, to check the automatic labels by ear.

Usage: python tools/vad-onset/labels.py   (needs numpy and matplotlib)
"""
import csv
import html
import subprocess
import sys
from pathlib import Path

SAMPLE_RATE = 16000
DATA = Path(__file__).resolve().parents[2] / "tmp" / "vad-onset"


def main():
    cases_path = DATA / "cases.tsv"
    if not cases_path.exists():
        sys.exit(f"No {cases_path}: run tools/vad-onset/fetch.py, then VoicePreRollTrimFittingTest (see README.md).")

    import numpy as np
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    out = DATA / "labels"
    out.mkdir(exist_ok=True)
    rows = []
    with open(cases_path, encoding="utf-8") as f:
        for name, trigger, level_lead, band_lead, is_high_band in csv.reader(f, delimiter="\t"):
            source = DATA / ("pcm-soft" if "@" in name else "pcm") / f"{name}.pcm"
            x = np.fromfile(source, dtype="<i2").astype(np.float32) / 32768
            trigger, lead = float(trigger), float(band_lead) / 1000
            seg = x[max(0, int((trigger - 1.0) * SAMPLE_RATE)): int((trigger + 1.5) * SAMPLE_RATE)]
            offset = min(1.0, trigger)
            key = f"{name}-{trigger:.2f}".replace("@", "_")
            subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "s16le", "-ar", str(SAMPLE_RATE), "-ac", "1",
                            "-i", "-", str(out / f"{key}.wav")], input=(seg * 32767).astype("<i2").tobytes(), check=True)

            fig, ax = plt.subplots(2, 1, figsize=(10, 3.6), sharex=True, gridspec_kw=dict(height_ratios=[1, 2]))
            ax[0].plot(np.arange(len(seg)) / SAMPLE_RATE, seg, lw=0.4, color="#333")
            ax[1].specgram(seg, NFFT=512, Fs=SAMPLE_RATE, noverlap=384, cmap="magma", vmin=-130)
            ax[1].set_ylim(0, 8000)
            for a in ax:
                a.axvline(offset - lead, color="lime", lw=1.3, label="labelled onset")
                a.axvline(offset, color="red", lw=1.3, label="VAD trigger")
                a.axvline(offset - 0.4, color="#888", lw=0.8, ls=":", label="trigger - 400 ms")
            ax[0].legend(loc="upper right", fontsize=7)
            fig.tight_layout()
            fig.savefig(out / f"{key}.png", dpi=85)
            plt.close(fig)
            note = "<br>high-band onset" if is_high_band == "1" else ""
            rows.append(f"<tr><td><b>{html.escape(key)}</b><br>onset {float(band_lead):.0f} ms before trigger{note}</td>"
                        f"<td><audio controls preload='none' src='labels/{key}.wav'></audio>"
                        f"<br><img src='labels/{key}.png' width='760'></td></tr>")

    page = DATA / "labels.html"
    page.write_text("<!doctype html><meta charset='utf-8'><title>Onset labels</title>"
                    "<style>body{font:14px system-ui;margin:16px}td{vertical-align:top;padding:6px;"
                    "border-bottom:1px solid #ccc}</style><p>Green = labelled onset, red = VAD trigger, "
                    f"dotted = 400 ms lookback.</p><table>{''.join(rows)}</table>", encoding="utf-8")
    print(f"{len(rows)} cases: {page}")


if __name__ == "__main__":
    main()
