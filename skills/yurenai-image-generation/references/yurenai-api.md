# Yurenai Image API Reference

Source: https://docs.yurenai.top/use-cases/image-generation.html
Observed 2026-07-05.

## Base URL

Use `https://yurenapi.cn/v1` by default.

The Yurenai docs describe the image API as OpenAI-compatible and show these endpoints:

- `POST /images/generations` for text-to-image generation.
- `POST /images/edits` for editing an uploaded image.

## Defaults

- `model`: `gpt-image2.0-1k`
- `size`: `1024x1024`
- `quality`: `auto`
- `output_format`: `png`
- `response_format`: `b64_json`
- `n`: `1`

The docs recommend using one image for the first test, then increasing quality or count after the request works.

## Generation Request

Endpoint: `POST https://yurenapi.cn/v1/images/generations`

Headers:

- `Authorization: Bearer <key>`
- `Content-Type: application/json`
- Add `Accept: text/event-stream` when `stream` is true.

JSON fields:

- `model`: real model ID from the Yurenai channel/model page.
- `prompt`: describe subject, scene, style, composition, detail, and usage.
- `stream`: optional boolean for real-time progress events.
- `size`: image size such as `1024x1024`.
- `quality`: `auto` is safe when uncertain.
- `output_format`: output file format such as `png`.
- `response_format`: use `b64_json` when saving local files directly.
- `n`: image count; use `1` for smoke tests.

## Edit Request

Endpoint: `POST https://yurenapi.cn/v1/images/edits`

Use multipart form data:

- `image=@input.png`
- `model=gpt-image2.0-1k`
- `prompt=<edit instruction>`
- `stream=true` or `false`
- `size=1024x1024`
- `quality=auto`
- `output_format=png`
- `response_format=b64_json`

## Troubleshooting

- Missing key: require `YURENAI_API_KEY`, or `OPENAI_API_KEY` as a fallback.
- Auth or permission failure: check that the key is complete and bound to an Image 2.0 generation group.
- Quota failure: check account balance, package, and key quota.
- Prompt rejected: remove or rewrite sensitive, infringing, or disallowed content.
- Poor result: add concrete subject, scene, style, composition, ratio, number of people, background, color, and whether text should appear.
- Slow request: image generation may take longer for high resolution, multiple images, or busy periods.
