"""Check the maintained NOF topic-branch and squash-message policy."""

import json
import os
import re
import subprocess
from pathlib import Path


ISSUE_REFERENCE = re.compile(
    r"(?:\b(?:refs:\s*|(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?)\s+)"
    r"(?:[\w.-]+/[\w.-]+)?#[1-9]\d*\b"
    r"|https://github\.com/[\w.-]+/[\w.-]+/issues/[1-9]\d*\b)",
    re.IGNORECASE,
)


def main():
    event_path = os.environ.get("GITHUB_EVENT_PATH")
    event = json.loads(Path(event_path).read_text(encoding="utf-8")) if event_path else {}
    pull = event.get("pull_request")
    if pull:
        branch = pull["head"]["ref"]
        message = pull["title"]
        body = pull.get("body") or ""
        if not re.fullmatch(r"(?:feature|fix|hotfix|refactor|docs|test|chore|spike)/[a-z0-9]+(?:-[a-z0-9]+)*", branch):
            raise ValueError(f"Noncompliant topic branch: {branch}")
        if not re.fullmatch(r"(?:feat|fix|refactor|perf|style|docs|test|build|ci|chore|revert)(?:\([a-z0-9-]+\))?!?: [^\n.].*[^.]", message):
            raise ValueError("The pull-request title must be a Conventional Commit squash subject.")
        if not ISSUE_REFERENCE.search(body):
            raise ValueError("Link the tracking issue using a reference, an issue URL, or a GitHub closing directive.")
        if "!:" in message and not all(word in body.lower() for word in ("breaking", "migration")):
            raise ValueError("Describe the breaking impact and migration in the pull request.")
    else:
        branch = subprocess.check_output(["git", "branch", "--show-current"], text=True).strip()
        if branch not in ("main", "develop") and not re.fullmatch(r"(?:feature|fix|hotfix|refactor|docs|test|chore|spike)/[a-z0-9]+(?:-[a-z0-9]+)*", branch):
            raise ValueError(f"Noncompliant topic branch: {branch}")
    print("Contribution policy verified.")


if __name__ == "__main__":
    main()
