using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Variables;
using MacroDeck.Teamspeak3Plugin.Model;
using MacroDeck.Teamspeak3Plugin.Service;
using Serilog;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeck.Teamspeak3Plugin
{
    public sealed class PluginIntegration : IPluginIntegration, IVariableProvider, IUiConfigFlowProvider
    {
        // Notifications trigger an immediate read; polling only catches anything a notification missed.
        private static readonly TimeSpan FallbackRefreshInterval = TimeSpan.FromSeconds(30);

        private readonly ILogger _logger;
        private readonly Teamspeak3NotificationListener _notifications;
        private volatile IIntegrationContext? _context;

        public PluginIntegration(ILogger logger, Teamspeak3NotificationListener notifications)
        {
            _logger = logger.ForContext<PluginIntegration>();
            _notifications = notifications;
            Teamspeak3Telnet.Instance.WithLogger(logger);

            //Init Subclasses
            var actionDefinitions = new List<IActionDefinition>();
            var allTypes = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var item in allTypes)
            {
                if (typeof(IActionDefinition).IsAssignableFrom(item) && !item.IsAbstract)
                {
                    var o = Activator.CreateInstance(item, logger) as IActionDefinition;
                    if (o == null)
                        continue;

                    actionDefinitions.Add(o);
                }
            }
            Actions = actionDefinitions;
        }

        public IReadOnlyList<IActionDefinition> Actions { get; }

        public IConfigFlow CreateConfigFlow() => new PluginConfigFlow();

        public IReadOnlyList<VariableDefinition> Variables { get; } =
        [
            VariableDefinition.Eager(VariableIds.CurrentChannel, VariableType.Text, refreshInterval: FallbackRefreshInterval) with { Id = VariableIds.CurrentChannel },
            VariableDefinition.Eager(VariableIds.OutputState, VariableType.Boolean, refreshInterval: FallbackRefreshInterval) with { Id = VariableIds.OutputState },
            VariableDefinition.Eager(VariableIds.InputState, VariableType.Boolean, refreshInterval: FallbackRefreshInterval) with { Id = VariableIds.InputState },
        ];

        public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default) => ValueTask.FromResult(
            localId switch
            {
                VariableIds.CurrentChannel => VariableReading.Of(Teamspeak3Telnet.Instance.CurrentChannelVariable()),
                VariableIds.OutputState => VariableReading.Of(Teamspeak3Telnet.Instance.OutputMuteVariable()),
                VariableIds.InputState => VariableReading.Of(Teamspeak3Telnet.Instance.InputMuteVariable()),
                _ => VariableReading.Unavailable
            });

        public async Task InitializeAsync(IIntegrationContext context)
        {
            _context = context;
            _notifications.VariablesChanged -= OnVariablesChanged;
            _notifications.VariablesChanged += OnVariablesChanged;

            try
            {
                var entries = await context.Config.GetEntriesAsync();
                if (entries.Count == 0)
                {
                    Teamspeak3Telnet.Instance.UseApiKey(null);
                    _logger.Warning("No plugin configuration entry found during initialization.");
                    return;
                }

                var config = entries[0]; // It is only one Config Entry for this Plugin, so we can just take the first one
                var apiKey = await context.Config.GetSecretAsync(config.Id, "api_key");

                Teamspeak3Telnet.Instance.UseApiKey(apiKey);
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    _logger.Warning("No API key configured; skipping TeamSpeak connection.");
                    return;
                }

                if (!Teamspeak3Telnet.Instance.TryReconnect(out var error))
                {
                    _logger.Warning("Unable to connect to TeamSpeak during initialization, retrying in the background: {Error}", error);
                }

                /*
                var pollSeconds = await context.Config.GetStringAsync(config.Id, "poll_seconds");
                if(int.TryParse(pollSeconds, out var interval))
                {
                    foreach(var variable in Variables)
                    {
                        variable. = TimeSpan.FromSeconds(interval);
                    }
                }
                */
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "An unexpected issue occurred while initializing the plugin.");
            }
        }

        public Task ShutdownAsync()
        {
            _notifications.VariablesChanged -= OnVariablesChanged;
            _context = null;
            Teamspeak3Telnet.Instance.Dispose();
            return Task.CompletedTask;
        }

        private void OnVariablesChanged(object? sender, VariablesChangedEventArgs e)
        {
            var context = _context;
            if (context is not null)
            {
                _ = RequestRefreshAsync(context, e.VariableIds);
            }
        }

        private async Task RequestRefreshAsync(IIntegrationContext context, IReadOnlyCollection<string> variableIds)
        {
            try
            {
                await context.Variables.RequestDefinitionRefreshAsync(variableIds, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not request a refresh of {VariableIds}", variableIds);
            }
        }

    }
}
