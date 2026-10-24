using MacroDeck.Teamspeak3Plugin.Helper;
using MacroDeck.Teamspeak3Plugin.Model;
using MacroDeck.Teamspeak3Plugin.Service;
using MacroDeck.Teamspeak3Plugin.Widgets;
using NUnit.Framework;
using Serilog;

namespace MacroDeck.Teamspeak3Plugin.Tests;

[TestFixture]
public sealed class CurrentChannelWidgetTests
{
	private static ChannelClient Client(int id, string name, int talkPower = 0, bool talking = false, bool talker = false)
		=> new() { ClientId = id, Nickname = name, TalkPower = talkPower, IsTalking = talking, IsTalker = talker };

	[Test]
	public void Clients_follow_TeamSpeaks_order_of_talk_power_talker_and_name()
	{
		var clients = new[]
		{
			Client(1, "bravo"),
			Client(2, "Alpha"),
			Client(3, "zulu", talkPower: 50),
			Client(4, "charlie", talker: true),
		};

		var ordered = clients.Order(ChannelClient.TeamspeakOrder).Select(c => c.Nickname);

		Assert.That(ordered, Is.EqualTo(new[] { "zulu", "charlie", "Alpha", "bravo" }));
	}

	[Test]
	public void Everyone_who_fits_is_shown_in_TeamSpeaks_order()
	{
		var rows = ChannelWidgetRows.Select([Client(1, "b", talking: true), Client(2, "a")], 5);

		Assert.That(rows.Select(r => r.Client!.Nickname), Is.EqualTo(new[] { "a", "b" }));
	}

	[Test]
	public void Speakers_come_first_when_not_everyone_fits()
	{
		var clients = Enumerable.Range(1, 8).Select(i => Client(i, $"user{i}", talking: i is 6 or 8)).ToList();

		var rows = ChannelWidgetRows.Select(clients, 4);

		Assert.Multiple(() =>
		{
			Assert.That(rows.Take(3).Select(r => r.Client!.Nickname), Is.EqualTo(new[] { "user6", "user8", "user1" }));
			Assert.That(rows[3].Client, Is.Null);
			Assert.That(rows[3].HiddenCount, Is.EqualTo(5));
		});
	}

	[Test]
	public void Columns_split_the_rows_in_order()
	{
		var rows = ChannelWidgetRows.Select(Enumerable.Range(1, 4).Select(i => Client(i, $"u{i}")).ToList(), 10);

		Assert.That(ChannelWidgetRows.Column(rows, 1, 3).Select(r => r.Client!.Nickname), Is.EqualTo(new[] { "u4" }));
	}

	[TestCase(true, false, false, false, "Away")]
	[TestCase(false, true, false, false, "OutputMuted")]
	[TestCase(false, false, true, true, "InputMuted")]
	[TestCase(false, false, false, true, "Talking")]
	[TestCase(false, false, false, false, "Silent")]
	public void The_status_follows_TeamSpeaks_precedence(bool away, bool outputMuted, bool inputMuted, bool talking, string expected)
	{
		var client = Client(1, "a", talking: talking) with { IsAway = away, OutputMuted = outputMuted, InputMuted = inputMuted };

		Assert.That(client.Status.ToString(), Is.EqualTo(expected));
	}

	[Test]
	public void Missing_hardware_ranks_before_a_mute()
	{
		var client = Client(1, "a") with { HasOutputHardware = false, InputMuted = true };

		Assert.That(client.Status, Is.EqualTo(ChannelClientStatus.OutputHardwareDisabled));
	}

	[Test]
	public void A_channel_client_list_entry_is_read_with_unescaped_names()
	{
		var entries = Teamspeak3Response.Entries(
			"clid=4 cid=1 client_nickname=Muh\\sChy client_type=0 client_flag_talking=1 client_input_hardware=0|clid=5 cid=1 client_nickname=B\n"
			+ "error id=0 msg=ok");

		var client = new ChannelClient { ClientId = 4, Nickname = string.Empty }.With(entries[0]);

		Assert.Multiple(() =>
		{
			Assert.That(entries, Has.Count.EqualTo(2));
			Assert.That(client.Nickname, Is.EqualTo("Muh Chy"));
			Assert.That(client.IsTalking, Is.True);
			Assert.That(client.HasInputHardware, Is.False);
		});
	}

	[TestCase("a\\sb\\pc\\/d\\\\e", "a b|c/d\\e")]
	[TestCase("plain", "plain")]
	public void ClientQuery_escapes_are_undone(string escaped, string expected)
	{
		Assert.That(Teamspeak3Escaping.Unescape(escaped), Is.EqualTo(expected));
	}

	[Test]
	public void A_talk_status_notification_updates_the_client_without_a_query()
	{
		var state = new Teamspeak3ChannelState(new LoggerConfiguration().CreateLogger());
		state.Publish(new CurrentChannelSnapshot(1, 7, "Lobby", [Client(4, "a"), Client(5, "b")]));
		var changes = 0;
		state.Changed += (_, _) => changes++;

		state.Apply(Teamspeak3Notification.Parse("notifytalkstatuschange schandlerid=1 status=1 isreceivedwhisper=0 clid=5", 4));

		Assert.Multiple(() =>
		{
			Assert.That(state.Current!.Clients.Single(c => c.ClientId == 5).IsTalking, Is.True);
			Assert.That(changes, Is.EqualTo(1));
		});
	}

	[Test]
	public void A_notification_from_another_server_tab_is_ignored()
	{
		var state = new Teamspeak3ChannelState(new LoggerConfiguration().CreateLogger());
		state.Publish(new CurrentChannelSnapshot(1, 7, "Lobby", [Client(5, "b")]));

		state.Apply(Teamspeak3Notification.Parse("notifytalkstatuschange schandlerid=2 status=1 isreceivedwhisper=0 clid=5", 4));

		Assert.That(state.Current!.Clients.Single().IsTalking, Is.False);
	}

	[Test]
	public void A_client_update_changes_the_mute_indicators()
	{
		var state = new Teamspeak3ChannelState(new LoggerConfiguration().CreateLogger());
		state.Publish(new CurrentChannelSnapshot(1, 7, "Lobby", [Client(5, "b")]));

		state.Apply(Teamspeak3Notification.Parse("notifyclientupdated schandlerid=1 clid=5 client_output_muted=1", 4));

		Assert.That(state.Current!.Clients.Single().Status, Is.EqualTo(ChannelClientStatus.OutputMuted));
	}
}
