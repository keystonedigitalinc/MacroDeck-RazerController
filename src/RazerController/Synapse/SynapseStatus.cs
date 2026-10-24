using KeystoneDigital.RazerController.Autostart;

namespace KeystoneDigital.RazerController.Synapse;

internal enum SynapseStatus
{
	Starting,
	Ready,
	InspectorUnavailable,
	InspectorUntrusted,
	SynapseUnrecognised,
	NoDevices,

	/// <summary>The user has not turned Synapse control on, so the inspector is not contacted.</summary>
	ControlOff,
}

internal enum CommandOutcome
{
	Applied,
	NotConnected,
	DeviceNotFound,
	NotApplied,
	NoChange,
	Invalid,
}

/// <summary>
/// What to send, decided from the device's state at the moment the command runs. <see cref="WindowUpdate"/>, when
/// set, is posted to the device's Synapse window after the command, so an open window shows the change at once.
/// </summary>
internal sealed record DevicePlan(string? EventType, object? Payload, Func<RazerDeviceInfo, bool>? IsApplied, CommandOutcome? Shortcut, WindowUpdate? WindowUpdate = null)
{
	public static DevicePlan NoChange { get; } = new(null, null, null, CommandOutcome.NoChange);

	public static DevicePlan Invalid { get; } = new(null, null, null, CommandOutcome.Invalid);

	public static DevicePlan Send(string eventType, object payload, Func<RazerDeviceInfo, bool> isApplied) => new(eventType, payload, isApplied, null);
}

/// <summary>A Redux action for the device's Synapse window, in the form Synapse's own device handler sends it.</summary>
internal sealed record WindowUpdate(string Type, object Payload);

internal static class SynapseStatusText
{
	/// <summary>The value of the razer_status variable, naming the setup problem when there is one.</summary>
	public static string ToVariableValue(this SynapseStatus status, SetupIssue? issue) => issue switch
	{
		SetupIssue.FlagMissing => "flag_missing",
		SetupIssue.RestartNeeded => "restart_needed",
		_ => status.ToVariableValue(),
	};

	/// <summary>The value of the razer_status variable without a setup problem.</summary>
	public static string ToVariableValue(this SynapseStatus status) => status switch
	{
		SynapseStatus.Starting => "starting",
		SynapseStatus.Ready => "ready",
		SynapseStatus.InspectorUnavailable => "inspector_unavailable",
		SynapseStatus.InspectorUntrusted => "inspector_untrusted",
		SynapseStatus.SynapseUnrecognised => "synapse_unrecognised",
		SynapseStatus.NoDevices => "no_devices",
		SynapseStatus.ControlOff => "control_off",
		_ => "unknown",
	};
}
