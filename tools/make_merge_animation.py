"""Builds the changelog animation for merging a password-protected PDF.

assets/github/merge-protected-pdf.svg is self-contained (mascot pictures are embedded), loops every 10 seconds and
shows a static summary when animations are off (prefers-reduced-motion, or renderers without CSS animation).
assets/github/merge-protected-pdf.png is that summary at 2x, and assets/github/merge-protected-pdf.gif the animation
as a GIF, both rendered with Microsoft Edge, for places that cannot show SVG. The GIF starts on the finished merge, so
a viewer that shows only its first frame, like the release notes inside Kumo, still shows the result.

    python tools/make_merge_animation.py
"""

import base64
import io
import os
import subprocess
import tempfile
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
MASCOTS = ROOT / "src" / "Kumo.App" / "Mascot" / "Images"
OUT = ROOT / "assets" / "github" / "merge-protected-pdf.svg"
PREVIEW = OUT.with_suffix(".png")
GIF = OUT.with_suffix(".gif")
LOOP_MS = 10_000
FPS = 15
GIF_START_MS = 8_400
EDGE = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Microsoft" / "Edge" / "Application" / "msedge.exe"


def mascot(name: str) -> str:
    image = Image.open(MASCOTS / f"{name}.png").convert("RGBA")
    image.thumbnail((160, 160), Image.LANCZOS)
    buffer = io.BytesIO()
    image.save(buffer, "WEBP", quality=88, method=6)
    return "data:image/webp;base64," + base64.b64encode(buffer.getvalue()).decode("ascii")


def page(x: int, y: int, name: str, locked: bool, cls: str) -> str:
    lock = (
        f'<g class="lock" transform="translate({x + 84} {y + 112})">'
        '<circle r="17" fill="#2c2826"/>'
        '<path class="shackle" d="M-6 -2v-5a6 6 0 0 1 12 0v5" fill="none" stroke="#f2c230" stroke-width="3" stroke-linecap="round"/>'
        '<rect x="-9" y="-2" width="18" height="13" rx="3" fill="#f2c230"/></g>'
        if locked
        else ""
    )
    return f"""
  <g class="{cls}">
    <rect x="{x}" y="{y}" width="96" height="124" rx="8" fill="#fff" stroke="#d9cdbf" stroke-width="1.5"/>
    <rect x="{x}" y="{y + 62}" width="96" height="24" fill="#d93a2b"/>
    <text x="{x + 48}" y="{y + 79}" class="pdf">PDF</text>
    <rect x="{x + 14}" y="{y + 16}" width="50" height="6" rx="3" fill="#cfc4b8"/>
    <rect x="{x + 14}" y="{y + 30}" width="68" height="5" rx="2.5" fill="#e6ded4"/>
    <rect x="{x + 14}" y="{y + 42}" width="60" height="5" rx="2.5" fill="#e6ded4"/>
    {lock}
    <text x="{x + 48}" y="{y + 146}" class="name">{name}</text>
  </g>"""


def sheet(cls: str, x: int, y: int, locked: bool) -> str:
    lock = (
        '<g class="sheet-lock" transform="translate(30 36)"><circle r="12" fill="#2c2826"/>'
        '<path class="sheet-shackle" d="M-4 -1v-4a4 4 0 0 1 8 0v4" fill="none" stroke="#f2c230" stroke-width="2.4" stroke-linecap="round"/>'
        '<rect x="-6.5" y="-1" width="13" height="9" rx="2" fill="#f2c230"/></g>'
        if locked
        else ""
    )
    return f"""
  <g class="{cls}" transform="translate({x} {y})">
    <g class="sheet-body">
      <rect width="44" height="56" rx="5" fill="#fff" stroke="#d9cdbf" stroke-width="1.5"/>
      <rect y="28" width="44" height="11" fill="#d93a2b"/>
      <rect x="7" y="8" width="24" height="4" rx="2" fill="#cfc4b8"/>
      {lock}
    </g>
  </g>"""


