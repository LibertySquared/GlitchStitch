import argparse
import concurrent.futures
import hashlib
import json
import os
import subprocess
import sys
import tempfile
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import numpy as np
import onnxruntime as ort
from PIL import Image, ImageOps
from tokenizers import Tokenizer
from reference_labels import reference_label, target_labels, extract_reference_frames, CACHE_VERSION
from visual_event_gate import BACKGROUND, is_elimination_request, event_prompts, background_prompts, rank_matches
from outcome_verifier import verify as verify_outcomes


def emit(message):
    print(message, flush=True)


def make_text(tokenizer, prompt, game, irl, game_mode=""):
    if not irl and game_mode:
        game = f"{game or 'a video game'} ({game_mode} mode)"
    concepts = [part.strip().rstrip(".") for part in prompt.replace(";", ",").replace(chr(10), ",").split(",") if part.strip()]
    pairs, guarded = [], set()
    for concept in concepts or [prompt]:
        if not irl and is_elimination_request(concept):
            templates = event_prompts(game)
            guarded.add(concept)
        elif irl:
            templates = [concept, f"Real-life video footage showing {concept}.", f"A candid real-world moment showing {concept}."]
        elif game:
            templates = [concept, f"Gameplay footage from {game} showing {concept}.", f"A gameplay screenshot from the video game {game}: {concept}."]
        else:
            templates = [concept, f"A video frame showing {concept}.", f"A screenshot of {concept}."]
        pairs.extend((text, concept) for text in templates)
    if guarded:
        pairs.extend((text, BACKGROUND) for text in background_prompts(game))
    pairs = list(dict.fromkeys(pairs))
    rows, labels = [], []
    for text, label in pairs:
        ids = tokenizer.encode(text).ids
        if len(ids) > 77:
            ids = ids[:77]
            ids[-1] = 2
        rows.append(ids + [1] * (77 - len(ids)))
        labels.append(label)
    return np.asarray(rows, dtype=np.int64), labels, guarded

def create_session(model, providers):
    options = ort.SessionOptions()
    options.intra_op_num_threads = 4
    options.inter_op_num_threads = 1
    return ort.InferenceSession(model, sess_options=options, providers=providers)


def image_tensor(path):
    image = Image.open(path).convert("RGB")
    image = ImageOps.fit(image, (224, 224), method=Image.Resampling.BICUBIC, centering=(0.5, 0.5))
    data = np.asarray(image, dtype=np.float32) / 255.0
    mean = np.asarray([0.48145466, 0.4578275, 0.40821073], dtype=np.float32)
    std = np.asarray([0.26862954, 0.26130258, 0.27577711], dtype=np.float32)
    return ((data - mean) / std).transpose(2, 0, 1)


