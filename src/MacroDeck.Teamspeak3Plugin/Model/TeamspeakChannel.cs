using MacroDeck.Teamspeak3Plugin.Helper;

namespace MacroDeck.Teamspeak3Plugin.Model
{
    public class TeamspeakChannel
    {
        public string ChannelId { get; set; }
        public string Name { get; set; }

        public TeamspeakChannel() { }
        public TeamspeakChannel(string tempChannel)
        {
            if (!tempChannel.Contains("channel_name=") || !tempChannel.Contains("cid="))
                return;

            Name = tempChannel.Substring("channel_name=", " channel");
            ChannelId = tempChannel.Substring("cid=", " pid=");
        }
    }
}
