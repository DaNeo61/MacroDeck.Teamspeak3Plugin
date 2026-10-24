using MacroDeck.Plugin.Testing;
using MacroDeck.Teamspeak3Plugin.Service;
using NUnit.Framework;

namespace MacroDeck.Teamspeak3Plugin.Tests;

[TestFixture]
public sealed class MuteActionStateTests
{
	private static PluginTestHarness CreateHarness() =>
		PluginTestHarness.Create(builder =>
		{
			builder.Services.AddTeamspeak3Connection();
			builder
				.UseLocalization(Strings.LocalizationCatalog)
				.RegisterIntegration<PluginIntegration>();
		});

	[TestCase("input-mute")]
	[TestCase("output-mute")]
	public async Task A_mute_action_reports_unavailable_without_a_TeamSpeak_connection(string actionId)
	{
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var outcome = await harness.Actions.GetActionStateAsync(actionId, new Dictionary<string, object?>());

		Assert.That(outcome.Succeeded, Is.True);
		var snapshot = outcome.Data!.Value.GetRawText();
		Assert.Multiple(() =>
		{
			Assert.That(snapshot, Does.Contain("\"muted\""));
			Assert.That(snapshot, Does.Contain("\"unmuted\""));
			Assert.That(snapshot, Does.Contain("\"unavailable\""));
		});
	}

	[Test]
	public void An_own_mute_notification_carries_the_new_values()
	{
		var notification = Teamspeak3Notification.Parse("notifyclientupdated schandlerid=1 clid=5 client_input_muted=1 client_output_muted=0", 5);

		Assert.Multiple(() =>
		{
			Assert.That(notification.InputMuted, Is.True);
			Assert.That(notification.OutputMuted, Is.False);
		});
	}

	[Test]
	public void Another_clients_mute_notification_carries_no_values()
	{
		var notification = Teamspeak3Notification.Parse("notifyclientupdated schandlerid=1 clid=9 client_input_muted=1", 5);

		Assert.Multiple(() =>
		{
			Assert.That(notification.InputMuted, Is.Null);
			Assert.That(notification.OutputMuted, Is.Null);
		});
	}

	[Test]
	public void The_mute_mode_options_are_translated_to_German()
	{
		Assert.That(Strings.LocalizationCatalog.KeysOf("de"), Does.Contain("Actions.MuteMode.Toggle"));
	}
}
