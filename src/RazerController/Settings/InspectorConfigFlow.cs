using System.Text.Json;
using KeystoneDigital.RazerController.Autostart;
using KeystoneDigital.RazerController.Synapse;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;

namespace KeystoneDigital.RazerController.Settings;

/// <summary>
/// The one setting: whether Synapse control is on. This screen is the only place that changes Synapse's
/// HKCU startup entry: on appends the inspector flag, off removes exactly that flag again.
/// </summary>
internal sealed class InspectorConfigFlow(IAutostartRegistry registry, SynapseService synapse, ISynapseControlIntent intent, InspectorGuard guard, ISynapseProcess process) : IConfigFlow
{
	public const string InspectFlag = "--inspect=127.0.0.1:9229";

	private const string StepId = "inspector";
	private const string EnabledField = "enabled";
	private const string RestartField = "restart";
	private const string PopupsField = "popups";

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken) =>
		Task.FromResult(ConfigFlowResult.Step(BuildStep()));

	public async Task<ConfigFlowResult> SubmitAsync(string stepId, IReadOnlyDictionary<string, object?> input, IConfigFlowContext context, CancellationToken cancellationToken)
	{
		var current = registry.GetValue();
		if (string.IsNullOrWhiteSpace(current))
		{
			return ConfigFlowResult.Error(BuildStep(), Strings.Settings.Inspector.NotInstalled(), new Dictionary<string, MacroDeck.Localization.LocalizedText>());
		}

		var enable = ReadBool(input, EnabledField);
		synapse.Popups.Enabled = !input.ContainsKey(PopupsField) || ReadBool(input, PopupsField);
		intent.Set(enable);
		var updated = enable ? AutostartCommandLine.AppendFlag(current, InspectFlag) : AutostartCommandLine.RemoveFlag(current, InspectFlag);
		if (updated != current)
		{
			try
			{
				registry.SetValue(updated);
			}
			catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
			{
				return ConfigFlowResult.Error(BuildStep(), Strings.Settings.Inspector.WriteFailed(), new Dictionary<string, MacroDeck.Localization.LocalizedText>());
			}
		}

		// Only when the user ticks it. Turning control off with a restart closes the debug port right away.
		if (ReadBool(input, RestartField))
		{
			try
			{
				await process.RestartAsync(updated, cancellationToken).ConfigureAwait(false);
				if (enable) FlagKeeper.Start(registry);
			}
			catch (Exception ex) when (ex is InvalidOperationException or System.Management.ManagementException or System.ComponentModel.Win32Exception)
			{
				return ConfigFlowResult.Error(BuildStep(), Strings.Settings.Inspector.RestartFailed(), new Dictionary<string, MacroDeck.Localization.LocalizedText>());
			}
		}

		// The host keeps the submitted form and fills the screen from it next time. "enabled" stays the toggle's own
		// true/false (a text "true" shows as off), and "restart" is stored as off so it never comes back ticked.
		return ConfigFlowResult.Complete(
			"Razer App Controller",
			new Dictionary<string, ConfigFlowValue> { [RestartField] = ConfigFlowValue.Plain("false") });
	}

	private ConfigFlowStep BuildStep()
	{
		// Only the user's own choice counts as on. A flag someone added by hand is not consent.
		var enabled = intent.Get() == true;

		return new ConfigFlowStep
		{
			StepId = StepId,
			Title = Strings.Settings.Inspector.Title(),
			Description = Strings.Settings.Inspector.Description(
				enabled ? Strings.Settings.Inspector.On() : Strings.Settings.Inspector.Off(),
				synapse.Status.ToVariableValue(guard.Issue)),
			Instructions =
			[
				new ConfigFlowInstruction { Text = Strings.Settings.Inspector.SecurityWarning() },
				new ConfigFlowInstruction { Text = Strings.Settings.Inspector.StartupEntry() },
			],
			Fields =
			[
				ActionParameter.Toggle(EnabledField, label: Strings.Settings.Inspector.Enabled.Label(), description: Strings.Settings.Inspector.Enabled.Description(), defaultValue: enabled),
				ActionParameter.Toggle(RestartField, label: Strings.Settings.Inspector.Restart.Label(), description: Strings.Settings.Inspector.Restart.Description(), defaultValue: false),
				ActionParameter.Toggle(PopupsField, label: Strings.Settings.Inspector.Popups.Label(), description: Strings.Settings.Inspector.Popups.Description(), defaultValue: synapse.Popups.Enabled),
			],
		};
	}

	private static bool ReadBool(IReadOnlyDictionary<string, object?> input, string name) =>
		input.TryGetValue(name, out var value) && value switch
		{
			bool flag => flag,
			JsonElement { ValueKind: JsonValueKind.True } => true,
			JsonElement { ValueKind: JsonValueKind.String } json => bool.TryParse(json.GetString(), out var parsed) && parsed,
			string text => bool.TryParse(text, out var parsed) && parsed,
			_ => false,
		};
}
