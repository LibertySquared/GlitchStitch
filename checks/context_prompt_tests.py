import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Resources/LaughDetection/src"))
from clip_video_scan import make_text
from audio_prompt import resolve_request


class Recorder:
    def __init__(self):
        self.texts = []

    def encode(self, text):
        self.texts.append(text)
        return SimpleNamespace(ids=[0, 12, 2])


class ContextTests(unittest.TestCase):
    def test_game_mode_reaches_outcome_and_background_prompts(self):
        tokenizer = Recorder()
        ids, labels, guarded = make_text(tokenizer, "eliminations", "Fortnite", False, "Zero Build")
        self.assertTrue(all("Fortnite (Zero Build mode)" in text for text in tokenizer.texts))
        self.assertIn("eliminations", guarded)
        self.assertTrue(any("talking during a story" in text for text in tokenizer.texts))
        self.assertEqual(ids.shape[1], 77)

    def test_irl_ignores_game_and_mode(self):
        tokenizer = Recorder()
        make_text(tokenizer, "laughter", "Fortnite", True, "Zero Build")
        self.assertTrue(all("Fortnite" not in text and "Zero Build" not in text for text in tokenizer.texts))

    def test_dialogue_and_player_reactions_route_to_supported_audio(self):
        names = ["Speech", "Laughter", "Screaming", "Cheering", "Music"]
        selected, _ = resolve_request("character dialogue, player reactions", names)
        self.assertEqual({names[i] for i in selected}, {"Speech", "Laughter", "Screaming", "Cheering"})


if __name__ == "__main__":
    unittest.main()
