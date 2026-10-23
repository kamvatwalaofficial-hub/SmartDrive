#!/usr/bin/env python3
"""Generate the original SVG illustrations used as SmartDrive's sample vehicle media.

The fleet list is read from backend/src/SmartDrive.Application/Data/Seed/SampleData.cs so the
illustrations always match the seeded vehicles. Each vehicle gets six images (side, front, rear,
interior, dashboard, boot) drawn in its colour and body shape. They are clearly labelled as
illustrations; real photos can be uploaded through the admin dashboard.

Usage:  python3 tools/generate_sample_media.py
Output: backend/src/SmartDrive.Api/wwwroot/media/sample/<slug>/<view>.svg
"""
from __future__ import annotations

import pathlib
import re
from xml.sax.saxutils import escape

ROOT = pathlib.Path(__file__).resolve().parents[1]
SOURCE = ROOT / "backend/src/SmartDrive.Application/Data/Seed/SampleData.cs"
OUT = ROOT / "backend/src/SmartDrive.Api/wwwroot/media/sample"

W, H = 800, 450
NAVY, INK, ACCENT = "#0f1b2d", "#1b2a41", "#f97316"

VEHICLE = re.compile(
    r'new\("(?P<slug>[a-z0-9-]+)", "(?P<make>[^"]+)", "(?P<model>[^"]+)", (?P<year>\d{4}), "(?P<cat>[a-z]+)",'
    r'.*?TransmissionType\.\w+, FuelType\.\w+, [^,]+, \d, [^,]+, [^,]+, [^,]+, [^,]+, "(?P<colour>#[0-9a-fA-F]{6})", "(?P<shape>[a-z]+)",'
    r'\s*"[^"]*", \[[^\]]*\], [0-9.]+\)',
    re.S,
)
LUGGAGE = re.compile(r'new\("(?P<slug>[a-z0-9-]+)",(?:[^,]*,){6} \d+, \d+, (?P<bags>\d+),')


def shade(hex_colour: str, factor: float) -> str:
    """Lighten (factor > 1) or darken (factor < 1) a colour."""
    r, g, b = (int(hex_colour[i:i + 2], 16) for i in (1, 3, 5))
    if factor >= 1:
        f = factor - 1
        r, g, b = (int(c + (255 - c) * f) for c in (r, g, b))
    else:
        r, g, b = (int(c * factor) for c in (r, g, b))
    return f"#{r:02x}{g:02x}{b:02x}"


def is_light(hex_colour: str) -> bool:
    r, g, b = (int(hex_colour[i:i + 2], 16) for i in (1, 3, 5))
    return (0.299 * r + 0.587 * g + 0.114 * b) > 170


def frame(title: str, body: str, view: str) -> str:
    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" role="img" aria-label="{escape(title)} ({view}, illustration)">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#1e2d44"/><stop offset="0.62" stop-color="{NAVY}"/><stop offset="1" stop-color="#0a1220"/>
    </linearGradient>
    <radialGradient id="spot" cx="0.5" cy="0.35" r="0.65">
      <stop offset="0" stop-color="#ffffff" stop-opacity="0.16"/><stop offset="1" stop-color="#ffffff" stop-opacity="0"/>
    </radialGradient>
    <linearGradient id="glass" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#9fc3e0"/><stop offset="1" stop-color="#2b4a6b"/>
    </linearGradient>
  </defs>
  <rect width="{W}" height="{H}" fill="url(#bg)"/>
  <rect width="{W}" height="{H}" fill="url(#spot)"/>
  <rect y="352" width="{W}" height="98" fill="#09111d" opacity="0.55"/>
{body}
  <text x="28" y="40" fill="#e2e8f0" font-family="Inter, Segoe UI, Arial, sans-serif" font-size="20" font-weight="600">{escape(title)}</text>
  <text x="28" y="64" fill="#94a3b8" font-family="Inter, Segoe UI, Arial, sans-serif" font-size="14">{escape(view.capitalize())} view</text>
  <text x="{W - 24}" y="{H - 18}" text-anchor="end" fill="#94a3b8" font-family="Inter, Segoe UI, Arial, sans-serif" font-size="13">Illustration · sample media</text>
