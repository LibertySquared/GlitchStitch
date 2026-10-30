import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Resources/LaughDetection/src"))
from audio_prompt import resolve_request
from reference_labels import reference_label, target_labels

NAMES = ["Laughter", "Baby laughter", "Giggle", "Chuckle, chortle", "Shout", "Screaming", "Cheering", "Engine", "Light engine (high frequency)", "Music"]

class ScannerTests(unittest.TestCase):
    def test_broad_request(self):
        indices, notices = resolve_request("High excitement, laughter, eliminations", NAMES)
        selected = {NAMES[i] for i in indices}
        self.assertEqual(selected, {"Laughter", "Giggle", "Chuckle, chortle", "Shout", "Screaming", "Cheering"})
        self.assertTrue(any("visual review" in n for n in notices))
        self.assertTrue(any("excitement" in n for n in notices))

    def test_explicit_multiword_class(self):
        indices, _ = resolve_request("Light engine (high frequency)", NAMES)
        self.assertEqual([NAMES[i] for i in indices], ["Light engine (high frequency)"])

    def test_visual_only(self):
        selected, notices = resolve_request("eliminations", NAMES)
        self.assertEqual(selected, [])
        self.assertTrue(notices)

    def test_unknown_request(self):
        selected, notices = resolve_request("sarcastic plot twists", NAMES)
        self.assertEqual(selected, [])
        self.assertTrue(any("could not map" in n for n in notices))

    def test_exclusion_is_not_positive(self):
        selected, notices = resolve_request("no music", NAMES)
        self.assertEqual(selected, [])
        self.assertTrue(notices)

    def test_comedy_does_not_select_every_conversation(self):
        names = NAMES + ["Speech", "Applause"]
        selected, notices = resolve_request("funny jokes", names)
        self.assertNotIn(names.index("Speech"), selected)
        self.assertNotIn(names.index("Applause"), selected)
        self.assertIn(names.index("Laughter"), selected)
        self.assertTrue(any("does not verify" in n for n in notices))

    def test_filename_labels(self):
        self.assertEqual(reference_label("Fortnite.Double Elimination.DVR.mp4"), "Elimination")
        self.assertEqual(reference_label("Fortnite.Eliminated.DVR.mp4"), "Player eliminated")
        self.assertEqual(reference_label("Fortnite.Down.DVR.mp4"), "Down")
        self.assertIsNone(reference_label("Fortnite.ordinary recording.mp4"))
        self.assertEqual(target_labels("eliminations"), {"Elimination"})
        self.assertEqual(target_labels("player eliminated"), {"Player eliminated"})

if __name__ == "__main__": unittest.main()
