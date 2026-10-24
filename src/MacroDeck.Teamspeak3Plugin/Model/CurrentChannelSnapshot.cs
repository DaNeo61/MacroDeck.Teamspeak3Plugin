namespace MacroDeck.Teamspeak3Plugin.Model
{
    internal sealed record CurrentChannelSnapshot(
        int ServerConnectionHandlerId,
        int ChannelId,
        string ChannelName,
        IReadOnlyList<ChannelClient> Clients);
}
