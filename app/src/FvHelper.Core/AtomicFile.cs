using System.IO;

namespace FvHelper.Core;

/// <summary>
/// 原子替换（P1 V5 / I-05 的落地）：
/// 目标存在 → `File.Replace(tmp, target, backup)`；目标不存在 → `File.Move(tmp, target)`；
/// 只读目标 → 清属性后重试一次；跨盘不支持（`.dat` 与 `.fvhelper\` 必须同目录，格式前提）。
/// </summary>
public static class AtomicFile
{
    /// <summary>把 <paramref name="tempPath"/> 原子地变成 <paramref name="targetPath"/>，旧内容保留到 <paramref name="backupPath"/>。</summary>
    public static void Replace(string tempPath, string targetPath, string? backupPath)
    {
        if (!File.Exists(tempPath))
        {
            throw new FileNotFoundException("待替换的临时文件不存在", tempPath);
        }

        if (!File.Exists(targetPath))
        {
            File.Move(tempPath, targetPath);
            return;
        }

        try
        {
            ReplaceCore(tempPath, targetPath, backupPath);
        }
        catch (UnauthorizedAccessException)
        {
            // 目标只读（P1 V5-C）或带 Hidden（版本数据默认隐藏）：清属性后重试一次
            TryClearBlockingAttributes(targetPath);
            if (backupPath is not null)
            {
                TryClearBlockingAttributes(backupPath);
            }

            ReplaceCore(tempPath, targetPath, backupPath);
        }
    }

    private static void ReplaceCore(string tempPath, string targetPath, string? backupPath)
    {
        if (backupPath is null)
        {
            File.Replace(tempPath, targetPath, null, ignoreMetadataErrors: true);
            return;
        }

        File.Replace(tempPath, targetPath, backupPath, ignoreMetadataErrors: true);
    }

    /// <summary>
    /// 清掉会挡住写入的属性：ReadOnly（只读介质/用户设的只读）、Hidden（版本数据默认隐藏——
    /// Windows 上带 Hidden 的文件用 CREATE_ALWAYS / 覆盖语义打开会被拒，必须先摘掉）。
    /// </summary>
    private static void TryClearBlockingAttributes(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var attributes = File.GetAttributes(path);
                var blocking = FileAttributes.ReadOnly | FileAttributes.Hidden;
                if ((attributes & blocking) != 0)
                {
                    File.SetAttributes(path, attributes & ~blocking);
                }
            }
        }
        catch
        {
            // 清属性失败就让上层重试去报错
        }
    }
}
