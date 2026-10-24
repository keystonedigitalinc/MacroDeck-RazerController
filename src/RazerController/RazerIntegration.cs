using KeystoneDigital.RazerController.Actions;
using KeystoneDigital.RazerController.Autostart;
using KeystoneDigital.RazerController.Chroma;
using KeystoneDigital.RazerController.Hid;
using KeystoneDigital.RazerController.Synapse;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Variables;
using KeystoneDigital.RazerController.Settings;
using KeystoneDigital.RazerController.Variables;
using Microsoft.Extensions.DependencyInjection;

namespace KeystoneDigital.RazerController;

public sealed class RazerIntegration : IPluginIntegration, IVariableProvider, IConfigFlowProvider
{
	private readonly MouseDpiService _dpi;
	private readonly ChromaClient _chroma;
	private readonly ISynapseProcess _process;

	internal RazerIntegration(SynapseService synapse, MouseDpiService dpi, IAutostartRegistry autostart, ISynapseControlIntent intent, InspectorGuard guard, ISynapseProcess process, ChromaClient chroma)
	{
		Guard = guard;
		_process = process;
		Intent = intent;
		_dpi = dpi;
		_chroma = chroma;
		Synapse = synapse;
		Autostart = autostart;
		Actions =
		[
			new SwitchProfileAction(synapse),
			new LightingEffectAction(synapse),
			new BrightnessAction(synapse),
			new GamingModeAction(synapse),
			new StepDpiAction(dpi, synapse),
			new TemporaryDpiAction(dpi),
			new DpiStageAction(dpi, synapse),
			new ChromaLightingAction(chroma),
			new CycleLightingAction(synapse),
			new RestartSynapseAction(autostart, process, intent),
		];
	}

	public IReadOnlyList<IActionDefinition> Actions { get; }

	internal SynapseService Synapse { get; }

	internal IAutostartRegistry Autostart { get; }

	internal ISynapseControlIntent Intent { get; }

	internal InspectorGuard Guard { get; }

	public static RazerIntegration Create(IServiceProvider services) => new(
		services.GetRequiredService<SynapseService>(),
		services.GetRequiredService<MouseDpiService>(),
		services.GetRequiredService<IAutostartRegistry>(),
		services.GetRequiredService<ISynapseControlIntent>(),
		services.GetRequiredService<InspectorGuard>(),
		services.GetRequiredService<ISynapseProcess>(),
		services.GetRequiredService<ChromaClient>());

	public Task InitializeAsync(IIntegrationContext context) => _dpi.RefreshAsync(CancellationToken.None);

	public Task ShutdownAsync() => Task.CompletedTask;

	public IReadOnlyList<VariableDefinition> Variables => RazerVariables.Definitions;

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(RazerVariables.Read(localId, Synapse, _dpi, Guard.Issue, _chroma));

	public bool RequiresConfiguration => false;

	public bool AllowsMultipleConfigurations => false;

	public IConfigFlow CreateConfigFlow() => new InspectorConfigFlow(Autostart, Synapse, Intent, Guard, _process);
}
