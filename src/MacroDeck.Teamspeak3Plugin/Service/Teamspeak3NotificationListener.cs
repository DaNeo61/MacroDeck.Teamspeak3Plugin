using MacroDeck.Sdk.Logging;
using MacroDeck.Teamspeak3Plugin.Helper;
using MacroDeck.Teamspeak3Plugin.Model;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Net.Sockets;

namespace MacroDeck.Teamspeak3Plugin.Service
{
    // Uses its own ClientQuery connection, so unsolicited notify lines never interleave with the
    // request/response traffic of the synchronous command connection in Teamspeak3Telnet.
    public sealed class Teamspeak3NotificationListener : BackgroundService
    {
        private const string Host = "127.0.0.1";
        private const int Port = 25639;
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(60);
        // A failed attempt is an instantly refused loopback connection, so a fixed short interval costs nothing.
        private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

        private readonly ILogger _logger;
        private readonly Teamspeak3ChannelState _channelState;
        private readonly FailureEpisodeTracker _failures = new(null, TimeProvider.System);
        private CancellationTokenSource? _session;

        public Teamspeak3NotificationListener(ILogger logger, Teamspeak3ChannelState channelState)
        {
            _logger = logger.ForContext<Teamspeak3NotificationListener>();
            _channelState = channelState;
        }

        public event EventHandler<VariablesChangedEventArgs>? VariablesChanged;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var telnet = Teamspeak3Telnet.Instance;
            telnet.ApiKeyChanged += OnApiKeyChanged;
            telnet.Connected += OnCommandConnectionEstablished;
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var apiKey = telnet.ApiKey;
                    if (apiKey is null)
                    {
                        _failures.RecordSuccess();
                        await Task.Delay(RetryInterval, stoppingToken);
                        continue;
                    }

                    using var session = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    Volatile.Write(ref _session, session);
                    if (telnet.ApiKey != apiKey)
                    {
                        // The key changed before this session was published, so its cancellation was missed.
                        continue;
                    }

                    try
                    {
                        await ListenAsync(apiKey, session.Token);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        continue;
                    }
                    catch (Exception ex)
                    {
                        RecordFailure(ex.Message);
                        _channelState.Reset();
                    }
                    finally
                    {
                        Interlocked.CompareExchange(ref _session, null, session);
                    }

                    await Task.Delay(RetryInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            finally
            {
                telnet.ApiKeyChanged -= OnApiKeyChanged;
                telnet.Connected -= OnCommandConnectionEstablished;
            }
        }

        private async Task ListenAsync(string apiKey, CancellationToken cancellationToken)
        {
            using var client = new TcpClient();
            using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connectTimeout.CancelAfter(ConnectTimeout);
                try
                {
                    await client.ConnectAsync(Host, Port, connectTimeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("Timed out connecting to the TeamSpeak ClientQuery.");
                }
            }

            using var stream = client.GetStream();
            using var reader = new StreamReader(stream);
            using var writer = new StreamWriter(stream) { AutoFlush = true };

            await SendAsync(reader, writer, $"auth apikey={Teamspeak3Escaping.Escape(apiKey)}", cancellationToken);
            foreach (var eventName in Teamspeak3Notification.RegisteredEvents)
            {
                await SendAsync(reader, writer, $"clientnotifyregister schandlerid=0 event={eventName}", cancellationToken);
            }

            var end = _failures.RecordSuccess();
            if (end is not null)
            {
                _logger.Information("Listening for TeamSpeak notifications again after {Failures} failed attempts over {Duration}", end.Failures, end.Duration);
            }

            RaiseVariablesChanged(VariableIds.All);
            _channelState.RequestRefresh();

            using var keepAliveStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var keepAlive = KeepAliveAsync(writer, keepAliveStop.Token);
            try
            {
                while (true)
                {
                    var line = await reader.ReadLineAsync(cancellationToken)
                        ?? throw new IOException("The TeamSpeak client closed the notification connection.");
                    await DispatchAsync(line, cancellationToken);
                }
            }
            finally
            {
                await keepAliveStop.CancelAsync();
                await keepAlive;
            }
        }

