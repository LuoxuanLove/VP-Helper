namespace VPHelper.WinUI.Services;

public readonly record struct WindowBounds(int X, int Y, int Width, int Height);
public readonly record struct WindowLimits(int MinWidth, int MinHeight, int MaxWidth, int MaxHeight,
    double MinAspect, double MaxAspect);

/// <summary>All persisted bounds are physical pixels; design sizes are DIPs.</summary>
public static class WindowLayoutPolicy
{
    public const int InitialWidth = 1440;
    public const int InitialHeight = 900;
    public const int MinimumWidth = 1000;
    public const int MinimumHeight = 680;
    public const double MinimumAspect = 4d / 3;
    public const double MaximumAspect = 21d / 9;

    public static WindowLimits GetLimits(int workWidth, int workHeight, double scale)
    {
        var maxWidth = Math.Max(1, workWidth);
        var maxHeight = Math.Max(1, workHeight);
        scale = double.IsFinite(scale) && scale > 0 ? scale : 1;
        var minWidth = Math.Min(maxWidth, (int)Math.Ceiling(MinimumWidth * scale));
        var minHeight = Math.Min(maxHeight, (int)Math.Ceiling(MinimumHeight * scale));
        // On very small/portrait displays, fitting the actual work area takes precedence.
        return new(minWidth, minHeight, maxWidth, maxHeight,
            Math.Min(MinimumAspect, (double)maxWidth / minHeight),
            Math.Max(MaximumAspect, (double)minWidth / maxHeight));
    }

    public static (int Width, int Height) Constrain(int width, int height, WindowLimits limits, bool heightDriven = false)
    {
        if (heightDriven)
        {
            height = Math.Clamp(height, Math.Max(limits.MinHeight, (int)Math.Ceiling(limits.MinWidth / limits.MaxAspect)),
                Math.Min(limits.MaxHeight, (int)Math.Floor(limits.MaxWidth / limits.MinAspect)));
            width = Math.Clamp(width, Math.Max(limits.MinWidth, (int)Math.Ceiling(height * limits.MinAspect)),
                Math.Min(limits.MaxWidth, (int)Math.Floor(height * limits.MaxAspect)));
        }
        else
        {
            width = Math.Clamp(width, Math.Max(limits.MinWidth, (int)Math.Ceiling(limits.MinHeight * limits.MinAspect)),
                Math.Min(limits.MaxWidth, (int)Math.Floor(limits.MaxHeight * limits.MaxAspect)));
            height = Math.Clamp(height, Math.Max(limits.MinHeight, (int)Math.Ceiling(width / limits.MaxAspect)),
                Math.Min(limits.MaxHeight, (int)Math.Floor(width / limits.MinAspect)));
        }
        return (width, height);
    }

    public static WindowBounds Restore(WindowBounds? saved, WindowBounds work, double scale)
    {
        var limits = GetLimits(work.Width, work.Height, scale);
        var fit = Math.Min(1, Math.Min(work.Width * .9 / (InitialWidth * scale), work.Height * .9 / (InitialHeight * scale)));
        var (width, height) = Constrain(saved?.Width ?? (int)(InitialWidth * scale * fit),
            saved?.Height ?? (int)(InitialHeight * scale * fit), limits);
        var x = saved?.X ?? work.X + (work.Width - width) / 2;
        var y = saved?.Y ?? work.Y + (work.Height - height) / 2;
        return new(Math.Clamp(x, work.X, work.X + work.Width - width),
            Math.Clamp(y, work.Y, work.Y + work.Height - height), width, height);
    }
}
