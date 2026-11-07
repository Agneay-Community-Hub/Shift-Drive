"""Edit the SHIFT DRIVE trailer from a "-trailer <dir>" recording.

    python tools/make_trailer.py <recording_dir> <output.mp4>

The recording dir holds frames/f00000.jpg..., frames.csv, events.csv and audio/*.wav
(written by TrailerRecorder.cs). This script cuts the footage, adds captions and
flash cuts with ffmpeg, and rebuilds the soundtrack (music, engine, SFX) with numpy
from the logged per-frame car state and events, so audio matches the picture exactly.
"""
import csv
import os
import shutil
import subprocess
import sys
import wave

import numpy as np

FPS = 30
SR = 44100
FONT = "C\\:/Windows/Fonts/arialbd.ttf"
FONT_TITLE = "C\\:/Windows/Fonts/impact.ttf"
GEAR_HEX = {1: "1AE6FF", 2: "FF33D9", 3: "8CFF40", 4: "FF731A"}

# (src_start, src_end) frame ranges, in order.
EDIT = [
    (15, 105),     # title orbit
    (245, 281),    # launch in 1st
    (281, 324),    # flip cam: onto the ceiling
    (334, 392),    # ceiling chase, flip back down
    (600, 656),    # crusher hall, high cam
    (668, 760),    # slow-mo blades, chase
    (1150, 1224),  # overdrive runway
    (1224, 1262),  # jump cam into the sun
    (1262, 1290),  # landing
    (1470, 1520),  # upside-down slalom
    (1660, 1720),  # final blades
    (1800, 1935),  # last run, jump, finish
    (1935, 2056),  # end card background
]

# Captions: (first edit index, last edit index, line1, line2, colour hex or None, size)
CAPTIONS = [
    (0, 0, "EVERY GEAR", "CHANGES THE RULES", None, 96),
    (1, 1, "1ST", "GRIP", "1AE6FF", 120),
    (2, 3, "2ND", "GRAVITY FLIPS", "FF33D9", 120),
    (4, 5, "3RD", "TIME SLOWS", "8CFF40", 120),
    (6, 8, "4TH", "NO BRAKES", "FF731A", 120),
    (9, 9, "FLIP IT.", None, "FF33D9", 130),
    (10, 10, "SLOW IT.", None, "8CFF40", 130),
    (11, 11, "SEND IT.", None, "FF731A", 130),
]


def read_wav(path):
    with wave.open(path) as w:
        sr = w.getframerate()
        data = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float32) / 32767.0
    if sr != SR:  # resample (music is 22050 Hz)
        x = np.arange(0, len(data), sr / SR)
        data = np.interp(x, np.arange(len(data)), data).astype(np.float32)
    return data


def esc(t):
    return t.replace(":", "\\:").replace("'", "’")


