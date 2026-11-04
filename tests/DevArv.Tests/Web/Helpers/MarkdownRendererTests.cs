using DevArv.Web.Helpers;

namespace DevArv.Tests.Web.Helpers;

public class MarkdownRendererTests
{
    [Fact]
    public void EscapesHtmlAndUnsafeLinks()
    {
        var html = MarkdownRenderer.Render("<script>alert(1)</script> [x](javascript:alert(1)) [ok](https://github.com)").ToString()!;
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("javascript:", html);
        Assert.Contains("https://github.com", html);
    }
}
