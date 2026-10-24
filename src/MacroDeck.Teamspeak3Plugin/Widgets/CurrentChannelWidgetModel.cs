using MacroDeck.Teamspeak3Plugin.Model;
using MacroDeck.Teamspeak3Plugin.Service;
using MacroDeck.Ui.Runtime;
using Serilog;

namespace MacroDeck.Teamspeak3Plugin.Widgets
{
    // The state every channel widget reads. Bursts of talk notifications are coalesced so a busy channel
    // stays well below the per-session patch rate.
    public sealed class CurrentChannelWidgetModel : IDisposable
    {
        private static readonly TimeSpan FlushDelay = TimeSpan.FromMilliseconds(50);

        private readonly Teamspeak3ChannelState _channelState;
        private readonly ILogger _logger;
        private int _flushScheduled;

        public CurrentChannelWidgetModel(Teamspeak3ChannelState channelState, ILogger logger)
        {
            _channelState = channelState;
            _logger = logger.ForContext<CurrentChannelWidgetModel>();
            _channelState.Changed += OnChannelChanged;
        }

        internal UiState<CurrentChannelSnapshot?> Snapshot { get; } = new(null);

        public void Dispose() => _channelState.Changed -= OnChannelChanged;

        private void OnChannelChanged(object? sender, EventArgs e)
        {
            if (Interlocked.Exchange(ref _flushScheduled, 1) == 0)
            {
                _ = FlushAsync();
            }
        }

        private async Task FlushAsync()
        {
            await Task.Delay(FlushDelay);
            Interlocked.Exchange(ref _flushScheduled, 0);
            try
            {
                Snapshot.Value = _channelState.Current;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Could not update the channel widgets");
            }
        }
    }
}
