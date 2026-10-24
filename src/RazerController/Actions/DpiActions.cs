using KeystoneDigital.RazerController.Hid;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

namespace KeystoneDigital.RazerController.Actions;

internal sealed class StepDpiAction(MouseDpiService dpi) : IActionDefinition
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

	public IActionExecutor CreateExecutor() => new Executor(dpi);

	internal static ActionResult ToResult(DpiOutcome outcome) => outcome switch
	{
		DpiOutcome.Applied => ActionResult.Success(),
		DpiOutcome.NoMouse => ActionResult.Failed(ActionErrorCodes.NotConnected, Strings.Actions.Errors.NoMouse()),
		DpiOutcome.NoSuchStage => ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Actions.Errors.NoSuchStage()),
		_ => ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Actions.Errors.MouseFailed()),
	};

	private sealed class Executor(MouseDpiService dpi) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var up = (ParameterReader.String(context.Parameters, DirectionParameter) ?? "up") != "down";
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

internal sealed class DpiStageAction(MouseDpiService dpi) : IActionDefinition
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

	public IActionExecutor CreateExecutor() => new Executor(dpi);

	private sealed class Executor(MouseDpiService dpi) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (ParameterReader.Int(context.Parameters, StageParameter) is not { } stage)
			{
				return DeviceOptions.Invalid(Strings.Actions.DpiStage.Stage.Label());
			}

			return StepDpiAction.ToResult(await dpi.GoToStageAsync(stage, context.CancellationToken).ConfigureAwait(false));
		}
	}
}
