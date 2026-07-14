using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;

namespace CloudMusicBot;

public class Menu
{
    private static readonly ConcurrentDictionary<SocketUser, Menu> UserMenu = new();
    private static readonly ConcurrentDictionary<SocketUser, CancellationTokenSource> ExpireTokens = new();
    
    private int _currentPage = 1;
    private readonly string _id;
    private readonly string _hint;
    private readonly List<SelectMenuOptionBuilder> _options;
    private readonly bool _multi;
    private readonly SocketUser _user;
    private readonly Dictionary<int, string[]> _select = new Dictionary<int, string[]>();
    
    public Menu(string id, List<SelectMenuOptionBuilder> options, string hint, bool multi, SocketUser user)
    {
        _id = id;
        _hint = hint;
        _options = options;
        _multi = multi;
        _user = user;
        Add();
    }

    public static bool TryGetUserCurrentMenu(SocketUser user, out Menu? menu)
    {
        return UserMenu.TryGetValue(user, out menu);
    }
    
    private void Add()
    {
        if (ExpireTokens.TryRemove(_user, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }

        UserMenu[_user] = this;

        var cts = new CancellationTokenSource();
        ExpireTokens[_user] = cts;

        _ = ExpireAfterDelayAsync(_user, cts.Token);
    }

    private void Remove(SocketUser key)
    {
        UserMenu.TryRemove(key, out _);

        if (ExpireTokens.TryRemove(key, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }
    
    private async Task ExpireAfterDelayAsync(SocketUser key, CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(10), token);
            UserMenu.TryRemove(key, out _);
        }
        catch (TaskCanceledException) { }
        finally
        {
            if (ExpireTokens.TryRemove(key, out var cts) && cts.Token == token)
            {
                cts.Dispose();
            }
        }
    }

    public MessageComponent GetCurrentPage()
    {
        var builder = new ComponentBuilder();
        var selectMenuBuilder = new SelectMenuBuilder();
        selectMenuBuilder.CustomId = _id;
        selectMenuBuilder.Placeholder = $"{_hint}({_currentPage}/{(_options.Count + 20 - 1) / 20})";
        int item = 0;
        for (int i = (_currentPage-1)*20; i < _currentPage*20; i++)
        {
            if (i >= _options.Count)
            {
                break;
            }

            var option = _options[i];
            option.IsDefault = _select.Values.Any(x => x.Contains(option.Value));
            selectMenuBuilder.AddOption(option);
            item++;
        }
        
        selectMenuBuilder.MaxValues = _multi ? item : 1;

        builder.WithSelectMenu(selectMenuBuilder);
        if (_currentPage > 1)
        {
            builder.WithButton("上一页", "menu_previous", ButtonStyle.Secondary);
        }
        if (_currentPage < (_options.Count + 20 - 1) / 20)
        {
            builder.WithButton("下一页", "menu_next", ButtonStyle.Primary);
        }
        if (_multi)
        {
            builder.WithButton("确定", $"menu_{_id}_confirm", ButtonStyle.Success);
        }
        builder.WithButton("取消", "menu_cancel", ButtonStyle.Danger);
        
        return builder.Build();
    }
    
    public void Previous()
    {
        _currentPage--;
    }

    public void Next()
    {
        _currentPage++;
    }

    public void Close()
    {
        Remove(_user);
    }

    public void UpdateSelect(string[] values)
    {
        _select[_currentPage] = values;
    }

    public string[] GetSelected()
    {
        List<string> result = new List<string>();
        for (int i = 1; i <= _select.Count; i++)
        {
            if (_select.TryGetValue(i, out var value))
            {
                result.AddRange(value);
            }
            
        }

        return result.ToArray();
    }
}