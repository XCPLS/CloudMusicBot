using Newtonsoft.Json.Linq;

namespace CloudMusicBot;

public static class MusicUtils
{
    public static string GetArtists(JToken json)
    {
        List<string> name = new List<string>();
        if (json is JObject obj)
        {
            if (obj.ContainsKey("artists"))
            {
                foreach (var ar in (JArray)json["artists"])
                {
                    name.Add((string)ar["name"]);
                }
            }
            else
            {
                foreach (var ar in (JArray)json["ar"])
                {
                    name.Add((string)ar["name"]);
                }
            }
        }

        if (name.Count == 0)
        {
            return "未知歌手";
        }

        return string.Join(" / ", name);
    }
    
    public static string GetAlbum(JToken json, out string cover)
    {
        if (json is JObject obj)
        {
            if (obj.ContainsKey("album"))
            {
                cover = (string)obj["album"]["artist"]["img1v1url"];
                return (string)obj["album"]["name"];
            }
            else
            {
                cover = (string)obj["al"]["picUrl"];
                return (string)obj["al"]["name"];
            }
        }

        cover = "";
        return "未知专辑";
    }
}