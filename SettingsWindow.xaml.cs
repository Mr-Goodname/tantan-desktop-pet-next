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
    private readonly UpdateService _updates = new("Mr-Goodname", "tantan-desktop-pet");
    private CancellationTokenSource? _updateCancellation;
    private Uri? _latestReleasePage;
    private readonly FontFamily[] _fonts = Fonts.SystemFontFamilies.OrderBy(font => font.Source, StringComparer.CurrentCultureIgnoreCase).ToArray();
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
        AudioCheck.IsChecked = s.AudioEnabled; SensitivitySlider.Value = s.Sensitivity; VolumeSlider.Value = s.SqueakVolume;
        SqueezeSlider.Value = s.SqueezeStrength;
        SoundCombo.SelectedIndex = s.SqueakSound == "duck" ? 1 : s.SqueakSound == "custom" ? 2 : 0;
        CustomSoundText.Text = s.CustomSoundPath == null ? "未导入自定义音效" : "自定义音效：" + Path.GetFileName(s.CustomSoundPath);
        SizeSlider.Value = s.Size; TopmostCheck.IsChecked = s.Topmost; FullscreenCheck.IsChecked = s.HideOnFullscreen;
        PhysicsCheck.IsChecked = s.PhysicsEnabled; DecayCheck.IsChecked = s.Decay; SpeedSlider.Value = s.Speed;
        AutoKickCheck.IsChecked = s.AutoKick; KickSlider.Value = s.KickInterval; BubblesCheck.IsChecked = s.BubblesEnabled;
        BubbleSlider.Value = s.BubbleInterval; PhrasesBox.Text = s.CustomPhrases;
        PresetPhrasesBox.Text = s.PresetPhrases ?? PhraseBook.DefaultPresetText;
        FontCombo.SelectedItem = FindFont(s.BubbleFontFamily);
        FontCombo.Text = s.BubbleFontFamily;
        FontSizeSlider.Value = s.BubbleFontSize;
        FpsCombo.SelectedIndex = s.Fps == 20 ? 0 : s.Fps == 60 ? 2 : 1;
        CharacterText.Text = s.CharacterPath == null ? "默认角色 GIF" : Path.GetFileName(s.CharacterPath);
        PreviewImage.Source = _pet.CurrentFrame;
        Labels();
        _ready = true;
    }
    private void Labels()
    {
        SensitivityText.Text = $"{SensitivitySlider.Value:F1} ×"; VolumeText.Text = $"{VolumeSlider.Value:P0}";
        SqueezeText.Text = $"{SqueezeSlider.Value:F1} ×";
        SizeText.Text = $"{SizeSlider.Value:F0} px"; SpeedText.Text = $"{SpeedSlider.Value:F0} px/s";
        KickText.Text = $"{KickSlider.Value:F0} 秒"; BubbleText.Text = $"{BubbleSlider.Value:F0} 秒";
        FontSizeText.Text = $"{FontSizeSlider.Value:F1} px";
        PreviewPhrase.FontFamily = FindFont(_pet.Settings.BubbleFontFamily) ?? new FontFamily(PetSettings.DefaultBubbleFontFamily);
        PreviewPhrase.FontSize = FontSizeSlider.Value;
        PreviewPhrase.LineHeight = FontSizeSlider.Value * 1.55;
    }
    private void Apply()
    {
        if (!_ready) return;
        var s = _pet.Settings;
        s.AudioEnabled = AudioCheck.IsChecked == true; s.Sensitivity = SensitivitySlider.Value; s.SqueakVolume = VolumeSlider.Value;
        s.SqueezeStrength = SqueezeSlider.Value;
        s.Size = SizeSlider.Value; s.Topmost = TopmostCheck.IsChecked == true; s.HideOnFullscreen = FullscreenCheck.IsChecked == true;
        s.PhysicsEnabled = PhysicsCheck.IsChecked == true; s.Decay = DecayCheck.IsChecked == true; s.Speed = SpeedSlider.Value;
        s.AutoKick = AutoKickCheck.IsChecked == true; s.KickInterval = KickSlider.Value; s.BubblesEnabled = BubblesCheck.IsChecked == true;
        s.BubbleInterval = BubbleSlider.Value;
        s.BubbleFontSize = FontSizeSlider.Value;
        if (FpsCombo.SelectedItem is ComboBoxItem item) s.Fps = int.Parse((string)item.Tag);
        Labels(); _pet.ApplySettings();
    }
    public void UpdatePreview(BitmapSource frame, float level, string status, double squashX, double squashY)
    {
        if (!IsVisible) return;
        if (!ReferenceEquals(PreviewImage.Source, frame)) PreviewImage.Source = frame;
        AudioMeter.Value = level;
        if (AudioStatus.Text != status) AudioStatus.Text = status;
        PreviewScale.ScaleX = squashX; PreviewScale.ScaleY = squashY;
    }
    public void ShowPhrase(string text) => PreviewPhrase.Text = text;
    private void ControlsChanged(object sender, RoutedEventArgs e) => Apply();
    private void SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => Apply();
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
        _pet.Settings.BubbleFontFamily = font.Source; Labels(); _pet.ApplySettings();
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
