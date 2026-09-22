namespace VPHelper.Core.Scraping;

/// <summary>
/// Sink for scrape diagnostics. Kept in Core (no I/O) so the scraper can emit structured facts;
/// the WinUI layer supplies a file-backed implementation. Lets a real, human-driven run be
/// diagnosed from a log + captured HTML instead of screenshots when the live DOM differs from
/// the fixtures the parser was written against.
/// </summary>
public interface IScrapeDiagnostics
{
    /// <summary>Append a single diagnostic line (timestamped by the implementation).</summary>
    void Log(string message);

    /// <summary>Persist a raw artifact (e.g. a page's HTML) under <paramref name="name"/>.</summary>
    void CaptureRaw(string name, string content);
}
