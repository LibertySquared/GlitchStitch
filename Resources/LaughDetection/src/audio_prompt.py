"""Explicit sound-class routing for scan prompts; independent of model loading."""
import re

ALIASES = {
    "laughter": ["Laughter", "Giggle", "Chuckle, chortle"],
    "excitement": ["Cheering", "Shout", "Screaming"], "excited": ["Cheering", "Shout", "Screaming"],
    "laugh": ["Laughter", "Giggle"], "laughing": ["Laughter", "Giggle"], "laughs": ["Laughter", "Giggle"],
    "funny": ["Laughter", "Giggle", "Chuckle, chortle"],
    "comedy": ["Laughter", "Giggle", "Chuckle, chortle"],
    "joke": ["Laughter", "Giggle", "Chuckle, chortle"], "jokes": ["Laughter", "Giggle", "Chuckle, chortle"], "reaction": ["Laughter", "Screaming", "Cheering"],
    "yell": ["Screaming", "Shout"], "yells": ["Screaming", "Shout"], "yelling": ["Screaming", "Shout"], "shout": ["Shout", "Screaming"], "shouting": ["Shout", "Screaming"], "scream": ["Screaming"], "screaming": ["Screaming"],
    "cheer": ["Cheering", "Applause"], "clap": ["Applause"], "clapping": ["Applause"],
    "talk": ["Speech"], "talking": ["Speech"], "speak": ["Speech"], "speaking": ["Speech"], "voice": ["Speech"], "voices": ["Speech"], "conversation": ["Speech"], "dialogue": ["Speech"], "dialog": ["Speech"], "reactions": ["Laughter", "Screaming", "Cheering"], "arguing": ["Speech", "Screaming"], "argument": ["Speech", "Screaming"],
    "music": ["Music"], "song": ["Singing", "Music"], "singing": ["Singing"],
    "crash": ["Crash"], "impact": ["Thump, thud", "Bang"], "explosion": ["Explosion"],
    "gunshot": ["Gunshot, gunfire"], "shot": ["Gunshot, gunfire"], "footsteps": ["Walk, footsteps"],
    "step": ["Walk, footsteps"], "steps": ["Walk, footsteps"], "silence": ["Silence"],
    "game": ["Video game sound"], "gaming": ["Video game sound"], "notification": ["T beep", "Beep, bleep"],
    "beep": ["Beep, bleep"], "cry": ["Crying, sobbing"], "crying": ["Crying, sobbing"],
    "cough": ["Cough"], "coughing": ["Cough"], "breathing": ["Breathing"],
    "dog": ["Bark", "Dog"], "barking": ["Bark"], "alarm": ["Alarm"], "engine": ["Engine"],
    "water": ["Water"], "wind": ["Wind"], "door": ["Door"], "typing": ["Typing"],
}
STOP = {"a", "an", "the", "when", "while", "where", "what", "look", "for", "find", "detect", "anything", "that", "with", "and", "or", "in", "on", "of", "my", "their", "is", "are", "moment", "moments", "clip", "clips", "video", "audio", "sound", "sounds", "event", "events"}


def normalize(text):
    return " ".join(re.findall(r"[a-z0-9]+", text.lower()))

VIDEO_WORDS = {"elimination", "eliminations", "kill", "kills", "elim", "knockdown", "knocked", "downs", "victory", "victories", "win", "wins", "eliminated", "death", "deaths"}

def resolve_request(query, names):
    by_name = {name.lower(): i for i, name in enumerate(names)}
    normalized = {normalize(name): i for i, name in enumerate(names)}
    chosen, notices = set(), []
    for phrase in re.split(r"[,;\n]+", query):
        phrase = phrase.strip()
        if not phrase: continue
        words = re.findall(r"[a-z0-9]+", phrase.lower())
        if set(words) & {"without", "exclude", "excluding", "not", "no"}:
            notices.append(f"Audio exclusions need manual review: {phrase}.")
            continue
        cleaned = " ".join(word for word in words if word not in STOP)
        exact = normalized.get(cleaned) if " " in cleaned else None
        matches = {exact} if exact is not None else set()
        if exact is None:
            for word in words:
                for alias in ALIASES.get(word, []):
                    if alias.lower() in by_name: matches.add(by_name[alias.lower()])
                # Match whole single-word class names only. Modifiers such as
                # 'high' must never select an unrelated multiword engine class.
                if word not in STOP and word in normalized:
                    matches.add(normalized[word])
        chosen.update(matches)
        if set(words) & {"excitement", "excited"}:
            notices.append("Audio uses cheering, shouts and screams as excitement cues; excitement itself is not a verified sound class.")
        if set(words) & {"funny", "comedy", "joke", "jokes"}:
            notices.append("Audio uses laughter as a possible comedy cue; it does not verify the joke or its context.")
        if set(words) & VIDEO_WORDS:
            notices.append(f"Game outcomes require visual review: {phrase} is not verified by the sound detector.")
        elif not matches:
            notices.append(f"Audio could not map this request to a supported sound: {phrase}.")
    return sorted(chosen), notices

def resolve_classes(query, names):
    return resolve_request(query, names)[0]
