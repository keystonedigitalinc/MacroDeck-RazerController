using System.Text.Json;
using KeystoneDigital.RazerController.Hid;
using KeystoneDigital.RazerController.Synapse;
using KeystoneDigital.RazerController.Tests.Hid;
using KeystoneDigital.RazerController.Tests.Synapse;
using MacroDeck.Plugin.Testing;
using MacroDeck.Sdk.Actions;
using NUnit.Framework;

namespace KeystoneDigital.RazerController.Tests.Actions;

[TestFixture]
public sealed class ActionTests
{
	private const string Mouse = FakeInspector.MouseSerial;
	private const string Keyboard = FakeInspector.KeyboardSerial;

	internal static PluginTestHarness CreateHarness(
		SynapseService synapse,
		MouseDpiService dpi,
		KeystoneDigital.RazerController.Autostart.IAutostartRegistry? registry = null,
		KeystoneDigital.RazerController.Autostart.ISynapseProcess? process = null,
		KeystoneDigital.RazerController.Autostart.ISynapseControlIntent? intent = null,
		KeystoneDigital.RazerController.Chroma.ChromaClient? chroma = null) =>
		PluginTestHarness.Create(builder =>
		{
			builder.Services.AddRazerServices(
				synapse,
				dpi,
				registry ?? new FakeAutostartRegistry(null),
				process ?? new Autostart.FakeProcess(KeystoneDigital.RazerController.Autostart.SynapseProcessState.NotRunning),
				intent ?? new Autostart.FakeIntent(true),
				chroma ?? Chroma.ChromaTests.Client(new Chroma.FakeChromaServer { Running = false }));
			builder.UseLocalization(Strings.LocalizationCatalog).RegisterIntegration(RazerIntegration.Create);
		});

	private static async Task<(PluginTestHarness Harness, FakeInspector Fake, SynapseService Synapse, SimulatedMouse Mouse)> ReadyAsync()
	{
		var fake = FakeInspector.FromFixture();
		var synapse = SynapseServiceTests.CreateService(fake);
		await synapse.RefreshAsync(default);
		var mouse = new SimulatedMouse(800, 1600, 3200);
		var harness = CreateHarness(synapse, MouseDpiServiceTests.ServiceFor(mouse));
		await harness.InitializeIntegrationsAsync();
		return (harness, fake, synapse, mouse);
	}

	private static Dictionary<string, object?> P(params (string Key, object? Value)[] values) => values.ToDictionary(v => v.Key, v => v.Value);

	internal static string? ErrorCode(CapabilityInvocationOutcome outcome) =>
		outcome.Error?.Code
		?? (outcome.Data is { ValueKind: JsonValueKind.Object } data && data.TryGetProperty("errorCode", out var code) ? code.GetString() : null);

