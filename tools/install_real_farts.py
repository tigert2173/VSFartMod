"""Install royalty-free / CC0 real fart WAVs into VSFartSounds/Farts (44.1k mono 16-bit)."""
from __future__ import annotations

import struct
import wave
from pathlib import Path

SRC_DIRS = [
    Path(r"C:\Users\Tiger\Downloads\VS Modding\_fart_sfx_tmp\jg"),
    Path(r"C:\Users\Tiger\Downloads\VS Modding\_fart_sfx_tmp"),
    Path(r"C:\Users\Tiger\Downloads\VS Modding\_fart_sfx_tmp\fsb"),
]
DEST = Path(
    r"C:\Users\Tiger\Downloads\VS Modding\VS48R1 PCFULL\Virtual Succubus_Data\StreamingAssets\VSFartSounds\Farts"
)
TARGET_SR = 44100


def read_wav(path: Path):
    with wave.open(str(path), "rb") as w:
        ch = w.getnchannels()
        sw = w.getsampwidth()
        sr = w.getframerate()
        raw = w.readframes(w.getnframes())
    if sw != 2:
        raise ValueError(f"unsupported width {sw}")
    count = len(raw) // 2
    samples = [struct.unpack_from("<h", raw, i * 2)[0] / 32768.0 for i in range(count)]
    if ch > 1:
        samples = [sum(samples[i : i + ch]) / ch for i in range(0, len(samples), ch)]
    return samples, sr


def resample(samples, sr_from, sr_to):
    if sr_from == sr_to:
        return samples
    ratio = sr_to / sr_from
    n = int(len(samples) * ratio)
    out = [0.0] * n
    last = len(samples) - 1
    for i in range(n):
        x = i / ratio
        i0 = int(x)
        i1 = min(i0 + 1, last)
        f = x - i0
        out[i] = samples[i0] * (1 - f) + samples[i1] * f
    return out


def trim_silence(samples, thr=0.008):
    if not samples:
        return samples
    a = 0
    while a < len(samples) and abs(samples[a]) < thr:
        a += 1
    b = len(samples) - 1
    while b > a and abs(samples[b]) < thr:
        b -= 1
    a = max(0, a - int(0.01 * TARGET_SR))
    b = min(len(samples) - 1, b + int(0.05 * TARGET_SR))
    return samples[a : b + 1]


def normalize(samples, peak=0.88):
    m = max((abs(x) for x in samples), default=0.0)
    if m < 1e-6:
        return samples
    g = peak / m
    return [max(-1.0, min(1.0, x * g)) for x in samples]


def write_wav(path: Path, samples):
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(TARGET_SR)
        frames = b"".join(
            struct.pack("<h", int(max(-1.0, min(1.0, s)) * 32767.0)) for s in samples
        )
        w.writeframes(frames)


def main() -> None:
    DEST.mkdir(parents=True, exist_ok=True)
    for old in DEST.glob("*.wav"):
        old.unlink()
    for old in DEST.glob(".hq_*"):
        old.unlink()

    files = []
    for d in SRC_DIRS:
        if not d.exists():
            continue
        for p in d.rglob("*.wav"):
            if "__MACOSX" in str(p) or p.name.startswith("._"):
                continue
            files.append(p)
    files = sorted(set(files), key=lambda p: p.name.lower())
    print(f"sources: {len(files)}")

    idx = 1
    for p in files:
        try:
            samples, sr = read_wav(p)
            samples = resample(samples, sr, TARGET_SR)
            samples = trim_silence(samples)
            if len(samples) < int(0.07 * TARGET_SR):
                print("skip short", p.name)
                continue
            samples = normalize(samples)
            out = DEST / f"Fart_{idx:02d}_real.wav"
            write_wav(out, samples)
            print(f"wrote {out.name} <- {p.name} ({len(samples)/TARGET_SR:.2f}s)")
            idx += 1
        except Exception as e:
            print("fail", p.name, e)

    (DEST / ".hq_real_v1").write_text(
        "real royalty-free/CC0 recordings, 44.1k mono 16-bit\n", encoding="utf-8"
    )
    print(f"done: {idx-1} clips -> {DEST}")


if __name__ == "__main__":
    main()
