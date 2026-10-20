using AngryWeb.Service.Core;
using Xunit;

namespace AngryWeb.Tests;

public class PatternMatcherTests
{
    private readonly PatternMatcher _matcher = new();

    [Theory]
    // 1. 주소/ -> 정확히 해당 경로만 차단
    [InlineData("example.com/", "https://example.com/", true)]
    [InlineData("example.com/", "https://example.com", true)]
    [InlineData("example.com/", "https://example.com/subpath", false)]
    // 2. 주소/* -> 해당 주소 및 하위 모든 경로 차단
    [InlineData("badsite.org/*", "https://badsite.org/", true)]
    [InlineData("badsite.org/*", "https://badsite.org/games", true)]
    [InlineData("badsite.org/*", "https://badsite.org/games/play/123", true)]
    [InlineData("badsite.org/*", "https://other.org/badsite.org", false)]
    // 3. *.주소/* -> 와일드카드 서브도메인 및 하위 경로 차단
    [InlineData("*.stream.tv/*", "https://stream.tv/live", true)]
    [InlineData("*.stream.tv/*", "https://sub.stream.tv/live", true)]
    [InlineData("*.stream.tv/*", "https://asia.sub.stream.tv/live", true)]
    [InlineData("*.stream.tv/*", "https://diffstream.tv/live", false)]
    // 4. *주소* / 주소* -> 키워드 포함 및 접두사 매칭 차단
    [InlineData("*casino*", "https://safe.com/casino/win", true)]
    [InlineData("*casino*", "https://casinoking.com", true)]
    [InlineData("*casino*", "https://news.com/today", false)]
    [InlineData("shorts*", "https://shorts.youtube.com", true)]
    // 5. 주소/ (++ 키워드1 ++ 키워드2) | 키워드3 -> 복합 조건식
    [InlineData("youtube.com/ (++ watch ++ v=fun) | shorts", "https://youtube.com/watch?v=fun", true)]
    [InlineData("youtube.com/ (++ watch ++ v=fun) | shorts", "https://youtube.com/watch?v=study", false)]
    [InlineData("youtube.com/ (++ watch ++ v=fun) | shorts", "https://youtube.com/shorts/abc123", true)]
    public void Test_PatternRules(string pattern, string testUrl, bool expectedBlocked)
    {
        _matcher.LoadRules(new List<RuleDefinition>
        {
            new() { Pattern = pattern, Enabled = true }
        });

        var isBlocked = _matcher.IsBlocked(testUrl, out var matched);
        Assert.Equal(expectedBlocked, isBlocked);
    }
}