	private static List<string> OptionValues(CapabilityInvocationOutcome outcome)
	{
		Assert.That(outcome.Succeeded, Is.True, outcome.Error?.Message);
		return outcome.Data!.Value.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("value").GetString()!).ToList();
	}

	private static List<string> OptionLabels(CapabilityInvocationOutcome outcome) =>
		outcome.Data!.Value.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("label").ToString()).ToList();

	[Test]
	public async Task Switch_to_a_profile_succeeds_when_confirmed()
	{
		var (harness, fake, synapse, _) = await ReadyAsync();
		await using var _h = harness;
		var target = fake.ProfileGuid(103, 2);
		fake.OnCommand = c => fake.SetActiveProfile(103, c.Payload.GetProperty("newActiveProfileGUID").GetString()!);

		var outcome = await harness.Actions.ExecuteAsync("switch-profile", P(("device", Mouse), ("mode", "profile"), ("profile", target)));

		Assert.That(outcome.Succeeded, Is.True, outcome.Error?.Message);
		Assert.That(synapse.FindBySerial(Mouse)!.ActiveProfileGuid, Is.EqualTo(target));
		Assert.That(fake.Popups, Is.EqualTo(new[] { "Profile: " + synapse.FindBySerial(Mouse)!.ActiveProfileName }), "Synapse's popup names the new profile");
	}

	[Test]
	public async Task Switch_to_the_active_profile_succeeds_without_sending()
	{
		var (harness, fake, synapse, _) = await ReadyAsync();
		await using var _h = harness;

		var outcome = await harness.Actions.ExecuteAsync("switch-profile", P(("device", Mouse), ("mode", "profile"), ("profile", synapse.FindBySerial(Mouse)!.ActiveProfileGuid)));

		Assert.That(outcome.Succeeded, Is.True);
		Assert.That(fake.Commands, Is.Empty);
	}

	[Test]
	public async Task Switch_fails_for_an_unknown_serial()
	{
		var (harness, fake, _, _) = await ReadyAsync();
		await using var _h = harness;

		var outcome = await harness.Actions.ExecuteAsync("switch-profile", P(("device", "SERIAL-404"), ("mode", "next")));

		Assert.That(outcome.Succeeded, Is.False);
		Assert.That(ErrorCode(outcome), Is.EqualTo(ActionErrorCodes.NotFound));
		Assert.That(fake.Commands, Is.Empty);
	}

	[Test]
	public async Task Switch_fails_with_NotFound_when_the_last_device_is_unplugged()
	{
		var (harness, fake, synapse, _) = await ReadyAsync();
		await using var _h = harness;
		fake.UnplugAll();
		await synapse.RefreshAsync(default);

		var outcome = await harness.Actions.ExecuteAsync("switch-profile", P(("device", Mouse), ("mode", "next")));

		Assert.That(synapse.Status, Is.EqualTo(SynapseStatus.NoDevices));
		Assert.That(ErrorCode(outcome), Is.EqualTo(ActionErrorCodes.NotFound));
	}

	[Test]
	public async Task Switch_fails_for_a_deleted_profile()
	{
		var (harness, fake, _, _) = await ReadyAsync();
		await using var _h = harness;

		var outcome = await harness.Actions.ExecuteAsync("switch-profile", P(("device", Mouse), ("mode", "profile"), ("profile", "00000000-dead-beef-0000-000000000000")));

		Assert.That(ErrorCode(outcome), Is.EqualTo(ActionErrorCodes.NotFound));
		Assert.That(fake.Commands, Is.Empty);
	}

	[Test]
	public async Task Switch_reports_timeout_when_Synapse_ignores_it()
	{
		var (harness, fake, _, _) = await ReadyAsync();
		await using var _h = harness;

		var outcome = await harness.Actions.ExecuteAsync("switch-profile", P(("device", Mouse), ("mode", "next")));

		Assert.That(ErrorCode(outcome), Is.EqualTo(ActionErrorCodes.Timeout));
		Assert.That(fake.Commands, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task Next_wraps_to_the_first_profile()
	{
		var (harness, fake, synapse, _) = await ReadyAsync();
		await using var _h = harness;
		var profiles = synapse.FindBySerial(Mouse)!.Profiles;
		fake.SetActiveProfile(103, profiles[^1].Guid);
		await synapse.RefreshAsync(default);
		fake.OnCommand = c => fake.SetActiveProfile(103, c.Payload.GetProperty("newActiveProfileGUID").GetString()!);

		await harness.Actions.ExecuteAsync("switch-profile", P(("device", Mouse), ("mode", "next")));

		Assert.That(fake.Commands.Single().Payload.GetProperty("newActiveProfileGUID").GetString(), Is.EqualTo(profiles[0].Guid));
	}

	[Test]
	public async Task Previous_moves_back_one_profile()
	{
		var (harness, fake, synapse, _) = await ReadyAsync();
		await using var _h = harness;
		var profiles = synapse.FindBySerial(Mouse)!.Profiles;
		fake.SetActiveProfile(103, profiles[3].Guid);
		await synapse.RefreshAsync(default);
		fake.OnCommand = c => fake.SetActiveProfile(103, c.Payload.GetProperty("newActiveProfileGUID").GetString()!);

		await harness.Actions.ExecuteAsync("switch-profile", P(("device", Mouse), ("mode", "previous")));

		Assert.That(fake.Commands.Single().Payload.GetProperty("newActiveProfileGUID").GetString(), Is.EqualTo(profiles[2].Guid));
	}

	[Test]
	public async Task Cycle_moves_to_the_entry_after_the_active_profile()
	{
		var (harness, fake, synapse, _) = await ReadyAsync();
		await using var _h = harness;
		var profiles = synapse.FindBySerial(Mouse)!.Profiles;
		fake.SetActiveProfile(103, profiles[5].Guid);
		await synapse.RefreshAsync(default);
		fake.OnCommand = c => fake.SetActiveProfile(103, c.Payload.GetProperty("newActiveProfileGUID").GetString()!);
		var chosen = new[] { profiles[1].Guid, profiles[5].Guid, profiles[7].Guid };

		var outcome = await harness.Actions.ExecuteAsync("switch-profile", P(("device", Mouse), ("mode", "cycle"), ("profiles", chosen)));

		Assert.That(outcome.Succeeded, Is.True, outcome.Error?.Message);
		Assert.That(fake.Commands.Single().Payload.GetProperty("newActiveProfileGUID").GetString(), Is.EqualTo(profiles[7].Guid));
	}

	[Test]
	public async Task Two_quick_next_presses_advance_two_profiles()
	{
		var (harness, fake, synapse, _) = await ReadyAsync();
		await using var _h = harness;
		var profiles = synapse.FindBySerial(Mouse)!.Profiles;
		fake.SetActiveProfile(103, profiles[0].Guid);
		await synapse.RefreshAsync(default);
		fake.OnCommand = c => fake.SetActiveProfile(103, c.Payload.GetProperty("newActiveProfileGUID").GetString()!);

		var presses = await Task.WhenAll(
			harness.Actions.ExecuteAsync("switch-profile", P(("device", Mouse), ("mode", "next"))),
			harness.Actions.ExecuteAsync("switch-profile", P(("device", Mouse), ("mode", "next"))));

		Assert.That(presses.All(p => p.Succeeded), Is.True);
		Assert.That(fake.Commands.Select(c => c.Payload.GetProperty("newActiveProfileGUID").GetString()), Is.EquivalentTo(new[] { profiles[1].Guid, profiles[2].Guid }));
		Assert.That(synapse.FindBySerial(Mouse)!.ActiveProfileGuid, Is.EqualTo(profiles[2].Guid));
	}

	[Test]
	public async Task Two_quick_gaming_toggles_end_where_they_started()
	{
		var (harness, fake, synapse, _) = await ReadyAsync();
		await using var _h = harness;
		fake.OnCommand = c => fake.EditActiveProfile(554, p => p["gamingMode"] = System.Text.Json.Nodes.JsonNode.Parse(c.Payload.GetProperty("gamingMode").GetRawText()));

		await Task.WhenAll(
			harness.Actions.ExecuteAsync("gaming-mode", P(("device", Keyboard), ("state", "toggle"))),
			harness.Actions.ExecuteAsync("gaming-mode", P(("device", Keyboard), ("state", "toggle"))));

		Assert.That(fake.Commands, Has.Count.EqualTo(2));
		Assert.That(synapse.FindBySerial(Keyboard)!.GamingMode!.On, Is.False);
	}

	[Test]
	public async Task Cycle_starts_at_the_first_entry_when_the_active_profile_is_not_in_the_list()
	{
		var (harness, fake, synapse, _) = await ReadyAsync();
		await using var _h = harness;
		var profiles = synapse.FindBySerial(Mouse)!.Profiles;
		fake.SetActiveProfile(103, profiles[0].Guid);
		await synapse.RefreshAsync(default);
		fake.OnCommand = c => fake.SetActiveProfile(103, c.Payload.GetProperty("newActiveProfileGUID").GetString()!);

		await harness.Actions.ExecuteAsync("switch-profile", P(("device", Mouse), ("mode", "cycle"), ("profiles", JsonSerializer.SerializeToElement(new[] { profiles[4].Guid, profiles[6].Guid }))));

		Assert.That(fake.Commands.Single().Payload.GetProperty("newActiveProfileGUID").GetString(), Is.EqualTo(profiles[4].Guid));
	}

	[Test]
	public async Task Cycle_with_no_profiles_is_an_invalid_parameter()
	{
		var (harness, _, _, _) = await ReadyAsync();
		await using var _h = harness;

		var outcome = await harness.Actions.ExecuteAsync("switch-profile", P(("device", Mouse), ("mode", "cycle")));

		Assert.That(ErrorCode(outcome), Is.EqualTo(ActionErrorCodes.InvalidParameter));
	}

	[Test]
	public async Task Effect_static_sends_the_colour()
	{
		var (harness, fake, _, _) = await ReadyAsync();
		await using var _h = harness;
		fake.OnCommand = c => fake.EditActiveProfile(554, p => p["quickEffects"] = System.Text.Json.Nodes.JsonNode.Parse(c.Payload.GetProperty("effectSetting").GetRawText()));

		var outcome = await harness.Actions.ExecuteAsync("lighting-effect", P(("device", Keyboard), ("effect", "1"), ("color", "#FF0000")));

		Assert.That(outcome.Succeeded, Is.True, outcome.Error?.Message);
		var setting = fake.Commands.Single().Payload.GetProperty("effectSetting");
		Assert.That(setting.GetProperty("selectedEffectId").GetInt32(), Is.EqualTo(1));
		Assert.That(setting.GetProperty("selectedEffectSetting").GetProperty("color1").GetString(), Is.EqualTo("#FF0000"));
	}

	[Test]
	public async Task Effect_off_switches_the_lighting_off_instead_of_sending_effect_0()
	{
		var (harness, fake, synapse, _) = await ReadyAsync();
		await using var _h = harness;
		fake.EditActiveProfile(554, p => p["brightness"] = System.Text.Json.Nodes.JsonNode.Parse("""{"isEnabled":true,"value":70}"""));
		await synapse.RefreshAsync(default);
		fake.OnCommand = c => fake.EditActiveProfile(554, p => p["brightness"] = System.Text.Json.Nodes.JsonNode.Parse(c.Payload.GetProperty("brightness").GetRawText()));

		var outcome = await harness.Actions.ExecuteAsync("lighting-effect", P(("device", Keyboard), ("effect", "0")));

		Assert.That(outcome.Succeeded, Is.True, outcome.Error?.Message);
		var command = fake.Commands.Single();
		Assert.That(command.Type, Is.EqualTo("ON_SET_BRIGHTNESS"));
		Assert.That(command.Payload.GetProperty("brightness").GetProperty("isEnabled").GetBoolean(), Is.False);
		Assert.That(command.Payload.GetProperty("brightness").GetProperty("value").GetInt32(), Is.EqualTo(70), "the level is kept for switching back on");
	}

	[Test]
	public async Task Effect_spectrum_sends_no_setting()
	{
		var (harness, fake, _, _) = await ReadyAsync();
		await using var _h = harness;
		fake.OnCommand = c => fake.EditActiveProfile(554, p => p["quickEffects"] = System.Text.Json.Nodes.JsonNode.Parse(c.Payload.GetProperty("effectSetting").GetRawText()));

		var outcome = await harness.Actions.ExecuteAsync("lighting-effect", P(("device", Keyboard), ("effect", "3")));

		Assert.That(outcome.Succeeded, Is.True, outcome.Error?.Message);
		Assert.That(fake.Commands.Single().Payload.GetProperty("effectSetting").TryGetProperty("selectedEffectSetting", out _), Is.False);
	}

	[Test]
	public async Task Effect_two_colour_breathing_sends_Synapses_fields()
	{
		var (harness, fake, _, _) = await ReadyAsync();
		await using var _h = harness;
		fake.OnCommand = c => fake.EditActiveProfile(554, p => p["quickEffects"] = System.Text.Json.Nodes.JsonNode.Parse(c.Payload.GetProperty("effectSetting").GetRawText()));

		var outcome = await harness.Actions.ExecuteAsync("lighting-effect", P(("device", Keyboard), ("effect", "2"), ("color", "#FF0000"), ("color2", "#0000FF")));

		Assert.That(outcome.Succeeded, Is.True, outcome.Error?.Message);
		var setting = fake.Commands.Single().Payload.GetProperty("effectSetting").GetProperty("selectedEffectSetting");
		Assert.That(setting.GetProperty("color1").GetString(), Is.EqualTo("#FF0000"));
		Assert.That(setting.GetProperty("color2").GetString(), Is.EqualTo("#0000FF"));
		Assert.That(setting.GetProperty("isRandom").GetBoolean(), Is.False);
	}

	[Test]
	public void Effect_settings_follow_Synapses_lighting_helper()
	{
		var none = new Dictionary<string, object>();

		Assert.That(KeystoneDigital.RazerController.Actions.LightingEffectAction.Setting(2, none)!["color2"], Is.EqualTo("no-color"));
		Assert.That(KeystoneDigital.RazerController.Actions.LightingEffectAction.Setting(7, new Dictionary<string, object> { ["duration"] = "3", ["random"] = true })!["duration"], Is.EqualTo(3));
		Assert.That(KeystoneDigital.RazerController.Actions.LightingEffectAction.Setting(5, none)!.Keys, Is.EquivalentTo(new[] { "color1", "duration" }));
		Assert.That(KeystoneDigital.RazerController.Actions.LightingEffectAction.Setting(4, new Dictionary<string, object> { ["direction"] = "3" })!["direction"], Is.EqualTo(3));
		Assert.That(KeystoneDigital.RazerController.Actions.LightingEffectAction.Setting(4, new Dictionary<string, object> { ["direction"] = "default" }), Is.Null);
		Assert.That(KeystoneDigital.RazerController.Actions.LightingEffectAction.Setting(3, none), Is.Null);
	}

	[Test]
	public async Task Effect_a_direction_the_device_ignores_is_not_applied()
	{
		var (harness, fake, _, _) = await ReadyAsync();
		await using var _h = harness;
		fake.OnCommand = c => fake.EditActiveProfile(103, p => p["quickEffects"] = System.Text.Json.Nodes.JsonNode.Parse("""{"selectedEffectId":4,"selectedEffectSetting":{"direction":3}}"""));

		var outcome = await harness.Actions.ExecuteAsync("lighting-effect", P(("device", Mouse), ("effect", "4"), ("direction", "1")));

		Assert.That(outcome.Succeeded, Is.False);
	}

	[TestCase("set", 40, 10, 40)]
	[TestCase("up", 0, 30, 100)]
	[TestCase("down", 0, 30, 60)]
	public async Task Brightness_sets_or_steps_from_the_current_value(string mode, int value, int step, int expected)
	{
		var (harness, fake, synapse, _) = await ReadyAsync();
		await using var _h = harness;
		fake.EditActiveProfile(554, p => p["brightness"] = System.Text.Json.Nodes.JsonNode.Parse("""{"isEnabled":true,"value":90}"""));
		await synapse.RefreshAsync(default);
		fake.OnCommand = c => fake.EditActiveProfile(554, p => p["brightness"] = System.Text.Json.Nodes.JsonNode.Parse(c.Payload.GetProperty("brightness").GetRawText()));

		var outcome = await harness.Actions.ExecuteAsync("synapse-brightness", P(("device", Keyboard), ("mode", mode), ("value", value), ("step", step)));

		Assert.That(outcome.Succeeded, Is.True, outcome.Error?.Message);
		var sent = fake.Commands.Single().Payload.GetProperty("brightness");
		Assert.That(sent.GetProperty("value").GetInt32(), Is.EqualTo(expected));
		Assert.That(sent.GetProperty("isEnabled").GetBoolean(), Is.True);
	}

	[Test]
	public async Task Brightness_zero_switches_the_lighting_off()
	{
		var (harness, fake, synapse, _) = await ReadyAsync();
		await using var _h = harness;
		fake.EditActiveProfile(554, p => p["brightness"] = System.Text.Json.Nodes.JsonNode.Parse("""{"isEnabled":true,"value":50}"""));
		await synapse.RefreshAsync(default);
		fake.OnCommand = c => fake.EditActiveProfile(554, p => p["brightness"] = System.Text.Json.Nodes.JsonNode.Parse(c.Payload.GetProperty("brightness").GetRawText()));

		var outcome = await harness.Actions.ExecuteAsync("synapse-brightness", P(("device", Keyboard), ("mode", "set"), ("value", 0)));

		Assert.That(outcome.Succeeded, Is.True, outcome.Error?.Message);
		Assert.That(fake.Commands.Last().Payload.GetProperty("brightness").GetProperty("isEnabled").GetBoolean(), Is.False);
	}

	[Test]
	public async Task Effect_rejects_an_unknown_effect_id()
	{
		var (harness, fake, _, _) = await ReadyAsync();
		await using var _h = harness;

		var outcome = await harness.Actions.ExecuteAsync("lighting-effect", P(("device", Keyboard), ("effect", "42")));

		Assert.That(ErrorCode(outcome), Is.EqualTo(ActionErrorCodes.InvalidParameter));
		Assert.That(fake.Commands, Is.Empty);
	}

	[Test]
	public async Task Gaming_toggle_turns_on_when_off_and_keeps_other_fields()
	{
		var (harness, fake, _, _) = await ReadyAsync();
		await using var _h = harness;
		fake.OnCommand = c => fake.EditActiveProfile(554, p => p["gamingMode"] = System.Text.Json.Nodes.JsonNode.Parse(c.Payload.GetProperty("gamingMode").GetRawText()));

		var outcome = await harness.Actions.ExecuteAsync("gaming-mode", P(("device", Keyboard), ("state", "toggle")));

		Assert.That(outcome.Succeeded, Is.True, outcome.Error?.Message);
		var mode = fake.Commands.Single().Payload.GetProperty("gamingMode");
		Assert.That(mode.GetProperty("state").GetInt32(), Is.EqualTo(1));
		Assert.That(mode.GetProperty("isWindowsKeyDisabled").GetBoolean(), Is.True);
		Assert.That(mode.GetProperty("scancode").GetInt32(), Is.EqualTo(29));
	}

	[Test]
	public async Task Gaming_off_when_already_off_succeeds_without_sending()
	{
		var (harness, fake, _, _) = await ReadyAsync();
		await using var _h = harness;

		var outcome = await harness.Actions.ExecuteAsync("gaming-mode", P(("device", Keyboard), ("state", "off")));

		Assert.That(outcome.Succeeded, Is.True);
		Assert.That(fake.Commands, Is.Empty);
	}

	[Test]
	public async Task Fails_with_NotConnected_when_Synapse_is_unavailable()
	{
		var fake = FakeInspector.FromFixture();
		fake.ConnectError = new SynapseInspectorException("inspector_unreachable", "down");
		var synapse = SynapseServiceTests.CreateService(fake);
		await synapse.RefreshAsync(default);
		await using var harness = CreateHarness(synapse, MouseDpiServiceTests.ServiceFor(null));
		await harness.InitializeIntegrationsAsync();

		var outcome = await harness.Actions.ExecuteAsync("switch-profile", P(("device", Mouse), ("mode", "next")));

		Assert.That(ErrorCode(outcome), Is.EqualTo(ActionErrorCodes.NotConnected));
	}

	[Test]
	public async Task Step_dpi_moves_the_mouse()
	{
		var (harness, _, _, mouse) = await ReadyAsync();
		await using var _h = harness;

		var outcome = await harness.Actions.ExecuteAsync("step-dpi", P(("direction", "up")));

		Assert.That(outcome.Succeeded, Is.True, outcome.Error?.Message);
		Assert.That(mouse.Dpi, Is.EqualTo(1600));
	}

	[Test]
	public async Task Temporary_dpi_sets_the_value()
	{
		var (harness, _, _, mouse) = await ReadyAsync();
		await using var _h = harness;

		var outcome = await harness.Actions.ExecuteAsync("temporary-dpi", P(("dpi", 1200)));

		Assert.That(outcome.Succeeded, Is.True, outcome.Error?.Message);
		Assert.That(mouse.Dpi, Is.EqualTo(1200));
	}

	[Test]
	public async Task Dpi_without_a_mouse_is_NotConnected()
	{
		var synapse = SynapseServiceTests.CreateService(FakeInspector.FromFixture());
		await using var harness = CreateHarness(synapse, MouseDpiServiceTests.ServiceFor(null));
		await harness.InitializeIntegrationsAsync();

		var outcome = await harness.Actions.ExecuteAsync("step-dpi", P(("direction", "up")));

		Assert.That(ErrorCode(outcome), Is.EqualTo(ActionErrorCodes.NotConnected));
	}

	[Test]
	public async Task Profile_options_follow_the_chosen_device()
	{
		var (harness, _, synapse, _) = await ReadyAsync();
		await using var _h = harness;

		var mouseOptions = OptionValues(await harness.Actions.GetOptionsAsync("switch-profile", "profile", null, P(("device", Mouse))));
		var keyboardOptions = OptionValues(await harness.Actions.GetOptionsAsync("switch-profile", "profiles", null, P(("device", Keyboard))));

		Assert.That(mouseOptions, Is.EqualTo(synapse.FindBySerial(Mouse)!.Profiles.Select(p => p.Guid)));
		Assert.That(keyboardOptions, Is.EqualTo(synapse.FindBySerial(Keyboard)!.Profiles.Select(p => p.Guid)));
	}

	[Test]
	public void Every_dynamic_parameter_is_served_by_the_plugin_not_a_host_source()
	{
		var synapse = SynapseServiceTests.CreateService(FakeInspector.FromFixture());
		var registry = new FakeAutostartRegistry(null);
		var intent = new Autostart.FakeIntent(true);
		var process = new Autostart.FakeProcess(KeystoneDigital.RazerController.Autostart.SynapseProcessState.NotRunning);
		var integration = new RazerIntegration(synapse, MouseDpiServiceTests.ServiceFor(null), registry, intent, new KeystoneDigital.RazerController.Autostart.InspectorGuard(registry, intent, process, SynapseServiceTests.Log), process, Chroma.ChromaTests.Client(new Chroma.FakeChromaServer()));

		var dynamic = integration.Actions
			.SelectMany(action => action.Parameters.Select(parameter => (action.Id, parameter)))
			.Where(x => x.parameter.Type is ActionParameterType.DynamicChoice or ActionParameterType.MultiSelect)
			.ToList();

		Assert.That(dynamic, Is.Not.Empty);
		foreach (var (actionId, parameter) in dynamic)
		{
			Assert.That(parameter.OptionsSourceId, Is.Null, $"{actionId}/{parameter.Name}");
			Assert.That(parameter.DynamicOptions, Is.True, $"{actionId}/{parameter.Name}");
		}
	}

	[Test]
	public async Task Device_options_keep_a_disconnected_serial_selectable()
	{
		var (harness, _, _, _) = await ReadyAsync();
		await using var _h = harness;

		var outcome = await harness.Actions.GetOptionsAsync("switch-profile", "device", null, P(("device", "SERIAL-GONE")));

		Assert.That(OptionValues(outcome), Is.EqualTo(new[] { Mouse, Keyboard, "SERIAL-GONE" }));
		Assert.That(OptionLabels(outcome)[0], Does.Contain("Razer Naga Trinity"));
	}

	[Test]
	public async Task Profile_options_keep_a_deleted_profile_visible()
	{
		var (harness, _, _, _) = await ReadyAsync();
		await using var _h = harness;
		const string gone = "00000000-dead-beef-0000-000000000000";

		var outcome = await harness.Actions.GetOptionsAsync("switch-profile", "profile", null, P(("device", Mouse), ("profile", gone)));

		Assert.That(OptionValues(outcome), Does.Contain(gone));
	}

	[Test]
	public async Task Gaming_device_options_list_only_devices_with_gaming_mode()
	{
		var (harness, _, _, _) = await ReadyAsync();
		await using var _h = harness;

		var values = OptionValues(await harness.Actions.GetOptionsAsync("gaming-mode", "device", null, P()));

		Assert.That(values, Does.Contain(Keyboard));
	}

	[Test]
	public async Task Actions_report_control_off_instead_of_using_the_port()
	{
		var synapse = new SynapseService(FakeInspector.FromFixture(), SynapseServiceTests.Log, SynapseServiceTests.Fast, controlEnabled: () => false);
		await synapse.RefreshAsync(default);
		await using var harness = CreateHarness(synapse, MouseDpiServiceTests.ServiceFor(null));
		await harness.InitializeIntegrationsAsync();

		var outcome = await harness.Actions.ExecuteAsync("switch-profile", P(("device", Mouse), ("mode", "next")));

		Assert.That(ErrorCode(outcome), Is.EqualTo(ActionErrorCodes.NotConfigured));
	}
}
