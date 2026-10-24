using System.Text.Json;
using KeystoneDigital.RazerController.Actions;
using KeystoneDigital.RazerController.Synapse;
using NUnit.Framework;

namespace KeystoneDigital.RazerController.Tests.Synapse;

[TestFixture]
public sealed class SynapseDpiTests
{
	// As Synapse 4 stores it: five slots, three in use, slot 2 active.
	private const string Slots = """[{"x":1000,"y":1000,"visible":true,"index":0},{"x":1200,"y":1200,"visible":true,"index":1},{"x":1400,"y":1400,"visible":true,"index":2},{"x":16000,"y":16000,"visible":false,"index":3},{"x":16000,"y":16000,"visible":false,"index":4}]""";

	private static DpiStageSet Stages(int active) => new(active, true, [1, 2, 3], Fixture.Parse(Slots));

	private static RazerDeviceInfo Mouse(DpiStageSet? stages) =>
		new("SERIAL-1", 103, "Razer Naga Trinity", DeviceCategory.Mouse, "usb_5426_103_{X}_mw", [], null, null, null, null, stages);

	[Test]
	public void The_parser_reads_the_active_profiles_stages()
	{
		var profile = """{"guid":"g1","name":"Default","dpiStages":{"stages":""" + Slots + ""","active":2,"enable":true,"count":3}}""";
		var device = JsonSerializer.Serialize(new { activeProfile = "g1", profiles = new[] { JsonDocument.Parse(profile).RootElement }, deviceMetadatas = new Dictionary<string, object> { ["SERIAL-1"] = new { name = "Razer Naga Trinity" } }, category = "MOUSE" });
		var snapshot = JsonSerializer.Serialize(new
		{
			version = "4.0.827",
			windows = new[] { new { productId = 103, title = "usb_5426_103_{X}_mw" } },
			storage = new { connectedDeviceInfo = "[]", devices = new Dictionary<string, string> { ["103"] = device } },
		});

		var stages = SynapseCatalogParser.Parse(Fixture.Parse(snapshot)).Devices.Single().DpiStages;

		Assert.That(stages, Is.Not.Null);
		Assert.That((stages!.Active, stages.Enable), Is.EqualTo((2, true)));
		Assert.That(stages.Visible, Is.EqualTo(new[] { 1, 2, 3 }));
		Assert.That(stages.Raw.GetArrayLength(), Is.EqualTo(5));
	}

	[Test]
	public void Stage_numbers_count_only_the_stages_in_use()
	{
		Assert.That(Stages(1).SlotOf(3), Is.EqualTo(3));
		Assert.That(Stages(1).SlotOf(4), Is.Null);
		Assert.That(Stages(1).SlotOf(0), Is.Null);
	}

	[Test]
	public void Stepping_stops_at_either_end()
	{
		Assert.That(Stages(2).Step(up: true), Is.EqualTo(3));
		Assert.That(Stages(2).Step(up: false), Is.EqualTo(1));
		Assert.That(Stages(3).Step(up: true), Is.Null);
		Assert.That(Stages(1).Step(up: false), Is.Null);
	}

	[Test]
	public void The_plan_sends_what_Synapses_window_sends_and_updates_the_window()
	{
		var plan = SynapseDpi.Plan(Mouse(Stages(1)), stages => stages.SlotOf(3));
		var payload = JsonSerializer.SerializeToElement(plan.Payload);

		Assert.That(plan.EventType, Is.EqualTo("ON_STAGES_CHANGE"));
		Assert.That(payload.GetProperty("active").GetInt32(), Is.EqualTo(3));
		Assert.That(payload.GetProperty("enableStages").GetBoolean(), Is.True);
		Assert.That(payload.GetProperty("stages").GetArrayLength(), Is.EqualTo(5));
		Assert.That(plan.WindowUpdate?.Type, Is.EqualTo("MW_SET_PERFORMANCE_TO_UI"));
		Assert.That(plan.IsApplied!(Mouse(Stages(3))), Is.True);
		Assert.That(plan.IsApplied!(Mouse(Stages(1))), Is.False);
	}

	[Test]
	public void The_active_stage_or_no_stage_is_no_change()
	{
		Assert.That(SynapseDpi.Plan(Mouse(Stages(2)), stages => stages.SlotOf(2)).Shortcut, Is.EqualTo(CommandOutcome.NoChange));
		Assert.That(SynapseDpi.Plan(Mouse(Stages(3)), stages => stages.Step(up: true)).Shortcut, Is.EqualTo(CommandOutcome.NoChange));
	}

