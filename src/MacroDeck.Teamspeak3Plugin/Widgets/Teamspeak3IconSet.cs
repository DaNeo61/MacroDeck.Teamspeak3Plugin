using MacroDeck.Sdk.Ui;
using MacroDeck.Teamspeak3Plugin.Model;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;
using Serilog;
using System.IO.Compression;

namespace MacroDeck.Teamspeak3Plugin.Widgets
{
    // TeamSpeak's own status icons, read at runtime from the user's TeamSpeak installation. They are not
    // shipped with the plugin: the icons belong to TeamSpeak Systems and are only shown where TeamSpeak is
    // installed. Macro Deck cannot draw SVG, so the PNG set in default.zip is used.
    public sealed class Teamspeak3IconSet
    {
        private const string IconArchive = "default.zip";
        private static readonly int[] PreferredPixelSizes = [32, 24, 16];

        private static readonly IReadOnlyList<string> IconNames =
        [
            "player_on",
            "player_off",
            "player_commander_on",
            "player_commander_off",
            "input_muted",
            "output_muted",
            "hardware_input_muted",
            "hardware_output_muted",
            "input_muted_local",
            "away",
        ];

        private readonly ILogger _logger;

        public Teamspeak3IconSet(ILogger logger) => _logger = logger.ForContext<Teamspeak3IconSet>();

        // Empty until registered, and when TeamSpeak's icons cannot be found; the widget then draws its fallback.
        public UiState<IReadOnlyDictionary<string, UiResource>> Icons { get; } = new(new Dictionary<string, UiResource>());

        public async Task RegisterAsync(IUiResourceRegistry registry, CancellationToken cancellationToken)
        {
            var archivePath = FindIconArchive();
            if (archivePath is null)
            {
                _logger.Information("TeamSpeak icons not found, the channel widget uses its built-in indicators");
                return;
            }

            try
            {
                var icons = new Dictionary<string, UiResource>(StringComparer.Ordinal);
                using (var archive = ZipFile.OpenRead(archivePath))
                {
                    foreach (var name in IconNames)
                    {
                        var entry = PreferredPixelSizes
                            .Select(size => archive.GetEntry($"{size}x{size}_{name}.png"))
                            .FirstOrDefault(found => found is not null);
                        if (entry is null)
                        {
                            continue;
                        }

                        using var content = new MemoryStream();
                        await using (var stream = entry.Open())
                        {
                            await stream.CopyToAsync(content, cancellationToken);
                        }

                        icons[name] = await registry.RegisterAsync($"ts3-{name}", content.ToArray(), "image/png", cancellationToken);
                    }
                }

                Icons.Value = icons;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                _logger.Warning(ex, "Could not read the TeamSpeak icons from {Path}", archivePath);
            }
        }

        internal static string IconNameFor(ChannelClient client) => client.Status switch
        {
            ChannelClientStatus.Away => "away",
            ChannelClientStatus.OutputHardwareDisabled => "hardware_output_muted",
            ChannelClientStatus.OutputMuted => "output_muted",
            ChannelClientStatus.InputHardwareDisabled => "hardware_input_muted",
            ChannelClientStatus.InputMuted => "input_muted",
            ChannelClientStatus.LocallyMuted => "input_muted_local",
            ChannelClientStatus.Talking => client.IsChannelCommander ? "player_commander_on" : "player_on",
            _ => client.IsChannelCommander ? "player_commander_off" : "player_off",
        };

        private static string? FindIconArchive()
            => InstallDirectories()
                .Select(directory => Path.Combine(directory, "gfx", IconArchive))
                .FirstOrDefault(File.Exists);

        private static IEnumerable<string> InstallDirectories()
        {
            if (OperatingSystem.IsWindows())
            {
                foreach (var root in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
                {
                    using var key = root.OpenSubKey(@"SOFTWARE\TeamSpeak 3 Client");
                    if (key?.GetValue(null) is string directory && directory.Length > 0)
                    {
                        yield return directory;
                    }
                }

                yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "TeamSpeak 3 Client");
                yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TeamSpeak 3 Client");
            }
            else if (OperatingSystem.IsMacOS())
            {
                yield return "/Applications/TeamSpeak 3 Client.app/Contents/Resources";
            }
        }
    }
}
