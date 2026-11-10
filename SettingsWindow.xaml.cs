using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BonbonPet.Core;
using BonbonPet.Services;

namespace BonbonPet;

public partial class SettingsWindow : Window
{
    private readonly PetWindow _pet;
    private readonly UpdateService _updates = new("Mr-Goodname", "tantan-desktop-pet-next");
    private CancellationTokenSource? _updateCancellation;
    private Uri? _latestReleasePage;
    private static readonly FontFamily[] _fonts = Fonts.SystemFontFamilies.OrderBy(font => font.Source, StringComparer.CurrentCultureIgnoreCase).ToArray();
    private bool _refreshingInstances;
    private bool _ready;
    public SettingsWindow(PetWindow pet)
    {
        _pet = pet;
        InitializeComponent();
        VersionText.Text = "当前版本：" + _updates.CurrentVersion;
        FooterStatus.Text = "设置自动保存 · v" + _updates.CurrentVersion;
        ((ComboBoxItem)SoundCombo.Items[1]).IsEnabled = AudioService.HasDuckAssets;
        if (!AudioService.HasDuckAssets) ((ComboBoxItem)SoundCombo.Items[1]).ToolTip = "此版本未附带橡胶鸭音效，可导入自定义音效。";
        Closed += (_, _) => { _updateCancellation?.Cancel(); _updates.Dispose(); };
        FontCombo.ItemsSource = _fonts;
        Icon = BitmapFrame.Create(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "bonbon.ico")));
        Reload();
    }
    public void Reload()
    {
        _ready = false;
        var s = _pet.Settings;
        RefreshStartupSetting();
        InstanceNameBox.Text = s.InstanceName;
        AudioCheck.IsChecked = s.AudioEnabled; SensitivitySlider.Value = s.Sensitivity; VolumeSlider.Value = s.SqueakVolume;
        RefreshGifAudioControls();
        SqueezeSlider.Value = s.SqueezeStrength;
        SoundCombo.SelectedIndex = s.SqueakSound == "duck" ? 1 : s.SqueakSound == "custom" ? 2 : 0;
        CustomSoundText.Text = s.CustomSoundPath == null ? "未导入自定义音效" : "自定义音效：" + Path.GetFileName(s.CustomSoundPath);
        SizeSlider.Value = s.Size; TopmostCheck.IsChecked = s.Topmost; FullscreenCheck.IsChecked = s.HideOnFullscreen;
        PhysicsCheck.IsChecked = s.PhysicsEnabled; DecayCheck.IsChecked = s.Decay; SpeedSlider.Value = s.Speed;
        RandomSpeedCheck.IsChecked = s.RandomKickSpeed;
        AutoKickCheck.IsChecked = s.AutoKick; KickSlider.Value = s.KickInterval; BubblesCheck.IsChecked = s.BubblesEnabled;
        ClickDialogueCheck.IsChecked = s.ClickShowsDialogue;
        BubbleSlider.Value = s.BubbleInterval; PhrasesBox.Text = s.CustomPhrases;
        PresetPhrasesBox.Text = s.PresetPhrases ?? PhraseBook.DefaultPresetText;
        FontCombo.SelectedItem = FindFont(s.BubbleFontFamily);
        FontCombo.Text = s.BubbleFontFamily;
        BubbleScaleSlider.Value = s.BubbleScale;
        FontWeightCombo.SelectedItem = FontWeightCombo.Items.OfType<ComboBoxItem>()
            .OrderBy(item => Math.Abs(int.Parse((string)item.Tag) - s.BubbleFontWeight)).First();
        SyncFontSizeSlider();
        FpsCombo.SelectedIndex = s.Fps == 20 ? 0 : s.Fps == 60 ? 2 : 1;
        CharacterText.Text = s.CharacterPath == null ? "默认角色 GIF · 哔哩哔哩 @疚刃" : Path.GetFileName(s.CharacterPath);
        PreviewImage.Source = _pet.CurrentFrame;
        Labels();
        RefreshInstances();
        _ready = true;
    }
    public void RefreshInstances()
    {
        _refreshingInstances = true;
        try
        {
            Title = $"弹弹桌宠 · Tantan · {_pet.Settings.InstanceName} · 设置";
            _pet.Title = $"弹弹桌宠 · Tantan · {_pet.Settings.InstanceName}";
            if (_pet.Coordinator is null) { InstanceBar.Visibility = Visibility.Collapsed; return; }
            // Windows are UIElements. Putting them directly in an ItemsControl
            // makes WPF treat the window as visual content instead of displaying
            // its name. Keep only the plain label in Content and identity in Tag.
            var choices = _pet.Coordinator.Instances.Select(pet => new ComboBoxItem
            {
                Content = pet.Settings.InstanceName,
                Tag = pet
            }).ToArray();
            InstanceCombo.ItemsSource = choices;
            InstanceCombo.SelectedItem = choices.FirstOrDefault(item => ReferenceEquals(item.Tag, _pet));
            CreateInstanceButton.IsEnabled = _pet.Coordinator.Instances.Count < PetProfiles.MaximumInstances;
            RemoveInstanceButton.IsEnabled = _pet.Coordinator.Instances.Count > 1;
        }
        finally { _refreshingInstances = false; }
    }
    private void InstanceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingInstances || !_ready || InstanceCombo.SelectedItem is not ComboBoxItem { Tag: PetWindow pet } || ReferenceEquals(pet, _pet)) return;
        pet.ShowSettings(); Close();
    }
    private void CreateInstance_Click(object sender, RoutedEventArgs e)
    {
        if (_pet.Coordinator is null) return;
        try { var pet = _pet.Coordinator.AddInstance(_pet, showSettings: false); pet.ShowSettings(); Close(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "创建角色", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
    private void RemoveInstance_Click(object sender, RoutedEventArgs e)
    {
        var coordinator = _pet.Coordinator;
        if (coordinator is null || coordinator.Instances.Count <= 1) return;
        var next = coordinator.Instances.First(pet => !ReferenceEquals(pet, _pet));
        if (coordinator.RemoveInstance(_pet)) next.ShowSettings();
    }
    private void InstanceNameChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        _pet.Settings.InstanceName = string.IsNullOrWhiteSpace(InstanceNameBox.Text) ? "角色" : InstanceNameBox.Text.Trim();
        _pet.Coordinator?.NotifyRosterChanged(); _pet.ScheduleSave();
    }
    private void ExitAll_Click(object sender, RoutedEventArgs e) => _pet.Exit();
    private void RefreshStartupSetting()
    {
        bool ready = _ready;
        _ready = false;
        try
        {
            // Self-tests use isolated settings and must never inspect or modify
            // the user's actual Windows startup registration.
            StartupCheck.IsEnabled = !App.IsTesting;
            StartupCheck.IsChecked = !App.IsTesting && StartupService.IsEnabled;
            StartupStatusText.Text = "登录 Windows 后自动恢复所有角色。此开关对整个程序生效。";
        }
        catch (Exception ex)
        {
            App.Log(ex); StartupCheck.IsEnabled = false;
            StartupStatusText.Text = "无法读取开机自启设置。";
        }
        finally { _ready = ready; }
    }
    private void StartupChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready || App.IsTesting) return;
        try
        {
            StartupService.SetEnabled(StartupCheck.IsChecked == true);
            if (_pet.Coordinator is { } coordinator)
            {
                foreach (var pet in coordinator.Instances) pet.SettingsPanel?.RefreshStartupSetting();
            }
            else RefreshStartupSetting();
        }
        catch (Exception ex)
        {
            App.Log(ex); RefreshStartupSetting();
            MessageBox.Show(this, "无法修改开机自启设置。\n" + ex.Message, "开机自启", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
    private void Labels()
    {
        SensitivityText.Text = $"{SensitivitySlider.Value:F1} ×"; VolumeText.Text = $"{VolumeSlider.Value:P0}";
        SqueezeText.Text = $"{SqueezeSlider.Value:F1} ×";
        SizeText.Text = $"{SizeSlider.Value:F0} px"; SpeedText.Text = $"{SpeedSlider.Value:F0} px/s";
        SpeedLabel.Text = _pet.Settings.RandomKickSpeed ? "速度上限" : "推进速度";
        SpeedHint.Text = _pet.Settings.RandomKickSpeed
            ? $"每次在 25–{SpeedSlider.Value:F0} px/s 范围内随机选择速度。" : "每次按此速度推进，方向随机。";
        KickText.Text = $"{KickSlider.Value:F0} 秒"; BubbleText.Text = $"{BubbleSlider.Value:F0} 秒";
        BubbleScaleText.Text = $"{BubbleScaleSlider.Value:P0}";
        FontSizeText.Text = $"{FontSizeSlider.Value:F1} px";
        PreviewPhrase.FontFamily = FindFont(_pet.Settings.BubbleFontFamily) ?? new FontFamily(PetSettings.DefaultBubbleFontFamily);
        PreviewPhrase.FontWeight = FontWeight.FromOpenTypeWeight(_pet.Settings.BubbleFontWeight);
        PreviewPhrase.FontSize = FontSizeSlider.Value;
        PreviewPhrase.LineHeight = FontSizeSlider.Value * 1.55;
    }
    private double TotalBubbleScale => _pet.Settings.Size / 220d * _pet.Settings.BubbleScale;
    private void SyncFontSizeSlider()
    {
        bool ready = _ready;
        _ready = false;
        try
        {
            double scale = TotalBubbleScale, minimum = 10 * scale, maximum = 32 * scale;
            // Widen the current range before assigning its new limits, so WPF
            // never coerces a valid value through an inverted range.
            FontSizeSlider.Minimum = Math.Min(FontSizeSlider.Minimum, minimum);
            FontSizeSlider.Maximum = maximum;
            FontSizeSlider.Minimum = minimum;
            FontSizeSlider.TickFrequency = .5 * scale;
            FontSizeSlider.SmallChange = .5 * scale;
            FontSizeSlider.LargeChange = 2 * scale;
            FontSizeSlider.Value = _pet.Settings.BubbleFontSize * scale;
        }
        finally { _ready = ready; }
    }
    private void Apply()
    {
        if (!_ready) return;
        var s = _pet.Settings;
        s.AudioEnabled = AudioCheck.IsChecked == true; s.Sensitivity = SensitivitySlider.Value; s.SqueakVolume = VolumeSlider.Value;
        s.SqueezeStrength = SqueezeSlider.Value;
        s.Size = SizeSlider.Value; s.Topmost = TopmostCheck.IsChecked == true; s.HideOnFullscreen = FullscreenCheck.IsChecked == true;
        s.PhysicsEnabled = PhysicsCheck.IsChecked == true; s.Decay = DecayCheck.IsChecked == true; s.Speed = SpeedSlider.Value;
        s.RandomKickSpeed = RandomSpeedCheck.IsChecked == true;
        s.AutoKick = AutoKickCheck.IsChecked == true; s.KickInterval = KickSlider.Value; s.BubblesEnabled = BubblesCheck.IsChecked == true;
        s.ClickShowsDialogue = ClickDialogueCheck.IsChecked == true;
        s.BubbleInterval = BubbleSlider.Value;
        s.BubbleScale = BubbleScaleSlider.Value;
        if (FpsCombo.SelectedItem is ComboBoxItem item) s.Fps = int.Parse((string)item.Tag);
        SyncFontSizeSlider(); Labels(); _pet.ApplySettings();
    }
    public void UpdatePreview(BitmapSource frame, float level, string status, double squashX, double squashY)
    {
        if (!IsVisible) return;
        RefreshGifAudioControls();
        if (!ReferenceEquals(PreviewImage.Source, frame)) PreviewImage.Source = frame;
        AudioMeter.Value = level;
        if (AudioStatus.Text != status) AudioStatus.Text = status;
        PreviewScale.ScaleX = squashX; PreviewScale.ScaleY = squashY;
    }
    public void ShowPhrase(string text) => PreviewPhrase.Text = text;
    private void RefreshGifAudioControls()
    {
        bool ready = _ready;
        _ready = false;
        try
        {
            bool animated = _pet.IsAnimatedCharacter;
            GifAudioModeCombo.IsEnabled = animated;
            GifAudioModeCombo.SelectedIndex = _pet.Settings.GifAudioMode == "deform" ? 1 : 0;
            GifAudioModeHint.Text = !animated
                ? "启用音频律动时，静态图片固定通过拉伸、弹跳和摇摆响应音频。"
                : _pet.Settings.GifAudioMode == "deform"
                    ? "GIF 保持原速，跟随音频拉伸、弹跳和摇摆。"
                    : "跟随音频加快或放慢 GIF 播放。";
        }
        finally { _ready = ready; }
    }
    private void GifAudioModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || !_pet.IsAnimatedCharacter || GifAudioModeCombo.SelectedItem is not ComboBoxItem item) return;
        _pet.Settings.GifAudioMode = (string)item.Tag;
        // Animation reads this preference on its next frame. Changing the mode
        // does not need to reload audio devices or the rest of the settings.
        _pet.ScheduleSave(); RefreshGifAudioControls();
    }
    private void ControlsChanged(object sender, RoutedEventArgs e) => Apply();
    private void SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => Apply();
    private void BubbleScaleChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        _pet.Settings.BubbleScale = BubbleScaleSlider.Value;
        SyncFontSizeSlider(); Labels(); _pet.ApplyBubbleAppearance();
    }
    private void FontSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        _pet.Settings.BubbleFontSize = Math.Clamp(FontSizeSlider.Value / TotalBubbleScale, 10, 32);
        Labels(); _pet.ApplyBubbleAppearance();
    }
    private void FontWeightChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || FontWeightCombo.SelectedItem is not ComboBoxItem item) return;
        _pet.Settings.BubbleFontWeight = int.Parse((string)item.Tag);
        Labels(); _pet.ApplyBubbleAppearance();
    }
    private void FpsChanged(object sender, SelectionChangedEventArgs e) => Apply();
    private void SoundChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || SoundCombo.SelectedItem is not ComboBoxItem item) return;
        string kind = (string)item.Tag;
        if (kind == "custom" && _pet.Settings.CustomSoundPath == null)
        {
            _pet.ImportSound(this); Reload(); return;
        }
        _pet.Settings.SqueakSound = kind; _pet.ApplySettings(); Reload();
    }
    private void ImportSound_Click(object sender, RoutedEventArgs e) { if (_pet.ImportSound(this)) Reload(); }
    private void PhrasesChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        _pet.Settings.CustomPhrases = PhrasesBox.Text; _pet.ApplyDialogueSettings();
    }
    private void PresetPhrasesChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        _pet.Settings.PresetPhrases = PresetPhrasesBox.Text; _pet.ApplyDialogueSettings();
    }
    private void RestorePreset_Click(object sender, RoutedEventArgs e)
    {
        _pet.Settings.PresetPhrases = null;
        PresetPhrasesBox.Text = PhraseBook.DefaultPresetText;
        // TextChanged stores editor contents. Keep null as the explicit default
        // selection after refreshing just this editor, without reloading fonts,
        // audio controls and the rest of the settings panel.
        _pet.Settings.PresetPhrases = null;
        _pet.ApplyDialogueSettings();
    }
    private FontFamily? FindFont(string name) => _fonts.FirstOrDefault(font => string.Equals(font.Source, name, StringComparison.OrdinalIgnoreCase));
    private void SetBubbleFont(FontFamily? font)
    {
        if (!_ready || font == null || _pet.Settings.BubbleFontFamily == font.Source) return;
        _pet.Settings.BubbleFontFamily = font.Source; Labels(); _pet.ApplyBubbleAppearance();
    }
    private void FontChanged(object sender, SelectionChangedEventArgs e) => SetBubbleFont(FontCombo.SelectedItem as FontFamily);
    private void FontTextChanged(object sender, TextChangedEventArgs e) => SetBubbleFont(FindFont(FontCombo.Text.Trim()));
    private void FontLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_ready) return;
        var font = FindFont(FontCombo.Text.Trim());
        if (font != null) SetBubbleFont(font);
        else FontCombo.Text = _pet.Settings.BubbleFontFamily;
    }
    private void Squeeze_Click(object sender, RoutedEventArgs e) => _pet.Squeeze();
    private void Kick_Click(object sender, RoutedEventArgs e) { _pet.Kick(); Reload(); }
    private void Say_Click(object sender, RoutedEventArgs e) => _pet.SayRandom();
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_pet.ImportCharacter(this)) Reload();
    }
    private void Default_Click(object sender, RoutedEventArgs e) { _pet.ResetCharacter(); Reload(); }
    internal async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        if (_updateCancellation != null) throw new InvalidOperationException("Update check is already running.");
        _updateCancellation = new CancellationTokenSource();
        CheckUpdateButton.IsEnabled = false; UpdateDownloadButton.IsEnabled = false;
        UpdateStatusText.Text = "正在检查更新…";
        try
        {
            var result = await _updates.CheckAsync(_updateCancellation.Token);
            if (IsLoaded)
            {
                UpdateStatusText.Text = result.Message;
                _latestReleasePage = result.ReleasePageUri;
                UpdateDownloadButton.IsEnabled = result.UpdateAvailable;
            }
            return result;
        }
        finally
        {
            _updateCancellation.Dispose(); _updateCancellation = null;
            if (IsLoaded) CheckUpdateButton.IsEnabled = true;
        }
    }
    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        try { await CheckForUpdatesAsync(); }
        catch (Exception ex) { App.Log(ex); if (IsLoaded) UpdateStatusText.Text = "检查更新失败，请稍后重试。"; }
    }
    private void OpenReleasePage(Uri page)
    {
        try { Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { App.Log(ex); MessageBox.Show(this, "无法打开浏览器。请访问 GitHub 发布页下载。", "打开发布页失败", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
    private void UpdateDownload_Click(object sender, RoutedEventArgs e) { if (_latestReleasePage != null) OpenReleasePage(_latestReleasePage); }
    private void OpenReleases_Click(object sender, RoutedEventArgs e) => OpenReleasePage(_updates.ReleasesPageUri);
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
