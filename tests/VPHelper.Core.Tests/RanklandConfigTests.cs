using System.Linq;
using VPHelper.Core.Analysis;
using VPHelper.Core.Rankland;
using Xunit;

namespace VPHelper.Core.Tests;

public class RanklandConfigTests
{
    private const string SampleYaml = @"
root:
  children:
    - path: icpc
      name: ICPC
      children:
        - path: icpc2024
          name: ICPC 2024
          children:
            - path: icpc2024invitational-wuhan
              format: srk.json
              name: 2024-05-02 武汉邀请赛
            - path: icpc2024nanjing
              format: srk.json
              name: 2024-11-09 南京站
    - path: ucup
      name: Universal Cup
      children:
        - path: ucup3rdfinals
          format: srk.json
          name: 3rd Universal Cup Finals
";

    [Fact]
    public void Parse_ExpandsLeaves()
    {
        var leaves = RanklandConfigParser.Parse(SampleYaml);
        Assert.Equal(3, leaves.Count);
        Assert.Contains(leaves, l => l.Path == "icpc2024invitational-wuhan");
        Assert.Contains(leaves, l => l.Path == "ucup3rdfinals");
    }

    [Fact]
    public void Parse_CapturesPathSegments_And_Category()
    {
        var leaves = RanklandConfigParser.Parse(SampleYaml);
        var wuhan = leaves.Single(l => l.Path == "icpc2024invitational-wuhan");
        Assert.Equal(new[] { "icpc", "icpc2024", "icpc2024invitational-wuhan" }, wuhan.PathSegments.ToArray());
        Assert.Equal("icpc", wuhan.CategoryPath);
    }

    [Fact]
    public void BuildRawUrl_FlatLayout_NoRepeatedLeafDir()
    {
        var leaves = RanklandConfigParser.Parse(SampleYaml);
        var wuhan = leaves.Single(l => l.Path == "icpc2024invitational-wuhan");
        var url = wuhan.BuildRawUrl();
        // Real (verified) layout: official/<seg1>/<seg2>/<leaf>.srk.json — leaf dir NOT repeated.
        Assert.Equal(
            "https://raw.githubusercontent.com/algoux/srk-collection/master/official/icpc/icpc2024/icpc2024invitational-wuhan.srk.json",
            url);
    }

    [Fact]
    public void BuildRawUrl_Depth3_CategoryDirectLeaf()
    {
        var leaves = RanklandConfigParser.Parse(SampleYaml);
        var finals = leaves.Single(l => l.Path == "ucup3rdfinals");
        var url = finals.BuildRawUrl();
        Assert.Equal(
            "https://raw.githubusercontent.com/algoux/srk-collection/master/official/ucup/ucup3rdfinals.srk.json",
            url);
    }

    [Theory]
    [InlineData("hecpc9th", "https://rl.algoux.cn/collection/official?rankId=hecpc9th")]
    [InlineData("ICPC 2024/南京", "https://rl.algoux.cn/collection/official?rankId=ICPC%202024%2F%E5%8D%97%E4%BA%AC")]
    public void BuildOfficialContestUrl_UsesEscapedRankId(string officialId, string expected)
    {
        Assert.Equal(expected, RanklandPaths.BuildOfficialContestUrl(officialId));
    }

    [Fact]
    public void BuildOfficialContestUrl_EmptyId_ReturnsNull()
    {
        Assert.Null(RanklandPaths.BuildOfficialContestUrl("  "));
    }

    [Fact]
    public void Parse_RealConfig_ExpandsManyLeaves()
    {
        var yaml = Fixtures.Read("config.yaml");
        var leaves = RanklandConfigParser.Parse(yaml);
        // The real config carries hundreds of leaves.
        Assert.True(leaves.Count > 100, $"expected >100 leaves, got {leaves.Count}");
        // Every leaf must have a non-empty path and category.
        Assert.All(leaves, l =>
        {
            Assert.False(string.IsNullOrEmpty(l.Path));
            Assert.False(string.IsNullOrEmpty(l.CategoryPath));
            Assert.EndsWith(".srk.json", l.BuildRawUrl());
        });
    }

    [Fact]
    public void Parse_RealConfig_KnownLeafPathIsCorrect()
    {
        var yaml = Fixtures.Read("config.yaml");
        var leaves = RanklandConfigParser.Parse(yaml);
        var wuhan = leaves.FirstOrDefault(l => l.Path == "icpc2024invitational-wuhan");
        Assert.NotNull(wuhan);
        Assert.Equal(
            "https://raw.githubusercontent.com/algoux/srk-collection/master/official/icpc/icpc2024/icpc2024invitational-wuhan.srk.json",
            wuhan!.BuildRawUrl());
    }

    [Fact]
    public void Parse_RealConfig_ProvincialAndInvitationalLeavesRemainInSupportedCatalog()
    {
        var leaves = RanklandConfigParser.Parse(Fixtures.Read("config.yaml"));

        var supported = leaves
            .Where(leaf => ContestDisplayNameBuilder.IsSupportedDirectoryCategory(leaf.CategoryPath))
            .Where(leaf => ContestDisplayNameBuilder.ShouldShowInDirectory(leaf.Path, leaf.Name, leaf.CategoryPath))
            .Select(leaf => leaf.Path)
            .ToHashSet();

        // Regression guard: Rankland provincial leaves and East-Continent invitationals are
        // loaded from config.yaml, rather than being limited to the hand-curated link table.
        foreach (var officialId in new[]
                 {
                     "hecpc9th", "jscpc10th", "gdcpc22nd", "jxcpc2025", "sdcpc15th",
                     "zjcpc22nd", "icpc2025invitational-nanchang", "icpc2026invitational-shenzhen",
                 })
        {
            Assert.Contains(officialId, supported);
        }
    }
}
