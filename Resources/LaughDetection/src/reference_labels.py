"""Filename labels for explicitly named capture highlights; no model dependencies."""
import re
import os
import subprocess
from pathlib import Path

CACHE_VERSION = "reference-labels-v2-tail-sampling"

def extract_reference_frames(ffmpeg, clip, pattern, use_gpu=False):
    command = [str(ffmpeg), "-hide_banner", "-loglevel", "error", "-y", "-threads", "2",
               "-sseof", "-6", "-i", str(clip), "-vf",
               "fps=1,scale=224:224:force_original_aspect_ratio=decrease,pad=224:224:(ow-iw)/2:(oh-ih)/2",
               "-frames:v", "6", "-q:v", "3", str(pattern)]
    options = {"stdout": subprocess.DEVNULL, "stderr": subprocess.PIPE, "check": False}
    if os.name == "nt": options["creationflags"] = subprocess.BELOW_NORMAL_PRIORITY_CLASS | subprocess.CREATE_NO_WINDOW
    result = None
    if use_gpu:
        gpu_command = command[:7] + ["-hwaccel", "cuda"] + command[7:]
        result = subprocess.run(gpu_command, **options)
    if result is None or result.returncode != 0:
        result = subprocess.run(command, **options)
    if result.returncode != 0: return []
    pattern = Path(pattern)
    return sorted(pattern.parent.glob(pattern.name.replace("%02d", "*")))

def reference_label(filename):
    words = set(re.findall(r"[a-z]+", filename.lower()))
    if "eliminated" in words:
        return "Player eliminated"
    if words & {"elimination", "eliminations"}:
        return "Elimination"
    if words & {"down", "downs", "knock", "knocked"}:
        return "Down"
    if words & {"win", "wins", "victory"}:
        return "Win"
    return None

def target_labels(query):
    words = set(re.findall(r"[a-z]+", query.lower()))
    labels = set()
    if words & {"elimination", "eliminations", "elim", "kill", "kills"}: labels.add("Elimination")
    if words & {"down", "downs", "knock", "knocked", "knockdown"}: labels.add("Down")
    if words & {"win", "wins", "victory", "victories"}: labels.add("Win")
    if words & {"eliminated", "death", "deaths", "died", "dying"}: labels.add("Player eliminated")
    return labels
