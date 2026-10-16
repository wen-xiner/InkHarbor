namespace InkHarbor.Core.Services;

/// <summary>
/// 工作区路径解析。软件只认这一处，别的服务都从这里取路径。
/// </summary>
public sealed class WorkspaceService
{
    /// <summary>环境变量名，设了就用它当根目录，方便换机。</summary>
    public const string RootEnvVar = "INKHARBOR_ROOT";

    /// <summary>本机默认根目录。</summary>
    public const string DefaultRoot = @"D:\Test\InkHarbor";

    public WorkspaceService(string? root = null)
    {
        Root = ResolveRoot(root);
        EnsureDirectories();
    }

    public string Root { get; }

    /// <summary>范文库。</summary>
    public string CorpusDir => Path.Combine(Root, "corpus");

    /// <summary>配方预设。</summary>
    public string PresetsDir => Path.Combine(Root, "presets");

    /// <summary>底稿与修订版本。</summary>
    public string DraftsDir => Path.Combine(Root, "drafts");

    /// <summary>终稿封存。</summary>
    public string FinalDir => Path.Combine(Root, "final");

    /// <summary>缓存（范文的嵌入向量等），删了会自动重建。</summary>
    public string CacheDir => Path.Combine(Root, "cache");

    /// <summary>范文的回收站。软件里"删除"范文只是移到这儿，用户随时能捞回来。</summary>
    public string CorpusTrashDir => Path.Combine(CorpusDir, "_trash");

    private static string ResolveRoot(string? explicitRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            return explicitRoot;
        }

        var fromEnv = Environment.GetEnvironmentVariable(RootEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        // 默认目录所在盘不存在时（换机）退到用户目录，至少能跑起来。
        var drive = Path.GetPathRoot(DefaultRoot);
        if (!string.IsNullOrEmpty(drive) && !Directory.Exists(drive))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "InkHarbor");
        }

        return DefaultRoot;
    }

    /// <summary>
    /// 启动时保证目录都在。用户可能在资源管理器里删过。
    /// 注意 <see cref="CorpusTrashDir"/> 不在这里建——没删过范文就不该在 corpus 里
    /// 凭空多出一个文件夹，改成第一次删除时按需创建。
    /// </summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(CorpusDir);
        Directory.CreateDirectory(PresetsDir);
        Directory.CreateDirectory(DraftsDir);
        Directory.CreateDirectory(FinalDir);
        Directory.CreateDirectory(CacheDir);
    }

    /// <summary>把标题清成合法目录名，去掉路径非法字符并裁掉首尾空白和点。</summary>
    public static string SanitizeTitle(string title)
    {
        var name = string.IsNullOrWhiteSpace(title) ? "未命名" : title.Trim();
        foreach (var bad in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(bad, '_');
        }

        name = name.Trim().TrimEnd('.');
        return string.IsNullOrEmpty(name) ? "未命名" : name;
    }
}
