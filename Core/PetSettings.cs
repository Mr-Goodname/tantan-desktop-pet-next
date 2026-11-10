using System;
using System.IO;
using System.Text.Json;

namespace BonbonPet.Core;

public sealed class PetSettings
{
    public const string DefaultBubbleFontFamily = "Microsoft YaHei UI";
    public const double DefaultBubbleFontSize = 13.5;
    public double Size { get; set; } = 220;
    public bool AudioEnabled { get; set; } = true;
    public string GifAudioMode { get; set; } = "speed";
    public double Sensitivity { get; set; } = 1.3;
    public double SqueakVolume { get; set; } = .42;
    public double SqueezeStrength { get; set; } = 1.8;
    public string SqueakSound { get; set; } = "duck";
    public string? CustomSoundPath { get; set; }
    public int SchemaVersion { get; set; } = 5;
    public string InstanceId { get; set; } = Guid.NewGuid().ToString("N");
    public string InstanceName { get; set; } = "角色 1";
    public bool PhysicsEnabled { get; set; }
    public bool Decay { get; set; }
    public double Speed { get; set; } = 150;
    public bool RandomKickSpeed { get; set; }
    public bool AutoKick { get; set; }
    public double KickInterval { get; set; } = 12;
    public bool Topmost { get; set; } = true;
    public bool HideOnFullscreen { get; set; } = true;
    public bool BubblesEnabled { get; set; } = true;
    public bool ClickShowsDialogue { get; set; } = true;
    public double BubbleInterval { get; set; } = 35;
    public string BubbleFontFamily { get; set; } = DefaultBubbleFontFamily;
    public double BubbleFontSize { get; set; } = DefaultBubbleFontSize;
    public double BubbleScale { get; set; } = 1;
    public int BubbleFontWeight { get; set; } = 400;
    public int Fps { get; set; } = 60;
    public string? CharacterPath { get; set; }
    public string? PresetPhrases { get; set; }
    public string CustomPhrases { get; set; } = "";
    public double? CenterX { get; set; }
    public double? CenterY { get; set; }
    public bool FirstRun { get; set; } = true;

    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BonbonPet");
    public static string FilePath => Path.Combine(DataDirectory, "settings.json");

    public static PetSettings Load()
    {
        try
        {
            string json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<PetSettings>(json) ?? new PetSettings();
            using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("SchemaVersion", out _) && settings.Fps == 30) settings.Fps = 60;
        if (!document.RootElement.TryGetProperty("SchemaVersion", out var version) || version.GetInt32() < 3)
                if (settings.SqueakSound != "custom") settings.SqueakSound = "duck";
        settings.Normalize();
            return settings;
        }
        catch { return new PetSettings(); }
    }

    public void Normalize()
    {
        var settings = this;
        settings.SchemaVersion = 5;
        if (!Guid.TryParse(settings.InstanceId, out _)) settings.InstanceId = Guid.NewGuid().ToString("N");
        settings.InstanceName = string.IsNullOrWhiteSpace(settings.InstanceName) ? "角色 1" : settings.InstanceName.Trim();
        if (settings.InstanceName.Length > 40) settings.InstanceName = settings.InstanceName[..40];
        settings.Size = Safe(settings.Size, 72, 480, 220);
        settings.Sensitivity = Safe(settings.Sensitivity, .2, 4, 1.3);
        if (settings.GifAudioMode is not ("speed" or "deform")) settings.GifAudioMode = "speed";
        settings.SqueakVolume = Safe(settings.SqueakVolume, 0, 1, .42);
        settings.SqueezeStrength = Safe(settings.SqueezeStrength, 0, 3, 1.8);
        if (settings.SqueakSound is not ("original" or "duck" or "custom")) settings.SqueakSound = "original";
        settings.Speed = Safe(settings.Speed, 25, 650, 150);
        settings.KickInterval = Safe(settings.KickInterval, 3, 120, 12);
        settings.BubbleInterval = Safe(settings.BubbleInterval, 1, 60, 35);
        settings.BubbleFontSize = Safe(settings.BubbleFontSize, 10, 32, DefaultBubbleFontSize);
        settings.BubbleScale = Safe(settings.BubbleScale, .5, 2, 1);
        settings.BubbleFontWeight = Math.Clamp((int)Math.Round(settings.BubbleFontWeight / 100d) * 100, 100, 900);
        settings.BubbleFontFamily = string.IsNullOrWhiteSpace(settings.BubbleFontFamily)
                ? DefaultBubbleFontFamily : settings.BubbleFontFamily.Trim();
        if (settings.Fps != 20 && settings.Fps != 30 && settings.Fps != 60) settings.Fps = 60;
        if (settings.CenterX.HasValue && !double.IsFinite(settings.CenterX.Value)) settings.CenterX = null;
        if (settings.CenterY.HasValue && !double.IsFinite(settings.CenterY.Value)) settings.CenterY = null;
        settings.CustomPhrases ??= "";
    }

    public PetSettings Copy() => JsonSerializer.Deserialize<PetSettings>(JsonSerializer.Serialize(this))!;

    private static double Safe(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }
}
