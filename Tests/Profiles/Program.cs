using System.Text.Json;
using BonbonPet.Core;

int checks = 0;
void Check(bool passed, string message)
{
    if (!passed) throw new InvalidOperationException(message);
    checks++;
    Console.WriteLine("PASS: " + message);
}
void Reject(string json, string name)
{
    bool rejected = false;
    try { PetProfiles.Deserialize(json); }
    catch (Exception error) when (error is InvalidDataException or JsonException) { rejected = true; }
    Check(rejected, name + " is rejected without writing user data");
}

var first = new PetSettings
{
    InstanceName = "小绿",
    CharacterPath = "Characters/green.png",
    CustomSoundPath = "Sounds/click.wav",
    SqueakSound = "custom",
    Size = 180,
    CenterX = -620.25,
    CenterY = 333.5,
    PhysicsEnabled = true,
    RandomKickSpeed = true,
    Speed = 370,
    AutoKick = true,
    BubbleFontFamily = "Arial",
    BubbleFontSize = 24,
    BubbleScale = 1.5,
    BubbleFontWeight = 700,
    PresetPhrases = "小绿的第一句\n小绿的第二句",
    CustomPhrases = "喝水呀\n耶！"
};
var second = new PetSettings
{
    InstanceName = "小蓝",
    CharacterPath = "Characters/blue.gif",
    Size = 330,
    CenterX = 1675,
    CenterY = -120,
    PhysicsEnabled = false,
    RandomKickSpeed = false,
    BubbleScale = .5,
    BubbleFontWeight = 100,
    PresetPhrases = "",
    CustomPhrases = "自己的话"
};
string json = PetProfiles.Serialize(new[] { first, second });
var loaded = PetProfiles.Deserialize(json);
Check(loaded.Count == 2 && loaded[0].InstanceId == first.InstanceId && loaded[1].InstanceId == second.InstanceId,
    "Two profiles preserve their instance identities and ordering");
Check(loaded[0].CharacterPath == first.CharacterPath && loaded[1].CharacterPath == second.CharacterPath &&
    loaded[0].CustomSoundPath == first.CustomSoundPath && loaded[0].SqueakSound == "custom",
    "Each profile preserves its own character and sound asset path");
Check(loaded[0].CenterX == -620.25 && loaded[0].CenterY == 333.5 && loaded[1].CenterY == -120,
    "Positions on multiple monitors preserve negative and fractional coordinates");
Check(loaded[0].Size == 180 && loaded[1].Size == 330 && loaded[0].BubbleScale == 1.5 && loaded[1].BubbleScale == .5 &&
    loaded[0].BubbleFontWeight == 700 && loaded[1].BubbleFontWeight == 100 && loaded[0].BubbleFontFamily == "Arial" && loaded[0].BubbleFontSize == 24,
    "Per-instance character size and complete bubble typography roundtrip");
Check(loaded[0].PhysicsEnabled && loaded[0].RandomKickSpeed && loaded[0].AutoKick && loaded[0].Speed == 370 &&
    !loaded[1].PhysicsEnabled && !loaded[1].RandomKickSpeed,
    "Moving and fixed profiles retain independent physics and random-speed choices");
Check(loaded[0].PresetPhrases == first.PresetPhrases && loaded[0].CustomPhrases == first.CustomPhrases &&
    loaded[1].PresetPhrases == "" && loaded[1].CustomPhrases == second.CustomPhrases,
    "Edited, empty and custom phrase pools preserve each instance's text exactly");
loaded[0].InstanceName = "changed"; loaded[0].CustomPhrases = "changed";
Check(first.InstanceName == "小绿" && loaded[1].InstanceName == "小蓝" && first.CustomPhrases != "changed",
    "Deserialized profiles are independent from input settings and siblings");
var copy = first.Copy(); copy.Size = 400; copy.PresetPhrases = "copy only";
Check(!ReferenceEquals(copy, first) && first.Size == 180 && first.PresetPhrases != copy.PresetPhrases && copy.InstanceId == first.InstanceId,
    "Copy returns independent settings while preserving identity for the caller to replace");

var maximum = Enumerable.Range(1, PetProfiles.MaximumInstances).Select(i => new PetSettings { InstanceName = "角色 " + i }).ToArray();
Check(PetProfiles.Deserialize(PetProfiles.Serialize(maximum)).Count == 12, "Maximum twelve instances serialize and deserialize successfully");
Reject(PetProfiles.Serialize(maximum.Concat(new[] { new PetSettings() }).ToArray()), "Thirteen-instance document");
Reject("{\"Instances\":[]}", "Empty profile list");
Reject("{\"Instances\":null}", "Null profile list");
Reject("{\"Instances\":[null]}", "Missing instance settings");
Reject("null", "Null document");
Reject("{unfinished", "Malformed JSON");
Reject("{\"Version\":9,\"Instances\":[{}]}", "Unsupported future profile version");

