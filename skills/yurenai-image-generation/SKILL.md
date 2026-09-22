---
name: yurenai-image-generation
description: Generate or edit images with the Yurenai OpenAI-compatible image API. Use when the user asks to create images through Yurenai, test the Yurenai image-generation loop, call gpt-image2.0-1k, save returned base64/url images to files, or troubleshoot Yurenai image API parameters, credentials, and responses.
---

# Yurenai Image Generation

## Overview

Use Yurenai's OpenAI-compatible image endpoints to generate or edit images and save the results as local files. Prefer the bundled script for repeatable API calls and secret handling.

## Safety

- Never place API keys in files, command arguments, screenshots, or final answers.
- Read the API key from `YURENAI_API_KEY`; fall back to `OPENAI_API_KEY` only when the Yurenai-specific variable is missing.
- If the user pasted a key into chat, avoid echoing it and recommend rotating it.
- Use `--dry-run` before the first live call to inspect endpoint, payload shape, and output path.

## Quick Start

Run from the skill folder or pass an absolute script path:

```powershell
python .\scripts\yurenai_image.py generate `
  --prompt "A clean product poster for an AI workspace tool, white desk, soft natural light, subtle glass data panels, minimal composition" `
  --outdir .\out `
  --dry-run
```

For a live call, set `YURENAI_API_KEY` in the environment first, then remove `--dry-run`.

```powershell
python .\scripts\yurenai_image.py generate `
  --prompt "A cinematic UI launcher concept, Fluent Design, soft acrylic glass, blue and mint accents" `
  --outdir .\out
```

Edit an existing image:

```powershell
python .\scripts\yurenai_image.py edit `
  --image .\input.png `
  --prompt "Keep the main subject, replace the background with a rainy neon street corner, softer lighting" `
  --outdir .\out
```

## Workflow

1. Read `references/yurenai-api.md` when you need endpoint details, model defaults, request fields, or troubleshooting notes.
2. Run the script with `--dry-run` to confirm parameters.
3. Ensure `YURENAI_API_KEY` is set in the process environment.
4. Run a live request with `n=1`, `size=1024x1024`, `quality=auto`, and `response_format=b64_json` unless the user asks otherwise.
5. Verify that output files were created and report their absolute paths.

## Script Notes

- `scripts/yurenai_image.py generate` calls `/images/generations` with JSON.
- `scripts/yurenai_image.py edit` calls `/images/edits` with multipart form data.
- The script supports `--stream`; when streaming is enabled it parses server-sent `data:` events and saves any final `b64_json` or `url` images it finds.
- The script uses only Python standard-library modules.

If a request fails, report the failing layer first: missing key, network, HTTP status, JSON parse, no image field, or file write.
