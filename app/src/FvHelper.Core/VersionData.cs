namespace FvHelper.Core;

/// <summary>
/// 版本数据（`.fvhelper.dat` / `.bak` / `.tmp` / `.fvhelper\`）默认"对用户不可见"：
/// 写完就打上 Windows 的 Hidden 属性，用户在资源管理器里看不到，也不容易误删。
/// 只改属性、不改内容；改失败（只读介质、非 NTFS 等）不影响主流程，下次写库会再试一次。
/// </summary>
public static class VersionData
{
    /// <summary>给 root 下的版本数据打 Hidden 属性（幂等，可反复调用）。</summary>
    public static void Hide(string root)
    {
        HideFile(DatFile.PathFor(root));
        HideFile(DatFile.BackupPathFor(root));
        HideFile(DatFile.TempPathFor(root));
        HideDirectory(Path.Combine(root, BlobStore.StoreDirName));
    }

    /// <summary>版本数据是不是都已经是隐藏状态（界面上用来提示"已经藏好了"）。</summary>
    public static bool IsHidden(string root)
    {
        var dat = DatFile.PathFor(root);
        var hidden = !File.Exists(dat) || HasHidden(dat);
        var store = Path.Combine(root, BlobStore.StoreDirName);
        return hidden && (!Directory.Exists(store) || HasHidden(store));
    }

    /// <summary>
    /// 写入前先解除隐藏：Windows 上带 Hidden 的文件不能用 `File.WriteAllBytes` / CREATE_ALWAYS
    /// 这类覆盖语义直接打开（会 ACCESS_DENIED），所以"要整文件重写"的地方先调这个。
    /// </summary>
    public static void Unhide(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return;
            }

            var attrs = File.GetAttributes(path);
            if (attrs.HasFlag(FileAttributes.Hidden))
            {
                File.SetAttributes(path, attrs & ~FileAttributes.Hidden);
            }
        }
        catch
        {
        }
    }

    private static bool HasHidden(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.Hidden);
        }
        catch
        {
            return false;
        }
    }

    private static void HideFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            var attrs = File.GetAttributes(path);
            if (!attrs.HasFlag(FileAttributes.Hidden))
            {
                File.SetAttributes(path, attrs | FileAttributes.Hidden);
            }
        }
        catch
        {
            // 改不了属性不影响读写：隐藏只是"看得见 / 看不见"的体验问题
        }
    }

    private static void HideDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            var attrs = File.GetAttributes(path);
            if (!attrs.HasFlag(FileAttributes.Hidden))
            {
                File.SetAttributes(path, attrs | FileAttributes.Hidden);
            }
        }
        catch
        {
        }
    }
}
