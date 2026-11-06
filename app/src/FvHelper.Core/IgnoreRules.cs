using System.IO;
using System.Text.RegularExpressions;

namespace FvHelper.Core;

/// <summary>
/// 忽略规则（glob）：工具私有文件永远忽略（I-09），其余按索引里的 `ignore` 列表匹配。
/// 规则含 `/` 时按完整相对路径匹配，否则按文件名（最后一段）匹配。
/// </summary>
public sealed class IgnoreRules
{
    public static readonly string[] Defaults =
    {
        ".fvhelper/**",
        ".fvhelper.dat*",
        "~$*",
        "*.tmp",
        "Thumbs.db",
        "desktop.ini"
    };

    private readonly List<(Regex Regex, bool FullPath)> _rules = new();

    public IgnoreRules(IEnumerable<string>? patterns = null)
    {
        foreach (var pattern in Defaults.Concat(patterns ?? Array.Empty<string>()))
        {
            AddPattern(pattern);
        }
    }

    private void AddPattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return;
        }

        var p = pattern.Replace('\\', '/').Trim();
        var fullPath = p.Contains('/');
        if (!fullPath && p.StartsWith("*.") && p.Length > 2)
        {
            // *.ext 只需比后缀，但统一走 glob 更简单
        }

        var regex = GlobToRegex(p);
        _rules.Add((regex, fullPath));
    }

    public bool IsIgnored(string relative)
    {
        var p = relative.Replace('\\', '/');
        if (PathSafety.IsInternalPath(p))
        {
            return true;
        }

        var name = p.Contains('/') ? p[(p.LastIndexOf('/') + 1)..] : p;

        foreach (var (regex, fullPath) in _rules)
        {
            if (fullPath)
            {
                // 含 `/` 的规则除了整条路径，也允许匹配"路径的某个后缀段"
                // 这样 `草稿/*` 能命中 `正文/草稿/第一章.md`
                var candidate = p;
                while (true)
                {
                    if (regex.IsMatch(candidate))
                    {
                        return true;
                    }

                    var slash = candidate.IndexOf('/');
                    if (slash < 0)
                    {
                        break;
                    }

                    candidate = candidate[(slash + 1)..];
                }
            }
            else if (regex.IsMatch(name) || regex.IsMatch(p))
            {
                return true;
            }
        }

        return false;
    }

    private static Regex GlobToRegex(string pattern)
    {
        var sb = new System.Text.StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '*')
            {
                var isDouble = i + 1 < pattern.Length && pattern[i + 1] == '*';
                if (isDouble)
                {
                    sb.Append(".*");
                    i++;
                }
                else
                {
                    sb.Append("[^/]*");
                }
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
            }
        }

        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
