using Discord;
using CloudMusicBot.Models;

namespace CloudMusicBot.Ui;

public static class Embeds
{
    public static Embed NowPlaying(Music music) =>
        Base(Color.Green, "🎵正在播放")
            .WithDescription(Describe(music))
            .WithCover(music.Cover)
            .Build();

    public static Embed PlayFailed(Music music, string reason) =>
        Base(Color.Red, "❌播放失败")
            .WithDescription($"{Describe(music)}\n信息: {reason}")
            .WithCover(music.Cover)
            .Build();

    private static string Describe(Music music) =>
        $"歌曲: {music.Name}\n歌手: {music.Artist}\n专辑: 《{music.Album}》";

    private static EmbedBuilder Base(Color color, string title) =>
        new EmbedBuilder()
            .WithColor(color)
            .WithCurrentTimestamp()
            .WithFooter("CloudMusic")
            .WithTitle(title);

    private static EmbedBuilder WithCover(this EmbedBuilder builder, string cover) =>
        string.IsNullOrWhiteSpace(cover) ? builder : builder.WithThumbnailUrl(cover);
}
