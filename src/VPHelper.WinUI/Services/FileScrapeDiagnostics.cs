using System;
using System.IO;
using System.Text;
using VPHelper.Core.Scraping;

namespace VPHelper.WinUI.Services;

/// <summary>
/// File-backed <see cref="IScrapeDiagnostics"/>: writes a timestamped log and captured raw pages
/// under a per-run folder in the app data directory. Lets a real, human-driven sync be diagnosed
/// from artifacts (log + first page's HTML) instead of screenshots when the live Codeforces DOM
/// differs from the fixtures the parser was written against.
/// </summary>
public sealed class FileScrapeDiagnostics : IScrapeDiagnostics
{
    private readonly string _dir;
    private readonly object _gate = new();

    public FileScrapeDiagnostics(string baseFolder)
    {
        _dir = Path.Combine(baseFolder, "diagnostics", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(_dir);
    }

    /// <summary>Absolute path of the log file, so the UI can point the user at it.</summary>
    public string LogPath => Path.Combine(_dir, "scrape.log");

    /// <summary>Absolute path of the run folder (log + captured HTML).</summary>
    public string Folder => _dir;

    public void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";
        lock (_gate)
            File.AppendAllText(LogPath, line, Encoding.UTF8);
    }

    public void CaptureRaw(string name, string content)
    {
        // Sanitize the artifact name to a safe file name.
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        try
        {
            lock (_gate)
                File.WriteAllText(Path.Combine(_dir, name), content ?? string.Empty, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Log($"CaptureRaw('{name}') failed: {ex.Message}");
        }
    }
}
