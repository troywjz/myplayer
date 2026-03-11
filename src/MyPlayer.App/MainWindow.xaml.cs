using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MyPlayer.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const double SeekCommitToleranceSeconds = 0.35;
    private const int SeekCommitTimeoutMilliseconds = 1200;

    private readonly DispatcherTimer _positionTimer;
    private readonly SettingsStore _settingsStore = new();
    private AppSettings _settings = new();
    private PlayerService? _playerService;
    private bool _isLoaded;
    private bool _isSeeking;
    private bool _isAdjustingVolume;
    private bool _isFullscreen;
    private bool _hasPendingSeek;
    private double _pendingSeekTargetSeconds;
    private DateTime _pendingSeekExpiresAtUtc;
    private Rect _restoreBounds = Rect.Empty;
    private WindowState _restoreWindowState;
    private WindowStyle _restoreWindowStyle;
    private ResizeMode _restoreResizeMode;

    private double _durationSeconds;
    private double _positionSeconds;
    private double _selectedSpeed;
    private double _volume;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        SpeedOptions = new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };
        SelectedSpeed = 1.0;
        OverlayTitle = "MyPlayer";
        OverlayMessage = "拖入音频或视频文件，或按 Ctrl+O 打开";
        StatusText = "未打开文件";
        PlaybackBadge = "空闲";
        CurrentTimeText = "00:00";
        DurationText = "00:00";
        Volume = 100;

        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };
        _positionTimer.Tick += PositionTimer_OnTick;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IEnumerable<double> SpeedOptions { get; }

    public string OverlayTitle { get; private set; } = string.Empty;

    public string OverlayMessage { get; private set; } = string.Empty;

    public string StatusText { get; private set; } = string.Empty;

    public string PlaybackBadge { get; private set; } = string.Empty;

    public string CurrentTimeText { get; private set; } = string.Empty;

    public string DurationText { get; private set; } = string.Empty;

    public string PlayPauseButtonText => IsPlaying ? "暂停" : "播放";

    public string MuteButtonText => IsMuted ? "取消静音" : "静音";

    public string FullscreenButtonText => _isFullscreen ? "退出全屏" : "全屏";

    public string VolumeText => $"{Math.Round(Volume):0}%";

    public Visibility OverlayVisibility => !HasMedia || !IsPlaying || IsPlaybackEnded
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool IsPlaying { get; private set; }

    public bool IsMuted { get; private set; }

    public bool HasMedia { get; private set; }

    public bool IsPlaybackEnded { get; private set; }

    public double DurationSeconds
    {
        get => _durationSeconds;
        private set
        {
            if (Math.Abs(_durationSeconds - value) < 0.01)
            {
                return;
            }

            _durationSeconds = value;
            OnPropertyChanged();
            UpdateSeekBarVisual(PositionSeconds);
        }
    }

    public double PositionSeconds
    {
        get => _positionSeconds;
        set
        {
            var sanitizedValue = SanitizeSeekTarget(value);
            if (Math.Abs(_positionSeconds - sanitizedValue) < 0.01)
            {
                return;
            }

            _positionSeconds = sanitizedValue;
            OnPropertyChanged();
            UpdateSeekBarVisual(_positionSeconds);
        }
    }

    public double SelectedSpeed
    {
        get => _selectedSpeed;
        set
        {
            if (Math.Abs(_selectedSpeed - value) < 0.0001)
            {
                return;
            }

            _selectedSpeed = value;
            OnPropertyChanged();
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            if (Math.Abs(_volume - value) < 0.01)
            {
                return;
            }

            _volume = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeText));
        }
    }

    public string? PendingOpenPath { get; set; }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void RaiseControlPropertyChanges()
    {
        OnPropertyChanged(nameof(CurrentTimeText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(PlayPauseButtonText));
        OnPropertyChanged(nameof(MuteButtonText));
        OnPropertyChanged(nameof(FullscreenButtonText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(PlaybackBadge));
        OnPropertyChanged(nameof(OverlayTitle));
        OnPropertyChanged(nameof(OverlayMessage));
        OnPropertyChanged(nameof(OverlayVisibility));
    }

    private void Window_OnSourceInitialized(object? sender, EventArgs e)
    {
        _settings = _settingsStore.Load();
        ApplyWindowSettings();
        Volume = _settings.Volume;
        SelectedSpeed = _settings.Speed;
    }

    private async void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        await Dispatcher.InvokeAsync(InitializePlayer, DispatcherPriority.Loaded);
        UpdateSeekBarVisual(PositionSeconds);
        Focus();
        Keyboard.Focus(this);
    }

    private void InitializePlayer()
    {
        _playerService = new PlayerService(VideoHostControl.Handle);
        _playerService.Initialize();
        SetVolumeAndSync(Volume);
        _playerService.SetMuted(_settings.IsMuted);
        ApplySpeed(SelectedSpeed);
        IsMuted = _settings.IsMuted;
        _positionTimer.Start();
        RefreshPlaybackState();

        if (!string.IsNullOrWhiteSpace(PendingOpenPath) && File.Exists(PendingOpenPath))
        {
            OpenMedia(PendingOpenPath);
        }
    }

    private void ApplyWindowSettings()
    {
        if (_settings.WindowWidth >= MinWidth)
        {
            Width = _settings.WindowWidth;
        }

        if (_settings.WindowHeight >= MinHeight)
        {
            Height = _settings.WindowHeight;
        }

        if (_settings.WindowLeft >= 0)
        {
            Left = _settings.WindowLeft;
        }

        if (_settings.WindowTop >= 0)
        {
            Top = _settings.WindowTop;
        }
    }

    private void PositionTimer_OnTick(object? sender, EventArgs e)
    {
        RefreshPlaybackState();
    }

    private void RefreshPlaybackState()
    {
        if (_playerService is null)
        {
            return;
        }

        var state = _playerService.GetState();

        HasMedia = state.HasMedia;
        IsPlaybackEnded = state.IsEof;
        IsPlaying = state.HasMedia && !state.IsPaused && !state.IsIdle && !state.IsEof;
        IsMuted = state.IsMuted;
        DurationSeconds = state.DurationSeconds;

        if (_hasPendingSeek)
        {
            var pendingSeekTimedOut = DateTime.UtcNow >= _pendingSeekExpiresAtUtc;
            var pendingSeekReached = Math.Abs(state.PositionSeconds - _pendingSeekTargetSeconds) <= SeekCommitToleranceSeconds;

            if (pendingSeekReached || pendingSeekTimedOut)
            {
                _hasPendingSeek = false;
            }
        }

        if (!_isSeeking && !_hasPendingSeek)
        {
            PositionSeconds = state.PositionSeconds;
        }

        if (!_isAdjustingVolume)
        {
            Volume = state.Volume;
        }

        var displayedPosition = (_isSeeking || _hasPendingSeek) ? PositionSeconds : state.PositionSeconds;
        CurrentTimeText = FormatTime(displayedPosition);
        DurationText = FormatTime(state.DurationSeconds);

        if (!state.HasMedia)
        {
            OverlayTitle = "MyPlayer";
            OverlayMessage = "拖入音频或视频文件，或按 Ctrl+O 打开";
            PlaybackBadge = "空闲";
            StatusText = "未打开文件";
        }
        else if (state.IsEof)
        {
            OverlayTitle = string.IsNullOrWhiteSpace(state.Title) ? "播放结束" : state.Title;
            OverlayMessage = "已停留在最后一帧，再按播放将从头开始";
            PlaybackBadge = "播放结束";
            StatusText = "播放结束";
        }
        else if (state.IsPaused)
        {
            OverlayTitle = string.IsNullOrWhiteSpace(state.Title) ? "已暂停" : state.Title;
            OverlayMessage = "已暂停";
            PlaybackBadge = "暂停";
            StatusText = "已暂停";
        }
        else
        {
            OverlayTitle = string.IsNullOrWhiteSpace(state.Title) ? "正在播放" : state.Title;
            OverlayMessage = "正在播放";
            PlaybackBadge = "播放中";
            StatusText = "播放中";
        }

        RaiseControlPropertyChanges();
    }

    private static string FormatTime(double seconds)
    {
        if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            return "00:00";
        }

        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1 ? ts.ToString(@"hh\:mm\:ss") : ts.ToString(@"mm\:ss");
    }

    private static Key GetEffectiveKey(KeyEventArgs e)
    {
        if (e.ImeProcessedKey != Key.None)
        {
            return e.ImeProcessedKey;
        }

        if (e.SystemKey != Key.None)
        {
            return e.SystemKey;
        }

        return e.Key;
    }

    private void OpenButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开媒体文件",
            Filter =
                "媒体文件|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.mp3;*.wav;*.flac;*.aac;*.m4a;*.ogg;*.opus;*.wma|所有文件|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            OpenMedia(dialog.FileName);
        }
    }

    private void OpenMedia(string path)
    {
        if (_playerService is null)
        {
            PendingOpenPath = path;
            return;
        }

        try
        {
            _playerService.Open(path);
            _hasPendingSeek = false;
            PositionSeconds = 0;
            DurationSeconds = 0;
            SetVolumeAndSync(Volume);
            _playerService.SetMuted(IsMuted);
            ApplySpeed(SelectedSpeed);
            RefreshPlaybackState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"无法打开文件：{ex.Message}", "打开失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PlayPauseButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_playerService is null || !HasMedia)
        {
            return;
        }

        if (IsPlaybackEnded)
        {
            BeginPendingSeek(0);
        }

        _playerService.PlayOrTogglePause(IsPlaybackEnded);
        RefreshPlaybackState();
    }

    private void RewindButton_OnClick(object sender, RoutedEventArgs e)
    {
        SeekRelative(-10);
    }

    private void ForwardButton_OnClick(object sender, RoutedEventArgs e)
    {
        SeekRelative(10);
    }

    private void SeekRelative(double deltaSeconds)
    {
        if (_playerService is null || !HasMedia)
        {
            return;
        }

        BeginPendingSeek(PositionSeconds + deltaSeconds);
        _playerService.SeekRelative(deltaSeconds);
    }

    private void MuteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_playerService is null)
        {
            return;
        }

        IsMuted = !IsMuted;
        _playerService.SetMuted(IsMuted);
        RaiseControlPropertyChanges();
    }

    private void FullscreenButton_OnClick(object sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void ToggleFullscreen()
    {
        if (_isFullscreen)
        {
            WindowStyle = _restoreWindowStyle;
            ResizeMode = _restoreResizeMode;
            WindowState = _restoreWindowState;

            if (_restoreBounds != Rect.Empty)
            {
                Left = _restoreBounds.Left;
                Top = _restoreBounds.Top;
                Width = _restoreBounds.Width;
                Height = _restoreBounds.Height;
            }

            _isFullscreen = false;
        }
        else
        {
            _restoreBounds = new Rect(Left, Top, Width, Height);
            _restoreWindowState = WindowState;
            _restoreWindowStyle = WindowStyle;
            _restoreResizeMode = ResizeMode;

            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            _isFullscreen = true;
        }

        OnPropertyChanged(nameof(FullscreenButtonText));
    }

    private void ApplySpeed(double speed)
    {
        SelectedSpeed = speed;
        _playerService?.SetSpeed(speed);
    }

    private void SetVolumeAndSync(double volume)
    {
        Volume = Math.Clamp(volume, 0, 100);
        _playerService?.SetVolume(Volume);
    }

    private void BeginPendingSeek(double targetSeconds)
    {
        PositionSeconds = targetSeconds;
        _hasPendingSeek = true;
        _pendingSeekTargetSeconds = PositionSeconds;
        _pendingSeekExpiresAtUtc = DateTime.UtcNow.AddMilliseconds(SeekCommitTimeoutMilliseconds);
        CurrentTimeText = FormatTime(PositionSeconds);
        OnPropertyChanged(nameof(CurrentTimeText));
    }

    private void CommitSeek(double targetSeconds)
    {
        if (_playerService is null || !HasMedia)
        {
            _isSeeking = false;
            _hasPendingSeek = false;
            return;
        }

        BeginPendingSeek(targetSeconds);
        _playerService.SeekAbsolute(PositionSeconds);
        _isSeeking = false;
    }

    private double SanitizeSeekTarget(double targetSeconds)
    {
        if (double.IsNaN(targetSeconds) || double.IsInfinity(targetSeconds))
        {
            return 0;
        }

        var max = DurationSeconds > 0 ? DurationSeconds : double.MaxValue;
        return Math.Clamp(targetSeconds, 0, max);
    }

    private void VolumeSlider_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isAdjustingVolume = true;
    }

    private void VolumeSlider_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isAdjustingVolume = false;
        SetVolumeAndSync(VolumeSlider.Value);
    }

    private void VolumeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isLoaded)
        {
            return;
        }

        if (_isAdjustingVolume || ReferenceEquals(e.OriginalSource, VolumeSlider))
        {
            SetVolumeAndSync(VolumeSlider.Value);
        }
    }

    private void SeekSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_playerService is null || !HasMedia || DurationSeconds <= 0)
        {
            return;
        }

        _isSeeking = true;
        Focus();
        Keyboard.Focus(this);
        SeekSurface.CaptureMouse();
        PreviewSeekFromPoint(e.GetPosition(SeekSurface));
        e.Handled = true;
    }

    private void SeekSurface_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isSeeking || Mouse.Captured != SeekSurface)
        {
            return;
        }

        PreviewSeekFromPoint(e.GetPosition(SeekSurface));
        e.Handled = true;
    }

    private void SeekSurface_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSeeking)
        {
            return;
        }

        PreviewSeekFromPoint(e.GetPosition(SeekSurface));
        CommitSeek(PositionSeconds);

        if (Mouse.Captured == SeekSurface)
        {
            SeekSurface.ReleaseMouseCapture();
        }

        e.Handled = true;
    }

    private void SeekSurface_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_isSeeking)
        {
            return;
        }

        CommitSeek(PositionSeconds);
    }

    private void SeekSurface_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSeekBarVisual(PositionSeconds);
    }

    private void PreviewSeekFromPoint(Point point)
    {
        PositionSeconds = GetSeekTargetFromPoint(point);
        CurrentTimeText = FormatTime(PositionSeconds);
        OnPropertyChanged(nameof(CurrentTimeText));
    }

    private double GetSeekTargetFromPoint(Point point)
    {
        if (DurationSeconds <= 0 || SeekSurface.ActualWidth <= 0)
        {
            return 0;
        }

        var ratio = Math.Clamp(point.X / SeekSurface.ActualWidth, 0, 1);
        return ratio * DurationSeconds;
    }

    private void UpdateSeekBarVisual(double positionSeconds)
    {
        if (SeekSurface is null || SeekProgressFill is null || SeekThumb is null)
        {
            return;
        }

        var width = SeekSurface.ActualWidth;
        if (width <= 0)
        {
            SeekProgressFill.Width = 0;
            SeekThumb.Margin = new Thickness(0, 0, 0, 0);
            return;
        }

        var ratio = DurationSeconds > 0 ? Math.Clamp(positionSeconds / DurationSeconds, 0, 1) : 0;
        var thumbWidth = SeekThumb.ActualWidth > 0 ? SeekThumb.ActualWidth : SeekThumb.Width;
        var thumbLeft = ratio * Math.Max(0, width - thumbWidth);

        SeekProgressFill.Width = ratio * width;
        SeekThumb.Margin = new Thickness(thumbLeft, 0, 0, 0);
    }

    private void SpeedComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded)
        {
            return;
        }

        ApplySpeed(SelectedSpeed);
    }

    private void Window_OnDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            OpenMedia(files[0]);
        }
    }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = GetEffectiveKey(e);

        if (key == Key.O && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            OpenButton_OnClick(sender, e);
            e.Handled = true;
            return;
        }

        switch (key)
        {
            case Key.Space:
                PlayPauseButton_OnClick(sender, e);
                e.Handled = true;
                break;
            case Key.Left:
                RewindButton_OnClick(sender, e);
                e.Handled = true;
                break;
            case Key.Right:
                ForwardButton_OnClick(sender, e);
                e.Handled = true;
                break;
            case Key.Up:
                SetVolumeAndSync(Volume + 5);
                e.Handled = true;
                break;
            case Key.Down:
                SetVolumeAndSync(Volume - 5);
                e.Handled = true;
                break;
            case Key.M:
                MuteButton_OnClick(sender, e);
                e.Handled = true;
                break;
            case Key.F:
                ToggleFullscreen();
                e.Handled = true;
                break;
            case Key.D1:
            case Key.NumPad1:
                ApplySpeed(0.5);
                e.Handled = true;
                break;
            case Key.D2:
            case Key.NumPad2:
                ApplySpeed(0.75);
                e.Handled = true;
                break;
            case Key.D3:
            case Key.NumPad3:
                ApplySpeed(1.0);
                e.Handled = true;
                break;
            case Key.D4:
            case Key.NumPad4:
                ApplySpeed(1.25);
                e.Handled = true;
                break;
            case Key.D5:
            case Key.NumPad5:
                ApplySpeed(1.5);
                e.Handled = true;
                break;
            case Key.D6:
            case Key.NumPad6:
                ApplySpeed(2.0);
                e.Handled = true;
                break;
            case Key.Escape when _isFullscreen:
                ToggleFullscreen();
                e.Handled = true;
                break;
        }
    }

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        _positionTimer.Stop();

        _settings.Volume = Volume;
        _settings.Speed = SelectedSpeed;
        _settings.IsMuted = IsMuted;
        _settings.WindowWidth = RestoreBounds.Width > 0 ? RestoreBounds.Width : Width;
        _settings.WindowHeight = RestoreBounds.Height > 0 ? RestoreBounds.Height : Height;
        _settings.WindowLeft = RestoreBounds.Left >= 0 ? RestoreBounds.Left : Left;
        _settings.WindowTop = RestoreBounds.Top >= 0 ? RestoreBounds.Top : Top;
        _settingsStore.Save(_settings);

        _playerService?.Dispose();
    }
}
