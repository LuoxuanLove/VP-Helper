import argparse
from pathlib import Path

from PIL import Image, ImageChops, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src" / "VPHelper.WinUI" / "Assets"
DEFAULT_SOURCE = ASSETS / "AppIconSource.png"
OUTPUT_SIZE = 512
PADDING = 46
WHITE = (246, 248, 250)
CYAN = (45, 226, 207)
SMALL_ICON_MAX_SIZE = 48
TITLE_BAR_SCALES = (100, 125, 150, 175, 200, 225, 250, 300, 350, 400)


def source_mask_and_colors(image: Image.Image) -> tuple[Image.Image, Image.Image]:
    red, green, blue = image.convert("RGB").split()

    # The Yurenai source uses a magenta chroma background. Foreground strokes have a
    # substantially stronger green channel, so this channel cleanly preserves their
    # antialiasing while rejecting the model's slight background brightness variation.
    alpha = green.point(lambda value: max(0, min(255, round((value - 34) * 1.34))))
    alpha = alpha.filter(ImageFilter.GaussianBlur(0.55))

    # Cyan generated strokes have blue noticeably above red. Everything else is the
    # warm-white Fluent outline. Normalize both to the application's existing palette.
    cyan_signal = ImageChops.subtract(blue, red)
    cyan_mask = cyan_signal.point(lambda value: max(0, min(255, round((value - 18) * 2.4))))
    cyan_mask = ImageChops.multiply(cyan_mask, alpha)

    color = Image.new("RGBA", image.size, (*WHITE, 0))
    cyan_layer = Image.new("RGBA", image.size, (*CYAN, 255))
    color.alpha_composite(Image.composite(cyan_layer, Image.new("RGBA", image.size), cyan_mask))
    color.putalpha(alpha)
    return color, alpha


def crop_and_center(image: Image.Image, alpha: Image.Image) -> Image.Image:
    # Ignore the low-alpha chroma noise produced around the canvas edge. Using the
    # raw alpha bbox would make a handful of nearly transparent pixels define the
    # crop and shrink the real symbol to roughly half of the output canvas.
    confident_alpha = alpha.point(lambda value: 255 if value >= 24 else 0)
    bbox = confident_alpha.getbbox()
    if bbox is None:
        raise ValueError("No icon foreground detected in AppIconSource.png")

    foreground = image.crop(bbox)
    available = OUTPUT_SIZE - PADDING * 2
    scale = min(available / foreground.width, available / foreground.height)
    resized = foreground.resize(
        (round(foreground.width * scale), round(foreground.height * scale)),
        Image.Resampling.LANCZOS,
    )

    canvas = Image.new("RGBA", (OUTPUT_SIZE, OUTPUT_SIZE), (0, 0, 0, 0))
    x = (OUTPUT_SIZE - resized.width) // 2
    y = (OUTPUT_SIZE - resized.height) // 2
    canvas.alpha_composite(resized, (x, y))

    # The generated geometry has balanced outer bounds, but the two horizontal
    # strokes and the top edge put more painted area above the center. Centering
    # only the bounding box therefore makes the icon look high in every host.
    # Align the alpha-weighted center instead; the existing padding leaves enough
    # room for this small optical correction without clipping the outline.
    return center_alpha_mass(canvas)


