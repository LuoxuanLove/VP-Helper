using VPHelper.Core.Scraping;
using Xunit;

namespace VPHelper.Core.Tests;

public class QojSessionParserTests
{
    [Fact]
    public void LoggedInFixture_UsesTopBarAccount()
    {
        var session = QojSessionParser.Parse(Fixtures.Read("LuoxuanLove - User profile - QOJ.ac.html"));

        Assert.True(session.IsLoggedIn);
        Assert.Equal("LuoxuanLove", session.Handle);
    }

    [Fact]
    public void LoggedOutPage_DoesNotTreatContentAuthorAsCurrentAccount()
    {
        const string html = """
            <html><body>
              <header><a href="/login">Login</a><a href="/register">Register</a></header>
              <main><article>by <a href="/user/profile/Qingyu">Qingyu</a></article></main>
            </body></html>
            """;

        var session = QojSessionParser.Parse(html);

        Assert.False(session.IsLoggedIn);
        Assert.Null(session.Handle);
    }

    [Fact]
    public void LogoutWithoutProfile_IsStillAuthenticatedButHandleUnknown()
    {
        var session = QojSessionParser.Parse("<header><a href='/logout?_token=x'>Logout</a></header>");

        Assert.True(session.IsLoggedIn);
        Assert.Null(session.Handle);
    }
}
