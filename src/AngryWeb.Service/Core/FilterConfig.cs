namespace AngryWeb.Service.Core;

public class FilterConfig
{
    public string MasterPasswordHash { get; set; } = "angryweb"; // Default or SHA256
    public string GitHubRulesUrl { get; set; } = "https://raw.githubusercontent.com/code-ing-m/Angry-Web/main/rules.json";
    public int ProxyPort { get; set; } = 8899;
    public int BlockPagePort { get; set; } = 8898;
    public int SyncIntervalMinutes { get; set; } = 10;
    public List<RuleDefinition> Rules { get; set; } = new();
    public BlockPageContent BlockPage { get; set; } = new();
}

public class RuleDefinition
{
    public string Pattern { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
}

public class BlockPageContent
{
    public string Title { get; set; } = "접속이 차단되었습니다 (Angry Web)";
    public string Heading { get; set; } = "집중할 시간입니다!";
    public string SubHeading { get; set; } = "현재 사이트는 블랙리스트 규칙에 의해 차단되었습니다.";
    public string Message { get; set; } = "이 사이트는 생산성 향상을 위해 차단된 사이트입니다. 지금 하는 일에 집중하세요!";
    public string Quote { get; set; } = "오늘 걷지 않으면 내일은 뛰어야 한다.";
}
