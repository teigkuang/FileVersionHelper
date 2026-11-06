using System.Text.Json.Serialization;

namespace FvHelper.Core;

/// <summary>
/// 索引文档（`.fvhelper.dat` 解压后 JSON 的根）。字段语义见 `docs\03` §3。
/// 序列化统一用 camelCase + 不忽略 null，保证与他人交换时字段齐全。
/// </summary>
public sealed class IndexDocument
{
    public const string SchemaId = "fvhelper.index/1";

    public string Schema { get; set; } = SchemaId;
    public string RepoId { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public int Head { get; set; }
    public string Author { get; set; } = "";
    public List<string> Ignore { get; set; } = new();
    public List<CommitRecord> Commits { get; set; } = new();
}

public sealed class CommitRecord
{
    public int Rev { get; set; }
    public string Id { get; set; } = "";
    public int ParentRev { get; set; }

    /// <summary>提交标题（一行摘要）。</summary>
    public string Message { get; set; } = "";

    /// <summary>提交详情（多行，可空；旧库没有这个字段，读出来是空串）。</summary>
    public string Detail { get; set; } = "";

    public string Author { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public CommitStats Stats { get; set; } = new();
    public List<ChangeRecord> Changes { get; set; } = new();
    public List<TreeEntry> Tree { get; set; } = new();
}

public sealed class CommitStats
{
    public int Files { get; set; }
    public int Added { get; set; }
    public int Modified { get; set; }
    public int Deleted { get; set; }
    public int Renamed { get; set; }
    public long Bytes { get; set; }
}

public sealed class ChangeRecord
{
    /// <summary>相对路径，正斜杠，见 <see cref="PathSafety"/>。</summary>
    public string Path { get; set; } = "";

    /// <summary>"A" / "M" / "D" / "R"。</summary>
    public string Action { get; set; } = "M";

    /// <summary>`sha256:<64hex>`；D 动作无此字段。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Hash { get; set; }

    public long Size { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mtime { get; set; }

    /// <summary>D/R 才有：被删/被改名前的哈希。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OldHash { get; set; }

    /// <summary>R 才有：改名前的路径。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? From { get; set; }
}

public sealed class TreeEntry
{
    public string Path { get; set; } = "";
    public string Hash { get; set; } = "";
    public long Size { get; set; }
}