var duplicate = first.Copy(); duplicate.InstanceId = first.InstanceId.ToUpperInvariant();
var repaired = PetProfiles.Deserialize(PetProfiles.Serialize(new[] { first, duplicate }));
Check(repaired[0].InstanceId == first.InstanceId && repaired[0].InstanceId != repaired[1].InstanceId &&
    repaired.Select(s => s.InstanceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2,
    "Case-insensitive duplicate identities are repaired without replacing the first profile");
var invalid = PetProfiles.Deserialize("{\"Instances\":[{\"InstanceId\":\"bad\",\"InstanceName\":\"   \",\"BubbleScale\":99,\"BubbleFontWeight\":990,\"Size\":1}]}")[0];
Check(Guid.TryParse(invalid.InstanceId, out _) && invalid.InstanceName == "角色 1" && invalid.Size == 72 &&
    invalid.BubbleScale == 2 && invalid.BubbleFontWeight == 900,
    "Invalid identity, empty name and out-of-range display settings are normalized safely");
var legacy = PetProfiles.Deserialize("{\"Instances\":[{\"SchemaVersion\":4,\"Size\":220,\"PresetPhrases\":null,\"CustomPhrases\":\"旧台词\"}]}")[0];
Check(legacy.SchemaVersion == 5 && legacy.BubbleScale == 1 && legacy.BubbleFontWeight == 400 &&
    !legacy.RandomKickSpeed && legacy.PresetPhrases is null && legacy.CustomPhrases == "旧台词",
    "Older settings acquire new defaults while retaining default-preset selection and user text");
Check(legacy.ClickShowsDialogue && legacy.GifAudioMode == "speed", "Existing profiles acquire click dialogue and the native-animation audio mode");
var clickDisabled = first.Copy(); clickDisabled.ClickShowsDialogue = false; clickDisabled.BubblesEnabled = true; clickDisabled.GifAudioMode = "deform";
var clickEnabled = second.Copy(); clickEnabled.ClickShowsDialogue = true; clickEnabled.BubblesEnabled = false; clickEnabled.GifAudioMode = "speed";
var clickProfiles = PetProfiles.Deserialize(PetProfiles.Serialize(new[] { clickDisabled, clickEnabled }));
Check(!clickProfiles[0].ClickShowsDialogue && clickProfiles[0].BubblesEnabled &&
    clickProfiles[1].ClickShowsDialogue && !clickProfiles[1].BubblesEnabled,
    "Click dialogue and timed dialogue retain independent choices in each profile");
Check(clickProfiles[0].GifAudioMode == "deform" && clickProfiles[1].GifAudioMode == "speed",
    "Each character preserves its own GIF animation or deformation audio mode");
var unknownGifMode = new PetSettings { GifAudioMode = "unknown" }; unknownGifMode.Normalize();
Check(unknownGifMode.GifAudioMode == "speed", "Unknown GIF audio modes safely restore native animation speed control");
foreach (var (input, expected) in new[] { (0d, 1d), (1d, 1d), (60d, 60d), (999d, 60d), (double.NaN, 35d) })
{
    var interval = new PetSettings { BubbleInterval = input }; interval.Normalize();
    Check(interval.BubbleInterval == expected, $"Dialogue interval {input} restores within one to sixty seconds");
}
var nonFinite = new PetSettings { CenterX = double.NaN, CenterY = double.PositiveInfinity, BubbleScale = double.NaN, Speed = double.NaN };
nonFinite.Normalize();
Check(nonFinite.CenterX is null && nonFinite.CenterY is null && nonFinite.BubbleScale == 1 && nonFinite.Speed == 150,
    "Non-finite runtime values recover safe defaults before persistence");
string temporaryFolder = Path.Combine(Path.GetTempPath(), "Tantan-profile-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporaryFolder);
string damagedPath = Path.Combine(temporaryFolder, "profiles.json");
try
{
    const string damaged = "{unfinished multi-profile data";
    File.WriteAllText(damagedPath, damaged);
    var recovered = PetProfiles.Load(damagedPath, first);
    var backups = Directory.GetFiles(temporaryFolder, "profiles.json.corrupt-*");
    Check(recovered.Count == 1 && recovered[0].PresetPhrases == first.PresetPhrases && backups.Length == 1 && File.ReadAllText(backups[0]) == damaged,
        "Unreadable multi-profile data is preserved exactly before primary-profile recovery");
    File.WriteAllText(damagedPath, PetProfiles.Serialize(recovered));
    Check(File.ReadAllText(backups[0]) == damaged && PetProfiles.Load(damagedPath, second)[0].InstanceId == first.InstanceId,
        "A later autosave cannot destroy the backed-up damaged multi-profile document");
}
finally
{
    foreach (string file in Directory.GetFiles(temporaryFolder)) File.Delete(file);
    Directory.Delete(temporaryFolder);
}
Console.WriteLine($"{checks} profile checks passed without reading or writing live user settings.");
