"""Prompt-guided audio event scanner backed by Google's pretrained YAMNet."""
import argparse
import csv
import json
import os
import re
import sys
from pathlib import Path

# The bundled embeddable Python runtime uses an isolated ._pth file and does
# not automatically add the worker's directory to its import search path.
sys.path.insert(0, str(Path(__file__).resolve().parent))
from audio_prompt import resolve_request

os.environ.setdefault("TF_CPP_MIN_LOG_LEVEL", "2")
os.environ.setdefault("TF_USE_LEGACY_KERAS", "1")

import numpy as np
from scipy.io import wavfile
from scipy.signal import resample_poly
try:
    import pkg_resources
except ModuleNotFoundError:
    # Newer Python packaging environments may omit setuptools' legacy module;
    # TensorFlow Hub only needs version parsing from it during initialization.
    import types
    from packaging.version import parse as parse_version
    pkg_resources = types.ModuleType("pkg_resources")
    pkg_resources.parse_version = parse_version
    sys.modules["pkg_resources"] = pkg_resources
import tensorflow_hub as hub

MODEL_URL = "https://tfhub.dev/google/yamnet/1"
RATE = 16000
PATCH_HOP_SECONDS = 0.48
CHUNK_SECONDS = 9.6

def read_audio(path):
    rate, audio = wavfile.read(path)
    if np.issubdtype(audio.dtype, np.integer):
        info = np.iinfo(audio.dtype)
        audio = audio.astype(np.float32) / max(abs(info.min), info.max)
    else:
        audio = np.nan_to_num(audio.astype(np.float32))
        audio = np.clip(audio, -1.0, 1.0)
    if audio.ndim > 1:
        audio = audio.mean(axis=1)
    if rate != RATE:
        from math import gcd
        factor = gcd(RATE, int(rate))
        audio = resample_poly(audio, RATE // factor, int(rate) // factor).astype(np.float32)
    return audio

def scan_track(path, label, model, selected, names, threshold):
    audio = read_audio(path)
    hop = int(PATCH_HOP_SECONDS * RATE)
    chunk = int(CHUNK_SECONDS * RATE)
    all_frames = []
    starts = []
    offset = 0
    while offset < len(audio):
        end = min(len(audio), offset + chunk)
        wave = audio[offset:end]
        if len(wave) >= int(.975 * RATE):
            scores, _, _ = model(wave.astype(np.float32))
            matrix = scores.numpy()
            for frame_index, row in enumerate(matrix):
                timestamp = offset / RATE + frame_index * PATCH_HOP_SECONDS
                if starts and timestamp < starts[-1] + PATCH_HOP_SECONDS * .5:
                    continue
                starts.append(timestamp)
                all_frames.append(row)
            if len(wave) < chunk:
                break
        offset += chunk - hop
        print("SCANNING|{}|{:.1f}/{:.1f}".format(label, min(offset, len(audio)) / RATE, len(audio) / RATE), flush=True)
    print("SCANNING|{}|{:.1f}/{:.1f}".format(label, len(audio) / RATE, len(audio) / RATE), flush=True)
    if not all_frames:
        return []
    matrix = np.asarray(all_frames)
    events = []
    for class_index in selected:
        active = matrix[:, class_index] >= threshold
        i = 0
        while i < len(active):
            if not active[i]:
                i += 1
                continue
            j = i + 1
            while j < len(active) and (active[j] or (j + 1 < len(active) and active[j + 1])):
                j += 1
            peak = float(np.max(matrix[i:j, class_index]))
            events.append({"start": starts[i], "end": starts[min(j, len(starts)-1)] + PATCH_HOP_SECONDS,
                           "confidence": peak, "event": names[class_index], "track": label})
            i = j
    return events

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input-manifest")
    parser.add_argument("--output")
    parser.add_argument("--query")
    parser.add_argument("--threshold", type=float, default=.15)
    parser.add_argument("--prepare-model", action="store_true")
    args = parser.parse_args()
    model = hub.load(MODEL_URL)
    if args.prepare_model:
        print("YAMNet model downloaded and ready.", flush=True)
        return
    if not args.input_manifest or not args.output or not args.query:
        parser.error("--input-manifest, --output, and --query are required for a scan")
    with open(model.class_map_path().numpy().decode("utf-8"), newline="", encoding="utf-8") as file:
        names = [row["display_name"] for row in csv.DictReader(file)]
    selected, notices = resolve_request(args.query, names)
    for notice in notices: print("NOTICE|" + notice, flush=True)
    print("CLASSES|" + ", ".join(names[i] for i in selected), flush=True)
    manifest = json.loads(Path(args.input_manifest).read_text(encoding="utf-8"))
    detections = []
    for track in manifest["tracks"] if selected else []:
        detections.extend(scan_track(track["path"], track["label"], model, selected, names, args.threshold))
    Path(args.output).write_text(json.dumps({"segments": detections, "notices": notices}, indent=2), encoding="utf-8")
    print("Completed: {} audio event candidates".format(len(detections)), flush=True)

if __name__ == "__main__":
    main()