	[Test]
	public void A_command_with_a_window_update_also_posts_to_the_ui_channel()
	{
		var script = InspectorScripts.Command("usb_5426_103_{X}_mw", "ON_STAGES_CHANGE", new { active = 2 }, new WindowUpdate("MW_SET_PERFORMANCE_TO_UI", new { active = 2 }));

		Assert.That(script, Does.Contain("_ui").And.Contain("reduxStoreUpdate").And.Contain("timerTick"));
		Assert.That(script, Does.Contain("\"MW_SET_PERFORMANCE_TO_UI\""));
	}

	[Test]
	public void The_stage_list_shows_each_stage_in_use_with_its_dpi()
	{
		var options = DpiStageAction.Options(Stages(1));

		Assert.That(options.Select(o => o.Value), Is.EqualTo(new[] { "1", "2", "3" }));
		Assert.That(options[1].Label.ToString(), Does.Contain("DpiStage.Option("), "the label with the stage's DPI");
	}

	[Test]
	public void Without_synapse_the_stage_list_offers_stages_1_to_5() =>
		Assert.That(DpiStageAction.Options(null).Select(o => o.Value), Is.EqualTo(new[] { "1", "2", "3", "4", "5" }));

	[Test]
	public void Cycling_lighting_wraps_and_starts_from_the_first_unknown_effect()
	{
		Assert.That(CycleLightingAction.Next([1, 2, 3, 4], 2), Is.EqualTo(3));
		Assert.That(CycleLightingAction.Next([1, 2, 3, 4], 4), Is.EqualTo(1));
		Assert.That(CycleLightingAction.Next([1, 2, 3, 4], 7), Is.EqualTo(1));
		Assert.That(CycleLightingAction.Next([1, 2, 3, 4], null), Is.EqualTo(1));
	}

	[Test]
	public void No_ticked_effects_cycles_the_common_ones() =>
		Assert.That(CycleLightingAction.Cycle(new Dictionary<string, object>()), Is.EqualTo(new[] { 1, 2, 3, 4 }));
}

[TestFixture]
public sealed class RestartSynapseTests
{
	private const string Entry = @"""C:\Program Files\Razer\RazerAppEngine\RazerAppEngine.exe"" --autoStart=1";

	[Test]
	public void With_control_on_the_restart_puts_the_flag_back() =>
		Assert.That(RestartSynapseAction.CommandLineFor(Entry, controlOn: true), Does.Contain(KeystoneDigital.RazerController.Settings.InspectorConfigFlow.InspectFlag));

	[Test]
	public void With_control_off_the_entry_is_used_as_it_is() =>
		Assert.That(RestartSynapseAction.CommandLineFor(Entry, controlOn: false), Is.EqualTo(Entry));
}

[TestFixture]
public sealed class SynapsePopupTests
{
	private const string Slots = """[{"x":1000,"y":1000,"visible":true},{"x":1200,"y":1200,"visible":true},{"x":16000,"y":16000,"visible":false}]""";

	[Test]
	public void The_active_stage_number_and_the_mouse_maximum_come_from_the_slots()
	{
		var stages = new DpiStageSet(2, true, [1, 2], Fixture.Parse(Slots));

		Assert.That(stages.ActiveNumber, Is.EqualTo(2));
		Assert.That(stages.MaxDpi, Is.EqualTo(16000));
	}

	[Test]
	public void Popup_text_stays_inert()
	{
		const string hostile = "x\"); require('child_process').exec('calc'); (\"";
		var script = InspectorScripts.Popup("usb_5426_103_{X}_mw", hostile, hostile, 1200, 8);

		var afterTarget = script.Split("const target = ", 2)[1];
		var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(afterTarget));
		var target = JsonElement.ParseValue(ref reader);

		Assert.That(target.GetProperty("header").GetString(), Is.EqualTo(hostile));
		Assert.That(target.GetProperty("payload").GetProperty("dpiValue").GetInt32(), Is.EqualTo(1200));
		Assert.That(afterTarget[(int)reader.BytesConsumed..].TrimStart(), Does.StartWith(";"));
	}
}

[TestFixture]
public sealed class PopupPreferenceTests
{
	[Test]
	public void Popups_are_on_until_turned_off_and_the_choice_survives()
	{
		var directory = Path.Combine(Path.GetTempPath(), "razer-popups-" + Guid.NewGuid().ToString("N"));
		try
		{
			Assert.That(new FilePopupPreference(directory).Enabled, Is.True);
			new FilePopupPreference(directory).Enabled = false;
			Assert.That(new FilePopupPreference(directory).Enabled, Is.False);
		}
		finally
		{
			if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
		}
	}
}
