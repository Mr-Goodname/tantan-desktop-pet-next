using System;
using System.Collections.Generic;
using System.Linq;

namespace BonbonPet.Core;

/// <summary>Short, playful desktop dialogue. Kept in memory; no file access while displaying a bubble.</summary>
public static class PhraseBook
{
    public static readonly string[] Phrases =
    {
        "昨日一去不复回~",
        "欧耶耶~",
        "开心比什么都贵~",
        "覆水不能再收回~",
        "桃花谢了有玫瑰~",
        "人生几十年，总会有风雨来陪~",
        "哦！",
        "潇潇洒洒赴会，今不醉不归~",
        "耶！",
        "往事后不后悔，慢慢去体会~",
        "此刻朋友这杯酒最珍贵~",
        "快把酒满上~",
        "干了这杯，大声歌唱~",
        "好朋友，好朋友今宵多欢畅~",
        "理想改变了我们的模样~",
        "也让我懂得了~",
        "要珍惜朋友的肩膀~",
        "阳光总会在风雨之后撒向苍茫~",
        "让我们抬起头，学会了坚强~",
        "就像朋友的这杯烈酒，烫我心上~",
        "温暖我，凡事别再放心上~",
        "Happy！Lucky！Smile！Yeah!",
        "今天我们来相会~",
        "深情斟满了酒杯~",
        "岁月匆匆如流水~",
        "管他昨天谁是谁~",
        "人生的旅程，有苦有甜也有泪~",
        "好汉不提当年，酒再干一杯~",
        "往事决不后悔，不必再理会~"
    };

    public static string DefaultPresetText => string.Join(Environment.NewLine, Phrases);

    /// <summary>Null presets use the defaults; an empty preset editor deliberately disables them.</summary>
    public static string[] GetPhrases(PetSettings settings) =>
        ReadLines(settings.PresetPhrases ?? DefaultPresetText)
            .Concat(ReadLines(settings.CustomPhrases)).ToArray();

    private static IEnumerable<string> ReadLines(string? text) =>
        (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Length > 110 ? line[..110] : line);
}
