using System.Globalization;
using KeystoneDigital.RazerController.Synapse;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

namespace KeystoneDigital.RazerController.Actions;

internal sealed class LightingEffectAction(SynapseService synapse) : IDynamicOptionsActionDefinition
{
	private const string EffectParameter = "effect";
	private const string ColorParameter = "color";
	private const string Color2Parameter = "color2";
	private const string RandomParameter = "random";
	private const string DurationParameter = "duration";
	private const string DirectionParameter = "direction";
	private const string DefaultDirection = "default";

	private const int Static = 1, Breathing = 2, Wave = 4, Reactive = 5, Ripple = 6, Starlight = 7;

	/// <summary>
	/// Synapse's quick-effect ids. A device's own supported list is not reachable from outside its
	/// window, so an effect a device refuses surfaces as "not applied" through confirmation.
	/// </summary>
	internal static readonly IReadOnlyList<(int Id, Func<LocalizedText> Name)> Effects =
	[
		(0, () => Strings.Actions.LightingEffect.Effects.Off()),
		(1, () => Strings.Actions.LightingEffect.Effects.Static()),
		(2, () => Strings.Actions.LightingEffect.Effects.Breathing()),
		(3, () => Strings.Actions.LightingEffect.Effects.Spectrum()),
		(4, () => Strings.Actions.LightingEffect.Effects.Wave()),
		(5, () => Strings.Actions.LightingEffect.Effects.Reactive()),
		(6, () => Strings.Actions.LightingEffect.Effects.Ripple()),
		(7, () => Strings.Actions.LightingEffect.Effects.Starlight()),
		(8, () => Strings.Actions.LightingEffect.Effects.Fire()),
		(11, () => Strings.Actions.LightingEffect.Effects.AmbientAwareness()),
		(12, () => Strings.Actions.LightingEffect.Effects.AudioMeter()),
	];

	public string Id => "lighting-effect";

	public LocalizedText Name => Strings.Actions.LightingEffect.Name();

