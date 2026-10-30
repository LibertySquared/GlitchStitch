import sys
import unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Resources/LaughDetection/src"))
import numpy as np
from visual_event_gate import rank_matches, BACKGROUND, is_elimination_request
from outcome_verifier import markers, supported_candidates

def image(text="ELIMINATION", phase="event", x=45):
    return {"candidate": 0, "phase": phase, "width": 100, "height": 100, "text": text,
            "words": [{"text": text, "x": x, "y": 75, "width": 10, "height": 10}]}

class GateTests(unittest.TestCase):
    def test_background_does_not_become_outcome(self):
        winners, scores = rank_matches(np.array([[.3, .6], [.7, .6]]), ["eliminations", BACKGROUND], {"eliminations"})
        self.assertTrue(np.isneginf(scores[0]))
        self.assertEqual(scores[1], .7)
        self.assertEqual(winners.tolist(), [0, 0])
    def test_general_prompt_is_not_filtered_as_elimination(self):
        _, scores = rank_matches(np.array([[.3, .6]]), ["laughing", BACKGROUND], {"eliminations"})
        self.assertEqual(scores[0], .3)
        self.assertFalse(is_elimination_request("player eliminated"))
    def test_new_central_outcome_is_supported(self):
        self.assertEqual(supported_candidates([image()]), {0})
    def test_static_outcome_is_not_repeated(self):
        self.assertEqual(supported_candidates([image(phase="baseline"), image()]), set())
    def test_corner_feed_and_subtitles_are_not_confirmations(self):
        self.assertEqual(markers(image(x=5)), set())
        self.assertEqual(markers(image(text="killed")), set())
    def test_player_death_is_not_positive(self):
        frame = image(); frame["text"] = "ELIMINATED BY another player"
        self.assertEqual(markers(frame), set())
        frame["text"] = "YOU WERE ELIMINATED"
        self.assertEqual(markers(frame), set())
    def test_failed_ocr_is_not_confirmation(self):
        frame = image(); frame["error"] = "decode failed"
        self.assertEqual(markers(frame), set())

if __name__ == "__main__": unittest.main()
