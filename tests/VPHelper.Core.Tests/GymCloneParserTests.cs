using VPHelper.Core.Scraping;
using Xunit;

namespace VPHelper.Core.Tests;

public class GymCloneParserTests
{
    [Fact]
    public void Parse_CodeforcesCloneSidebar_ReturnsDirectParent()
    {
        const string html = """
            <html><body>
            <div class="roundbox sidebox">
              <div class="caption titled">→ Clone Contest to Mashup</div>
              <div>
                You can not clone this contest because it was cloned from
                <a href="/gym/105486">The 2024 CCPC Shandong Invitational Contest and Provincial Collegiate Programming Contest</a>.
                Maybe you want to copy
                <a href="https://codeforces.com/gym/105486">The 2024 CCPC Shandong Invitational Contest and Provincial Collegiate Programming Contest</a>?
              </div>
            </div>
            </body></html>
            """;

        var clone = GymCloneParser.Parse(html);

        Assert.NotNull(clone);
        Assert.Equal(105486L, clone!.ParentGymId);
        Assert.Equal("The 2024 CCPC Shandong Invitational Contest and Provincial Collegiate Programming Contest",
            clone.ParentTitle);
    }

    [Fact]
    public void Parse_NotCloned_ReturnsNull()
    {
        const string html = """
            <html><body>
            <div class="roundbox sidebox">
              <div class="caption titled">→ Clone Contest to Mashup</div>
              <div>You can clone this contest to a mashup.</div>
            </div>
            </body></html>
            """;

        Assert.Null(GymCloneParser.Parse(html));
    }
}
