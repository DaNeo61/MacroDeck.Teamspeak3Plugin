using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Variables;
using MacroDeck.Teamspeak3Plugin.Service;
using Serilog;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeck.Teamspeak3Plugin
{
    public sealed class PluginIntegration : IPluginIntegration, IVariableProvider, IUiConfigFlowProvider
    {
        private readonly ILogger _logger;

        public PluginIntegration(ILogger logger)
        {
            _logger = logger.ForContext<PluginIntegration>();
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
            VariableDefinition.Eager("ts3-current-channel", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(5)) with { Id = "ts3-current-channel", Write = new VariableWriteCapability() },
            VariableDefinition.Eager("ts3-output-state", VariableType.Boolean, refreshInterval: TimeSpan.FromSeconds(5)) with { Id = "ts3-output-state", Write = new VariableWriteCapability() },
            VariableDefinition.Eager("ts3-input-state", VariableType.Boolean, refreshInterval: TimeSpan.FromSeconds(5)) with { Id = "ts3-input-state", Write = new VariableWriteCapability() },
        ];

        public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default) => ValueTask.FromResult(
            localId switch
            {
                "ts3-current-channel" => VariableReading.Of(Teamspeak3Telnet.Instance.CurrentChannelVariable()),
                "ts3-output-state" => VariableReading.Of(Teamspeak3Telnet.Instance.OutputMuteVariable()),
                "ts3-input-state" => VariableReading.Of(Teamspeak3Telnet.Instance.InputMuteVariable()),
                _ => VariableReading.Unavailable
            });

        public ValueTask<VariableWriteResult> SetValueAsync(string localId, object? value, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(VariableWriteResult.Applied());
        }

        public async Task InitializeAsync(IIntegrationContext context)
        {
            try
            {
                var entries = await context.Config.GetEntriesAsync();
                var config = entries.First(); // It is only one Config Entry for this Plugin, so we can just take the first one

                var apiKey = await context.Config.GetSecretAsync(config.Id, "api_key");
                if (apiKey != null)
                {
                    Teamspeak3Telnet.Instance.ConnectClient(apiKey);
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
                return;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "An error occurred while initializing the plugin.");
                return;
            }
        }

        public Task ShutdownAsync()
        {
            Teamspeak3Telnet.Instance.Dispose();
            return Task.CompletedTask;
        }

    }
}
