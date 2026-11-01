namespace Basalt.Services.Charts.Model;

public record MusicSpec(string File, double Offset = 0, float Volume = 1f)
{
    internal static MusicSpec? From(ChartDto dto)
        => dto.Music is { } music ? new MusicSpec(music.File!.Trim(), music.Offset ?? 0, music.Volume ?? 1f) : null;
}
