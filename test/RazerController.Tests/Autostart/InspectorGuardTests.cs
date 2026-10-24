using KeystoneDigital.RazerController.Autostart;
using KeystoneDigital.RazerController.Synapse;
using KeystoneDigital.RazerController.Tests.Synapse;
using NUnit.Framework;

namespace KeystoneDigital.RazerController.Tests.Autostart;

internal sealed class FakeIntent(bool? value) : ISynapseControlIntent
{
	public bool? Value { get; private set; } = value;

	public bool? Get() => Value;

	public void Set(bool enabled) => Value = enabled;
}

internal sealed class FakeProcess(SynapseProcessState state) : ISynapseProcess
{
	public SynapseProcessState State { get; set; } = state;

	public List<string> Restarts { get; } = [];

	public SynapseProcessState GetState() => State;

	public Task RestartAsync(string commandLine, CancellationToken cancellationToken)
	{
		Restarts.Add(commandLine);
		State = SynapseProcessState.RunningWithFlag;
		return Task.CompletedTask;
	}
}

[TestFixture]
public sealed class InspectorGuardTests
{
	internal const string Bare = "\"C:\\Program Files\\Razer\\RazerAppEngine\\RazerAppEngine.exe\" --url-params=apps=synapse --autoStart=1";
	internal const string Flagged = Bare + " --inspect=127.0.0.1:9229";

	private static InspectorGuard Guard(FakeAutostartRegistry registry, FakeIntent intent, FakeProcess process) =>
		new(registry, intent, process, SynapseServiceTests.Log);

	[Test]
	public void Reports_a_restart_without_restarting_or_writing()
	{
		var registry = new FakeAutostartRegistry(Flagged);
		var process = new FakeProcess(SynapseProcessState.RunningWithoutFlag);
		var guard = Guard(registry, new FakeIntent(true), process);

		guard.Check(SynapseStatus.InspectorUnavailable);

		Assert.That(guard.Issue, Is.EqualTo(SetupIssue.RestartNeeded));
		Assert.That(process.Restarts, Is.Empty);
		Assert.That(registry.Value, Is.EqualTo(Flagged));
	}

	[Test]
	public void Reports_a_missing_flag_without_putting_it_back()
	{
		var registry = new FakeAutostartRegistry(Bare);
		var process = new FakeProcess(SynapseProcessState.RunningWithoutFlag);
		var guard = Guard(registry, new FakeIntent(true), process);

		guard.Check(SynapseStatus.InspectorUnavailable);

		Assert.That(guard.Issue, Is.EqualTo(SetupIssue.FlagMissing));
		Assert.That(registry.Value, Is.EqualTo(Bare));
		Assert.That(process.Restarts, Is.Empty);
	}

	[TestCase(null)]
	[TestCase(false)]
	public void Reports_nothing_unless_the_user_turned_control_on(bool? choice)
	{
		var guard = Guard(new FakeAutostartRegistry(Bare), new FakeIntent(choice), new FakeProcess(SynapseProcessState.RunningWithoutFlag));

		guard.Check(SynapseStatus.InspectorUnavailable);

		Assert.That(guard.Issue, Is.Null);
	}

	[Test]
	public void A_working_connection_is_not_taken_as_consent()
	{
		var intent = new FakeIntent(null);
		var guard = Guard(new FakeAutostartRegistry(Flagged), intent, new FakeProcess(SynapseProcessState.RunningWithFlag));

		guard.Check(SynapseStatus.Ready);

		Assert.That(intent.Value, Is.Null);
		Assert.That(guard.Issue, Is.Null);
	}

	[Test]
	public void The_issue_clears_once_Synapse_answers()
	{
		var process = new FakeProcess(SynapseProcessState.RunningWithoutFlag);
		var guard = Guard(new FakeAutostartRegistry(Flagged), new FakeIntent(true), process);

		guard.Check(SynapseStatus.InspectorUnavailable);
		guard.Check(SynapseStatus.Ready);

		Assert.That(guard.Issue, Is.Null);
	}

	[Test]
	public void Missing_Synapse_install_is_left_alone()
	{
		var registry = new FakeAutostartRegistry(null);
		var guard = Guard(registry, new FakeIntent(true), new FakeProcess(SynapseProcessState.NotRunning));

		guard.Check(SynapseStatus.InspectorUnavailable);

		Assert.That(registry.Value, Is.Null);
		Assert.That(guard.Issue, Is.Null);
	}

	[Test]
	public void The_issue_shows_in_the_status_value()
	{
		Assert.That(SynapseStatus.InspectorUnavailable.ToVariableValue(SetupIssue.RestartNeeded), Is.EqualTo("restart_needed"));
		Assert.That(SynapseStatus.InspectorUnavailable.ToVariableValue(SetupIssue.FlagMissing), Is.EqualTo("flag_missing"));
		Assert.That(SynapseStatus.ControlOff.ToVariableValue(null), Is.EqualTo("control_off"));
	}


	[Test]
	public void File_intent_round_trips_and_starts_unknown()
	{
		var dir = Path.Combine(Path.GetTempPath(), "razer-intent-" + Guid.NewGuid().ToString("N"));
		try
		{
			var intent = new FileSynapseControlIntent(dir);
			Assert.That(intent.Get(), Is.Null);

			intent.Set(true);
			Assert.That(new FileSynapseControlIntent(dir).Get(), Is.True);

			intent.Set(false);
			Assert.That(new FileSynapseControlIntent(dir).Get(), Is.False);
		}
		finally
		{
			if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
		}
	}
}

[TestFixture]
public sealed class FlagKeeperTests
{
	[Test]
	public async Task The_flag_comes_back_when_Synapse_drops_it_after_a_restart()
	{
		var registry = new FakeAutostartRegistry(@"""C:\Razer\RazerAppEngine.exe"" --autoStart=1");

		await FlagKeeper.KeepAsync(registry, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(10), CancellationToken.None);

		Assert.That(registry.Value, Does.EndWith(KeystoneDigital.RazerController.Settings.InspectorConfigFlow.InspectFlag));
	}

	[Test]
	public async Task An_entry_that_has_the_flag_is_left_alone()
	{
		var entry = @"""C:\Razer\RazerAppEngine.exe"" --autoStart=1 " + KeystoneDigital.RazerController.Settings.InspectorConfigFlow.InspectFlag;
		var registry = new FakeAutostartRegistry(entry);

		await FlagKeeper.KeepAsync(registry, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(10), CancellationToken.None);

		Assert.That(registry.Value, Is.EqualTo(entry));
	}
}
