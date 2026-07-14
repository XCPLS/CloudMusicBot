namespace CloudMusicBot;

public static class HttpClientExtensions
{
    public static async Task<HttpResponseMessage> GetAsyncWithTimestamp(this HttpClient client, string requestUrl, ulong userid = 0)
    {
        if (requestUrl.Contains('?'))
        {
            requestUrl += $"&timestamp={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        }
        else
        {
            requestUrl += $"?timestamp={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        }
        var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        if (userid != 0)
        {
            if (!UserCookie.UserCookies.TryGetValue(userid, out var cookie))
            {
                throw new Exception("用户未登录");
            }
            request.Headers.Add("Cookie", $"MUSIC_U={cookie}");
        }

        return await client.SendAsync(request);
    }
}