</svg>
"""


def wheel(cx: float, cy: float, r: float = 44) -> str:
    return (f'  <circle cx="{cx}" cy="{cy}" r="{r}" fill="#111827"/>'
            f'<circle cx="{cx}" cy="{cy}" r="{r * 0.62}" fill="#9ca3af"/>'
            f'<circle cx="{cx}" cy="{cy}" r="{r * 0.18}" fill="#374151"/>\n')


SIDE_PROFILES = {
    # body outline, windows, wheel x positions
    "hatch": ("M95 300 Q90 248 140 238 L250 228 Q300 160 360 152 L520 150 Q585 156 640 226 L690 236 Q712 250 708 300 Z",
              "M268 226 Q312 172 360 166 L438 164 L438 226 Z M452 164 L514 164 Q560 170 610 226 L452 226 Z", (210, 590)),
    "sedan": ("M80 300 Q78 250 128 240 L262 230 Q318 162 380 154 L520 152 Q580 160 628 224 L706 232 Q724 248 720 300 Z",
              "M282 228 Q330 174 384 168 L452 166 L452 228 Z M466 166 L516 166 Q566 172 606 228 L466 228 Z", (205, 600)),
    "suv": ("M90 302 Q86 238 136 226 L218 218 Q262 128 330 122 L600 120 Q648 126 662 214 L700 222 Q718 240 714 302 Z",
            "M236 214 Q276 140 332 136 L430 136 L430 214 Z M444 136 L594 136 Q628 142 640 214 L444 214 Z", (215, 590)),
    "van": ("M92 304 Q88 220 132 196 Q170 108 250 104 L660 102 Q700 106 704 160 L708 304 Z",
            "M156 196 Q190 122 254 118 L330 118 L330 196 Z M344 118 L470 118 L470 196 L344 196 Z M484 118 L640 118 Q684 120 686 160 L686 196 L484 196 Z", (205, 600)),
    "bakkie": ("M80 302 Q78 244 124 232 L210 224 Q252 132 318 126 L452 124 Q470 128 474 224 L716 224 Q726 236 724 302 Z",
               "M228 220 Q264 144 320 140 L380 140 L380 220 Z M394 140 L448 140 Q458 142 460 220 L394 220 Z", (200, 610)),
}


def side(colour: str, shape: str) -> str:
    body, windows, (w1, w2) = SIDE_PROFILES.get(shape, SIDE_PROFILES["hatch"])
    stroke = shade(colour, 0.55 if is_light(colour) else 0.7)
    highlight = shade(colour, 1.35)
    bed = ('  <path d="M488 230 L712 230 L712 246 L488 246 Z" fill="#111827" opacity="0.45"/>\n' if shape == "bakkie" else "")
    return (f'  <ellipse cx="400" cy="342" rx="330" ry="22" fill="#000" opacity="0.45"/>\n'
            f'  <path d="{body}" fill="{colour}" stroke="{stroke}" stroke-width="3"/>\n'
            f'  <path d="M120 262 L690 262" stroke="{highlight}" stroke-width="4" opacity="0.5"/>\n'
            f'  <path d="{windows}" fill="url(#glass)" stroke="#0b1220" stroke-width="2"/>\n'
            f'{bed}'
            f'  <rect x="96" y="250" width="26" height="12" rx="4" fill="#fde68a"/>'
            f'<rect x="690" y="248" width="20" height="14" rx="4" fill="#ef4444"/>\n'
            + wheel(w1, 300) + wheel(w2, 300))


def front(colour: str, shape: str, rear: bool = False) -> str:
    tall = shape in ("suv", "van", "bakkie")
    top = 128 if tall else 150
    stroke = shade(colour, 0.55 if is_light(colour) else 0.7)
    lamp = "#ef4444" if rear else "#fde68a"
    grille = ("" if rear else f'  <rect x="330" y="262" width="140" height="34" rx="10" fill="#111827" stroke="{stroke}" stroke-width="2"/>\n')
    plate = '  <rect x="352" y="300" width="96" height="24" rx="3" fill="#f8fafc"/><text x="400" y="317" text-anchor="middle" font-family="Arial" font-size="13" fill="#111">SAMPLE</text>\n'
    return (f'  <ellipse cx="400" cy="350" rx="230" ry="20" fill="#000" opacity="0.45"/>\n'
            f'  <rect x="226" y="318" width="54" height="40" rx="10" fill="#111827"/><rect x="520" y="318" width="54" height="40" rx="10" fill="#111827"/>\n'
            f'  <path d="M200 330 L206 248 Q214 226 250 220 L282 {top + 10} Q290 {top} 320 {top} L480 {top} Q510 {top} 518 {top + 10} L550 220 Q586 226 594 248 L600 330 Z" '
            f'fill="{colour}" stroke="{stroke}" stroke-width="3"/>\n'
            f'  <path d="M292 {top + 14} Q298 {top + 8} 322 {top + 8} L478 {top + 8} Q502 {top + 8} 508 {top + 14} L530 214 L270 214 Z" fill="url(#glass)" stroke="#0b1220" stroke-width="2"/>\n'
            f'  <rect x="218" y="236" width="70" height="20" rx="8" fill="{lamp}"/><rect x="512" y="236" width="70" height="20" rx="8" fill="{lamp}"/>\n'
            f'{grille}{plate}')


def interior(colour: str) -> str:
    trim = shade(colour, 0.6)
    return (f'  <path d="M60 260 Q400 150 740 260 L740 300 L60 300 Z" fill="#1f2937"/>\n'
            f'  <path d="M60 300 L740 300 L740 450 L60 450 Z" fill="#111827"/>\n'
            f'  <rect x="140" y="300" width="200" height="150" rx="40" fill="#374151"/><rect x="460" y="300" width="200" height="150" rx="40" fill="#374151"/>\n'
            f'  <rect x="160" y="320" width="160" height="16" rx="8" fill="{trim}"/><rect x="480" y="320" width="160" height="16" rx="8" fill="{trim}"/>\n'
            f'  <circle cx="270" cy="250" r="62" fill="none" stroke="#0b1220" stroke-width="16"/><circle cx="270" cy="250" r="14" fill="#0b1220"/>\n'
            f'  <rect x="360" y="200" width="110" height="64" rx="10" fill="#0b1220" stroke="#334155" stroke-width="3"/>\n'
            f'  <rect x="372" y="212" width="86" height="40" rx="6" fill="#1d4ed8" opacity="0.7"/>\n')


def dashboard(colour: str) -> str:
    accent = shade(colour, 1.2) if not is_light(colour) else ACCENT
    gauges = "".join(
        f'  <circle cx="{x}" cy="230" r="70" fill="#0b1220" stroke="#334155" stroke-width="5"/>'
        f'<path d="M{x - 50} 260 A56 56 0 1 1 {x + 50} 260" fill="none" stroke="{accent}" stroke-width="6"/>'
        f'<line x1="{x}" y1="230" x2="{x + 34}" y2="198" stroke="#f8fafc" stroke-width="5" stroke-linecap="round"/>\n'
        for x in (290, 510))
    return (f'  <rect x="120" y="130" width="560" height="200" rx="36" fill="#111827" stroke="#1f2937" stroke-width="4"/>\n'
            f'{gauges}'
            f'  <rect x="352" y="196" width="96" height="70" rx="10" fill="#0f172a" stroke="#334155" stroke-width="3"/>\n'
            f'  <text x="400" y="240" text-anchor="middle" font-family="Arial" font-size="24" fill="#e2e8f0">0 km/h</text>\n')


def boot(colour: str, bags: int) -> str:
    stroke = shade(colour, 0.55 if is_light(colour) else 0.7)
    suitcase_colours = ["#f97316", "#38bdf8", "#a3e635", "#f472b6", "#facc15"]
    bags = max(1, min(bags, 5))
    cases = ""
    width = 360 / bags
    for i in range(bags):
        x = 220 + i * width + 6
        h = 120 - (i % 2) * 22
        cases += (f'  <rect x="{x:.0f}" y="{318 - h}" width="{width - 12:.0f}" height="{h}" rx="12" fill="{suitcase_colours[i]}" opacity="0.9"/>'
                  f'<rect x="{x + (width - 12) / 2 - 16:.0f}" y="{306 - h}" width="32" height="14" rx="6" fill="none" stroke="#e5e7eb" stroke-width="4"/>\n')
    return (f'  <path d="M180 330 L190 170 Q196 140 230 140 L570 140 Q604 140 610 170 L620 330 Z" fill="{colour}" stroke="{stroke}" stroke-width="3"/>\n'
            f'  <path d="M206 322 L214 186 L586 186 L594 322 Z" fill="#0b1220"/>\n'
            f'{cases}'
            f'  <text x="400" y="384" text-anchor="middle" font-family="Inter, Arial" font-size="18" fill="#e2e8f0">Fits about {bags} large suitcase{"s" if bags != 1 else ""}</text>\n')


def main() -> None:
    source = SOURCE.read_text(encoding="utf-8")
    luggage = {m["slug"]: int(m["bags"]) for m in LUGGAGE.finditer(source)}
    vehicles = list(VEHICLE.finditer(source))
    if not vehicles:
        raise SystemExit("No vehicles found in SampleData.cs")

    for m in vehicles:
        slug, colour, shape = m["slug"], m["colour"], m["shape"]
        title = f'{m["year"]} {m["make"]} {m["model"]}'
        folder = OUT / slug
        folder.mkdir(parents=True, exist_ok=True)
        views = {
            "side": side(colour, shape),
            "front": front(colour, shape),
            "rear": front(colour, shape, rear=True),
            "interior": interior(colour),
            "dashboard": dashboard(colour),
            "boot": boot(colour, luggage.get(slug, 2)),
        }
        for view, body in views.items():
            (folder / f"{view}.svg").write_text(frame(title, body, view), encoding="utf-8")

    print(f"Generated {len(vehicles) * 6} illustrations for {len(vehicles)} vehicles in {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
