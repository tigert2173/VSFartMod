from pathlib import Path
from io import StringIO
import csv

path = Path(r"c:\Users\Tiger\Downloads\VS Modding\VS48R1 PCFULL\Virtual Succubus_Data\StreamingAssets\VSFartSounds\Tasklist - Timed New.txt")
text = path.read_text(encoding="utf-8")
text = text.replace("\r\n", "\n").replace("\r", "\n")
text = text.replace("\u2014", "-").replace("\u2013", "-")
# Unknown kink flags in EventRequiredFeatures can infinite-loop TimedEventRepository.
text = text.replace('"Fart Torture, Farting, Smothering"', '"Farting, Smothering"')
text = text.replace('"Fart Torture, Farting, Butt Worship"', '"Farting, Butt Worship"')
text = text.replace('"Fart Torture, Farting"', "Farting")
text = text.replace("Fart Torture, Farting, Smothering", "Farting, Smothering")

path.write_text(text, encoding="utf-8", newline="\n")

rows = list(csv.reader(StringIO(text)))
print("rows", len(rows) - 1, "cols", len(rows[0]), "CR", text.count("\r"))
print("Fart Torture still in req col", sum(1 for r in rows if len(r) > 2 and "Fart Torture" in r[2]))
for r in rows:
    if r and r[0] in ("ID161", "ID162", "ID163", "ID178", "ID186", "ID193"):
        print(r[0], "|", r[2], "|", r[3][:70])

# Also normalize sibling CSVs
base = path.parent
for name in ("MenuExplanations.txt", "ListOfToggles.txt", "TogggleInfo.txt", "InteractionStrings.txt"):
    p = base / name
    if not p.exists():
        continue
    t = p.read_text(encoding="utf-8")
    fixed = t.replace("\r\n", "\n").replace("\r", "\n")
    if fixed != t:
        p.write_text(fixed, encoding="utf-8", newline="\n")
        print("normalized", name)
    else:
        print("ok", name, "CR", t.count("\r"))
