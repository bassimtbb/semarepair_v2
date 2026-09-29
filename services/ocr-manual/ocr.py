"""
OCR of the scanned Fiat 500 workshop manual (Rivista Tecnica 112, 2008).

Produces one text blob per page into ocr.json, next to the page images.

The text produced here is NEVER shown to the mechanic. It exists so a page
can be FOUND; what gets displayed is the page image itself. That division is
the whole reason OCR is acceptable in a product whose promise is that it
never invents: a misread character can only surface the wrong page - visible,
and recoverable in one glance - never a wrong torque or amperage on screen.

Resolution is the constraint. The scan is 150 DPI (1239x1755 for A4), which
is the ceiling: the PDF holds photographs of paper, so nothing can add detail
that was never captured. Tesseract's models are trained around 300 DPI and
degrade below it, which is why the page is upscaled before recognition -
not to invent detail, but to present the glyphs at the size the model expects.
"""

import json
import os
import re
import sys
import time

import pytesseract
from PIL import Image

MANUAL_PATH = os.environ.get("MANUAL_PATH", "/app/Data/MANUAL")
LANG = os.environ.get("OCR_LANG", "ita")

# --psm 3 = fully automatic page segmentation. The pages are two-column with
# photographs and callout diagrams between the columns; 3 is the only mode
# that detects that layout on its own. A fixed mode (6, "one uniform block")
# reads straight across the gutter and interleaves the two columns into
# nonsense - which on page 20 would splice a fuse rating onto the wrong fuse.
CONFIG = "--oem 1 --psm 3"

# 150 -> 300 DPI equivalent. Lanczos rather than nearest/bilinear: it keeps
# the thin strokes of the digits legible, and digits are what matter most here
# (amperages, torques, page references).
UPSCALE = 2


# The one systematic misreading in this manual: a capital O where a zero
# belongs, inside a reference code - "FO1" for F01, "TO9" for T09. Measured
# over the whole 89 pages it happens twice, both on the same page, while T05,
# T06, T07 and T09 come out correct elsewhere. Rare, but worth repairing
# because a reference code is exactly what a mechanic types: a page indexed as
# "FO1" is a page he cannot find by searching "F01".
#
# Deterministic, and narrow by construction: in this domain no word has a
# capital O between one of these letters and a digit, so there is no judgement
# involved and nothing that could "correct" a value. It touches the reference
# only - never an amperage, never a torque.
CODE_OH = re.compile(r"\b([FTRG])O(\d)\b")


def fix_reference_codes(text: str) -> str:
    return CODE_OH.sub(r"\g<1>0\g<2>", text)


def ocr_page(path: str) -> str:
    img = Image.open(path)
    img = img.convert("L")  # grayscale; the colour carries no information for text
    img = img.resize((img.width * UPSCALE, img.height * UPSCALE), Image.LANCZOS)
    return fix_reference_codes(pytesseract.image_to_string(img, lang=LANG, config=CONFIG))


def main() -> None:
    if not os.path.isdir(MANUAL_PATH):
        print(f"ERROR: {MANUAL_PATH} does not exist", file=sys.stderr)
        sys.exit(1)

    pages = sorted(f for f in os.listdir(MANUAL_PATH) if f.endswith(".jpg"))
    if not pages:
        print(f"ERROR: no .jpg pages in {MANUAL_PATH}", file=sys.stderr)
        sys.exit(1)

    print(f"Tesseract {pytesseract.get_tesseract_version()}, lang={LANG}")
    print(f"{len(pages)} pages to read from {MANUAL_PATH}\n")

    out = {}
    empty = []
    started = time.time()

    for i, name in enumerate(pages, start=1):
        asset_id = os.path.splitext(name)[0]
        text = ocr_page(os.path.join(MANUAL_PATH, name)).strip()
        out[asset_id] = text
        if len(text) < 40:
            # A near-empty page is either a full-page photograph or a failed
            # read. Both are worth naming rather than silently indexing as an
            # empty chunk.
            empty.append(asset_id)
        if i % 10 == 0 or i == len(pages):
            print(f"  {i}/{len(pages)} pages, {time.time() - started:.0f}s")

    dest = os.path.join(MANUAL_PATH, "ocr.json")
    with open(dest, "w", encoding="utf-8") as fh:
        json.dump(out, fh, ensure_ascii=False, indent=1)

    chars = sum(len(t) for t in out.values())
    print(f"\nwrote {dest}")
    print(f"  {len(out)} pages, {chars} characters, {chars // max(1, len(out))} per page")
    print(f"  pages with almost no text: {len(empty)} {empty[:8]}")
    print(f"  {time.time() - started:.0f}s total")


if __name__ == "__main__":
    main()
