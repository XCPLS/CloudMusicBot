namespace CloudMusicBot;

public class Music
{
    public ulong Id { get; set; }
    public string Name { get; set; } = "未知歌曲";
    public string Artist { get; set; } = "未知歌手";
    public string Album { get; set; } = "未知专辑";
    public string Cover { get; set; } = "";
    public ulong Owner { get; set; }
}