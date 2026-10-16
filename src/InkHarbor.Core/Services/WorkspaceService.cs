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

    /// <summary>启动时保证四个目录都在。用户可能在资源管理器里删过。</summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(CorpusDir);
        Directory.CreateDirectory(PresetsDir);
        Directory.CreateDirectory(DraftsDir);
        Directory.CreateDirectory(FinalDir);
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
