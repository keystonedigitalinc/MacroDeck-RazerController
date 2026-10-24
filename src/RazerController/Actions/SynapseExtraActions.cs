using System.Globalization;
using KeystoneDigital.RazerController.Autostart;
using KeystoneDigital.RazerController.Settings;
using KeystoneDigital.RazerController.Synapse;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

namespace KeystoneDigital.RazerController.Actions;

/// <summary>Steps a device through the lighting effects the user ticks, one per press, through Synapse.</summary>
internal sealed class CycleLightingAction(SynapseService synapse) : IDynamicOptionsActionDefinition
{
	private const string EffectsParameter = "effects";
	private const string ColorParameter = "color";

	/// <summary>Used when no effect is ticked: the effects every Razer lighting device has.</summary>
	private static readonly IReadOnlyList<int> DefaultCycle = [1, 2, 3, 4];

	public string Id => "cycle-lighting";

	public LocalizedText Name => Strings.Actions.CycleLighting.Name();

	public LocalizedText Description => Strings.Actions.CycleLighting.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		DeviceOptions.DeviceChoice(),
		new ActionParameter
		{
			Name = EffectsParameter,
			Type = ActionParameterType.MultiSelect,
			Label = Strings.Actions.CycleLighting.Effects.Label(),

			// Served by GetDynamicOptionsAsync: a multi-select with static options is read as a host source.
			Options = [],
			DynamicOptions = true,
		},
		ActionParameter.Color(ColorParameter, label: Strings.Actions.LightingEffect.Color.Label(), defaultValue: "#00FF00"),
	];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;

	public IActionExecutor CreateExecutor() => new Executor(synapse);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context, CancellationToken cancellationToken) =>
		Task.FromResult(context.ParameterName == DeviceOptions.DeviceParameter
			? DeviceOptions.Devices(synapse, context, device => device.Effect is not null)
			: new DynamicOptionsResult
			{
				Options = [.. LightingEffectAction.Effects.Select(effect => new ActionParameterOption { Value = effect.Id.ToString(CultureInfo.InvariantCulture), Label = effect.Name() })],
			});

	/// <summary>The effect after <paramref name="current"/> in <paramref name="cycle"/>, wrapping; the first when the current one is not in it.</summary>
	internal static int Next(IReadOnlyList<int> cycle, int? current)
	{
		var position = current is { } id ? cycle.ToList().IndexOf(id) : -1;
		return position < 0 ? cycle[0] : cycle[(position + 1) % cycle.Count];
	}

	internal static IReadOnlyList<int> Cycle(IReadOnlyDictionary<string, object> parameters)
	{
		var ticked = ParameterReader.StringList(parameters, EffectsParameter)
			.Select(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : -1)
			.Where(id => LightingEffectAction.Effects.Any(effect => effect.Id == id))
			.Distinct()
			.ToList();

		return ticked.Count > 0 ? ticked : DefaultCycle;
	}

	private sealed class Executor(SynapseService synapse) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var (device, failure) = DeviceOptions.Resolve(synapse, context.Parameters);
			if (device is null) return failure!;

			if (device.Effect is null)
			{
				return ActionResult.Failed(ActionErrorCodes.ProviderRejected, Strings.Actions.Errors.Unsupported());
			}

			var cycle = Cycle(context.Parameters);

			var outcome = await synapse.SendAndConfirmAsync(device.Serial, current =>
			{
				var id = Next(cycle, current.Effect?.Id);
				var setting = LightingEffectAction.Setting(id, context.Parameters);
				object effectSetting = setting is null
					? new { selectedEffectId = id }
					: new { selectedEffectId = id, selectedEffectSetting = setting };

				return DevicePlan.Send("ON_SET_EFFECTS", new { effectSetting }, latest => LightingEffectAction.Matches(latest.Effect, id, setting));
			}, context.CancellationToken).ConfigureAwait(false);

			return DeviceOptions.ToResult(outcome);
		}
	}
}

/// <summary>
/// Restarts Synapse with its own startup entry, so it comes back with Synapse control as the user set it. Synapse
/// sometimes drops the debug flag from that entry, so with control on the flag goes back in first, as the settings
/// screen does. It runs only when the user presses the button; the plugin never restarts Synapse by itself.
/// </summary>
internal sealed class RestartSynapseAction(IAutostartRegistry registry, ISynapseProcess process, ISynapseControlIntent intent) : IActionDefinition
{
	public string Id => "restart-synapse";

	public LocalizedText Name => Strings.Actions.RestartSynapse.Name();

	public LocalizedText Description => Strings.Actions.RestartSynapse.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } = [];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;

	public IActionExecutor CreateExecutor() => new Executor(registry, process, intent);

	/// <summary>The startup entry to restart with: with control on, always carrying the debug flag.</summary>
	internal static string CommandLineFor(string current, bool controlOn) =>
		controlOn ? AutostartCommandLine.AppendFlag(current, InspectorConfigFlow.InspectFlag) : current;

	private sealed class Executor(IAutostartRegistry registry, ISynapseProcess process, ISynapseControlIntent intent) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (registry.GetValue() is not { Length: > 0 } current)
			{
				return ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Settings.Inspector.NotInstalled());
			}

			var commandLine = CommandLineFor(current, intent.Get() == true);

			try
			{
				if (commandLine != current) registry.SetValue(commandLine);

				await process.RestartAsync(commandLine, context.CancellationToken).ConfigureAwait(false);
				if (commandLine.Contains(InspectorConfigFlow.InspectFlag, StringComparison.Ordinal)) FlagKeeper.Start(registry);
				return ActionResult.Success();
			}
			catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
			{
				return ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Settings.Inspector.WriteFailed());
			}
			catch (Exception ex) when (ex is InvalidOperationException or System.Management.ManagementException or System.ComponentModel.Win32Exception)
			{
				return ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Settings.Inspector.RestartFailed());
			}
		}
	}
}
