#!/usr/bin/env python3
"""ShellDeck tab command: model and context size of the Claude Code session of a tab.

ShellDeck sends the tab's data as JSON on the standard input and reads a JSON object on the standard output:
every key becomes a field for `tabLines` ({model}, {ctx}, {ctxpct}). Run it where the Claude Code transcripts are
(~/.claude/projects): inside WSL for a WSL tab.

The directory and the session id come from the terminal: they are only used to build a path under ~/.claude/projects,
checked below, and never run.
"""
import json
import os
import re
import sys

# Context window of the model, in tokens. The transcript does not say it: change it for a 1M window.
WINDOW = int(os.environ.get("SHELLDECK_CTX_WINDOW", "200000"))
TAIL = 400_000  # bytes read from the end of the transcript


def display_model(model):
    """claude-opus-5-5 -> Opus 5.5"""
    parts = re.sub(r"^claude-", "", model or "").split("-")
    parts = [part for part in parts if not re.fullmatch(r"\d{8}", part)]
    name = [part for part in parts if not part.isdigit()]
    version = [part for part in parts if part.isdigit()]
    return " ".join([" ".join(part.capitalize() for part in name), ".".join(version)]).strip()


def transcript_for(info):
    directory = info.get("directory")
    if not isinstance(directory, str) or not directory.startswith("/"):
        return None
    root = os.path.join(os.path.expanduser("~"), ".claude", "projects", re.sub(r"[^A-Za-z0-9-]", "-", directory))
    session = info.get("claudeSessionId")
    if isinstance(session, str) and re.fullmatch(r"[0-9a-fA-F-]{8,64}", session):
        path = os.path.join(root, session + ".jsonl")
        if os.path.isfile(path):
            return path
    try:
        files = [os.path.join(root, name) for name in os.listdir(root) if name.endswith(".jsonl")]
    except OSError:
        return None
    return max(files, key=os.path.getmtime, default=None)


def last_usage(path):
    with open(path, "rb") as stream:
        stream.seek(0, os.SEEK_END)
        stream.seek(max(0, stream.tell() - TAIL))
        lines = stream.read().decode("utf-8", "replace").splitlines()
    for line in reversed(lines):
        try:
            message = json.loads(line).get("message")
        except ValueError:
            continue
        if isinstance(message, dict) and message.get("role") == "assistant" and isinstance(message.get("usage"), dict):
            return message.get("model"), message["usage"]
    return None


def main():
    try:
        info = json.load(sys.stdin)
        path = transcript_for(info)
        found = last_usage(path) if path else None
    except (OSError, ValueError):
        found = None
    if not found:
        print("{}")
        return
    model, usage = found
    used = sum(int(usage.get(key) or 0) for key in ("input_tokens", "cache_creation_input_tokens", "cache_read_input_tokens"))
    percent = round(100 * used / WINDOW)
    style = "behind" if percent >= 80 else "attention" if percent >= 40 else "muted"  # orange, yellow, grey
    print(json.dumps({
        "model": display_model(model),
        "ctx": {"text": f"{used // 1000}k", "style": style},
        "ctxpct": {"text": f"{percent}%", "style": style},
    }))


main()
