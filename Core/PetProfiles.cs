using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BonbonPet.Core;

public static class PetProfiles
{
    public const int MaximumInstances = 12;
    public static string FilePath => Path.Combine(PetSettings.DataDirectory, "profiles.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static List<PetSettings> Load(string? profilesPath = null, PetSettings? fallback = null)
    {
        profilesPath ??= FilePath;
        if (File.Exists(profilesPath))
        {
            try { return Deserialize(File.ReadAllText(profilesPath)); }
            catch
            {
                // Startup schedules an autosave of the recovered primary. Keep
                // the exact damaged document before that can replace it.
                string backup = profilesPath + ".corrupt-" + Guid.NewGuid().ToString("N");
                try { File.Copy(profilesPath, backup); }
                catch (Exception ex) { throw new IOException("无法备份损坏的角色设置，请检查文件访问权限。", ex); }
            }
        }
        return new List<PetSettings> { fallback?.Copy() ?? PetSettings.Load() };
    }

    public static string Serialize(IReadOnlyList<PetSettings> settings) =>
        JsonSerializer.Serialize(new ProfileDocument { Instances = settings.ToList() }, JsonOptions);

    public static List<PetSettings> Deserialize(string json)
    {
        var document = JsonSerializer.Deserialize<ProfileDocument>(json) ?? throw new InvalidDataException("角色设置无法读取。");
        if (document.Version != 1) throw new InvalidDataException("角色设置版本不支持。");
        if (document.Instances is null || document.Instances.Count is < 1 or > MaximumInstances)
            throw new InvalidDataException("角色数量超出范围。");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var settings in document.Instances)
        {
            if (settings is null) throw new InvalidDataException("角色设置不完整。");
            settings.Normalize();
            if (!ids.Add(settings.InstanceId)) { settings.InstanceId = Guid.NewGuid().ToString("N"); ids.Add(settings.InstanceId); }
        }
        return document.Instances;
    }

    public sealed class ProfileDocument
    {
        public int Version { get; set; } = 1;
        public List<PetSettings> Instances { get; set; } = new();
    }
}
