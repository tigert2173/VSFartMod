"""Install CC0 BigSoundBank burp WAVs into VSFartSounds/Burps (44.1k mono 16-bit).

Expects raw downloads in _burp_sfx_tmp/bsb_*.bwf.wav (or *.mp3).
Download example:
  https://bigsoundbank.com/UPLOAD/bwf-en/3250.wav
"""
from __future__ import annotations

import struct
import subprocess
import wave
from pathlib import Path

try:
    import imageio_ffmpeg
except ImportError as e:
    raise SystemExit("pip install imageio-ffmpeg") from e

TMP = Path(r"C:\Users\Tiger\Downloads\VS Modding\_burp_sfx_tmp")
DEST = Path(
    r"C:\Users\Tiger\Downloads\VS Modding\VS48R1 PCFULL\Virtual Succubus_Data\StreamingAssets\VSFartSounds\Burps"
)
TARGET_SR = 44100
FF = imageio_ffmpeg.get_ffmpeg_exe()


def convert_to_pcm(src: Path) -> Path:
    out = TMP / (src.stem + "_pcm16.wav")
    r = subprocess.run(
        [FF, "-y", "-i", str(src), "-ac", "1", "-ar", str(TARGET_SR), "-sample_fmt", "s16", str(out)],
        capture_output=True,
        text=True,
    )
    if r.returncode != 0 or not out.exists():
        raise RuntimeError(r.stderr[-400:] if r.stderr else "ffmpeg failed")
    return out


def read_wav(path: Path):
    with wave.open(str(path), "rb") as w:
        ch = w.getnchannels()
        sw = w.getsampwidth()
        sr = w.getframerate()
        raw = w.readframes(w.getnframes())
    if sw != 2:
        raise ValueError(f"unsupported width {sw}")
    samples = [struct.unpack_from("<h", raw, i * 2)[0] / 32768.0 for i in range(len(raw) // 2)]
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


def trim_silence(samples, thr=0.01):
    if not samples:
        return samples
    a = 0
    while a < len(samples) and abs(samples[a]) < thr:
        a += 1
    b = len(samples) - 1
    while b > a and abs(samples[b]) < thr:
        b -= 1
    pad = int(0.01 * TARGET_SR)
    a = max(0, a - pad)
    b = min(len(samples) - 1, b + int(0.04 * TARGET_SR))
    return samples[a : b + 1]


def normalize(samples, peak=0.9):
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


def split_on_silence(samples, min_gap=0.18, thr=0.02, min_len=0.12):
    gap = int(min_gap * TARGET_SR)
    min_samples = int(min_len * TARGET_SR)
    segs = []
    i = 0
    n = len(samples)
    while i < n:
        while i < n and abs(samples[i]) < thr:
            i += 1
        if i >= n:
            break
        start = i
        quiet = 0
        while i < n:
            if abs(samples[i]) < thr:
                quiet += 1
                if quiet >= gap:
                    break
            else:
                quiet = 0
            i += 1
        end = i - quiet
        if end - start >= min_samples:
            segs.append(samples[start:end])
        i = end + quiet
    return segs if segs else [samples]


def pitch_up(samples, ratio=1.08):
    n = int(len(samples) / ratio)
    if n < 8:
        return samples
    out = [0.0] * n
    last = len(samples) - 1
    for i in range(n):
        x = i * ratio
        i0 = int(x)
        i1 = min(i0 + 1, last)
        f = x - i0
        out[i] = samples[i0] * (1 - f) + samples[i1] * f
    return normalize(out)


def main() -> None:
    DEST.mkdir(parents=True, exist_ok=True)
    for old in DEST.glob("*.wav"):
        old.unlink()
    for old in DEST.glob(".hq_*"):
        old.unlink()

    sources = sorted(TMP.glob("bsb_*.bwf.wav")) + sorted(TMP.glob("bsb_*.mp3"))
    if not sources:
        raise SystemExit(f"No sources in {TMP}")

    idx = 1
    for src in sources:
        pcm = convert_to_pcm(src)
        samples, sr = read_wav(pcm)
        samples = resample(samples, sr, TARGET_SR)
        samples = trim_silence(samples)
        parts = split_on_silence(samples) if "0242" in src.name else [samples]
        for part in parts:
            part = normalize(part)
            if len(part) < int(0.08 * TARGET_SR):
                print("skip short", src.name)
                continue
            out = DEST / f"Burp_{idx:02d}_real.wav"
            write_wav(out, part)
            print(f"wrote {out.name} ({len(part)/TARGET_SR:.2f}s) <- {src.name}")
            idx += 1
            if len(part) > int(0.15 * TARGET_SR):
                pitched = pitch_up(part)
                out2 = DEST / f"Burp_{idx:02d}_real.wav"
                write_wav(out2, pitched)
                print(f"wrote {out2.name} ({len(pitched)/TARGET_SR:.2f}s) (pitch+)")
                idx += 1

    (DEST / ".hq_real_v1").write_text(
        "CC0 BigSoundBank burps, 44.1k mono 16-bit\n", encoding="utf-8"
    )
    (DEST / "README.txt").write_text(
        "Real CC0 burp recordings from BigSoundBank. Plugin loads all *.wav here.\n",
        encoding="utf-8",
    )
    print(f"TOTAL {idx - 1} clips -> {DEST}")


if __name__ == "__main__":
    main()
