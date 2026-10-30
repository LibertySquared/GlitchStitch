"""Conservative local outcome-text evidence; no external model or upload."""
import concurrent.futures
import json
import os
from pathlib import Path
import re
import subprocess

TOKENS = {"ELIMINATION", "ELIMINATED", "KILLED", "KILL"}

def markers(image):
    if image.get("error"): return set()
    text = image.get("text", "").upper()
    if re.search(r"ELIMINATED BY|YOU (?:WERE )?KILLED|YOU DIED|YOU (?:WERE|ARE|HAVE BEEN) ELIMINATED|YOU PLACED", text): return set()
    found = set()
    for word in image.get("words", []):
        raw = re.sub(r"[^A-Za-z]", "", word["text"])
        x = (word["x"] + word["width"] / 2) / max(1, image.get("width", 1))
        y = (word["y"] + word["height"] / 2) / max(1, image.get("height", 1))
        if raw in TOKENS and .25 <= x <= .75 and .08 <= y <= .95:
            found.add(raw)
    return found

def supported_candidates(images):
    grouped = {}
    for image in images: grouped.setdefault(image["candidate"], []).append(image)
    accepted = set()
    for index, group in grouped.items():
        baseline = set().union(*(markers(i) for i in group if i["phase"] == "baseline"))
        events = set().union(*(markers(i) for i in group if i["phase"] == "event"))
        if events - baseline: accepted.add(index)
    return accepted

def verify(ffmpeg, video, detections, directory, use_gpu=False):
    folder = Path(directory); folder.mkdir(parents=True, exist_ok=True)
    requests = []
    for index, detection in enumerate(detections):
        timestamp = detection["eventStart"]
        times = [(max(0, timestamp - 6), "baseline")] if timestamp >= 6 else []
        times += [(max(0, timestamp + offset), "event") for offset in (-1, 0, 1)]
        for frame, (seconds, phase) in enumerate(times):
            requests.append({"candidate": index, "phase": phase, "time": seconds, "path": str((folder / f"candidate-{index}-{frame}.jpg").resolve())})

    def extract(request):
        command = [str(ffmpeg), "-y", "-v", "error", "-ss", str(request["time"]), "-i", str(video), "-frames:v", "1", "-vf", "scale=1600:-2", request["path"]]
        options = {"stdout": subprocess.DEVNULL, "stderr": subprocess.PIPE, "check": False, "timeout": 30}
        if os.name == "nt": options["creationflags"] = subprocess.CREATE_NO_WINDOW
        result = subprocess.run(command[:4] + ["-hwaccel", "cuda"] + command[4:], **options) if use_gpu else None
        if result is None or result.returncode: result = subprocess.run(command, **options)
        return request if result.returncode == 0 else None

    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        images = [image for image in pool.map(extract, requests) if image is not None]
    manifest = folder / "input.json"; output = folder / "output.json"
    manifest.write_text(json.dumps({"images": images}), encoding="utf-8")
    options = {"stdout": subprocess.PIPE, "stderr": subprocess.PIPE, "text": True, "timeout": 120}
    if os.name == "nt": options["creationflags"] = subprocess.CREATE_NO_WINDOW
    result = subprocess.run(["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(Path(__file__).with_name("verify_ocr.ps1")), "-Manifest", str(manifest.resolve()), "-OutputPath", str(output.resolve())], **options)
    if result.returncode: raise RuntimeError("Windows outcome-text verification could not run.")
    data = json.loads(output.read_text(encoding="utf-8"))
    if not data.get("available"): raise RuntimeError(data.get("error", "Windows outcome-text verification is unavailable."))
    if not data.get("images") or all(image.get("error") for image in data["images"]):
        raise RuntimeError("No candidate frames could be read for outcome verification.")
    accepted = supported_candidates(data["images"])
    return [detection for index, detection in enumerate(detections) if index in accepted], data
