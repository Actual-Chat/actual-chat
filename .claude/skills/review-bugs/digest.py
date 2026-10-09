import argparse
import datetime
import json
import sys

parser = argparse.ArgumentParser()
parser.add_argument("files", nargs="+")
parser.add_argument("--after", type=int, default=0)
parser.add_argument("--chat", default="")
args = parser.parse_args()

messages = {}
for path in args.files:
    with open(path, encoding="utf8") as f:
        data = json.load(f)
    for m in data["messages"]:
        messages[m["id"]] = m

sys.stdout.reconfigure(encoding="utf8")
ordered = sorted(messages.values(), key=lambda m: m["id"])
count = 0
for m in ordered:
    if m["id"] <= args.after or m["isRemoved"]:
        continue
    when = datetime.datetime.fromtimestamp(m["createdAt"] / 1000, datetime.UTC)
    reply = f" r{m['repliedToId']}" if m["repliedToId"] else ""
    atts = f" [att{len(m['attachments'])}]" if m["attachments"] else ""
    text = (m["text"] or "").replace("\n", " / ")
    print(f"#{m['id']} {when:%m-%d %H:%M}Z {m['author']['name']}{reply}{atts}: {text}")
    count += 1

first = ordered[0]["id"] if ordered else 0
last = ordered[-1]["id"] if ordered else 0
print(f"-- {count} messages after #{args.after}; files cover #{first}..#{last}", file=sys.stderr)
