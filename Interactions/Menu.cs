using Discord;

namespace CloudMusicBot.Interactions;

/// <summary>
/// Paged select menu owned by a single user. Selections are remembered per page,
/// so a user can pick from page 1, walk to page 3 and keep both choices.
/// </summary>
public sealed class Menu
{
    private const int PageSize = 20;

    private readonly string _customId;
    private readonly string _placeholder;
    private readonly bool _multiSelect;
    private readonly List<SelectMenuOptionBuilder> _options;
    private readonly Dictionary<int, string[]> _selection = [];

    private int _page = 1;

    internal Menu(
        string customId,
        string placeholder,
        bool multiSelect,
        IEnumerable<SelectMenuOptionBuilder> options)
    {
        _customId = customId;
        _placeholder = placeholder;
        _multiSelect = multiSelect;
        _options = options.ToList();
    }

    private int PageCount => Math.Max(1, (_options.Count + PageSize - 1) / PageSize);

    /// <summary>
    /// Builds an option with the label shortened to what Discord accepts; song titles
    /// and playlist names come from users and regularly blow past that limit.
    /// </summary>
    public static SelectMenuOptionBuilder Option(string label, string value, string? description = null)
    {
        var option = new SelectMenuOptionBuilder(TextFormat.Clip(label), value);
        if (!string.IsNullOrWhiteSpace(description))
        {
            option.Description = TextFormat.Clip(description);
        }

        return option;
    }

    public void Previous()
    {
        if (_page > 1)
        {
            _page--;
        }
    }

    public void Next()
    {
        if (_page < PageCount)
        {
            _page++;
        }
    }

    public void UpdateSelection(IReadOnlyList<string> values) => _selection[_page] = [.. values];

    /// <summary>Selected values across every page, in pick order.</summary>
    public string[] GetSelected() =>
        _selection.Values.SelectMany(values => values).Distinct().ToArray();

    public MessageComponent BuildCurrentPage()
    {
        var select = new SelectMenuBuilder()
            .WithCustomId(_customId)
            .WithPlaceholder($"{_placeholder}({_page}/{PageCount})");

        var selected = _selection.TryGetValue(_page, out var values) ? values : [];
        var count = 0;

        for (var index = (_page - 1) * PageSize; index < _page * PageSize && index < _options.Count; index++)
        {
            var option = _options[index];
            option.Label = TextFormat.Clip(option.Label);
            if (!string.IsNullOrWhiteSpace(option.Description))
            {
                option.Description = TextFormat.Clip(option.Description);
            }
            option.IsDefault = selected.Contains(option.Value);
            select.AddOption(option);
            count++;
        }

        // Discord rejects a menu without options, so every page keeps at least one.
        select.MaxValues = _multiSelect ? Math.Max(1, count) : 1;

        var components = new ComponentBuilder().WithSelectMenu(select);
        if (_page > 1)
        {
            components.WithButton("上一页", "menu_previous", ButtonStyle.Secondary);
        }

        if (_page < PageCount)
        {
            components.WithButton("下一页", "menu_next", ButtonStyle.Primary);
        }

        if (_multiSelect)
        {
            components.WithButton("确定", $"menu_{_customId}_confirm", ButtonStyle.Success);
        }

        components.WithButton("取消", "menu_cancel", ButtonStyle.Danger);
        return components.Build();
    }
}