	public LocalizedText Description => Strings.Actions.LightingEffect.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		DeviceOptions.DeviceChoice(),
		ActionParameter.Choice(
			EffectParameter,
			[.. Effects.Select(effect => new ActionParameterOption { Value = effect.Id.ToString(CultureInfo.InvariantCulture), Label = effect.Name() })],
			label: Strings.Actions.LightingEffect.Effect.Label(),
			defaultValue: "1",
			required: true),
		ActionParameter.Color(ColorParameter, label: Strings.Actions.LightingEffect.Color.Label(), defaultValue: "#00FF00")
			.OnlyWhen(EffectParameter, "1", "2", "5", "6", "7"),
		ActionParameter.Color(Color2Parameter, label: Strings.Actions.ChromaLighting.Color2.Label(), supportsReset: true)
			.OnlyWhen(EffectParameter, "2", "7"),
		ActionParameter.Toggle(RandomParameter, label: Strings.Actions.LightingEffect.Random.Label())
			.OnlyWhen(EffectParameter, "2", "7"),
		ActionParameter.Choice(
			DurationParameter,
			[
				new ActionParameterOption { Value = "1", Label = Strings.Actions.LightingEffect.Duration.Short() },
				new ActionParameterOption { Value = "2", Label = Strings.Actions.LightingEffect.Duration.Medium() },
				new ActionParameterOption { Value = "3", Label = Strings.Actions.LightingEffect.Duration.Long() },
			],
			label: Strings.Actions.LightingEffect.Duration.Label(),
			defaultValue: "2")
			.OnlyWhen(EffectParameter, "5", "7"),
		ActionParameter.Choice(
			DirectionParameter,
			[
				new ActionParameterOption { Value = DefaultDirection, Label = Strings.Actions.LightingEffect.Direction.Default() },
				new ActionParameterOption { Value = "1", Label = Strings.Actions.LightingEffect.Direction.Left() },
				new ActionParameterOption { Value = "2", Label = Strings.Actions.LightingEffect.Direction.Right() },
				new ActionParameterOption { Value = "3", Label = Strings.Actions.LightingEffect.Direction.Up() },
				new ActionParameterOption { Value = "4", Label = Strings.Actions.LightingEffect.Direction.Down() },
			],
			label: Strings.Actions.LightingEffect.Direction.Label(),
			description: Strings.Actions.LightingEffect.Direction.Description(),
			defaultValue: DefaultDirection)
			.OnlyWhen(EffectParameter, "4"),
	];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;

	public IActionExecutor CreateExecutor() => new Executor(synapse);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context, CancellationToken cancellationToken) =>
		Task.FromResult(DeviceOptions.Devices(synapse, context, device => device.Effect is not null));

	/// <summary>
	/// The selectedEffectSetting Synapse's own UI stores for <paramref name="id"/>, or null for effects without
	/// settings. Field names and values come from Synapse's lighting helper: a missing second colour is
	/// "no-color", durations are 1 to 3, directions 1 left, 2 right, 3 up, 4 down.
	/// </summary>
	internal static Dictionary<string, object>? Setting(int id, IReadOnlyDictionary<string, object> parameters)
	{
		var color = ParameterReader.String(parameters, ColorParameter) ?? "#00FF00";
		var color2 = ParameterReader.String(parameters, Color2Parameter) ?? "no-color";
		var random = string.Equals(ParameterReader.String(parameters, RandomParameter), "true", StringComparison.OrdinalIgnoreCase);
		var duration = Math.Clamp(ParameterReader.Int(parameters, DurationParameter) ?? 2, 1, 3);
		var direction = ParameterReader.String(parameters, DirectionParameter) is { } text && text != DefaultDirection ? ParameterReader.Int(parameters, DirectionParameter) : null;

		return id switch
		{
			Static or Ripple => new() { ["color1"] = color },
			Breathing => new() { ["color1"] = color, ["color2"] = color2, ["isRandom"] = random },
			Starlight => new() { ["color1"] = color, ["color2"] = color2, ["isRandom"] = random, ["duration"] = duration },
			Reactive => new() { ["color1"] = color, ["duration"] = duration },
			Wave when direction is >= 1 and <= 4 => new() { ["direction"] = direction.Value },
			_ => null,
		};
	}

	/// <summary>
	/// Synapse has no "off" effect: its Off switches the profile's lighting brightness off and keeps the level and the
	/// effect. Effect 0 sent as a quick effect shows Fire on some keyboards, so Off goes through brightness instead.
	/// </summary>
	internal const int Off = 0;

	/// <summary>Switches the profile's lighting on or off, keeping its brightness level.</summary>
	internal static Task<CommandOutcome> SetLightingAsync(SynapseService synapse, string serial, bool on, CancellationToken ct) =>
		synapse.SendAndConfirmAsync(serial, current =>
		{
			if (current.Brightness is not { } now) return DevicePlan.Invalid;
			if (on == now > 0) return DevicePlan.NoChange;

			var level = current.BrightnessLevel is int saved and > 0 ? saved : 100;
			return DevicePlan.Send("ON_SET_BRIGHTNESS", new { brightness = new { isEnabled = on, value = level } }, latest => latest.Brightness > 0 == on);
		}, ct);

	/// <summary>Sets <paramref name="id"/> on the device: Off switches the lighting off; any effect also switches it back on.</summary>
	internal static async Task<CommandOutcome> ApplyAsync(SynapseService synapse, string serial, Func<RazerDeviceInfo, int> pick, IReadOnlyDictionary<string, object> parameters, CancellationToken ct)
	{
		if (synapse.FindBySerial(serial) is { } device && pick(device) == Off)
		{
			var off = await SetLightingAsync(synapse, serial, on: false, ct).ConfigureAwait(false);
			await PopupAsync(synapse, serial, off, Off, ct).ConfigureAwait(false);
			return off;
		}

		var chosen = -1;

		var outcome = await synapse.SendAndConfirmAsync(serial, current =>
		{
			var id = pick(current);
			if (id == Off) return DevicePlan.Invalid;
			chosen = id;

			var setting = Setting(id, parameters);
			object effectSetting = setting is null
				? new { selectedEffectId = id }
				: new { selectedEffectId = id, selectedEffectSetting = setting };

			return DevicePlan.Send("ON_SET_EFFECTS", new { effectSetting }, latest => Matches(latest.Effect, id, setting));
		}, ct).ConfigureAwait(false);

		if (outcome is CommandOutcome.Applied or CommandOutcome.NoChange && synapse.FindBySerial(serial) is { Brightness: 0 })
		{
			var on = await SetLightingAsync(synapse, serial, on: true, ct).ConfigureAwait(false);
			await PopupAsync(synapse, serial, on, chosen, ct).ConfigureAwait(false);
			return on;
		}

		await PopupAsync(synapse, serial, outcome, chosen, ct).ConfigureAwait(false);
		return outcome;
	}

	/// <summary>Synapse's own popup naming the effect, after a change that took effect.</summary>
	private static async Task PopupAsync(SynapseService synapse, string serial, CommandOutcome outcome, int id, CancellationToken ct)
	{
		if (outcome != CommandOutcome.Applied || id < 0 || synapse.FindBySerial(serial) is not { } device) return;

		await synapse.ShowPopupAsync(device, "Lighting: " + Variables.RazerVariables.EffectName(id), null, null, ct).ConfigureAwait(false);
	}

	/// <summary>Whether <paramref name="effect"/> shows what was asked: the effect, and every setting field that was sent.</summary>
	internal static bool Matches(LightingEffect? effect, int id, Dictionary<string, object>? setting) =>
		effect?.Id == id
		&& (setting is null || setting.All(field => string.Equals(effect.Read(field.Key), Format(field.Value), StringComparison.OrdinalIgnoreCase)));

	private static string Format(object value) => value switch
	{
		bool flag => flag ? "True" : "False",
		IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
		_ => value.ToString() ?? "",
	};

	private sealed class Executor(SynapseService synapse) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var (device, failure) = DeviceOptions.Resolve(synapse, context.Parameters);
			if (device is null) return failure!;

			var effect = ParameterReader.Int(context.Parameters, EffectParameter);
			if (effect is not { } id || Effects.All(e => e.Id != id))
			{
				return DeviceOptions.Invalid(Strings.Actions.LightingEffect.Effect.Label());
			}

			if (device.Effect is null)
			{
				return ActionResult.Failed(ActionErrorCodes.ProviderRejected, Strings.Actions.Errors.Unsupported());
			}

			var outcome = await ApplyAsync(synapse, device.Serial, _ => id, context.Parameters, context.CancellationToken).ConfigureAwait(false);
			return DeviceOptions.ToResult(outcome);
		}
	}
}
