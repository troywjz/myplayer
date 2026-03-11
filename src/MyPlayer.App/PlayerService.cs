using System.IO;
using HanumanInstitute.LibMpv;
using HanumanInstitute.LibMpv.Core;

namespace MyPlayer.App;

public sealed class PlayerService : IDisposable
{
    private readonly IntPtr _targetHandle;
    private MpvContext? _mpv;
    private bool _initialized;
    private bool _disposed;

    public PlayerService(IntPtr targetHandle)
    {
        _targetHandle = targetHandle;
    }

    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        var baseDirectory = AppContext.BaseDirectory;
        var mpvDirectory = Path.Combine(baseDirectory, "libmpv", "win-x64");
        MpvApi.RootPath = mpvDirectory;
        _mpv = new MpvContext();

        _mpv.SetOptionString("wid", _targetHandle.ToInt64().ToString());
        _mpv.SetOptionString("vo", "gpu");
        _mpv.SetOptionString("gpu-context", "d3d11");
        _mpv.SetOptionString("hwdec", "auto-safe");
        _mpv.SetOptionString("idle", "yes");
        _mpv.SetOptionString("keep-open", "yes");
        _mpv.SetOptionString("force-window", "yes");
        _mpv.SetOptionString("osc", "no");
        _mpv.SetOptionString("input-default-bindings", "no");
        _mpv.SetOptionString("input-vo-keyboard", "no");
        _mpv.SetOptionString("terminal", "no");
        _mpv.SetOptionString("msg-level", "all=error");
        _mpv.Initialize();
        _initialized = true;
    }

    public void Open(string path)
    {
        EnsureInitialized();
        var mpv = _mpv!;
        mpv.RunCommand(null!, new object[] { "loadfile", path, "replace" });
        mpv.SetProperty("pause", false);
    }

    public void PlayOrTogglePause(bool restartFromBeginning)
    {
        EnsureInitialized();
        var mpv = _mpv!;

        if (restartFromBeginning)
        {
            mpv.RunCommand(null!, new object[] { "seek", 0, "absolute+exact" });
            mpv.SetProperty("pause", false);
            return;
        }

        var isPaused = ReadProperty("pause", false);
        mpv.SetProperty("pause", !isPaused);
    }

    public void SetMuted(bool isMuted)
    {
        EnsureInitialized();
        _mpv!.SetProperty("mute", isMuted);
    }

    public void SetVolume(double volume)
    {
        EnsureInitialized();
        _mpv!.SetProperty("volume", Math.Clamp(volume, 0, 100));
    }

    public void SetSpeed(double speed)
    {
        EnsureInitialized();
        _mpv!.SetProperty("speed", speed);
    }

    public void SeekRelative(double seconds)
    {
        EnsureInitialized();
        _mpv!.RunCommand(null!, new object[] { "seek", seconds, "relative+exact" });
    }

    public void SeekAbsolute(double seconds)
    {
        EnsureInitialized();
        _mpv!.RunCommand(null!, new object[] { "seek", Math.Max(0, seconds), "absolute+exact" });
    }

    public PlaybackState GetState()
    {
        EnsureInitialized();

        var path = ReadProperty("path", string.Empty);
        var hasMedia = !string.IsNullOrWhiteSpace(path);
        var title = ReadProperty("media-title", string.Empty);

        return new PlaybackState
        {
            HasMedia = hasMedia,
            IsPaused = ReadProperty("pause", false),
            IsMuted = ReadProperty("mute", false),
            IsIdle = ReadProperty("idle-active", true),
            IsEof = ReadProperty("eof-reached", false),
            DurationSeconds = ReadProperty("duration", 0d),
            PositionSeconds = ReadProperty("time-pos", 0d),
            Volume = ReadProperty("volume", 100d),
            Title = title,
            Path = path,
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (_initialized)
            {
                _mpv!.TerminateDestroy();
            }
        }
        catch
        {
        }
    }

    private T ReadProperty<T>(string name, T fallback)
    {
        try
        {
            return _mpv!.GetProperty<T>(name);
        }
        catch
        {
            return fallback;
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized || _mpv is null)
        {
            throw new InvalidOperationException("Player is not initialized.");
        }
    }
}
