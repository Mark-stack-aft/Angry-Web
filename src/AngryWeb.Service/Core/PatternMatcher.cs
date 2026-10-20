using System.Text.RegularExpressions;

namespace AngryWeb.Service.Core;

/// <summary>
/// 고급 패턴 매칭 엔진:
/// 1. 주소/ -> 정확히 해당 호스트 및 루트 경로만 차단
/// 2. 주소/* -> 해당 주소 및 하위 모든 경로 차단
/// 3. *.주소/* -> 와일드카드 서브도메인 및 하위 경로 차단
/// 4. *주소* / 주소* -> 키워드 포함 및 접두사 매칭 차단
/// 5. 주소/ (++ 키워드1 ++ 키워드2) | 키워드3 -> 복합 AND/OR 조건식 파싱
/// </summary>
public class PatternMatcher
{
    private readonly List<CompiledRule> _compiledRules = new();

    public void LoadRules(IEnumerable<RuleDefinition> rules)
    {
        _compiledRules.Clear();
        foreach (var rule in rules)
        {
            if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Pattern))
                continue;

            try
            {
                var compiled = CompileRule(rule.Pattern.Trim());
                if (compiled != null)
                {
                    _compiledRules.Add(compiled);
                }
            }
            catch
            {
                // Ignore invalid patterns gracefully
            }
        }
    }

    public bool IsBlocked(string fullUrl, out string? matchedPattern)
    {
        matchedPattern = null;
        if (string.IsNullOrWhiteSpace(fullUrl))
            return false;

        // Clean url: remove protocol for easier matching
        var urlNoProto = fullUrl;
        if (urlNoProto.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            urlNoProto = urlNoProto.Substring(8);
        else if (urlNoProto.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            urlNoProto = urlNoProto.Substring(7);

        foreach (var rule in _compiledRules)
        {
            if (rule.Matches(urlNoProto, fullUrl))
            {
                matchedPattern = rule.OriginalPattern;
                return true;
            }
        }

        return false;
    }

    private CompiledRule? CompileRule(string pattern)
    {
        // Check for composite expressions: 주소/ (++ kw1 ++ kw2) | kw3
        // Look for '(' and ')' or '++' or '|'
        if (pattern.Contains('|') || pattern.Contains("++") || (pattern.Contains('(') && pattern.Contains(')')))
        {
            return new CompositeCompiledRule(pattern);
        }

        return new StandardCompiledRule(pattern);
    }
}

public abstract class CompiledRule
{
    public string OriginalPattern { get; }
    protected CompiledRule(string pattern) => OriginalPattern = pattern;
    public abstract bool Matches(string urlNoProto, string fullUrl);
}

public class StandardCompiledRule : CompiledRule
{
    private readonly Regex _regex;

    public StandardCompiledRule(string pattern) : base(pattern)
    {
        _regex = BuildRegex(pattern);
    }

    public override bool Matches(string urlNoProto, string fullUrl)
    {
        return _regex.IsMatch(urlNoProto) || _regex.IsMatch(fullUrl);
    }

    private static Regex BuildRegex(string pattern)
    {
        // Normalize pattern: remove http:// or https:// if present
        var p = pattern;
        if (p.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            p = p.Substring(8);
        else if (p.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            p = p.Substring(7);

        // Case 1: *.주소/* -> Subdomain wildcard
        // e.g. *.youtube.com/*
        // Case 2: 주소/ -> Exact root path e.g. youtube.com/
        // Case 3: 주소/* -> Prefix with wildcard path
        // Case 4: *주소* -> Keyword contains
        // Case 5: 주소* -> Prefix

        string regexString;

        if (p.StartsWith("*.") && p.EndsWith("/*"))
        {
            var hostPart = p.Substring(2, p.Length - 4);
            regexString = $"^([^/]+\\.)?{Regex.Escape(hostPart)}(/.*)?$";
        }
        else if (p.EndsWith("/*"))
        {
            var basePart = p.Substring(0, p.Length - 2);
            regexString = $"^{Regex.Escape(basePart)}(/.*)?$";
        }
        else if (p.EndsWith("/"))
        {
            var basePart = p.Substring(0, p.Length - 1);
            // Matches strictly host/ or host without further subpath
            regexString = $"^{Regex.Escape(basePart)}/?$";
        }
        else if (p.StartsWith("*") && p.EndsWith("*") && p.Length > 2)
        {
            var keyword = p.Substring(1, p.Length - 2);
            regexString = Regex.Escape(keyword);
        }
        else if (p.EndsWith("*"))
        {
            var prefix = p.Substring(0, p.Length - 1);
            regexString = $"^{Regex.Escape(prefix)}.*";
        }
        else if (p.StartsWith("*"))
        {
            var suffix = p.Substring(1);
            regexString = $".*{Regex.Escape(suffix)}$";
        }
        else
        {
            // Default: match host or contains
            regexString = Regex.Escape(p);
        }

        return new Regex(regexString, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}

public class CompositeCompiledRule : CompiledRule
{
    private readonly string _basePrefix;
    private readonly List<List<string>> _orBranches = new(); // OR of AND-groups

    public CompositeCompiledRule(string pattern) : base(pattern)
    {
        // Example: 주소/ (++ 키워드1 ++ 키워드2) | 키워드3
        // Parse base host prefix if any:
        var parenStart = pattern.IndexOf('(');
        if (parenStart > 0)
        {
            _basePrefix = pattern.Substring(0, parenStart).Trim();
            var conditionPart = pattern.Substring(parenStart);
            ParseConditions(conditionPart);
        }
        else
        {
            _basePrefix = "";
            ParseConditions(pattern);
        }
    }

    private void ParseConditions(string raw)
    {
        var cleaned = raw.Replace("(", "").Replace(")", "");
        // Split OR '|'
        var orParts = cleaned.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var branch in orParts)
        {
            // Split AND '++'
            var andParts = branch.Split("++", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            _orBranches.Add(andParts.ToList());
        }
    }

    public override bool Matches(string urlNoProto, string fullUrl)
    {
        if (!string.IsNullOrEmpty(_basePrefix))
        {
            var prefix = _basePrefix.TrimEnd('/');
            if (!urlNoProto.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Must satisfy at least one OR branch
        foreach (var andList in _orBranches)
        {
            bool allAndMatch = true;
            foreach (var keyword in andList)
            {
                if (!urlNoProto.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
                    !fullUrl.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    allAndMatch = false;
                    break;
                }
            }

            if (allAndMatch && andList.Count > 0)
                return true;
        }

        return false;
    }
}
