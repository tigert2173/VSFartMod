"""Generate organic multi-layer fart WAVs (noise-led, less tonal/sine)."""
from __future__ import annotations

import math
import os
import random
import struct
import wave

OUT = r"c:\Users\Tiger\Downloads\VS Modding\VS48R1 PCFULL\Virtual Succubus_Data\StreamingAssets\VSFartSounds\Farts"
SR = 44100

STYLES = [
    "toot", "rasp", "wet", "squeak", "rumble", "flutter", "hollow", "burst", "mixed",
]


def clamp(x: float, lo: float = -1.0, hi: float = 1.0) -> float:
    return lo if x < lo else hi if x > hi else x


def softsat(x: float) -> float:
    return math.tanh(x * 1.15)


def lp(x: float, s: list[float], a: float) -> float:
    s[0] += a * (x - s[0])
    return s[0]


def hp(x: float, s: list[float], a: float) -> float:
    # s: [lp]
    s[0] += a * (x - s[0])
    return x - s[0]


def envelope(t: float, attack: float, hold: float, release: float) -> float:
    if t < attack:
        return t / max(1e-6, attack)
    if t < attack + hold:
        # Slight pressure sag while holding
        u = (t - attack) / max(1e-6, hold)
        return 1.0 - 0.12 * u
    u = (t - attack - hold) / max(1e-6, release)
    if u >= 1.0:
        return 0.0
    return (1.0 - u) ** 1.55


def synth_fart(style: str, seed: int) -> list[float]:
    rng = random.Random(seed)
    dur_map = {
        "toot": (0.28, 0.55),
        "rasp": (0.55, 1.05),
        "wet": (0.40, 0.85),
        "squeak": (0.22, 0.45),
        "rumble": (0.85, 1.55),
        "flutter": (0.45, 0.95),
        "hollow": (0.40, 0.90),
        "burst": (0.18, 0.40),
        "mixed": (0.55, 1.20),
    }
    lo, hi = dur_map.get(style, (0.4, 0.95))
    dur = lo + rng.random() * (hi - lo)
    n = int(SR * dur)

    # Prefer lower, less musical centers
    f_center = {
        "toot": (70, 130),
        "rasp": (45, 95),
        "wet": (40, 90),
        "squeak": (110, 190),
        "rumble": (22, 48),
        "flutter": (50, 100),
        "hollow": (35, 75),
        "burst": (55, 120),
        "mixed": (40, 100),
    }[style if style in STYLES else "mixed"]
    band = f_center[0] + rng.random() * (f_center[1] - f_center[0])

    attack = 0.008 + rng.random() * 0.035
    hold = dur * (0.18 + rng.random() * 0.4)
    release = max(0.1, dur - attack - hold)

    wet = style in ("wet", "mixed", "flutter")
    flutter = style in ("flutter", "rasp", "mixed")
    bursty = style in ("burst", "toot", "squeak")

    lp1 = [0.0]
    lp2 = [0.0]
    lp3 = [0.0]
    hp1 = [0.0]
    bub = [0.0]
    pressure = 0.75 + rng.random() * 0.2
    flutter_hold = 1.0
    flutter_timer = 0.0

    # Pink-ish noise state (Paul Kellet approx simplified)
    b0 = b1 = b2 = 0.0
    out = [0.0] * n

    for i in range(n):
        t = i / SR
        tn = t / dur
        env = envelope(t, attack, hold, release)

        # Random-walk pressure (organic, not a sine AM)
        pressure += rng.uniform(-0.045, 0.045)
        pressure = clamp(pressure, 0.25, 1.15)
        # Occasional sphincter flutters / drops
        if flutter:
            flutter_timer -= 1.0 / SR
            if flutter_timer <= 0.0:
                flutter_hold = 0.35 + rng.random() * 0.75
                flutter_timer = 0.02 + rng.random() * (0.08 if style == "flutter" else 0.14)
            pressure *= 0.55 + 0.45 * flutter_hold

        # Slow downward pitch drift as air escapes
        band_now = band * (1.0 - 0.18 * tn) * (0.92 + 0.08 * pressure)

        # White -> pink-ish
        w = rng.uniform(-1.0, 1.0)
        b0 = 0.99886 * b0 + w * 0.0555179
        b1 = 0.99332 * b1 + w * 0.0750759
        b2 = 0.96900 * b2 + w * 0.1538520
        pink = (b0 + b1 + b2 + w * 0.3) * 0.2

        # Band-pass around body cavity (noise-led — avoids cartoon sine toot)
        # Map band Hz roughly to one-pole alphas
        a_lp = clamp(band_now / 900.0, 0.03, 0.35)
        a_hp = clamp(band_now / 4500.0, 0.008, 0.08)
        if style == "hollow":
            a_lp *= 0.65
            a_hp *= 0.7
        elif style == "squeak":
            a_lp *= 1.35
            a_hp *= 1.4
        elif style == "rumble":
            a_lp *= 0.45
            a_hp *= 0.5

        x = pink * pressure
        x = lp(x, lp1, a_lp)
        x = hp(x, hp1, a_hp)
        x = lp(x, lp2, a_lp * 0.85)

        # Very quiet sub thump (not a musical tone)
        sub = math.sin(2 * math.pi * (band_now * 0.45) * t) * 0.08 * pressure
        sub = lp(sub, lp3, 0.04)

        # Wet bubbles
        bubbles = 0.0
        if wet and rng.random() < (0.01 + 0.025 * (1.0 - tn)):
            bubbles = rng.uniform(0.25, 0.8) * (1.0 - 0.4 * tn)
        bubbles = lp(bubbles, bub, 0.4)

        # Style mix — noise first
        if style == "toot":
            mix = x * 0.78 + sub * 0.18 + pink * 0.08
        elif style == "rasp":
            mix = x * 0.88 + sub * 0.08 + pink * 0.12
        elif style == "wet":
            mix = x * 0.62 + bubbles * 0.4 + sub * 0.1 + pink * 0.08
        elif style == "squeak":
            mix = x * 0.85 + sub * 0.05 + pink * 0.06
        elif style == "rumble":
            mix = x * 0.7 + sub * 0.35 + pink * 0.12
        elif style == "flutter":
            mix = x * 0.9 + sub * 0.08 + pink * 0.1
        elif style == "hollow":
            mix = x * 0.7 + sub * 0.2 + pink * 0.18
        elif style == "burst":
            gate = 1.15 if tn < 0.28 else max(0.0, 1.35 - tn * 2.4)
            mix = (x * 0.75 + pink * 0.28 + sub * 0.1) * gate
        else:
            mix = x * 0.72 + bubbles * 0.18 + sub * 0.12 + pink * 0.08

        # Valve open transient (short, not a click beep)
        if t < 0.014:
            mix += pink * (1.0 - t / 0.014) * (0.45 if bursty else 0.22)

        # Air leak at the end
        mix += pink * 0.1 * (tn ** 1.7)

        out[i] = clamp(softsat(mix * env * 0.95))

    peak = max(1e-6, max(abs(x) for x in out))
    gain = 0.82 / peak
    out = [clamp(x * gain) for x in out]

    # Short soft reflection (body/room) — keeps it from sounding dry/synthy
    delay = int(0.009 * SR)
    amt = 0.14
    delayed = [0.0] * n
    for i in range(n):
        d = out[i - delay] * amt if i >= delay else 0.0
        delayed[i] = clamp(out[i] * 0.92 + d)
    return delayed


