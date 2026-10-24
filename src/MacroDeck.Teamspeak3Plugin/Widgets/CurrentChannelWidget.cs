using MacroDeck.Sdk.Ui;
using MacroDeck.Teamspeak3Plugin.Model;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Widgets;

namespace MacroDeck.Teamspeak3Plugin.Widgets
{
    internal static class CurrentChannelWidget
    {
        public const string LocalId = "current-channel";

        private static readonly UiSize TitleSize = 0.12;
        private static readonly UiSize RowTextSize = 0.1;
        private static readonly UiSize IconSize = 0.1;
        private static readonly UiSize RowGap = 0.02;
        private static readonly UiSize ColumnGap = 0.06;

        // Every length scales with the tile's smaller side, so how many rows and columns fit depends only on
        // the tile's aspect ratio. Measured in rendered previews, a row takes 0.121 of that side and title plus
        // padding 0.39, so a tile with aspect a (width over height) holds (1 / a - 0.39) / 0.121 rows. Each band
        // uses the count that still fits at its upper aspect, where the tile is least tall.
        private static readonly IReadOnlyList<(double? MinAspect, double? MaxAspect, int Rows, int Columns)> Layouts =
        [
            (null, 0.33, 21, 1),
            (null, 0.5, 13, 1),
            (null, 0.67, 9, 1),
            (null, 0.8, 7, 1),
            (2.9, null, 5, 3),
            (1.4, null, 5, 2),
        ];

        private const int SquareRows = 5;

        public static CurrentChannelSnapshot Sample { get; } = new(0, 0, "Lobby",
        [
            new ChannelClient { ClientId = 1, Nickname = "Alex", TalkPower = 75, IsTalking = true },
            new ChannelClient { ClientId = 2, Nickname = "Robin", TalkPower = 50 },
            new ChannelClient { ClientId = 3, Nickname = "Sam", TalkPower = 50, InputMuted = true },
            new ChannelClient { ClientId = 4, Nickname = "Kim", TalkPower = 25, OutputMuted = true },
        ]);

        public static UiElement Build(
            UiSize padding,
            UiState<CurrentChannelSnapshot?> snapshot,
            UiState<IReadOnlyDictionary<string, UiResource>> icons)
            => new UiResponsive
            {
                Key = "channel",
                Default = Layout(SquareRows, 1, padding, snapshot, icons),
                Variants = Layouts
                    .Select(layout => new UiResponsiveVariant
                    {
                        MinAspect = layout.MinAspect,
                        MaxAspect = layout.MaxAspect,
                        Content = Layout(layout.Rows, layout.Columns, padding, snapshot, icons),
                    })
                    .ToList(),
            };

        private static UiStack Layout(
            int rowsPerColumn,
            int columns,
            UiSize padding,
            UiState<CurrentChannelSnapshot?> snapshot,
            UiState<IReadOnlyDictionary<string, UiResource>> icons)
            => new()
            {
                Key = $"rows-{rowsPerColumn}x{columns}",
                Padding = padding,
                Gap = RowGap,
                Children =
                [
                    new UiTextRun
                    {
                        Key = "title",
                        Text = UiText.From(() => snapshot.Value?.ChannelName is { Length: > 0 } name ? name : "TeamSpeak"),
                        Size = TitleSize,
                        Weight = UiComponentTextWeights.Bold,
                        MaxLines = 1,
                    },
                    new UiWhen
                    {
                        Key = "offline-when",
                        Condition = () => snapshot.Value is null,
                        Content = () => new UiTextRun
                        {
                            Key = "offline",
                            Text = Strings.Widgets.CurrentChannel.NotConnected(),
                            Size = RowTextSize,
                            Role = UiComponentTextRoles.Secondary,
                            MaxLines = 1,
                        },
                    },
                    Columns(rowsPerColumn, columns, snapshot, icons),
                ],
            };

        private static UiStack Columns(
            int rowsPerColumn,
            int columns,
            UiState<CurrentChannelSnapshot?> snapshot,
            UiState<IReadOnlyDictionary<string, UiResource>> icons)
            => new()
            {
                Key = "users",
                Fill = true,
                Direction = UiComponentDirections.Horizontal,
                Gap = ColumnGap,
                Children = Enumerable.Range(0, columns)
                    .Select(column => (UiElement)new UiStack
                    {
                        Key = $"column-{column}",
                        Fill = true,
                        Gap = RowGap,
                        Children =
                        [
                            new UiRepeat<ChannelWidgetRow>
                            {
                                Key = "rows",
                                Items = UiValue.From(() => ChannelWidgetRows.Column(
                                    ChannelWidgetRows.Select(snapshot.Value?.Clients ?? [], rowsPerColumn * columns),
                                    column,
                                    rowsPerColumn)),
                                KeySelector = row => row.Key,
                                Template = (row, rowKey) => Row(rowKey, row, icons),
                            },
                        ],
                    })
                    .ToList(),
            };

        private static UiElement Row(string key, ChannelWidgetRow row, UiState<IReadOnlyDictionary<string, UiResource>> icons)
            => row.Client is { } client
                ? new UiStack
                {
                    Key = key,
                    Direction = UiComponentDirections.Horizontal,
                    Align = UiComponentAlignments.Center,
                    Gap = RowGap,
                    Children =
                    [
                        StatusIndicator(client, icons.Value),
                        new UiTextRun { Key = "name", Text = client.Nickname, Size = RowTextSize, MaxLines = 1, Fill = true },
                    ],
                }
                : new UiTextRun
                {
                    Key = key,
                    Text = Strings.Widgets.CurrentChannel.More(row.HiddenCount),
                    Size = RowTextSize,
                    Role = UiComponentTextRoles.Secondary,
                    MaxLines = 1,
                };

        private static UiElement StatusIndicator(ChannelClient client, IReadOnlyDictionary<string, UiResource> icons)
            => icons.TryGetValue(Teamspeak3IconSet.IconNameFor(client), out var icon)
                ? new UiImage { Key = "status", Source = UiValue.Of(icon), Size = IconSize }
                : new UiIcon { Key = "status", Icon = UiIcons.User, Size = IconSize, Color = FallbackColor(client.Status) };

        // Without TeamSpeak's icons the indicator falls back to a coloured user glyph in TeamSpeak's palette.
        private static string FallbackColor(ChannelClientStatus status) => status switch
        {
            ChannelClientStatus.Talking => "#3b82f6",
            ChannelClientStatus.Silent => "#94a3b8",
            ChannelClientStatus.Away => "#64748b",
            _ => "#dc2626",
        };
    }
}
