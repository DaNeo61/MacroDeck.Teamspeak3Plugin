using MacroDeck.Sdk.Logging;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeck.Teamspeak3Plugin.Service
{
    internal sealed class Teamspeak3ConnectionMonitor : BackgroundService
    {
        // A failed attempt is an instantly refused loopback connection, so retrying at the check interval
        // costs nothing and brings TeamSpeak back within seconds of a restart.
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

        private readonly ILogger _logger;
        private readonly FailureEpisodeTracker _failures = new(null, TimeProvider.System);

        public Teamspeak3ConnectionMonitor(ILogger logger) => _logger = logger.ForContext<Teamspeak3ConnectionMonitor>();

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(CheckInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                CheckOrReconnect();
            }
        }

        private void CheckOrReconnect()
        {
            var telnet = Teamspeak3Telnet.Instance;
            if (!telnet.HasApiKey)
            {
                _failures.RecordSuccess();
                return;
            }

            if (telnet.CheckConnection() || telnet.TryReconnect(out var error))
            {
                var end = _failures.RecordSuccess();
                if (end is not null)
                {
                    _logger.Information("Reconnected to the TeamSpeak ClientQuery after {Failures} failed attempts over {Duration}", end.Failures, end.Duration);
                }

                return;
            }

            var signal = _failures.RecordFailure(error ?? "Unknown error");
            switch (signal.Kind)
            {
                case FailureEpisodeSignalKind.Onset:
                    _logger.Warning("Lost the connection to the TeamSpeak ClientQuery, retrying in the background: {Error}", signal.LastError);
                    break;
                case FailureEpisodeSignalKind.SummaryDue:
                    _logger.Warning("Still unable to reconnect to the TeamSpeak ClientQuery after {Failures} attempts over {Duration}: {Error}", signal.ConsecutiveFailures, signal.Duration, signal.LastError);
                    break;
            }
        }
    }
}
