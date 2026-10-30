"""Comparative evidence for elimination queries; CLIP scores are not probabilities."""
import re
import numpy as np

BACKGROUND = "__background__"

def is_elimination_request(concept):
    return bool(set(re.findall(r"[a-z]+", concept.lower())) & {"elimination", "eliminations", "kill", "kills", "elim"})

def event_prompts(game):
    context = f"A screenshot from {game}" if game else "A video game screenshot"
    return [context + suffix for suffix in (
        " showing an enemy elimination notification after defeating an opponent.",
        " showing a kill confirmation or elimination banner.",
        " showing an opponent defeated in combat.")]

def background_prompts(game):
    context = f"A screenshot from {game}" if game else "A video game screenshot"
    return [context + suffix for suffix in (
        " showing characters walking and exploring.",
        " showing characters talking during a story.",
        " showing an objective update.",
        " showing a character holding a weapon.",
        " showing shooting and fighting enemies.")]

def rank_matches(similarities, labels, guarded_labels):
    positives = np.array([i for i, label in enumerate(labels) if label != BACKGROUND])
    background = [i for i, label in enumerate(labels) if label == BACKGROUND]
    winners = positives[np.argmax(similarities[:, positives], axis=1)]
    scores = similarities[np.arange(len(winners)), winners].copy()
    if background:
        baseline = np.max(similarities[:, background], axis=1)
        guarded = np.array([labels[i] in guarded_labels for i in winners])
        scores[guarded & (scores <= baseline)] = -np.inf
    return winners, scores
