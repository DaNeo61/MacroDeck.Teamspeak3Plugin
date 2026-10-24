using MacroDeck.Teamspeak3Plugin.Model;
using MacroDeck.Teamspeak3Plugin.Service;
using NUnit.Framework;

namespace MacroDeck.Teamspeak3Plugin.Tests;

[TestFixture]
public sealed class Teamspeak3NotificationTests
{
	private const int OwnClientId = 5;

	[Test]
	public void An_own_input_mute_change_refreshes_the_input_state()
	{
		var notification = Teamspeak3Notification.Parse("notifyclientupdated schandlerid=1 clid=5 client_input_muted=1", OwnClientId);

		Assert.That(notification.AffectedVariableIds, Is.EquivalentTo(new[] { VariableIds.InputState }));
	}

	[Test]
	public void An_own_output_mute_change_refreshes_the_output_state()
	{
		var notification = Teamspeak3Notification.Parse("notifyclientupdated schandlerid=1 clid=5 client_output_muted=0", OwnClientId);

		Assert.That(notification.AffectedVariableIds, Is.EquivalentTo(new[] { VariableIds.OutputState }));
	}

	[Test]
	public void Another_clients_mute_change_is_ignored()
	{
		var notification = Teamspeak3Notification.Parse("notifyclientupdated schandlerid=1 clid=9 client_input_muted=1", OwnClientId);

		Assert.That(notification.AffectedVariableIds, Is.Empty);
	}

	[Test]
	public void An_update_without_mute_fields_is_ignored()
	{
		var notification = Teamspeak3Notification.Parse("notifyclientupdated schandlerid=1 clid=5 client_away=1", OwnClientId);

		Assert.That(notification.AffectedVariableIds, Is.Empty);
	}

	[Test]
	public void Any_client_counts_while_the_own_id_is_unknown()
	{
		var notification = Teamspeak3Notification.Parse("notifyclientupdated schandlerid=1 clid=9 client_input_muted=1", -1);

		Assert.That(notification.AffectedVariableIds, Is.EquivalentTo(new[] { VariableIds.InputState }));
	}

	[Test]
	public void An_own_move_in_a_later_entry_refreshes_the_channel()
	{
		var notification = Teamspeak3Notification.Parse("notifyclientmoved schandlerid=1 ctid=3 reasonid=0 clid=7|clid=5", OwnClientId);

		Assert.That(notification.AffectedVariableIds, Is.EquivalentTo(new[] { VariableIds.CurrentChannel }));
	}

	[Test]
	public void Another_clients_move_is_ignored()
	{
		var notification = Teamspeak3Notification.Parse("notifyclientmoved schandlerid=1 ctid=3 reasonid=0 clid=7", OwnClientId);

		Assert.That(notification.AffectedVariableIds, Is.Empty);
	}

	[TestCase("notifychanneledited schandlerid=1 cid=3 reasonid=10 channel_name=Lobby")]
	[TestCase("notifychanneldeleted schandlerid=1 cid=3 invokerid=2")]
	public void A_channel_change_refreshes_the_channel(string line)
	{
		var notification = Teamspeak3Notification.Parse(line, OwnClientId);

		Assert.That(notification.AffectedVariableIds, Is.EquivalentTo(new[] { VariableIds.CurrentChannel }));
	}

	[TestCase("notifyconnectstatuschange schandlerid=1 status=connection_established error=0")]
	[TestCase("notifycurrentserverconnectionchanged schandlerid=2")]
	public void A_server_connection_change_refreshes_everything(string line)
	{
		var notification = Teamspeak3Notification.Parse(line, OwnClientId);

		Assert.Multiple(() =>
		{
			Assert.That(notification.AffectedVariableIds, Is.EquivalentTo(VariableIds.All));
			Assert.That(notification.ServerConnectionChanged, Is.True);
		});
	}

	[TestCase("")]
	[TestCase("error id=0 msg=ok")]
	[TestCase("clid=5 cid=3")]
	[TestCase("TS3 Client")]
	public void A_non_notification_line_is_ignored(string line)
	{
		var notification = Teamspeak3Notification.Parse(line, OwnClientId);

		Assert.That(notification.AffectedVariableIds, Is.Empty);
	}
}
