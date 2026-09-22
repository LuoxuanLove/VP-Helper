"""Produce inert, sanitized regression fixtures; never modify the original captures."""
from pathlib import Path
import hashlib
import json
import re

ROOT = Path(__file__).resolve().parents[1] / "tests" / "fixtures"
HTML = [
    "gym_submissions_p1.html", "gym_submissions_p2.html", "real_submissions.html",
    "LuoxuanLove - User profile - QOJ.ac.html",
    "Submissions - QOJ.ac.html", "Submissions2 - QOJ.ac.html",
    "第十一届中国大学生程序设计竞赛 哈尔滨站（CCPC 2025 Harbin Site） - Submissions - Contest - QOJ.ac.html",
]


def sanitize(text: str) -> str:
    text = re.sub(r"<script\b[^>]*>.*?</script\s*>", "", text, flags=re.I | re.S)
    text = re.sub(r"<style\b[^>]*>.*?</style\s*>", "", text, flags=re.I | re.S)
    # Preserve tables/attributes and the authenticated account-control structure for parser tests.
    text = re.sub(r"<(?:meta|input)\b[^>]*(?:csrf|token|password)[^>]*>", "", text, flags=re.I)
    text = re.sub(r"([?&](?:amp;)?(?:_token|csrf_token|csrf|token)=)[^\s\"'<> &]+", r"\1REDACTED", text, flags=re.I)
    text = re.sub(r"\s+on\w+\s*=\s*(?:\"[^\"]*\"|'[^']*')", "", text, flags=re.I)
    text = re.sub(r"\s+(?:src|srcset)\s*=\s*(?:\"[^\"]*\"|'[^']*')", "", text, flags=re.I)
    text = re.sub(r"<link\b[^>]*>", "", text, flags=re.I)
    text = re.sub(r"<!--.*?-->", "", text, flags=re.S)
    return "<!-- Sanitized real markup; inert parser fixture, not a saved login session. -->\n" + text


def main() -> None:
    target = ROOT / "public"
    target.mkdir(exist_ok=True)
    manifest = []
    for name in HTML + ["srk/config.yaml", "srk/gzcpc2024.srk.json", "srk/icpc2023macau.srk.json"]:
        source = ROOT / name
        output = target / name
        if output.exists():
            raise SystemExit(f"Refusing to overwrite an existing reviewed fixture: {output.name}")
        output.parent.mkdir(parents=True, exist_ok=True)
        raw = source.read_bytes()
        text = raw.decode("utf-8-sig").replace("\r\n", "\n").replace("\r", "\n")
        data = (sanitize(text) if source.suffix == ".html" else text).encode("utf-8")
        output.write_bytes(data)
        manifest.append({"path": name, "sha256": hashlib.sha256(data).hexdigest()})
    (target / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Prepared {len(manifest)} public fixtures; original captures retained locally.")


if __name__ == "__main__":
    main()
