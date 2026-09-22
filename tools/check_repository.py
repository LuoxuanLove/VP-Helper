"""Audit tracked and trackable files without displaying possible secret values."""
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
BLOCKED_DIRS = {"bin", "obj", "data", "webview2", "ebwebview", "out", "artifacts", ".codex", ".agents", "__pycache__"}
SECRETS = {
    "private key": rb"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----",
    "GitHub token": rb"(?:gh[pousr]_[A-Za-z0-9_]{30,}|github_pat_[A-Za-z0-9_]{30,})",
    "literal API credential": rb"(?i)(?:api[_-]?key|authorization)\s*[:=]\s*[\"'](?:sk-|Bearer )[A-Za-z0-9_-]{20,}",
}


def main() -> int:
    raw = subprocess.check_output(["git", "ls-files", "-z", "--cached", "--others", "--exclude-standard"], cwd=ROOT)
    paths = sorted(set(p.decode("utf-8") for p in raw.split(b"\0") if p))
    errors = []
    for name in paths:
        parts = PurePosixPath(name).parts
        file = ROOT / name
        if any(p.lower() in BLOCKED_DIRS for p in parts) or file.suffix.lower() in {".db", ".sqlite", ".sqlite3", ".pfx", ".p12", ".pem", ".key", ".log"}:
            errors.append((name, "local/private/generated file"))
        if name.startswith("tests/fixtures/") and not name.startswith("tests/fixtures/public/"):
            errors.append((name, "unreviewed raw capture"))
        if file.is_symlink():
            errors.append((name, "symlink requires explicit review"))
            continue
        if not file.exists():
            errors.append((name, "tracked file missing"))
            continue
        if file.stat().st_size > 10 * 1024 * 1024:
            errors.append((name, "file larger than 10 MiB"))
        if file.suffix.lower() in {".png", ".jpg", ".ico"}:
            continue
        content = file.read_bytes()
        for label, pattern in SECRETS.items():
            if re.search(pattern, content): errors.append((name, label))
        if name.startswith("tests/fixtures/public/") and file.suffix == ".html":
            if re.search(rb"<script\b|<(?:meta|input)\b[^>]*(?:csrf|password)", content, re.I):
                errors.append((name, "active content or authentication field in fixture"))
            for token in re.findall(rb"[?&](?:amp;)?_token=([^\s\"'<> &]+)", content, re.I):
                if token != b"REDACTED": errors.append((name, "unredacted logout token"))
    for name, error in errors:
        print(f"{name}: {error}")
    print(f"Audited {len(paths)} files; {len(errors)} findings.")
    return int(bool(errors))


if __name__ == "__main__":
    sys.exit(main())
