using VPHelper.Core.Analysis;
using Xunit;

namespace VPHelper.Core.Tests;

public class ContestDisplayNameBuilderTests
{
    [Theory]
    [InlineData("ccpc2026invitational-guizhou", "2026-06-07 贵州邀请赛", "2026 CCPC贵州邀请赛")]
    [InlineData("icpc2026invitational-shenzhen", "2026-04-11 深圳邀请赛", "2026 ICPC深圳邀请赛")]
    [InlineData("ahcpc2026", "2026-05-24 2026", "2026 安徽省赛")]
    [InlineData("ahcpc2026preliminary", "2026-05-10 2026（初赛）", "2026 安徽省赛初赛")]
    [InlineData("bjcpc2026", "2026-05-10 2026", "2026 北京市赛")]
    [InlineData("cqcpc14th", "2026-06-14 第十四届", "2026 重庆市赛")]
    [InlineData("fjcpc13th", "2026-05-30 第十三届", "2026 福建省赛")]
    [InlineData("gdcpc23rd", "2026-05-17 第二十三届", "2026 广东省赛")]
    [InlineData("gxcpc9th", "2026-05-31 第九届", "2026 广西区赛")]
    [InlineData("gzcpc2026", "2026-06-07 2026", "2026 贵州省赛")]
    [InlineData("haccpc8th", "2026-05-10 第八届 CCPC", "2026 CCPC河南省赛")]
    [InlineData("haicpc17th", "2026-05-24 第十七届 ICPC", "2026 ICPC河南省赛")]
    [InlineData("hbcpc7th", "2026-05-17 第七届", "2026 ICPC武汉邀请赛 & 湖北省赛")]
    [InlineData("hecpc10th", "2026-05-24 第十届", "2026 河北省赛")]
    [InlineData("hlcpc21st", "2026-05-10 第二十一届", "2026 黑龙江省赛")]
    [InlineData("jlcpc19th", "2026-05-24 第十九届", "2026 吉林省赛")]
    [InlineData("jscpc11th", "2026-05-17 第十一届", "2026 江苏省赛")]
    [InlineData("jxccpc3rd", "2026-05-24 第三届 CCPC", "2026 CCPC江西省赛")]
    [InlineData("jxcpc2026", "2026-05-17 2026", "2026 江西省赛")]
    [InlineData("nmcpc19th", "2026-05-31 第十九届", "2026 内蒙古区赛")]
    [InlineData("northeastcpc20th", "2026-05-24 第二十届", "2026 东北地区赛")]
    [InlineData("sccpc18th", "2026-05-31 第十八届", "2026 四川省赛")]
    [InlineData("sdcpc16th", "2026-05-24 第十六届", "2026 山东省赛")]
    [InlineData("sncpc14th", "2026-05-16 第十四届", "2026 陕西省赛")]
    [InlineData("xjcpc2026", "2026-04-25 2026", "2026 新疆区赛")]
    [InlineData("zjcpc23rd", "2026-04-25 第二十三届", "2026 浙江省赛")]
    public void BuildDisplayName_UsesContestFamilyForTerseOfficialNames(string officialId, string sourceName, string expected)
    {
        Assert.Equal(expected, ContestDisplayNameBuilder.BuildDisplayName(officialId, sourceName));
    }

    [Theory]
    [InlineData("icpc2009preliminary-wuhan", "2009-??-?? 武汉网络赛", "2009 ICPC武汉网络赛")]
    [InlineData("icpc2009preliminary-shanghai", "2009-??-?? 上海网络赛", "2009 ICPC上海网络赛")]
    [InlineData("icpc2011preliminary-ccniit", "2011-09-03 CCNIIT 网络赛", "2011 ICPC CCNIIT网络赛")]
    [InlineData("icpc2020preliminary-xiaomi-1", "2020-05-01 小米网络赛第一场", "2020 ICPC小米网络赛第一场")]
    [InlineData("icpc2026invitational-wuhan", "2026-05-17 武汉邀请赛", "2026 ICPC武汉邀请赛 & 湖北省赛")]
    [InlineData("ccpc2025invitational-guangdong-preliminary", "2025-06-02 广东邀请赛 预赛", "2025 CCPC广东邀请赛预赛")]
    public void BuildDisplayName_KeepsPreliminaryPlaceAndCoHostedNames(string officialId, string sourceName, string expected)
    {
        Assert.Equal(expected, ContestDisplayNameBuilder.BuildDisplayName(officialId, sourceName));
    }

    [Fact]
    public void BuildDisplayName_RemovesWarmupMarkerFromFallback()
    {
        var name = ContestDisplayNameBuilder.BuildDisplayName(
            "custom2026contest",
            "2026-05-01 测试比赛（热身赛）");

        Assert.Equal("2026 测试比赛", name);
    }

    [Theory]
    [InlineData("sdutpc18", "2026-05-31 第十八届", "university-level", false)]
    [InlineData("thupc2023", "2023-05-28 2023", "university-level", false)]
    [InlineData("custom2026warmup", "2026-05-01 测试比赛 Warmup", "icpc", false)]
    [InlineData("icpc2025ecfinalwarmup", "ICPC 2025 EC Final 热身赛", "icpc", false)]
    [InlineData("icpc2025preliminary-1", "2025 区域网络赛第一场", "icpc", false)]
    [InlineData("ccpc2025invitational-guangdong-preliminary", "2025 广东邀请赛 预赛", "ccpc", false)]
    [InlineData("ccpc2025preliminary", "2025 网络预选赛", "ccpc", false)]
    [InlineData("northeastcpc20th", "2026-05-24 第二十届", "provincial", true)]
    public void ShouldShowInDirectory_FiltersUniversityAndWarmup(string officialId, string sourceName, string categoryPath, bool expected)
    {
        Assert.Equal(expected, ContestDisplayNameBuilder.ShouldShowInDirectory(officialId, sourceName, categoryPath));
    }

    [Theory]
    [InlineData("bjcpc2024", "2024 北京市赛", true)]
    [InlineData("", "2023 北京市大学生程序设计竞赛", true)]
    [InlineData("icpc2024nanjing", "2024 ICPC 南京站", false)]
    public void IsProvincialOrInvitational_ClassifiesCityCollegiateContests(
        string officialId,
        string displayName,
        bool expected)
    {
        Assert.Equal(expected, ContestDisplayNameBuilder.IsProvincialOrInvitational(officialId, displayName));
    }

    [Theory]
    [InlineData("icpc", true)]
    [InlineData("ccpc", true)]
    [InlineData("provincial", true)]
    [InlineData("university-level", false)]
    [InlineData("ucup", false)]
    public void IsSupportedDirectoryCategory_OnlyKeepsOfficialCompetitionCollections(string categoryPath, bool expected)
    {
        Assert.Equal(expected, ContestDisplayNameBuilder.IsSupportedDirectoryCategory(categoryPath));
    }
}
