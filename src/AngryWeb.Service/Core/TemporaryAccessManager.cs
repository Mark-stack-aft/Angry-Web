using System.Collections.Concurrent;

namespace AngryWeb.Service.Core;

public class TemporaryAccessManager
{
    // Dictionary of Allowed Host or URL -> Expiration time
    private readonly ConcurrentDictionary<string, DateTime> _temporaryAllowed = new(StringComparer.OrdinalIgnoreCase);

    public void Allow(string target, TimeSpan duration)
    {
        var normalized = Normalize(target);
        var expiry = DateTime.UtcNow.Add(duration);
        _temporaryAllowed[normalized] = expiry;
    }

    public bool IsAllowed(string target)
    {
        var normalized = Normalize(target);
        if (_temporaryAllowed.TryGetValue(normalized, out var expiry))
        {
            if (DateTime.UtcNow < expiry)
            {
                return true;
            }
            // Expired
            _temporaryAllowed.TryRemove(normalized, out _);
        }

        // Also check domain match if target contains domain
        foreach (var kvp in _temporaryAllowed)
        {
            if (DateTime.UtcNow < kvp.Value)
            {
                if (normalized.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string Normalize(string target)
    {
        var t = target.Trim();
        if (t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            t = t.Substring(8);
        else if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            t = t.Substring(7);
        return t.TrimEnd('/');
    }
}
