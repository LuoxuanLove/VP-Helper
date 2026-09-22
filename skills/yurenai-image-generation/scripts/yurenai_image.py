#!/usr/bin/env python3
"""Generate or edit images with the Yurenai OpenAI-compatible image API."""

from __future__ import annotations

import argparse
import base64
import json
import mimetypes
import os
import platform
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from pathlib import Path
from typing import Any


DEFAULT_BASE_URL = "https://yurenapi.cn/v1"
DEFAULT_MODEL = "gpt-image2.0-1k"
DEFAULT_SIZE = "1024x1024"
DEFAULT_QUALITY = "auto"
DEFAULT_FORMAT = "png"
DEFAULT_RESPONSE_FORMAT = "b64_json"


class ImageApiError(RuntimeError):
    pass


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Generate or edit images with the Yurenai image API."
    )
    add_common_args(parser)

    subparsers = parser.add_subparsers(dest="command", required=True)

    generate = subparsers.add_parser("generate", help="Text-to-image generation.")
    add_common_args(generate, suppress_defaults=True)
    generate.add_argument("--prompt", required=True)

    edit = subparsers.add_parser("edit", help="Edit an uploaded source image.")
    add_common_args(edit, suppress_defaults=True)
    edit.add_argument("--prompt", required=True)
    edit.add_argument("--image", required=True, help="Input image path.")

    return parser.parse_args()


def add_common_args(parser: argparse.ArgumentParser, suppress_defaults: bool = False) -> None:
    default = argparse.SUPPRESS if suppress_defaults else None
    parser.add_argument(
        "--base-url",
        default=argparse.SUPPRESS
        if suppress_defaults
        else os.environ.get("YURENAI_BASE_URL", DEFAULT_BASE_URL),
        help=f"API base URL. Default: {DEFAULT_BASE_URL}",
    )
    parser.add_argument(
        "--api-key-env",
        default=default or "YURENAI_API_KEY",
        help="Environment variable that stores the API key. Falls back to OPENAI_API_KEY.",
    )
    parser.add_argument("--model", default=default or DEFAULT_MODEL)
    parser.add_argument("--size", default=default or DEFAULT_SIZE)
    parser.add_argument("--quality", default=default or DEFAULT_QUALITY)
    parser.add_argument("--output-format", default=default or DEFAULT_FORMAT)
    parser.add_argument("--response-format", default=default or DEFAULT_RESPONSE_FORMAT)
    parser.add_argument("--n", type=int, default=default or 1)
    parser.add_argument("--stream", action="store_true", default=default)
    parser.add_argument("--timeout", type=int, default=default or 300)
    parser.add_argument("--outdir", default=default or "out")
    parser.add_argument(
        "--dry-run",
        action="store_true",
        default=default,
        help="Print sanitized request details without sending the request.",
    )


def normalize_base_url(base_url: str) -> str:
    return base_url.rstrip("/")


def get_api_key(env_name: str) -> tuple[str, str]:
    key = read_env_var(env_name)
    if key:
        return key, env_name
    key = read_env_var("OPENAI_API_KEY")
    if key:
        return key, "OPENAI_API_KEY"
    raise ImageApiError(
        f"Missing API key. Set {env_name} or OPENAI_API_KEY in the environment."
    )


def read_env_var(name: str) -> str | None:
    value = os.environ.get(name)
    if value:
        return value

    if platform.system() != "Windows":
        return None

    try:
        import winreg
    except ImportError:
        return None

    locations = [
        (winreg.HKEY_CURRENT_USER, "Environment"),
        (
            winreg.HKEY_LOCAL_MACHINE,
            r"SYSTEM\CurrentControlSet\Control\Session Manager\Environment",
        ),
    ]
    for hive, path in locations:
        try:
            with winreg.OpenKey(hive, path) as key:
                value, _ = winreg.QueryValueEx(key, name)
        except OSError:
            continue
        if value:
            return str(value)
    return None


