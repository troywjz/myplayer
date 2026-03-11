namespace MyPlayer.App;

public sealed class AppSettings
{
    public double Volume { get; set; } = 100;

    public double Speed { get; set; } = 1.0;

    public bool IsMuted { get; set; }

    public double WindowWidth { get; set; } = 1120;

    public double WindowHeight { get; set; } = 720;

    public double WindowLeft { get; set; } = 120;

    public double WindowTop { get; set; } = 120;
}
