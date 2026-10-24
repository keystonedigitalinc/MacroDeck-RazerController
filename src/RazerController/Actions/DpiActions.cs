using KeystoneDigital.RazerController.Hid;
using KeystoneDigital.RazerController.Synapse;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

namespace KeystoneDigital.RazerController.Actions;

internal sealed class StepDpiAction(MouseDpiService dpi, SynapseService synapse) : IActionDefinition
{
	private const string DirectionParameter = "direction";

	public string Id => "step-dpi";

	public LocalizedText Name => Strings.Actions.StepDpi.Name();

	public LocalizedText Description => Strings.Actions.StepDpi.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Choice(
			DirectionParameter,
			[
				new ActionParameterOption { Value = "up", Label = Strings.Actions.StepDpi.Direction.Up() },
				new ActionParameterOption { Value = "down", Label = Strings.Actions.StepDpi.Direction.Down() },
			],
			label: Strings.Actions.StepDpi.Direction.Label(),
			defaultValue: "up",
			required: true),
	];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;

	public IActionExecutor CreateExecutor() => new Executor(dpi, synapse);

	internal static ActionResult ToResult(DpiOutcome outcome) => outcome switch
	{
		DpiOutcome.Applied => ActionResult.Success(),
		DpiOutcome.NoMouse => ActionResult.Failed(ActionErrorCodes.NotConnected, Strings.Actions.Errors.NoMouse()),
		DpiOutcome.NoSuchStage => ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Actions.Errors.NoSuchStage()),
		_ => ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Actions.Errors.MouseFailed()),
	};

	private sealed class Executor(MouseDpiService dpi, SynapseService synapse) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var up = (ParameterReader.String(context.Parameters, DirectionParameter) ?? "up") != "down";

			if (await SynapseDpi.TryAsync(synapse, dpi, stages => stages.Step(up), context.CancellationToken).ConfigureAwait(false) is { } done) return done;

			return ToResult(await dpi.StepAsync(up, context.CancellationToken).ConfigureAwait(false));
		}
	}
}

internal sealed class TemporaryDpiAction(MouseDpiService dpi) : IActionDefinition
{
	private const string DpiParameter = "dpi";

	public string Id => "temporary-dpi";

	public LocalizedText Name => Strings.Actions.TemporaryDpi.Name();

	public LocalizedText Description => Strings.Actions.TemporaryDpi.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Number(DpiParameter, label: Strings.Actions.TemporaryDpi.Dpi.Label(), min: 100, max: 20000, step: 50, defaultValue: 800, required: true),
	];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;

	public IActionExecutor CreateExecutor() => new Executor(dpi);

	private sealed class Executor(MouseDpiService dpi) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (ParameterReader.Int(context.Parameters, DpiParameter) is not { } value)
			{
				return DeviceOptions.Invalid(Strings.Actions.TemporaryDpi.Dpi.Label());
			}

			return StepDpiAction.ToResult(await dpi.SetTemporaryAsync(value, context.CancellationToken).ConfigureAwait(false));
		}
	}
}

internal sealed class DpiStageAction(MouseDpiService dpi, SynapseService synapse) : IActionDefinition
{
	private const string StageParameter = "stage";

	public string Id => "dpi-stage";

	public LocalizedText Name => Strings.Actions.DpiStage.Name();

	public LocalizedText Description => Strings.Actions.DpiStage.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Number(StageParameter, label: Strings.Actions.DpiStage.Stage.Label(), min: 1, max: 5, step: 1, defaultValue: 1, required: true),
	];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;

	public IActionExecutor CreateExecutor() => new Executor(dpi, synapse);

	private sealed class Executor(MouseDpiService dpi, SynapseService synapse) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (ParameterReader.Int(context.Parameters, StageParameter) is not { } stage)
			{
				return DeviceOptions.Invalid(Strings.Actions.DpiStage.Stage.Label());
			}

			if (synapse.FirstOf(DeviceCategory.Mouse)?.DpiStages is { } known && SynapseDpi.Usable(synapse) && known.SlotOf(stage) is null)
			{
				return StepDpiAction.ToResult(DpiOutcome.NoSuchStage);
			}

			if (await SynapseDpi.TryAsync(synapse, dpi, stages => stages.SlotOf(stage), context.CancellationToken).ConfigureAwait(false) is { } done) return done;

			return StepDpiAction.ToResult(await dpi.GoToStageAsync(stage, context.CancellationToken).ConfigureAwait(false));
		}
	}
}

/// <summary>
/// Changes the mouse's DPI stage through Synapse, as Synapse's own window does: the whole stage list with the new
/// active slot, then the update its window needs to show it. Synapse saves the stage in the profile. Without Synapse
/// control, or without a mouse Synapse knows, the actions fall back to setting the stage over USB.
/// </summary>
internal static class SynapseDpi
{
	public static bool Usable(SynapseService synapse) => synapse.Status == SynapseStatus.Ready;

	/// <summary>The plan for moving to the slot <paramref name="pick"/> chooses; no slot means no change.</summary>
	public static DevicePlan Plan(RazerDeviceInfo mouse, Func<DpiStageSet, int?> pick)
	{
		if (mouse.DpiStages is not { } stages) return DevicePlan.Invalid;
		if (pick(stages) is not { } slot || slot == stages.Active) return DevicePlan.NoChange;

		return DevicePlan.Send(
			"ON_STAGES_CHANGE",
			new { active = slot, enable = stages.Enable, stages = stages.Raw, enableStages = true },
			latest => latest.DpiStages?.Active == slot)
			with { WindowUpdate = new WindowUpdate("MW_SET_PERFORMANCE_TO_UI", new { active = slot }) };
	}

	/// <summary>The action's result when Synapse handled it, or null to fall back to USB.</summary>
	public static async Task<ActionResult?> TryAsync(SynapseService synapse, MouseDpiService dpi, Func<DpiStageSet, int?> pick, CancellationToken ct)
	{
		if (!Usable(synapse) || synapse.FirstOf(DeviceCategory.Mouse) is not { DpiStages: not null } mouse) return null;

		var outcome = await synapse.SendAndConfirmAsync(mouse.Serial, current => Plan(current, pick), ct).ConfigureAwait(false);
		if (outcome is CommandOutcome.NotConnected or CommandOutcome.DeviceNotFound or CommandOutcome.Invalid) return null;

		// The DPI variables read the mouse; refresh them now instead of at the next poll.
		await dpi.RefreshAsync(ct).ConfigureAwait(false);
		return DeviceOptions.ToResult(outcome);
	}
}
