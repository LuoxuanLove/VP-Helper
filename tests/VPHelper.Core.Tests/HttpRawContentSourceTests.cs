using System.Collections.Generic;
using System.Linq;
using VPHelper.Core.Rankland;
using Xunit;

namespace VPHelper.Core.Tests;

/// <summary>
/// Deterministic coverage of the mirror-URL rewrite logic in <see cref="HttpRawContentSource"/>.
/// The network path (fallback + retry) is exercised only by the opt-in live tests; this pins the
/// pure string transformation that decides which URLs get tried, which is where a rewrite bug
/// (wrong jsDelivr shape, dropped path segment) would silently break every fetch.
/// </summary>
public class HttpRawContentSourceTests
{
    private const string Raw =
        "https://raw.githubusercontent.com/algoux/srk-collection/master/official/config.yaml";

    [Fact]
    public void MirrorsFor_RawUrl_ExpandsToAllMirrors_CanonicalFirst()
    {
        using var src = new HttpRawContentSource();
        var mirrors = src.MirrorsFor(Raw);

        // The canonical host is tried first (it works off China networks, and this preserves the
        // original behaviour for users who can reach it directly).
        Assert.Equal(Raw, mirrors[0]);
        // More than one candidate, and every candidate is a distinct host.
        Assert.True(mirrors.Count >= 4, $"expected >=4 mirrors, got {mirrors.Count}");
        Assert.Equal(mirrors.Count, mirrors.Distinct().Count());
    }

    [Fact]
    public void MirrorsFor_RewritesJsDelivrShape_WithRefAsAtSegment()
    {
        using var src = new HttpRawContentSource();
        var mirrors = src.MirrorsFor(Raw);

        // jsDelivr maps "owner/repo/ref/rest" → "gh/owner/repo@ref/rest".
        var jsDelivr = mirrors.Single(m => m.Contains("cdn.jsdelivr.net"));
        Assert.Equal(
            "https://cdn.jsdelivr.net/gh/algoux/srk-collection@master/official/config.yaml",
            jsDelivr);
    }

    [Fact]
    public void MirrorsFor_PreservesNestedPathSegments()
    {
        using var src = new HttpRawContentSource();
        var nested =
            "https://raw.githubusercontent.com/algoux/srk-collection/master/official/icpc/icpc2024/wuhan.srk.json";
        var mirrors = src.MirrorsFor(nested);

        var jsDelivr = mirrors.Single(m => m.Contains("cdn.jsdelivr.net"));
        Assert.Equal(
            "https://cdn.jsdelivr.net/gh/algoux/srk-collection@master/official/icpc/icpc2024/wuhan.srk.json",
            jsDelivr);
    }

    [Fact]
    public void MirrorsFor_NonRawUrl_ReturnedAsSingleUnchanged()
    {
        using var src = new HttpRawContentSource();
        var other = "https://example.com/some/path.json";
        var mirrors = src.MirrorsFor(other);

        Assert.Single(mirrors);
        Assert.Equal(other, mirrors[0]);
    }
}
