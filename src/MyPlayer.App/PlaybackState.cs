namespace MyPlayer.App;

public sealed class PlaybackState
{
    public bool HasMedia { get; init; }

    public bool IsPaused { get; init; }

    public bool IsMuted { get; init; }

    public bool IsIdle { get; init; }

    public bool IsEof { get; init; }

    public double DurationSeconds { get; init; }

    public double PositionSeconds { get; init; }

    public double Volume { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;
}
