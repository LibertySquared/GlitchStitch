"""Inventory local test footage without modifying it; rerun to include new files."""
import argparse
import concurrent.futures
import json
from pathlib import Path
import subprocess

project = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser()
parser.add_argument("--root", default="D:/Videos")
args = parser.parse_args()
root = Path(args.root)
if not root.is_dir():
    raise SystemExit(f"Footage folder not found: {root}")
files = sorted(p for p in root.rglob("*") if p.is_file() and p.suffix.lower() in {".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm"})

def probe(path):
    try:
        result = subprocess.run([str(project / "tools/ffprobe.exe"), "-v", "error", "-show_streams", "-show_format", "-of", "json", str(path)], capture_output=True, text=True, timeout=30)
        if result.returncode:
            return {"path": str(path), "error": result.stderr}
        data = json.loads(result.stdout)
        video = next((s for s in data["streams"] if s["codec_type"] == "video"), {})
        audio = [s for s in data["streams"] if s["codec_type"] == "audio"]
        return {"path": str(path), "bytes": path.stat().st_size, "duration": float(data["format"].get("duration", 0)),
                "video": {k: video.get(k) for k in ("codec_name", "width", "height", "r_frame_rate", "avg_frame_rate", "start_time", "pix_fmt")},
                "audio": [{k: s.get(k) for k in ("index", "codec_name", "sample_rate", "channels", "start_time", "tags")} for s in audio]}
    except Exception as error:
        return {"path": str(path), "error": str(error)}

with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
    inventory = list(pool.map(probe, files))
output = project / "checks/results/inventory.json"
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(inventory, indent=2), encoding="utf-8")
print(f"Indexed {len(inventory)} files; {sum('error' in item for item in inventory)} probe errors. Report: {output}")