def main(rec, out):
    rows = list(csv.DictReader(open(os.path.join(rec, "frames.csv"))))
    events = list(csv.DictReader(open(os.path.join(rec, "events.csv"))))
    audio = {n[:-4]: read_wav(os.path.join(rec, "audio", n)) for n in os.listdir(os.path.join(rec, "audio"))}

    # ---------------------------------------------------------------- picture
    tmp = os.path.join(rec, "edit")
    shutil.rmtree(tmp, ignore_errors=True)
    os.makedirs(tmp)
    src_of_out = []
    seg_start = []
    for a, b in EDIT:
        seg_start.append(len(src_of_out))
        for f in range(a, b):
            src = os.path.join(rec, "frames", f"f{f:05d}.jpg")
            dst = os.path.join(tmp, f"o{len(src_of_out):05d}.jpg")
            try:
                os.link(src, dst)
            except OSError:
                shutil.copy(src, dst)
            src_of_out.append(f)
    seg_start.append(len(src_of_out))
    n_out = len(src_of_out)
    dur = n_out / FPS
    t = lambda i: seg_start[i] / FPS

    vf = ["scale=1920:1080"]
    # flash on every cut after the title
    for i in range(1, len(EDIT)):
        a = t(i)
        vf.append(f"drawbox=x=0:y=0:w=iw:h=ih:color=white@0.35:t=fill:enable='between(t,{a:.3f},{a + 0.05:.3f})'")
    # letterbox bars for a cinematic frame
    vf.append("drawbox=x=0:y=0:w=iw:h=70:color=black:t=fill")
    vf.append("drawbox=x=0:y=ih-70:w=iw:h=70:color=black:t=fill")

    for i0, i1, l1, l2, col, size in CAPTIONS:
        a, b = t(i0), t(i1 + 1)
        fade = f"if(lt(t,{a + 0.15:.3f}),(t-{a:.3f})/0.15,if(gt(t,{b - 0.15:.3f}),({b:.3f}-t)/0.15,1))"
        en = f"enable='between(t,{a:.3f},{b:.3f})'"
        c1 = f"0x{col}" if col else "white"
        if l2 is None:
            vf.append(f"drawtext=fontfile='{FONT_TITLE}':text='{esc(l1)}':fontsize={size}:fontcolor={c1}:"
                      f"x=(w-text_w)/2:y=h*0.62:shadowcolor=black@0.7:shadowx=5:shadowy=5:alpha='{fade}':{en}")
        elif col is None:
            vf.append(f"drawtext=fontfile='{FONT}':text='{esc(l1)}':fontsize={size}:fontcolor=white:"
                      f"x=(w-text_w)/2:y=h*0.36:shadowcolor=black@0.7:shadowx=4:shadowy=4:alpha='{fade}':{en}")
            vf.append(f"drawtext=fontfile='{FONT}':text='{esc(l2)}':fontsize={size}:fontcolor=white:"
                      f"x=(w-text_w)/2:y=h*0.36+{size * 1.15:.0f}:shadowcolor=black@0.7:shadowx=4:shadowy=4:alpha='{fade}':{en}")
        else:
            # gear badge + name, lower left
            vf.append(f"drawbox=x=110:y=ih-330:w=170:h=170:color=0x{col}@0.92:t=fill:{en}")
            vf.append(f"drawtext=fontfile='{FONT_TITLE}':text='{esc(l1)}':fontsize=80:fontcolor=black:"
                      f"x=110+(170-text_w)/2:y=h-330+(170-text_h)/2:{en}")
            vf.append(f"drawtext=fontfile='{FONT_TITLE}':text='{esc(l2)}':fontsize={size}:fontcolor=white:"
                      f"x=310:y=h-330+(170-text_h)/2:shadowcolor=black@0.8:shadowx=5:shadowy=5:alpha='{fade}':{en}")

    # end card
    e = t(len(EDIT) - 1)
    en = f"enable='gte(t,{e:.3f})'"
    fin = f"min(1,(t-{e:.3f})/0.5)"
    vf.append(f"drawbox=x=0:y=0:w=iw:h=ih:color=black@0.55:t=fill:{en}")
    letters = "SHIFT"
    for k, ch in enumerate(letters):
        col = GEAR_HEX[1 + k % 4] if k < 4 else GEAR_HEX[1]
        x = 960 - 2.5 * 150 + k * 150
        vf.append(f"drawtext=fontfile='{FONT_TITLE}':text='{ch}':fontsize=230:fontcolor=0x{col}:"
                  f"x={x:.0f}+(150-text_w)/2:y=230:shadowcolor=black@0.8:shadowx=6:shadowy=6:alpha='{fin}':{en}")
    for txt, y, size, alpha in [("D R I V E", 490, 110, fin),
                                ("A racing game where every gear changes the rules", 650, 46, fin),
                                ("GitHub Game Off 2026  ·  Theme\\: SHIFT", 760, 40, fin),
                                ("Play free in your browser on itch.io", 830, 40, fin)]:
        vf.append(f"drawtext=fontfile='{FONT}':text='{esc(txt) if 'Theme' not in txt else txt}':fontsize={size}:fontcolor=white:"
                  f"x=(w-text_w)/2:y={y}:shadowcolor=black@0.8:shadowx=4:shadowy=4:alpha='{alpha}':{en}")
    # fade in from / out to black
    vf.append(f"fade=t=in:st=0:d=0.6,fade=t=out:st={dur - 0.8:.3f}:d=0.8")

    filt_path = os.path.join(rec, "vf.txt")
    open(filt_path, "w", encoding="utf-8").write(",\n".join(vf))

    # ---------------------------------------------------------------- sound
    n_s = int(dur * SR) + SR
    mix = np.zeros(n_s, dtype=np.float32)
    row = {int(r["frame"]): r for r in rows}
    top = [0, 34, 42, 48, 92]

    def per_sample(fn):
        vals = np.array([fn(row[f]) for f in src_of_out], dtype=np.float32)
        x = np.arange(n_s) / SR * FPS
        return np.interp(x, np.arange(n_out), vals)

    # engine: loop resampled at a pitch driven by speed / gear top speed
    def eng_pitch(r):
        g = int(r["gear"]); spd = abs(float(r["speed"]))
        p = 0.55 + 1.35 * min(1.0, spd / top[g])
        if g == 3: p *= 0.85
        if r["grounded"] == "0": p += 0.15
        return p

    def eng_vol(r):
        if r["wrecked"] == "1": return 0.0
        if r["state"] == "Title": return 0.08
        g = int(r["gear"])
        return 0.14 + 0.1 * min(1.0, abs(float(r["speed"])) / top[g])

    pitch = per_sample(eng_pitch)
    eng = audio["engine"]
    idx = np.cumsum(pitch)
    mix += eng[(idx.astype(np.int64)) % len(eng)] * per_sample(eng_vol) * 1.2

    # music: continuous across cuts, slowed while world time is slowed
    rate = per_sample(lambda r: 0.78 if float(r["worldScale"]) < 0.99 else 1.0)
    mus = audio["music"]
    idx = np.cumsum(rate)
    music = mus[idx.astype(np.int64) % len(mus)] * 0.55
    env = np.ones(n_s, dtype=np.float32)
    fade_out = int(2.0 * SR)
    end = int(dur * SR)
    env[end - fade_out:end] = np.linspace(1, 0, fade_out)
    env[end:] = 0
    env[: int(0.6 * SR)] = np.linspace(0, 1, int(0.6 * SR))
    mix += music * env

    # SFX at their (re-timed) frames
    out_of_src = {}
    for o, f in enumerate(src_of_out):
        out_of_src.setdefault(f, o)

    def put(clip, o, gain):
        s = int(o / FPS * SR)
        n = min(len(clip), n_s - s)
        if n > 0: mix[s:s + n] += clip[:n] * gain

    for ev in events:
        f = int(ev["frame"])
        if f not in out_of_src: continue
        o, typ, arg = out_of_src[f], ev["type"], int(ev["arg"])
        if typ == "shiftup": put(audio[f"shift{arg}"], o, 0.9)
        elif typ == "shiftdown": put(audio["shiftdown"], o, 0.9)
        elif typ == "land": put(audio["thump"], o, 0.3 + 0.6 * min(1, arg / 150))
        elif typ == "slam": put(audio["thump"], o, 0.7 * arg / 100)
        elif typ == "wreck": put(audio["crash"], o, 1.0)
    fin_frame = next((int(r["frame"]) for r in rows if r["state"] == "Finished"), None)
    if fin_frame in out_of_src: put(audio["chime"], out_of_src[fin_frame], 1.0)

    mix = np.tanh(mix * 1.1) * 0.9
    mix = mix[:end]
    pcm = (np.clip(mix, -1, 1) * 32767).astype(np.int16)
    wav_path = os.path.join(rec, "trailer_audio.wav")
    with wave.open(wav_path, "w") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(pcm.tobytes())

    # ---------------------------------------------------------------- encode
    cmd = ["ffmpeg", "-v", "error", "-y", "-framerate", str(FPS), "-i", os.path.join(tmp, "o%05d.jpg"),
           "-i", wav_path, "-filter_script:v", filt_path,
           "-c:v", "libx264", "-preset", "slow", "-crf", "18", "-pix_fmt", "yuv420p",
           "-c:a", "aac", "-b:a", "192k", "-ac", "2", "-shortest", "-movflags", "+faststart", out]
    subprocess.run(cmd, check=True)
    print(f"wrote {out}: {n_out} frames, {dur:.1f}s")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
