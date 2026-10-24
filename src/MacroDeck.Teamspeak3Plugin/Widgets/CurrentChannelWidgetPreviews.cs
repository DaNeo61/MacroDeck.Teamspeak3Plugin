using MacroDeck.Teamspeak3Plugin.Model;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Widgets;

namespace MacroDeck.Teamspeak3Plugin.Widgets
{
    // Rendered by `macrodeck-plugin preview render`. Previews have no resource registry, so they show the
    // fallback indicators instead of TeamSpeak's icons.
    internal static class CurrentChannelWidgetPreviews
    {
        [UiPreview("Channel with a few users", Profile = UiPreviewProfiles.Widget)]
        public static UiPreview FewUsers() => Preview(CurrentChannelWidget.Sample);

        [UiPreview("Crowded channel with speakers", Profile = UiPreviewProfiles.Widget)]
        public static UiPreview CrowdedChannel() => Preview(new CurrentChannelSnapshot(1, 1, "Raid night - voice",
            Enumerable.Range(1, 24)
                .Select(i => new ChannelClient
                {
                    ClientId = i,
                    Nickname = $"Player {i:00}",
                    TalkPower = i % 3 == 0 ? 75 : 25,
                    IsTalking = i is 7 or 19 or 23,
                    InputMuted = i % 5 == 0,
                    OutputMuted = i % 11 == 0,
                    HasInputHardware = i != 13,
                    IsAway = i == 17,
                    IsChannelCommander = i == 3,
                })
                .ToList()));

        [UiPreview("Not connected", Profile = UiPreviewProfiles.Widget)]
        public static UiPreview NotConnected() => Preview(null);

        private static UiPreview Preview(CurrentChannelSnapshot? snapshot)
            => UiPreview.Of(CurrentChannelWidget.Build(
                UiWidgetSafeArea.For(UiWidgetSafeArea.DefaultCornerRadius),
                new UiState<CurrentChannelSnapshot?>(snapshot),
                new UiState<IReadOnlyDictionary<string, UiResource>>(new Dictionary<string, UiResource>())));
    }
}