def common_payload(args: argparse.Namespace) -> dict[str, Any]:
    payload: dict[str, Any] = {
        "model": args.model,
        "prompt": args.prompt,
        "size": args.size,
        "quality": args.quality,
        "output_format": args.output_format,
        "response_format": args.response_format,
        "n": args.n,
    }
    if args.stream:
        payload["stream"] = True
    return payload


def build_generation_request(
    args: argparse.Namespace, api_key: str
) -> urllib.request.Request:
    endpoint = normalize_base_url(args.base_url) + "/images/generations"
    data = json.dumps(common_payload(args), ensure_ascii=False).encode("utf-8")
    headers = {
        "Authorization": f"Bearer {api_key}",
        "Content-Type": "application/json",
    }
    if args.stream:
        headers["Accept"] = "text/event-stream"
    return urllib.request.Request(endpoint, data=data, headers=headers, method="POST")


def encode_multipart(fields: dict[str, str], files: dict[str, Path]) -> tuple[bytes, str]:
    boundary = "----yurenai-" + uuid.uuid4().hex
    chunks: list[bytes] = []
    for name, value in fields.items():
        chunks.append(f"--{boundary}\r\n".encode("utf-8"))
        chunks.append(
            f'Content-Disposition: form-data; name="{name}"\r\n\r\n'.encode("utf-8")
        )
        chunks.append(str(value).encode("utf-8"))
        chunks.append(b"\r\n")

    for name, path in files.items():
        filename = path.name
        content_type = mimetypes.guess_type(filename)[0] or "application/octet-stream"
        chunks.append(f"--{boundary}\r\n".encode("utf-8"))
        chunks.append(
            (
                f'Content-Disposition: form-data; name="{name}"; '
                f'filename="{filename}"\r\n'
            ).encode("utf-8")
        )
        chunks.append(f"Content-Type: {content_type}\r\n\r\n".encode("utf-8"))
        chunks.append(path.read_bytes())
        chunks.append(b"\r\n")

    chunks.append(f"--{boundary}--\r\n".encode("utf-8"))
    return b"".join(chunks), boundary


def build_edit_request(args: argparse.Namespace, api_key: str) -> urllib.request.Request:
    image_path = Path(args.image)
    if not image_path.is_file():
        raise ImageApiError(f"Input image does not exist: {image_path}")

    endpoint = normalize_base_url(args.base_url) + "/images/edits"
    fields = {k: str(v).lower() if isinstance(v, bool) else str(v) for k, v in common_payload(args).items()}
    body, boundary = encode_multipart(fields, {"image": image_path})
    headers = {
        "Authorization": f"Bearer {api_key}",
        "Content-Type": f"multipart/form-data; boundary={boundary}",
    }
    if args.stream:
        headers["Accept"] = "text/event-stream"
    return urllib.request.Request(endpoint, data=body, headers=headers, method="POST")


def sanitized_request(args: argparse.Namespace) -> dict[str, Any]:
    endpoint = normalize_base_url(args.base_url) + (
        "/images/edits" if args.command == "edit" else "/images/generations"
    )
    details: dict[str, Any] = {
        "endpoint": endpoint,
        "method": "POST",
        "auth": "Bearer <env>",
        "payload": common_payload(args),
        "outdir": str(Path(args.outdir).resolve()),
    }
    if args.command == "edit":
        details["image"] = str(Path(args.image).resolve())
        details["content_type"] = "multipart/form-data"
    else:
        details["content_type"] = "application/json"
    return details


def request_json(request: urllib.request.Request, timeout: int) -> Any:
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            body = response.read()
            charset = response.headers.get_content_charset() or "utf-8"
    except urllib.error.HTTPError as exc:
        error_body = exc.read().decode("utf-8", errors="replace")
        raise ImageApiError(f"HTTP {exc.code}: {error_body}") from exc
    except urllib.error.URLError as exc:
        raise ImageApiError(f"Network error: {exc.reason}") from exc

    try:
        return json.loads(body.decode(charset))
    except json.JSONDecodeError as exc:
        preview = body[:500].decode(charset, errors="replace")
        raise ImageApiError(f"Response was not JSON. Preview: {preview}") from exc


