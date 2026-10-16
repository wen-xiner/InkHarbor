using InkHarbor.Core.Json;
using InkHarbor.Core.Models;

namespace InkHarbor.Core.Services;

/// <summary>
/// 配方预设读写，一个预设一个 presets\&lt;名字&gt;.json。
/// </summary>
public sealed class PresetService
{
    private readonly WorkspaceService _ws;

    public PresetService(WorkspaceService ws) => _ws = ws;

    public IReadOnlyList<Preset> LoadAll()
    {
        var list = new List<Preset>();
        if (!Directory.Exists(_ws.PresetsDir))
        {
            return list;
        }

        foreach (var path in Directory.EnumerateFiles(_ws.PresetsDir, "*.json"))
        {
            try
            {
                var preset = JsonStore.Read<Preset>(path);
                if (preset is not null && !string.IsNullOrWhiteSpace(preset.Name))
                {
                    list.Add(preset);
                }
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
            {
                // 坏掉的单个预设跳过，不影响其它。
            }
        }

        return list.OrderBy(p => p.Name, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>存预设，同名覆盖。</summary>
    public Preset Save(string name, IEnumerable<string> corpusFiles)
    {
        var clean = SanitizeName(name);
        var preset = new Preset
        {
            Name = clean,
            CorpusFiles = corpusFiles.ToList(),
            CreatedAt = DateTimeOffset.Now
        };

        JsonStore.Write(Path.Combine(_ws.PresetsDir, clean + ".json"), preset);
        return preset;
    }

    public void Delete(string name)
    {
        var path = Path.Combine(_ws.PresetsDir, SanitizeName(name) + ".json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>给预设改名。目标名已被占用就抛错，不覆盖别人。</summary>
    public Preset Rename(string oldName, string newName)
    {
        var from = Path.Combine(_ws.PresetsDir, SanitizeName(oldName) + ".json");
        if (!File.Exists(from))
        {
            throw new IOException($"预设「{oldName}」已经不在了。");
        }

        var clean = SanitizeName(newName);
        var to = Path.Combine(_ws.PresetsDir, clean + ".json");

        var preset = JsonStore.Read<Preset>(from) ?? new Preset { Name = clean };
        preset.Name = clean;

        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            JsonStore.Write(from, preset);
            return preset;
        }

        if (File.Exists(to))
        {
            throw new IOException($"已经有叫「{clean}」的预设了，换个名字。");
        }

        // 先写新的再删老的，写挂了至少老预设还在。
        JsonStore.Write(to, preset);
        File.Delete(from);
        return preset;
    }

    /// <summary>这个预设还在不在。</summary>
    public bool Exists(string name) =>
        File.Exists(Path.Combine(_ws.PresetsDir, SanitizeName(name) + ".json"));

    private static string SanitizeName(string name)
    {
        var clean = WorkspaceService.SanitizeTitle(name);
        return string.IsNullOrWhiteSpace(clean) ? "未命名预设" : clean;
    }
}
