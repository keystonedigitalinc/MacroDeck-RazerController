using KeystoneDigital.RazerController.Synapse;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

namespace KeystoneDigital.RazerController.Actions;

internal sealed class SwitchProfileAction(SynapseService synapse) : IDynamicOptionsActionDefinition
{
	private const string ModeParameter = "mode";
	private const string ProfileParameter = "profile";
	private const string ProfilesParameter = "profiles";

	public string Id => "switch-profile";

	public LocalizedText Name => Strings.Actions.SwitchProfile.Name();

	public LocalizedText Description => Strings.Actions.SwitchProfile.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		DeviceOptions.DeviceChoice(),
		ActionParameter.Choice(
			ModeParameter,
			[
				new ActionParameterOption { Value = "profile", Label = Strings.Actions.SwitchProfile.Mode.Profile() },
				new ActionParameterOption { Value = "next", Label = Strings.Actions.SwitchProfile.Mode.Next() },
				new ActionParameterOption { Value = "previous", Label = Strings.Actions.SwitchProfile.Mode.Previous() },
				new ActionParameterOption { Value = "cycle", Label = Strings.Actions.SwitchProfile.Mode.Cycle() },
			],
			label: Strings.Actions.SwitchProfile.Mode.Label(),
			defaultValue: "profile",
			required: true),
		ActionParameter.DynamicChoice(ProfileParameter, label: Strings.Actions.SwitchProfile.Profile.Label())
			.OnlyWhen(ModeParameter, "profile"),
		ProfileList(),
	];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;

	// No optionsSourceId: the host reads that as the name of one of its own sources. With no static
	// options and DynamicOptions set, the host asks GetDynamicOptionsAsync instead.
	private static ActionParameter ProfileList()
	{
		var parameter = new ActionParameter
		{
			Name = ProfilesParameter,
			Type = ActionParameterType.MultiSelect,
			Label = Strings.Actions.SwitchProfile.Profiles.Label(),
			Options = [],
			DynamicOptions = true,
		};
		return parameter.OnlyWhen(ModeParameter, "cycle");
	}

	public IActionExecutor CreateExecutor() => new Executor(synapse);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context, CancellationToken cancellationToken) =>
		Task.FromResult(context.ParameterName == DeviceOptions.DeviceParameter
			? DeviceOptions.Devices(synapse, context, device => device.Profiles.Count > 0)
			: DeviceOptions.Profiles(synapse, context));

	/// <summary>The profile to switch to, or null when the parameters do not name one that exists.</summary>
	internal static string? ChooseTarget(RazerDeviceInfo device, string mode, string? profile, IReadOnlyList<string> cycle)
	{
		var profiles = device.Profiles;
		if (profiles.Count == 0) return null;

		var activeIndex = profiles.ToList().FindIndex(p => p.Guid == device.ActiveProfileGuid);

		switch (mode)
		{
			case "next":
				return profiles[(activeIndex + 1) % profiles.Count].Guid;
			case "previous":
				return profiles[activeIndex <= 0 ? profiles.Count - 1 : activeIndex - 1].Guid;
			case "cycle":
				var available = cycle.Where(guid => profiles.Any(p => p.Guid == guid)).ToList();
				if (available.Count == 0) return null;

				var position = available.IndexOf(device.ActiveProfileGuid ?? "");
				return position < 0 ? available[0] : available[(position + 1) % available.Count];
			default:
				return profiles.Any(p => p.Guid == profile) ? profile : null;
		}
	}

	private sealed class Executor(SynapseService synapse) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var (device, failure) = DeviceOptions.Resolve(synapse, context.Parameters);
			if (device is null) return failure!;

			var mode = ParameterReader.String(context.Parameters, ModeParameter) ?? "profile";
			var profile = ParameterReader.String(context.Parameters, ProfileParameter);
			var cycle = ParameterReader.StringList(context.Parameters, ProfilesParameter);

			if (mode == "profile" && profile is null) return DeviceOptions.Invalid(Strings.Actions.SwitchProfile.Profile.Label());
			if (mode == "cycle" && cycle.Count == 0) return DeviceOptions.Invalid(Strings.Actions.SwitchProfile.Profiles.Label());

			var outcome = await synapse.SendAndConfirmAsync(device.Serial, current =>
			{
				if (ChooseTarget(current, mode, profile, cycle) is not { } target) return DevicePlan.Invalid;
				if (target == current.ActiveProfileGuid) return DevicePlan.NoChange;

				return DevicePlan.Send("ON_SWITCH_PROFILE", new { newActiveProfileGUID = target }, latest => latest.ActiveProfileGuid == target);
			}, context.CancellationToken).ConfigureAwait(false);

			if (outcome == CommandOutcome.Applied && synapse.FindBySerial(device.Serial) is { ActiveProfileName: { } name } current)
			{
				await synapse.ShowPopupAsync(current, "Profile: " + name, null, null, context.CancellationToken).ConfigureAwait(false);
			}

			return DeviceOptions.ToResult(outcome, () => ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Actions.Errors.ProfileNotFound()));
		}
	}
}