def request_stream(request: urllib.request.Request, timeout: int) -> list[Any]:
    events: list[Any] = []
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            for raw in response:
                line = raw.decode("utf-8", errors="replace").strip()
                if not line or not line.startswith("data:"):
                    continue
                data = line[5:].strip()
                if data == "[DONE]":
                    break
                try:
                    event = json.loads(data)
                except json.JSONDecodeError:
                    event = {"raw": data}
                events.append(event)
                print(summarize_event(event), flush=True)
    except urllib.error.HTTPError as exc:
        error_body = exc.read().decode("utf-8", errors="replace")
        raise ImageApiError(f"HTTP {exc.code}: {error_body}") from exc
    except urllib.error.URLError as exc:
        raise ImageApiError(f"Network error: {exc.reason}") from exc
    return events


def summarize_event(event: Any) -> str:
    if not isinstance(event, dict):
        return "stream event"
    scrubbed = dict(event)
    scrub_large_fields(scrubbed)
    text = json.dumps(scrubbed, ensure_ascii=False)
    return text[:300] + ("..." if len(text) > 300 else "")


def scrub_large_fields(value: Any) -> None:
    if isinstance(value, dict):
        for key in list(value.keys()):
            if key in {"b64_json", "image", "data"} and isinstance(value[key], str) and len(value[key]) > 120:
                value[key] = "<omitted>"
            else:
                scrub_large_fields(value[key])
    elif isinstance(value, list):
        for item in value:
            scrub_large_fields(item)


def iter_image_items(response: Any) -> list[dict[str, Any]]:
    items: list[dict[str, Any]] = []
    if isinstance(response, dict):
        data = response.get("data")
        if isinstance(data, list):
            for item in data:
                if isinstance(item, dict):
                    items.append(item)
        if "b64_json" in response or "url" in response:
            items.append(response)
    elif isinstance(response, list):
        for item in response:
            items.extend(iter_image_items(item))
    return items


def save_images(response: Any, outdir: Path, output_format: str) -> list[Path]:
    outdir.mkdir(parents=True, exist_ok=True)
    saved: list[Path] = []
    stamp = time.strftime("%Y%m%d-%H%M%S")

    for index, item in enumerate(iter_image_items(response), start=1):
        suffix = output_format.lstrip(".") or "png"
        if isinstance(item.get("b64_json"), str):
            path = outdir / f"yurenai-{stamp}-{index}.{suffix}"
            try:
                path.write_bytes(base64.b64decode(item["b64_json"]))
            except Exception as exc:  # noqa: BLE001
                raise ImageApiError(f"Could not decode b64_json image #{index}: {exc}") from exc
            saved.append(path)
            continue

        if isinstance(item.get("url"), str):
            url = item["url"]
            path = outdir / f"yurenai-{stamp}-{index}.{suffix}"
            try:
                with urllib.request.urlopen(url, timeout=120) as response_stream:
                    path.write_bytes(response_stream.read())
            except Exception as exc:  # noqa: BLE001
                raise ImageApiError(f"Could not download image URL #{index}: {exc}") from exc
            saved.append(path)

    if not saved:
        preview = json.dumps(response, ensure_ascii=False)
        if len(preview) > 1000:
            preview = preview[:1000] + "..."
        raise ImageApiError(f"No b64_json or url image found in response: {preview}")

    return saved


def main() -> int:
    args = parse_args()

    if args.dry_run:
        print(json.dumps(sanitized_request(args), ensure_ascii=False, indent=2))
        return 0

    try:
        api_key, env_name = get_api_key(args.api_key_env)
        print(f"Using API key from {env_name}.")
        if args.command == "generate":
            request = build_generation_request(args, api_key)
        elif args.command == "edit":
            request = build_edit_request(args, api_key)
        else:
            raise ImageApiError(f"Unknown command: {args.command}")

        if args.stream:
            response_payload = request_stream(request, args.timeout)
        else:
            response_payload = request_json(request, args.timeout)

        saved = save_images(response_payload, Path(args.outdir), args.output_format)
        for path in saved:
            print(f"saved: {path.resolve()}")
        return 0
    except ImageApiError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
