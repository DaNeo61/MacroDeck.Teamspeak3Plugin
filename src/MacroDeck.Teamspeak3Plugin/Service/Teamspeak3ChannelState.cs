using MacroDeck.Teamspeak3Plugin.Helper;
using MacroDeck.Teamspeak3Plugin.Model;
using Serilog;

namespace MacroDeck.Teamspeak3Plugin.Service
{
    // The current channel and its clients. Talk and client updates are applied straight from the
    // notification, without a query, so speaking indicators follow in real time. Joins, leaves, moves and
    // renames trigger a debounced re-read through the command connection instead.
    public sealed class Teamspeak3ChannelState
    {
        private static readonly TimeSpan RefreshDebounce = TimeSpan.FromMilliseconds(150);

        private readonly ILogger _logger;
        private readonly object _gate = new();
        private CurrentChannelSnapshot? _current;
        private int _refreshScheduled;

        // Guarded by _gate. A refresh only publishes while its generation is still current, so a reset or a
        // newer refresh wins. Updates that arrive while a refresh is reading are replayed onto its result,
        // because the result reflects the moment of the query, not the moment it is published.
        private int _generation;
        private bool _refreshing;
        private readonly List<PendingUpdate> _pendingUpdates = [];

        public Teamspeak3ChannelState(ILogger logger) => _logger = logger.ForContext<Teamspeak3ChannelState>();

        public event EventHandler? Changed;

        internal CurrentChannelSnapshot? Current => Volatile.Read(ref _current);

        public void Reset() => Publish(null);

        public void RequestRefresh()
        {
            if (Interlocked.Exchange(ref _refreshScheduled, 1) == 1)
            {
                return;
            }

            _ = RefreshAsync();
        }

        internal void Apply(Teamspeak3Notification notification)
        {
            switch (notification.Name)
            {
                case "notifytalkstatuschange":
                    foreach (var entry in notification.Entries)
                    {
                        UpdateClient(entry, client => client with { IsTalking = entry.GetValueOrDefault("status") == "1" });
                    }

                    break;

                case "notifyclientupdated":
                    foreach (var entry in notification.Entries)
                    {
                        UpdateClient(entry, client => client.With(entry));
                    }

                    break;

                case "notifyclientmoved":
                case "notifycliententerview":
                case "notifyclientleftview":
                case "notifychanneledited":
                case "notifychanneldeleted":
                case "notifyconnectstatuschange":
                case "notifycurrentserverconnectionchanged":
                    RequestRefresh();
                    break;
            }
        }

        private async Task RefreshAsync()
        {
            await Task.Delay(RefreshDebounce);

            int generation;
            lock (_gate)
            {
                Interlocked.Exchange(ref _refreshScheduled, 0);
                generation = ++_generation;
                _refreshing = true;
                _pendingUpdates.Clear();
            }

            CurrentChannelSnapshot? snapshot;
            try
            {
                snapshot = await Task.Run(Teamspeak3Telnet.Instance.GetCurrentChannelSnapshot);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Could not read the current TeamSpeak channel");
                lock (_gate)
                {
                    if (generation == _generation)
                    {
                        _refreshing = false;
                        _pendingUpdates.Clear();
                    }
                }

                return;
            }

            lock (_gate)
            {
                if (generation != _generation)
                {
                    return;
                }

                foreach (var pending in _pendingUpdates)
                {
                    snapshot = WithUpdate(snapshot, pending);
                }

                _refreshing = false;
                _pendingUpdates.Clear();
                Volatile.Write(ref _current, snapshot);
            }

            RaiseChanged();
        }

        private void UpdateClient(IReadOnlyDictionary<string, string> entry, Func<ChannelClient, ChannelClient> update)
        {
            if (Teamspeak3Response.IntValue(entry, "clid") is not { } clientId)
            {
                return;
            }

            var pending = new PendingUpdate(clientId, Teamspeak3Response.IntValue(entry, "schandlerid"), update);
            lock (_gate)
            {
                if (_refreshing)
                {
                    _pendingUpdates.Add(pending);
                }

                var current = _current;
                var updated = WithUpdate(current, pending);
                if (ReferenceEquals(updated, current))
                {
                    return;
                }

                Volatile.Write(ref _current, updated);
            }

            RaiseChanged();
        }

        internal void Publish(CurrentChannelSnapshot? snapshot)
        {
            lock (_gate)
            {
                _generation++;
                _refreshing = false;
                _pendingUpdates.Clear();
                Volatile.Write(ref _current, snapshot);
            }

            RaiseChanged();
        }

        // Notifications arrive for every server tab; only the one the snapshot belongs to counts.
        private static CurrentChannelSnapshot? WithUpdate(CurrentChannelSnapshot? snapshot, PendingUpdate pending)
        {
            if (snapshot is null
                || pending.ServerConnectionHandlerId is { } handler && handler != snapshot.ServerConnectionHandlerId)
            {
                return snapshot;
            }

            var clients = snapshot.Clients.ToArray();
            var index = Array.FindIndex(clients, c => c.ClientId == pending.ClientId);
            if (index < 0)
            {
                return snapshot;
            }

            clients[index] = pending.Update(clients[index]);
            return snapshot with { Clients = clients };
        }

        private void RaiseChanged()
        {
            try
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "A channel state subscriber failed");
            }
        }

        private sealed record PendingUpdate(int ClientId, int? ServerConnectionHandlerId, Func<ChannelClient, ChannelClient> Update);
    }
}