svg = f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 960 460" width="960" height="460" role="img" aria-labelledby="title desc">
  <title id="title">Kumo merges a password-protected PDF with another PDF</title>
  <desc id="desc">Two PDFs are dropped on Kumo: notes.pdf, which has a password, and slides.pdf. Kumo asks for the password of notes.pdf and uses it to open the file; it does not remove or get around the protection. Both are merged on this PC into a new file, notes (merged).pdf, saved without a password. The two original files stay unchanged, and notes.pdf keeps its password.</desc>
  <style>
    text {{ font-family: "Segoe UI Variable Text", "Segoe UI", system-ui, -apple-system, sans-serif; fill: #22201d; }}
    .pdf {{ fill: #fff; font-size: 12px; font-weight: 700; text-anchor: middle; letter-spacing: .08em; }}
    .name {{ font-size: 14px; font-weight: 600; text-anchor: middle; }}
    .small {{ font-size: 12.5px; fill: #5f5850; }}
    .title {{ font-size: 20px; font-weight: 700; }}
    .step {{ font-size: 14px; font-weight: 600; }}
    .step-num {{ fill: #fff; font-size: 12px; font-weight: 700; text-anchor: middle; }}
    .dlg {{ font-size: 12.5px; fill: #efe7df; }}

    /* Every animated part runs on the same 10 s loop; the values without animation are the summary frame. */
    .sheet-a, .sheet-b, .spinner, .dlg-press {{ opacity: 0; }}
    .anim .sheet-a {{ animation: sheet-a 10s infinite cubic-bezier(.45,.05,.3,1); }}
    .anim .sheet-b {{ animation: sheet-b 10s infinite cubic-bezier(.45,.05,.3,1); }}
    .anim .sheet-shackle {{ animation: shackle 10s infinite; transform-box: fill-box; transform-origin: 100% 100%; }}
    .anim .dialog {{ animation: dialog 10s infinite; transform-origin: 500px 108px; }}
    .anim .dot {{ animation: dot 10s infinite; }}
    .anim .dot:nth-of-type(2) {{ animation-name: dot2; }}
    .anim .dot:nth-of-type(3) {{ animation-name: dot3; }}
    .anim .dot:nth-of-type(4) {{ animation-name: dot4; }}
    .anim .dlg-press {{ animation: press 10s infinite; }}
    .anim .gate {{ animation: gate 10s infinite; }}
    .anim .spinner {{ animation: spin-show 10s infinite; }}
    .anim .spinner path {{ animation: spin 1s linear infinite; transform-origin: 480px 266px; }}
    .anim .m-idle {{ animation: m-idle 10s infinite; }}
    .anim .m-work {{ animation: m-work 10s infinite; }}
    .anim .m-done {{ animation: m-done 10s infinite; }}
    .anim .output {{ animation: output 10s infinite cubic-bezier(.2,.75,.25,1); }}
    .anim .kept {{ animation: kept 10s infinite; }}
    .anim .s1 {{ animation: s1 10s infinite; }}
    .anim .s2 {{ animation: s2 10s infinite; }}
    .anim .s3 {{ animation: s3 10s infinite; }}
    .anim .s4 {{ animation: s4 10s infinite; }}

    @keyframes sheet-a {{
      0%, 8% {{ opacity: 0; transform: translate(84px, 268px); }}
      11% {{ opacity: 1; transform: translate(84px, 268px); }}
      20% {{ opacity: 1; transform: translate(380px, 250px); }}
      25%, 100% {{ opacity: 0; transform: translate(458px, 238px) scale(.6); }}
    }}
    @keyframes sheet-b {{
      0%, 8% {{ opacity: 0; transform: translate(84px, 104px); }}
      11% {{ opacity: 1; transform: translate(84px, 104px); }}
      22% {{ opacity: 1; transform: translate(330px, 176px); }}
      50% {{ opacity: 1; transform: translate(330px, 176px); }}
      58% {{ opacity: 0; transform: translate(458px, 238px) scale(.6); }}
      100% {{ opacity: 0; transform: translate(458px, 238px) scale(.6); }}
    }}
    @keyframes shackle {{
      0%, 44% {{ transform: translateY(0); }}
      47%, 100% {{ transform: translateY(-3px) rotate(-25deg); }}
    }}
    @keyframes gate {{
      0%, 21% {{ opacity: 0; }}
      24%, 44% {{ opacity: 1; }}
      48%, 100% {{ opacity: 0; }}
    }}
    @keyframes dialog {{
      0%, 23% {{ opacity: 0; transform: translateY(8px) scale(.96); }}
      26%, 44% {{ opacity: 1; transform: none; }}
      47%, 100% {{ opacity: 0; transform: translateY(-6px) scale(.98); }}
    }}
    @keyframes dot {{ 0%, 28% {{ opacity: 0; }} 29%, 100% {{ opacity: 1; }} }}
    @keyframes dot2 {{ 0%, 31% {{ opacity: 0; }} 32%, 100% {{ opacity: 1; }} }}
    @keyframes dot3 {{ 0%, 34% {{ opacity: 0; }} 35%, 100% {{ opacity: 1; }} }}
    @keyframes dot4 {{ 0%, 37% {{ opacity: 0; }} 38%, 100% {{ opacity: 1; }} }}
    @keyframes press {{ 0%, 40% {{ opacity: 0; }} 41%, 44% {{ opacity: .35; }} 45%, 100% {{ opacity: 0; }} }}
    @keyframes spin-show {{ 0%, 52% {{ opacity: 0; }} 55%, 64% {{ opacity: 1; }} 66%, 100% {{ opacity: 0; }} }}
    @keyframes spin {{ to {{ transform: rotate(360deg); }} }}
    @keyframes m-idle {{ 0%, 52% {{ opacity: 1; }} 54%, 96% {{ opacity: 0; }} 100% {{ opacity: 1; }} }}
    @keyframes m-work {{ 0%, 52% {{ opacity: 0; }} 54%, 65% {{ opacity: 1; }} 67%, 100% {{ opacity: 0; }} }}
    @keyframes m-done {{ 0%, 65% {{ opacity: 0; }} 67%, 94% {{ opacity: 1; }} 97%, 100% {{ opacity: 0; }} }}
    @keyframes output {{
      0%, 64% {{ opacity: 0; transform: translate(-300px, 30px) scale(.5); }}
      72%, 94% {{ opacity: 1; transform: none; }}
      98%, 100% {{ opacity: 0; transform: none; }}
    }}
    @keyframes kept {{ 0%, 74% {{ opacity: 0; }} 78%, 94% {{ opacity: 1; }} 98%, 100% {{ opacity: 0; }} }}
    @keyframes s1 {{ 0%, 21% {{ opacity: 1; }} 23%, 100% {{ opacity: .3; }} }}
    @keyframes s2 {{ 0%, 21% {{ opacity: .3; }} 23%, 49% {{ opacity: 1; }} 51%, 100% {{ opacity: .3; }} }}
    @keyframes s3 {{ 0%, 49% {{ opacity: .3; }} 51%, 73% {{ opacity: 1; }} 75%, 100% {{ opacity: .3; }} }}
    @keyframes s4 {{ 0%, 73% {{ opacity: .3; }} 75%, 96% {{ opacity: 1; }} 98%, 100% {{ opacity: .3; }} }}

    @media (prefers-reduced-motion: reduce) {{
      .anim * {{ animation: none !important; }}
    }}
  </style>

  <rect width="960" height="460" rx="24" fill="#faf6f0"/>
  <path d="M0 410c160-18 330-16 500-4s320-14 460-8v38a24 24 0 0 1-24 24H24a24 24 0 0 1-24-24Z" fill="#f3e4d6"/>

  <text x="36" y="46" class="title">Merge PDFs, one with a password</text>

  <g class="anim">
    <!-- Originals: they never move. -->
    {page(58, 70, "notes.pdf", True, "original")}
    {page(58, 234, "slides.pdf", False, "original")}
    <g class="kept">
      <g transform="translate(170 124)"><circle r="13" fill="#3f9e5a"/><path d="M-6 0l4 4 7-8" fill="none" stroke="#fff" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"/></g>
      <g transform="translate(170 288)"><circle r="13" fill="#3f9e5a"/><path d="M-6 0l4 4 7-8" fill="none" stroke="#fff" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"/></g>
      <text x="190" y="129" class="small">unchanged, still locked</text>
      <text x="190" y="293" class="small">unchanged</text>
    </g>

    <!-- Direction: two files in, one file out. -->
    <g fill="none" stroke="#cfc3b4" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
      <path d="M296 266h88" stroke-dasharray="2 8"/>
      <path d="M376 258l9 8-9 8"/>
      <path d="M574 250h160" stroke-dasharray="2 8"/>
      <path d="M726 242l9 8-9 8"/>
    </g>

    <!-- Copies read by Kumo. -->
    {sheet("sheet-b", 84, 104, True)}
    {sheet("sheet-a", 84, 268, False)}

    <!-- Kumo -->
    <circle cx="480" cy="266" r="78" fill="#2c2826"/>
    <circle cx="480" cy="266" r="78" fill="none" stroke="#4a433e" stroke-width="2"/>
    <g class="spinner"><path d="M480 182a84 84 0 0 1 84 84" fill="none" stroke="#e8742b" stroke-width="5" stroke-linecap="round"/></g>
    <image class="m-idle" href="{mascot("idle")}" x="436" y="222" width="88" height="88" opacity="0"/>
    <image class="m-work" href="{mascot("processing")}" x="436" y="222" width="88" height="88" opacity="0"/>
    <image class="m-done" href="{mascot("success")}" x="436" y="222" width="88" height="88"/>
    <text x="480" y="370" class="name">Kumo</text>

    <!-- Password check: the locked copy waits here until the password opens it. -->
    <g class="gate" opacity="0">
      <path d="M392 166v120" stroke="#e8742b" stroke-width="3" stroke-dasharray="6 7" stroke-linecap="round"/>
    </g>

    <g class="dialog">
      <rect x="370" y="56" width="260" height="104" rx="10" fill="#2b2522"/>
      <text x="386" y="80" class="dlg" style="font-weight:700">Unlock PDF</text>
      <text x="386" y="100" class="dlg">Password for notes.pdf</text>
      <rect x="386" y="110" width="150" height="30" rx="5" fill="#3a332f" stroke="#5a5048"/>
      <g fill="#efe7df">
        <circle class="dot" cx="402" cy="125" r="4"/>
        <circle class="dot" cx="416" cy="125" r="4"/>
        <circle class="dot" cx="430" cy="125" r="4"/>
        <circle class="dot" cx="444" cy="125" r="4"/>
      </g>
      <rect x="544" y="110" width="72" height="30" rx="5" fill="#e8742b"/>
      <rect class="dlg-press" x="544" y="110" width="72" height="30" rx="5" fill="#000"/>
      <text x="580" y="130" class="dlg" style="text-anchor:middle;font-weight:700;fill:#fff">Continue</text>
    </g>

    <!-- The new file. -->
    <g class="output">
      <rect x="770" y="176" width="96" height="124" rx="8" fill="#fff" stroke="#d9cdbf" stroke-width="1.5" transform="rotate(5 818 238)"/>
      <rect x="760" y="168" width="96" height="124" rx="8" fill="#fff" stroke="#d9cdbf" stroke-width="1.5"/>
      <rect x="760" y="230" width="96" height="24" fill="#d93a2b"/>
      <text x="808" y="247" class="pdf">PDF</text>
      <rect x="774" y="184" width="50" height="6" rx="3" fill="#cfc4b8"/>
      <rect x="774" y="198" width="68" height="5" rx="2.5" fill="#e6ded4"/>
      <rect x="774" y="210" width="60" height="5" rx="2.5" fill="#e6ded4"/>
      <text x="808" y="316" class="name">notes (merged).pdf</text>
      <text x="808" y="334" class="small" style="text-anchor:middle">new file, no password</text>
    </g>

    <!-- Steps -->
    <g transform="translate(36 438)">
      <g class="s1"><circle cx="11" cy="0" r="11" fill="#e8742b"/><text x="11" y="4" class="step-num">1</text><text x="30" y="5" class="step">Drop two PDFs</text></g>
      <g class="s2"><circle cx="231" cy="0" r="11" fill="#e8742b"/><text x="231" y="4" class="step-num">2</text><text x="250" y="5" class="step">Your password opens it</text></g>
      <g class="s3"><circle cx="491" cy="0" r="11" fill="#e8742b"/><text x="491" y="4" class="step-num">3</text><text x="510" y="5" class="step">Merged on this PC</text></g>
      <g class="s4"><circle cx="711" cy="0" r="11" fill="#e8742b"/><text x="711" y="4" class="step-num">4</text><text x="730" y="5" class="step">Originals untouched</text></g>
    </g>
  </g>
</svg>
"""

OUT.write_text(svg, encoding="utf-8")
print(f"{OUT.relative_to(ROOT)}: {OUT.stat().st_size // 1024} KB")



def render(markup: str, target: Path, folder: Path, scale: int, at_ms: int | None) -> None:
    """Screenshots the SVG in headless Edge, either still (animations off) or paused at one moment of the loop."""
    if at_ms is None:
        markup = markup.replace("</style>", ".anim * { animation: none !important; }</style>", 1)
        script = ""
    else:
        script = f"<script>for (const a of document.getAnimations()) {{ a.pause(); a.currentTime = {at_ms}; }}</script>"
    page = folder / f"{target.stem}.html"
    page.write_text(f'<!doctype html><meta charset="utf-8"><body style="margin:0;overflow:hidden">{markup}{script}</body>', encoding="utf-8")
    subprocess.run(
        [str(EDGE), "--headless=new", "--disable-gpu", "--hide-scrollbars", f"--force-device-scale-factor={scale}",
         f"--user-data-dir={folder / (target.stem + '-profile')}", "--default-background-color=00000000",
         "--window-size=960,460", f"--screenshot={target}", page.as_uri()],
        check=True, capture_output=True,
    )


with tempfile.TemporaryDirectory() as temp:
    folder = Path(temp)
    render(svg, PREVIEW, folder, 2, None)
    print(f"{PREVIEW.relative_to(ROOT)}: {PREVIEW.stat().st_size // 1024} KB")

    count = LOOP_MS * FPS // 1000
    times = [(GIF_START_MS + i * 1000 // FPS) % LOOP_MS for i in range(count)]
    paths = [folder / f"frame{i:03}.png" for i in range(count)]
    with ThreadPoolExecutor(max_workers=6) as pool:
        list(pool.map(lambda job: render(svg, job[0], folder, 1, job[1]), zip(paths, times)))

    frames = [Image.open(p).convert("RGBA") for p in paths]
    # One shared palette keeps colours from flickering between frames; corners outside the card stay transparent.
    sample = Image.new("RGB", (960, 460 * 4))
    for i, index in enumerate(range(0, count, count // 4)):
        sample.paste(frames[index].convert("RGB"), (0, 460 * i))
    palette = sample.quantize(colors=255, method=Image.Quantize.MEDIANCUT)
    images = []
    for frame in frames:
        indexed = frame.convert("RGB").quantize(palette=palette, dither=Image.Dither.NONE)
        indexed.paste(255, mask=frame.getchannel("A").point(lambda a: 255 if a < 128 else 0))
        images.append(indexed)
    images[0].save(GIF, save_all=True, append_images=images[1:], duration=1000 // FPS, loop=0, transparency=255,
                   disposal=1, optimize=False)
print(f"{GIF.relative_to(ROOT)}: {GIF.stat().st_size // 1024} KB")