def fortnite_examples(directory, cache, ffmpeg, session, input_names, output_names, text_ids):
    root = Path(directory)
    clips = sorted(path for path in root.iterdir() if path.suffix.lower() in {".mp4", ".mkv", ".mov", ".avi"})
    if not clips:
        return None, None
    signature_data = [CACHE_VERSION]
    for path in clips:
        stat = path.stat()
        signature_data.append((path.name, stat.st_size, stat.st_mtime_ns))
    signature = hashlib.sha256(json.dumps(signature_data, separators=(",", ":")).encode("utf-8")).hexdigest()
    cache_path = Path(cache)
    metadata_path = Path(str(cache_path) + ".json")
    if cache_path.is_file() and metadata_path.is_file():
        try:
            if json.loads(metadata_path.read_text(encoding="utf-8")).get("signature") == signature:
                cached = np.load(cache_path, allow_pickle=False)
                emit(f"EXAMPLES|cached|{len(cached['labels'])}")
                return cached["embeddings"].astype(np.float32), cached["labels"].astype(str)
        except Exception:
            pass

    emit(f"EXAMPLES|0|{len(clips)}")
    temp = tempfile.mkdtemp(prefix="glitchstich-fortnite-examples-")
    frames, labels = [], []
    try:
        def extract_example(entry):
            index, clip = entry
            label = reference_label(clip.name)
            if label is None: return [], None
            pattern = Path(temp) / f"example_{index:04d}_%02d.jpg"
            extracted = extract_reference_frames(ffmpeg, clip, pattern, "CUDAExecutionProvider" in ort.get_available_providers())
            return extracted, label

        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            for index, (extracted, label) in enumerate(pool.map(extract_example, enumerate(clips))):
                if extracted:
                    frames.extend(extracted)
                    labels.extend([label] * len(extracted))
                if (index + 1) % 4 == 0 or index + 1 == len(clips):
                    emit(f"EXAMPLES|{index + 1}|{len(clips)}")
        if not frames:
            return None, None

        vectors = []
        example_batch_size = 1 if "CUDAExecutionProvider" in session.get_providers() else 8
        for offset in range(0, len(frames), example_batch_size):
            pixels = np.stack([image_tensor(path) for path in frames[offset:offset + example_batch_size]]).astype(np.float32)
            feed = {"pixel_values": pixels, "input_ids": text_ids}
            if "attention_mask" in input_names:
                feed["attention_mask"] = (text_ids != 1).astype(np.int64)
            result = dict(zip(output_names, session.run(None, feed)))
            if "image_embeds" not in result:
                return None, None
            embeddings = np.asarray(result["image_embeds"], dtype=np.float32)
            embeddings /= np.maximum(np.linalg.norm(embeddings, axis=-1, keepdims=True), 1e-8)
            vectors.append(embeddings)
            if example_batch_size == 1:
                time.sleep(0.08)
        embeddings = np.concatenate(vectors, axis=0)
        cache_path.parent.mkdir(parents=True, exist_ok=True)
        np.savez_compressed(cache_path, embeddings=embeddings, labels=np.asarray(labels, dtype=str))
        metadata_path.write_text(json.dumps({"signature": signature, "clips": len(clips), "examples": len(labels)}, indent=2), encoding="utf-8")
        emit(f"EXAMPLES|cached|{len(labels)}")
        return embeddings, np.asarray(labels, dtype=str)
    finally:
        import shutil
        shutil.rmtree(temp, ignore_errors=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--video", required=True)
    parser.add_argument("--ffmpeg", required=True)
    parser.add_argument("--model", required=True)
    parser.add_argument("--tokenizer", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--query", required=True)
    parser.add_argument("--game", default="")
    parser.add_argument("--game-mode", default="")
    parser.add_argument("--irl", action="store_true")
    parser.add_argument("--threshold", type=float, default=0.15)
    parser.add_argument("--context", type=float, default=3.0)
    parser.add_argument("--window", type=float, default=30.0)
    parser.add_argument("--device", choices=["auto", "cpu", "cuda"], default="auto")
    parser.add_argument("--examples-dir")
    parser.add_argument("--examples-cache")
    parser.add_argument("--verification-report")
    args = parser.parse_args()
    sample_interval = 0.5
    review_window_seconds = min(300.0, max(2.0, args.window))
    candidate_spacing_seconds = review_window_seconds + 4.0

    model = Path(args.model)
    if not model.is_file():
        raise FileNotFoundError(f"Visual model is missing: {model}")
    temp = tempfile.mkdtemp(prefix="glitchstich-frames-")
    try:
        pattern = os.path.join(temp, "frame_%06d.jpg")
        command = [args.ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-threads", "2", "-i", args.video,
                   "-an", "-vf", "fps=2,scale=224:224:force_original_aspect_ratio=decrease,pad=224:224:(ow-iw)/2:(oh-ih)/2",
                   "-q:v", "4", pattern]
        emit("EXTRACT|0")
        def extract_frames(use_cuda):
            options = {"stdout": subprocess.PIPE, "stderr": subprocess.PIPE, "text": True}
            if os.name == "nt": options["creationflags"] = subprocess.BELOW_NORMAL_PRIORITY_CLASS | subprocess.CREATE_NO_WINDOW
            # Drain stderr concurrently so errors cannot block a long extraction.
            from concurrent.futures import ThreadPoolExecutor
            cmd = command[:7] + (["-hwaccel", "cuda"] if use_cuda else []) + command[7:]
            cmd = cmd[:1] + ["-progress", "pipe:1", "-nostats"] + cmd[1:]
            with subprocess.Popen(cmd, **options) as process, ThreadPoolExecutor(max_workers=1) as pool:
                errors = pool.submit(process.stderr.read)
                for line in process.stdout:
                    if line.startswith("out_time_us="):
                        try: emit("EXTRACT|{:.1f}".format(int(line.split("=", 1)[1]) / 1000000))
                        except ValueError: pass
                code = process.wait()
                error = errors.result()
            if code: raise subprocess.CalledProcessError(code, cmd, stderr=error)
        try:
            extract_frames("CUDAExecutionProvider" in ort.get_available_providers())
        except subprocess.CalledProcessError:
            extract_frames(False)
        frames = sorted(Path(temp).glob("frame_*.jpg"))
        if not frames:
            raise RuntimeError("No video frames could be extracted.")
        source_duration = len(frames) * sample_interval
        probe = Path(args.ffmpeg).with_name("ffprobe.exe" if os.name == "nt" else "ffprobe")
        if probe.is_file():
            probe_options = {"capture_output": True, "text": True, "check": True}
            if os.name == "nt": probe_options["creationflags"] = subprocess.CREATE_NO_WINDOW
            duration_text = subprocess.run([str(probe), "-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", args.video], **probe_options).stdout.strip()
            duration = float(duration_text)
            if np.isfinite(duration) and duration > 0: source_duration = duration

        tokenizer = Tokenizer.from_file(args.tokenizer)
        text_ids, text_labels, guarded_labels = make_text(tokenizer, args.query, args.game.strip(), args.irl, args.game_mode.strip())
        # This INT8 export contains CPU operators; transferring intermediate
        # tensors through CUDA was substantially slower in the local benchmark.
        # Keep an explicit CUDA override for profiling other model exports.
        quantized = "quant" in model.name.lower()
        use_cuda = args.device != "cpu" and (args.device == "cuda" or not quantized) and "CUDAExecutionProvider" in ort.get_available_providers()
        if use_cuda:
            try:
                preload = getattr(ort, "preload_dlls", None)
                if preload is not None:
                    preload()
                session = create_session(args.model, ["CUDAExecutionProvider", "CPUExecutionProvider"])
                use_cuda = "CUDAExecutionProvider" in session.get_providers()
                emit("PROVIDER|CUDA" if use_cuda else "PROVIDER|CPU")
            except Exception as exc:
                emit(f"GPU_FALLBACK|{str(exc).replace('|', '/').replace(chr(10), ' ')[:240]}")
                session = create_session(args.model, ["CPUExecutionProvider"])
                use_cuda = False
                emit("PROVIDER|CPU")
        else:
            session = create_session(args.model, ["CPUExecutionProvider"])
            emit("PROVIDER|CPU")
        input_names = {item.name for item in session.get_inputs()}
        output_names = [item.name for item in session.get_outputs()]
        if "pixel_values" not in input_names or "input_ids" not in input_names:
            raise RuntimeError("The visual model has an unsupported input layout: " + ", ".join(sorted(input_names)))

        example_vectors, example_labels = (None, None)
        if args.examples_dir and args.examples_cache and Path(args.examples_dir).is_dir():
            example_vectors, example_labels = fortnite_examples(args.examples_dir, args.examples_cache, args.ffmpeg,
                                                                 session, input_names, output_names, text_ids)
        requested_labels = target_labels(args.query)
        example_mask = np.isin(example_labels, list(requested_labels)) if example_vectors is not None and requested_labels else None
        if example_mask is not None and not np.any(example_mask):
            example_mask = None
        if example_mask is not None:
            example_vectors = example_vectors[example_mask]
            emit(f"EXAMPLES|active|{len(example_vectors)}")

        scored = []
        batch_size = 1 if use_cuda else 8
        for offset in range(0, len(frames), batch_size):
            group = frames[offset:offset + batch_size]
            pixels = np.stack([image_tensor(path) for path in group]).astype(np.float32)
            ids = text_ids
            feed = {"pixel_values": pixels, "input_ids": ids}
            if "attention_mask" in input_names:
                feed["attention_mask"] = (ids != 1).astype(np.int64)
            try:
                values = session.run(None, feed)
            except Exception as exc:
                if not use_cuda:
                    raise
                emit(f"GPU_FALLBACK|{str(exc).replace('|', '/').replace(chr(10), ' ')[:240]}")
                session = create_session(args.model, ["CPUExecutionProvider"])
                use_cuda = False
                batch_size = 8
                emit("PROVIDER|CPU")
                values = session.run(None, feed)
            by_name = dict(zip(output_names, values))
            if "image_embeds" in by_name and "text_embeds" in by_name:
                image_embeds = by_name["image_embeds"]
                text_embeds = by_name["text_embeds"]
                image_embeds /= np.maximum(np.linalg.norm(image_embeds, axis=-1, keepdims=True), 1e-8)
                text_embeds /= np.maximum(np.linalg.norm(text_embeds, axis=-1, keepdims=True), 1e-8)
                similarities = np.matmul(image_embeds, text_embeds.T)
                winners, scores = rank_matches(similarities, text_labels, guarded_labels)
                if example_vectors is not None and example_mask is not None:
                    reference_similarity = np.max(np.matmul(image_embeds, example_vectors.T), axis=1)
                    scores += np.clip(reference_similarity - 0.72, 0, 0.30) * 0.18
            elif "logits_per_image" in by_name:
                logits = np.asarray(by_name["logits_per_image"]).reshape(len(group), -1)
                winners, scores = rank_matches(logits / 100.0, text_labels, guarded_labels)
            else:
                raise RuntimeError("The visual model returned an unsupported output layout: " + ", ".join(output_names))
            for local, score in enumerate(scores):
                frame_number = offset + local
                scored.append((frame_number * sample_interval, float(score), text_labels[int(winners[local])]))
            emit(f"SCANNING|{min(offset + len(group), len(frames))}|{len(frames)}")
            if use_cuda:
                time.sleep(0.08)

        # Keep distinct moments separate while using the user-requested clip length.
        visual_threshold = args.threshold * 0.8
        detections = []
        for seconds, score, concept in sorted((item for item in scored if item[1] >= visual_threshold), key=lambda item: item[1], reverse=True):
            if all(abs(seconds - item["eventStart"]) >= candidate_spacing_seconds for item in detections):
                start = max(0.0, seconds - review_window_seconds / 2.0)
                detections.append({"start": start, "eventStart": seconds, "end": start + review_window_seconds, "confidence": score, "concept": concept})
            if len(detections) >= 20:
                break
        detections.sort(key=lambda item: item["start"])

        notices = []
        if guarded_labels:
            guarded = [item for item in detections if item["concept"] in guarded_labels]
            ordinary = [item for item in detections if item["concept"] not in guarded_labels]
            if guarded:
                emit(f"VERIFY|0|{len(guarded)}")
                try:
                    verified, evidence = verify_outcomes(args.ffmpeg, args.video, guarded, Path(temp) / "verification",
                                                        "CUDAExecutionProvider" in ort.get_available_providers())
                    if args.verification_report:
                        Path(args.verification_report).write_text(json.dumps(evidence, indent=2), encoding="utf-8")
                    detections = ordinary + verified
                    notices.append(f"Outcome text supported {len(verified)} of {len(guarded)} elimination candidates.")
                except Exception as exc:
                    detections = ordinary
                    notices.append(f"Outcome verification unavailable: {exc}. Unverified elimination matches were omitted.")
                emit(f"VERIFY|{len(guarded)}|{len(guarded)}")
            notices.append("Elimination bookmarks require a changing central English outcome label; games without that indicator can be missed.")

        # If the prompt has no strong matches, still offer a few explicitly low-confidence
        # places to review instead of silently returning an empty video scan.
        # A request for eliminations must not manufacture confident-looking
        # bookmarks when ordinary gameplay is a better explanation.
        fallback = not detections and not guarded_labels
        if fallback:
            candidates = []
            for seconds, score, concept in sorted(scored, key=lambda item: item[1], reverse=True):
                if all(abs(seconds - previous[0]) >= candidate_spacing_seconds for previous in candidates):
                    candidates.append((seconds, score, concept))
                if len(candidates) == 5:
                    break
            detections = [{"start": max(0.0, seconds - review_window_seconds / 2.0),
                           "eventStart": seconds,
                           "end": max(0.0, seconds - review_window_seconds / 2.0) + review_window_seconds,
                           "confidence": score, "concept": concept}
                          for seconds, score, concept in candidates]

        segments = []
        for item in detections:
            event_start = item.get("eventStart", item["start"])
            if args.irl:
                context_label = "IRL"
            elif args.game.strip():
                context_label = args.game.strip() + (f" / {args.game_mode.strip()}" if args.game_mode.strip() else "")
            else:
                context_label = "Video"
            label = f"Closest video candidate: {context_label} · {item['concept']}" if fallback else f"{context_label} · {item['concept']}"
            segments.append({"start": max(0.0, event_start - args.context), "eventStart": event_start,
                             "end": min(source_duration, event_start + max(0.0, review_window_seconds - args.context)), "confidence": item["confidence"],
                             "event": label, "track": "Video frames", "candidate": fallback})
        Path(args.output).write_text(json.dumps({"segments": segments, "notices": notices}), encoding="utf-8")
        emit(f"DONE|{len(segments)}")
    finally:
        import shutil
        shutil.rmtree(temp, ignore_errors=True)


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print(f"ERROR|{exc}", file=sys.stderr, flush=True)
        raise