def write_wav(path: str, samples: list[float]) -> None:
    with wave.open(path, "w") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        frames = b"".join(struct.pack("<h", int(clamp(s) * 32767.0)) for s in samples)
        w.writeframes(frames)


def main() -> None:
    marker = os.path.join(OUT, ".hq_real_v1")
    if os.path.exists(marker):
        print("REAL sound bank present (.hq_real_v1). Refusing to overwrite with synth.")
        print("Delete that marker file if you really want synthetic clips again.")
        return

    os.makedirs(OUT, exist_ok=True)
    # Clear old bank so stale thin clips don't linger under new names
    for name in os.listdir(OUT):
        if name.lower().endswith(".wav") or name.startswith(".hq_"):
            try:
                os.remove(os.path.join(OUT, name))
            except OSError:
                pass

    idx = 1
    for round_i in range(6):
        for style in STYLES:
            if idx > 48:
                break
            seed = 22000 + idx * 131 + round_i * 17
            samples = synth_fart(style, seed)
            name = f"Fart_{idx:02d}_{style}.wav"
            path = os.path.join(OUT, name)
            write_wav(path, samples)
            print(f"wrote {name} ({len(samples)/SR:.2f}s)")
            idx += 1
        if idx > 48:
            break

    with open(os.path.join(OUT, ".hq_v4"), "w", encoding="utf-8") as f:
        f.write("organic noise-led 44.1k bank v4\n")
    print(f"done -> {OUT}")


if __name__ == "__main__":
    main()