def alpha_centroid(alpha: Image.Image) -> tuple[float, float]:
    pixels = list(alpha.get_flattened_data())
    total = sum(pixels)
    if total == 0:
        raise ValueError("Cannot center an empty alpha channel")

    width = alpha.width
    x = sum((index % width) * value for index, value in enumerate(pixels)) / total
    y = sum((index // width) * value for index, value in enumerate(pixels)) / total
    return x, y


def center_alpha_mass(image: Image.Image) -> Image.Image:
    alpha = image.getchannel("A")
    center = (image.width - 1) / 2
    centroid_x, centroid_y = alpha_centroid(alpha)
    offset_x = round(center - centroid_x)
    offset_y = round(center - centroid_y)

    centered = Image.new("RGBA", image.size, (0, 0, 0, 0))
    centered.alpha_composite(image, (offset_x, offset_y))
    return centered


def resize_premultiplied(image: Image.Image, size: int) -> Image.Image:
    """Resize RGBA without allowing transparent RGB to create dark fringes."""
    image = image.convert("RGBA")
    alpha = image.getchannel("A")
    premultiplied = [ImageChops.multiply(channel, alpha) for channel in image.convert("RGB").split()]

    resized_alpha = alpha.resize((size, size), Image.Resampling.LANCZOS)
    resized_channels = [
        channel.resize((size, size), Image.Resampling.LANCZOS)
        for channel in premultiplied
    ]

    alpha_values = list(resized_alpha.get_flattened_data())
    channel_values = [list(channel.get_flattened_data()) for channel in resized_channels]
    straight_channels: list[Image.Image] = []
    for values in channel_values:
        straight = [
            0 if alpha_value == 0 else min(255, round(value * 255 / alpha_value))
            for value, alpha_value in zip(values, alpha_values)
        ]
        channel = Image.new("L", (size, size))
        channel.putdata(straight)
        straight_channels.append(channel)

    return Image.merge("RGBA", (*straight_channels, resized_alpha))


def sharpen_small_alpha(alpha: Image.Image, size: int) -> Image.Image:
    # Small Windows icon surfaces need a short, decisive antialiasing ramp. The
    # previous RGB-only sharpness pass left the actual alpha silhouette soft and
    # also composited transparent pixels against black before restoring alpha.
    if size <= 20:
        radius, percent, contrast = 0.55, 190, 1.30
    elif size <= 32:
        radius, percent, contrast = 0.65, 155, 1.20
    else:
        radius, percent, contrast = 0.70, 125, 1.10
    sharpened = alpha.filter(ImageFilter.UnsharpMask(radius=radius, percent=percent, threshold=1))

    def tighten(value: int) -> int:
        adjusted = round((value - 128) * contrast + 128)
        if adjusted <= 6:
            return 0
        if adjusted >= 249:
            return 255
        return max(0, min(255, adjusted))

    return sharpened.point(tighten)


def prepare_small_size(master: Image.Image, size: int) -> Image.Image:
    icon = resize_premultiplied(master, size)
    if size <= SMALL_ICON_MAX_SIZE:
        icon.putalpha(sharpen_small_alpha(icon.getchannel("A"), size))
    return icon


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Post-process a Yurenai app icon source.")
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--outdir", type=Path, default=ASSETS)
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    source_path = args.source.resolve()
    output_dir = args.outdir.resolve()
    if not source_path.exists():
        raise FileNotFoundError(f"Missing Yurenai source image: {source_path}")
    output_dir.mkdir(parents=True, exist_ok=True)

    source = Image.open(source_path).convert("RGBA")
    normalized, alpha = source_mask_and_colors(source)
    master = crop_and_center(normalized, alpha)

    app_png = resize_premultiplied(master, 256)
    app_png.save(output_dir / "AppIcon.png")

    # The custom title bar is exactly 20 DIPs. Scale-qualified resources let MRT
    # select a native physical-pixel frame at every common Windows display scale,
    # including this development machine's 125% setting.
    for scale in TITLE_BAR_SCALES:
        physical_size = round(20 * scale / 100)
        prepare_small_size(master, physical_size).save(
            output_dir / f"AppIcon20.scale-{scale}.png"
        )

    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    ico_frames = [prepare_small_size(master, size) for size in sizes]
    ico_frames[-1].save(
        output_dir / "AppIcon.ico",
        format="ICO",
        append_images=ico_frames[:-1],
        sizes=[(size, size) for size in sizes],
    )


if __name__ == "__main__":
    main()
