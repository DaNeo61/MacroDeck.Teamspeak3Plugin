using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Ui;
using MacroDeck.Teamspeak3Plugin.Model;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Teamspeak3Plugin.Service
{
    public sealed class PluginConfigFlow : IUiConfigFlow
    {
        private static ConfigFlowStep ConnectionStep() => new()
        {
            StepId = "connection",
            Title = Strings.Setup.ConnectionTitle(),
            Fields =
            [
                ActionParameter.Secret("api_key", label: Strings.Setup.ApiKey(), required: true),
                //ActionParameter.Number("poll_seconds", label: Strings.Setup.PollInterval(), min: 1, max: 3600, defaultValue: 30),
            ],
        };

        public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken) => Task.FromResult(ConfigFlowResult.Step(ConnectionStep()));

        public async Task<ConfigFlowResult> SubmitAsync(string stepId, IReadOnlyDictionary<string, object?> input, IConfigFlowContext context, CancellationToken cancellationToken)
        {
            var apiKey = input.GetValueOrDefault("api_key") as string ?? string.Empty;
            try
            {
                Teamspeak3Telnet.Instance.ConnectClient(apiKey);
                return ConfigFlowResult.Complete("Teamspeak3 Plugin");
            }
            catch (Exception ex)
            {
                return ConfigFlowResult.Error(ConnectionStep(), ex.Message);
            }
        }

        public Task<IUiSession?> CreateUiSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
        {
            var apiKey = new UiState<string>(string.Empty);
            var pollSeconds = new UiState<double>(30);

            var root = new UiConfigStack
            {
                Key = "root",
                Children =
                [
                    new UiSecretInput
                        {
                            Key = "api_key",
                            Label = Strings.Setup.ApiKey(),
                            Required = true,
                            Binding = Bind.To(apiKey),
                        },
                        /*new UiNumberInput
                        {
                            Key = "poll_seconds",
                            Label = Strings.Setup.PollInterval(),
                            Min = 5,
                            Max = 3600,
                            ShowSlider = true,
                            Binding = Bind.To(pollSeconds),
                        },*/
                    ],
            };

            return Task.FromResult<IUiSession?>(new ViewSession(new UiView(request.Surface, root)));
        }
    }
}
