using System;
using System.IO;
using System.Linq;
using FvHelper.Core;

/// <summary>
/// FvHelper 命令行（P2 起的"第二入口"）：既能给自动化测试用，也能在你没有界面的机器上验数据。
/// 用法：FvHelper.Cli &lt;命令&gt; &lt;项目目录&gt; [参数]
///   init &lt;dir&gt; [--author=名字]
///   status &lt;dir&gt;
///   commit &lt;dir&gt; --message="说明" [--author=名字] [--only=路径1,路径2] [--detail=""详情""]
///   log &lt;dir&gt; [--json]
///   show &lt;dir&gt; --rev=N
///   export &lt;dir&gt; --rev=N --to=目录 [--files-only] [--new-start]
///   verify &lt;dir&gt; [--deep] [--no-workspace] [--json]
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 2;
            }

            var command = args[0].ToLowerInvariant();
            var options = ParseOptions(args.Skip(1).ToArray());
            var target = options.Positional.FirstOrDefault();

            return command switch
            {
                "init" => Init(target, options),
                "status" => Status(target, options),
                "commit" => Commit(target, options),
                "log" => Log(target, options),
                "show" => Show(target, options),
                "export" => Export(target, options),
                "verify" => Verify(target, options),
                "help" or "--help" or "-h" => PrintUsage(),
                _ => Unknown(command)
            };
        }
        catch (RepositoryException ex)
        {
            Console.Error.WriteLine("错误：" + ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("意外错误：" + ex);
            return 3;
        }
    }

    private static int PrintUsage()
    {
        Console.WriteLine(@"FvHelper 命令行

  init    <项目目录> [--author=名字]
  status  <项目目录>
  commit  <项目目录> --message=""说明"" [--author=名字] [--only=路径1,路径2] [--detail=""详情""]
  log     <项目目录> [--json]
  show    <项目目录> --rev=N
  export  <项目目录> --rev=N --to=目录 [--files-only] [--new-start]
  verify  <项目目录> [--deep] [--no-workspace] [--json]");
        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine("未知命令：" + command);
        PrintUsage();
        return 2;
    }

    private static int Init(string? target, Options options)
    {
        if (target is null)
        {
            Console.Error.WriteLine("缺少项目目录");
            return 2;
        }

        var author = options.Value("author") ?? Environment.UserName;
        var repo = Repository.Init(target, author);
        Console.WriteLine($"已初始化：{repo.Root}");
        Console.WriteLine($"repoId：{repo.Index.RepoId}");
        Console.WriteLine($"作者：{repo.Index.Author}");
        return 0;
    }

    private static int Status(string? target, Options options)
    {
        var repo = Open(target);
        var status = repo.Status();
        var plan = status.Plan;

        Console.WriteLine($"项目：{repo.Root}");
        Console.WriteLine($"状态：{(status.Health == RepoHealth.RecoveredFromBackup ? "已从备份恢复" : "正常")}{(status.Notice is null ? "" : "（" + status.Notice + "）")}");
        if (plan.Changes.Count == 0)
        {
            Console.WriteLine("与最新提交一致，没有待提交改动。");
            return 0;
        }

        Console.WriteLine($"待提交：{plan.Changes.Count} 项（修改 {plan.Stats.Modified} / 新增 {plan.Stats.Added} / 删除 {plan.Stats.Deleted} / 改名 {plan.Stats.Renamed}，共 {plan.Stats.Files} 个文件在树中）");
        foreach (var change in plan.Changes)
        {
            var extra = change.Action == "R" && change.From is not null ? $" ← {change.From}" : "";
            Console.WriteLine($"  {change.Action}  {change.Path}{extra}  ({FormatSize(change.Size)})");
        }

        if (status.InUse.Count > 0)
        {
            Console.WriteLine("提示：以下文件正被其它程序打开（仍可提交）：" + string.Join("、", status.InUse));
        }

        foreach (var uncertain in plan.Uncertain)
        {
            Console.WriteLine("无法读取：" + uncertain);
        }

        return 0;
    }

    private static int Commit(string? target, Options options)
    {
        var message = options.Value("message");
        if (message is null)
        {
            Console.Error.WriteLine("缺少 --message=\"说明\"");
            return 2;
        }

        var repo = Open(target);
        var author = options.Value("author") ?? (string.IsNullOrWhiteSpace(repo.Index.Author) ? Environment.UserName : repo.Index.Author);
        var only = options.Value("only")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var detail = options.Value("detail") ?? "";

        var result = repo.Commit(message, author, only, default, detail);
        foreach (var warning in result.Warnings)
        {
            Console.WriteLine("提示：" + warning);
        }

        Console.WriteLine($"提交成功：rev {result.Record.Rev}（{result.Record.Id}）");
        Console.WriteLine($"说明：{result.Record.Message}");
        Console.WriteLine($"作者：{result.Record.Author}　时间：{result.Record.CreatedAt}");
        Console.WriteLine($"文件：{result.Record.Stats.Files} 个（A {result.Record.Stats.Added} / M {result.Record.Stats.Modified} / D {result.Record.Stats.Deleted} / R {result.Record.Stats.Renamed}）");
        Console.WriteLine($"数据文件：{result.DatPath}");
        return 0;
    }

    private static int Log(string? target, Options options)
    {
        var repo = Open(target);
        if (options.Flag("json"))
        {
            var json = System.Text.Json.JsonSerializer.Serialize(repo.History(), Repository.JsonOptions);
            Console.WriteLine(json);
            return 0;
        }

        if (repo.Index.Commits.Count == 0)
        {
            Console.WriteLine("还没有任何提交。");
            return 0;
        }

        foreach (var commit in repo.History())
        {
            Console.WriteLine($"rev {commit.Rev}　{commit.CreatedAt}　{commit.Author}　{commit.Message}");
            Console.WriteLine($"       {commit.Stats.Files} 个文件（A {commit.Stats.Added} / M {commit.Stats.Modified} / D {commit.Stats.Deleted} / R {commit.Stats.Renamed}）　{FormatSize(commit.Stats.Bytes)}");
        }

        return 0;
    }

    private static int Show(string? target, Options options)
    {
        var rev = int.TryParse(options.Value("rev"), out var n) ? n : -1;
        if (rev < 0)
        {
            Console.Error.WriteLine("缺少 --rev=N");
            return 2;
        }

        var repo = Open(target);
        var commit = repo.GetCommit(rev);
        if (commit is null)
        {
            Console.Error.WriteLine($"没有 rev {rev}");
            return 1;
        }

        Console.WriteLine($"rev {commit.Rev} · {commit.Message}");
        if (!string.IsNullOrWhiteSpace(commit.Detail))
        {
            Console.WriteLine("详情：" + commit.Detail.Replace("\r\n", " / ").Replace("\n", " / "));
        }

        Console.WriteLine($"作者 {commit.Author}　{commit.CreatedAt}");
        Console.WriteLine($"文件 {commit.Stats.Files} 个，{FormatSize(commit.Stats.Bytes)}");
        foreach (var change in commit.Changes)
        {
            var from = change.From is null ? "" : $" ← {change.From}";
            Console.WriteLine($"  {change.Action}  {change.Path}{from}");
        }

        return 0;
    }

    private static int Export(string? target, Options options)
    {
        var repo = Open(target);
        var rev = int.TryParse(options.Value("rev"), out var n) ? n : repo.Index.Head;
        var to = options.Value("to");
        if (to is null)
        {
            Console.Error.WriteLine("缺少 --to=导出目录");
            return 2;
        }

        var mode = options.Flag("files-only") ? ExportMode.FilesOnly : ExportMode.WithMeta;
        var result = Exporter.Export(repo, rev, new ExportOptions
        {
            TargetDirectory = to,
            Mode = mode,
            AsNewStart = options.Flag("new-start")
        });

        Console.WriteLine("导出完成：" + result.RootPath);
        Console.WriteLine($"文件 {result.FileCount} 个，{FormatSize(result.Bytes)}；模式：{(result.Mode == ExportMode.FilesOnly ? "仅文件" : "含版本信息" + (options.Flag("new-start") ? "（作为新起点）" : ""))}");
        if (result.DatPath is not null)
        {
            Console.WriteLine("数据文件：" + result.DatPath);
        }

        return 0;
    }

    private static int Verify(string? target, Options options)
    {
        var repoRoot = target ?? throw new RepositoryException("缺少项目目录");
        var result = Verifier.Check(repoRoot, deepHash: options.Flag("deep"),
            checkWorkspace: !options.Flag("no-workspace"));

        if (options.Flag("json"))
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                level = result.LevelText,
                isNewRepo = result.IsNewRepo,
                layers = result.LayersPassed,
                files = result.Files,
                bytes = result.Bytes,
                problems = result.Problems,
                warnings = result.Warnings
            }, Repository.JsonOptions);
            Console.WriteLine(json);
            return result.Level is VerifyLevel.Ok or VerifyLevel.Warn ? 0 : 1;
        }

        Console.WriteLine($"结论：{result.LevelText}（通过 {result.LayersPassed}/4 层）");
        foreach (var problem in result.Problems)
        {
            Console.WriteLine("  ✗ " + problem);
        }

        foreach (var warning in result.Warnings)
        {
            Console.WriteLine("  ! " + warning);
        }

        return result.Level is VerifyLevel.Ok or VerifyLevel.Warn ? 0 : 1;
    }

    private static Repository Open(string? target)
    {
        if (target is null)
        {
            throw new RepositoryException("缺少项目目录");
        }

        return Repository.Open(target);
    }

    private static string FormatSize(long bytes) =>
        bytes < 1024 ? bytes + " B"
        : bytes < 1024 * 1024 ? $"{bytes / 1024.0:F1} KB"
        : $"{bytes / 1024.0 / 1024.0:F1} MB";

    private sealed class Options
    {
        public List<string> Positional { get; } = new();
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

        public string? Value(string key) => _values.TryGetValue(key, out var v) ? v : null;

        public bool Flag(string key) => _flags.Contains(key);

        public void AddFlag(string key) => _flags.Add(key);

        public void AddValue(string key, string value) => _values[key] = value;
    }

    private static Options ParseOptions(string[] args)
    {
        var options = new Options();
        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var body = arg[2..];
                var eq = body.IndexOf('=');
                if (eq < 0)
                {
                    options.AddFlag(body);
                }
                else
                {
                    options.AddValue(body[..eq], body[(eq + 1)..].Trim('"'));
                }
            }
            else
            {
                options.Positional.Add(arg.Trim('"'));
            }
        }

        return options;
    }
}