        // Reads up to the command's error line. The welcome banner and notifications that arrive in the
        // meantime come first and are skipped or dispatched.
        private async Task SendAsync(StreamReader reader, StreamWriter writer, string command, CancellationToken cancellationToken)
        {
            await writer.WriteLineAsync(command.AsMemory(), cancellationToken);
            while (true)
            {
                var line = await reader.ReadLineAsync(cancellationToken)
                    ?? throw new IOException("The TeamSpeak client closed the notification connection.");
                if (line.StartsWith("error ", StringComparison.Ordinal))
                {
                    if (!line.Contains("msg=ok", StringComparison.Ordinal))
                    {
                        // The API key is never part of the message: only the command name is logged.
                        throw new InvalidOperationException($"TeamSpeak refused '{command.Split(' ')[0]}': {line}");
                    }

                    return;
                }

                await DispatchAsync(line, cancellationToken);
            }
        }

        private async Task DispatchAsync(string line, CancellationToken cancellationToken)
        {
            var notification = Teamspeak3Notification.Parse(line, Teamspeak3Telnet.Instance.ClientId);
            if (notification.Name.Length == 0)
            {
                return;
            }

            Teamspeak3Telnet.Instance.ApplyMuteNotification(notification.InputMuted, notification.OutputMuted);

            if (notification.ServerConnectionChanged)
            {
                // The tab and the own client id change with the server connection; follow them before anything is read.
                var newTab = notification.Name == "notifycurrentserverconnectionchanged" && notification.Entries is [var entry, ..]
                    ? Teamspeak3Response.IntValue(entry, "schandlerid")
                    : null;
                await Task.Run(() => Teamspeak3Telnet.Instance.FollowActiveServerConnection(newTab), cancellationToken);
            }

            _channelState.Apply(notification);

            if (notification.AffectedVariableIds.Count > 0)
            {
                RaiseVariablesChanged(notification.AffectedVariableIds);
            }
        }

        // Keeps an otherwise silent notification-only session from being dropped as idle.
        private static async Task KeepAliveAsync(StreamWriter writer, CancellationToken cancellationToken)
        {
            try
            {
                while (true)
                {
                    await Task.Delay(KeepAliveInterval, cancellationToken);
                    await writer.WriteLineAsync("whoami".AsMemory(), cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
                // The read loop sees the broken connection and reconnects.
            }
        }

        private void RecordFailure(string error)
        {
            var signal = _failures.RecordFailure(error);
            switch (signal.Kind)
            {
                case FailureEpisodeSignalKind.Onset:
                    _logger.Warning("Not receiving TeamSpeak notifications, variables fall back to the 30 second poll until reconnected: {Error}", signal.LastError);
                    break;
                case FailureEpisodeSignalKind.SummaryDue:
                    _logger.Warning("Still not receiving TeamSpeak notifications after {Failures} attempts over {Duration}: {Error}", signal.ConsecutiveFailures, signal.Duration, signal.LastError);
                    break;
            }
        }

        private void RaiseVariablesChanged(IReadOnlyCollection<string> variableIds)
            => VariablesChanged?.Invoke(this, new VariablesChangedEventArgs(variableIds));

        private void OnApiKeyChanged(object? sender, EventArgs e)
        {
            _channelState.Reset();
            try
            {
                Volatile.Read(ref _session)?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The session ended on its own in the meantime.
            }
        }

        private void OnCommandConnectionEstablished(object? sender, EventArgs e)
        {
            RaiseVariablesChanged(VariableIds.All);
            _channelState.RequestRefresh();
        }
    }

    public sealed class VariablesChangedEventArgs(IReadOnlyCollection<string> variableIds) : EventArgs
    {
        public IReadOnlyCollection<string> VariableIds { get; } = variableIds;
    }
}
