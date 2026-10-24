using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace KeystoneDigital.RazerController.Synapse;

internal sealed record SynapseServiceOptions(TimeSpan RefreshInterval, TimeSpan ConfirmTimeout, TimeSpan ConfirmPoll, TimeSpan MaxBackoff)
{
	public static SynapseServiceOptions Default { get; } = new(
		TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(10));
}

/// <summary>
/// Keeps a session to Synapse's inspector, a fresh device catalog, and the one path that changes
/// anything in Synapse. Synapse's stored state is the source of truth: a command counts as applied only
/// once a later catalog shows it.
/// </summary>
internal sealed class SynapseService : BackgroundService
{
	private readonly IInspector _inspector;
	private readonly ILogger _logger;
	private readonly SynapseServiceOptions _options;
	private readonly Func<bool> _controlEnabled;
	private readonly ConcurrentDictionary<string, SemaphoreSlim> _deviceGates = new(StringComparer.Ordinal);

	private IReadOnlyList<RazerDeviceInfo> _devices = [];
	private int _status = (int)SynapseStatus.Starting;

	/// <param name="controlEnabled">Whether the user turned Synapse control on in the settings. Until then the
	/// inspector is never contacted, even when its port already answers.</param>
	public SynapseService(IInspector inspector, ILogger logger, SynapseServiceOptions? options = null, Func<bool>? controlEnabled = null)
	{
		_controlEnabled = controlEnabled ?? (() => true);
		_inspector = inspector;
		_logger = logger.ForContext<SynapseService>();
		_options = options ?? SynapseServiceOptions.Default;
	}

	public SynapseStatus Status => (SynapseStatus)Volatile.Read(ref _status);

	/// <summary>Synapse answers, even if a given device is not there; a missing device is then NotFound, not "Synapse unavailable".</summary>
	public bool IsReachable => (Status is SynapseStatus.Ready or SynapseStatus.NoDevices or SynapseStatus.SynapseUnrecognised) && _inspector.IsConnected;

	public IReadOnlyList<RazerDeviceInfo> Devices => Volatile.Read(ref _devices);

	public string? SynapseVersion { get; private set; }

	public RazerDeviceInfo? FindBySerial(string serial) => Devices.FirstOrDefault(d => d.Serial == serial);

	public RazerDeviceInfo? FirstOf(DeviceCategory category) => Devices.FirstOrDefault(d => d.Category == category);

	public async Task RefreshAsync(CancellationToken cancellationToken)
	{
		if (!_controlEnabled())
		{
			_inspector.Disconnect();
			Volatile.Write(ref _devices, []);
			SetStatus(SynapseStatus.ControlOff);
			return;
		}

		try
		{
			if (!_inspector.IsConnected) await _inspector.ConnectAsync(cancellationToken).ConfigureAwait(false);

			var snapshot = await _inspector.EvaluateAsync(InspectorScripts.Catalog, cancellationToken).ConfigureAwait(false);
			var result = SynapseCatalogParser.Parse(snapshot);

			SynapseVersion = result.SynapseVersion;
			Volatile.Write(ref _devices, result.Devices);

			SetStatus(result switch
			{
				{ WindowCount: 0 } => SynapseStatus.SynapseUnrecognised,
				{ Devices.Count: 0 } => SynapseStatus.NoDevices,
				_ => SynapseStatus.Ready,
			});
		}
		catch (SynapseInspectorException ex)
		{
			_inspector.Disconnect();
			SetStatus(ex.Code == "incompatible_synapse" ? SynapseStatus.InspectorUntrusted : SynapseStatus.InspectorUnavailable, ex.Message);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// Synapse's storage is not a contract; an unexpected shape must not end the refresh loop.
			_logger.Error(ex, "Could not read Synapse's device catalog");
			SetStatus(SynapseStatus.SynapseUnrecognised, ex.Message);
		}
	}

	/// <summary>Shows Synapse's own popup for a change the plugin made. A popup that fails is only logged.</summary>
	public async Task ShowPopupAsync(RazerDeviceInfo device, string header, int? dpi, int? dpiPercentage, CancellationToken cancellationToken)
	{
		if (!IsReachable) return;

		try
		{
			await _inspector.EvaluateAsync(InspectorScripts.Popup(device.Channel, header, device.Name, dpi, dpiPercentage), cancellationToken).ConfigureAwait(false);
		}
		catch (SynapseInspectorException ex)
		{
			_logger.Warning("Synapse did not show the popup: {Message}", ex.Message);
		}
	}

	public Task<CommandOutcome> SendAndConfirmAsync(
		string serial, string eventType, object payload, Func<RazerDeviceInfo, bool> isApplied, CancellationToken cancellationToken) =>
		SendAndConfirmAsync(serial, _ => DevicePlan.Send(eventType, payload, isApplied), cancellationToken);

	/// <summary>
	/// Runs <paramref name="plan"/> against the device as it is once this device's previous command has
	/// confirmed, so two quick presses of Next or Toggle act twice instead of both acting on the same
	/// stale state.
	/// </summary>
	public async Task<CommandOutcome> SendAndConfirmAsync(string serial, Func<RazerDeviceInfo, DevicePlan> plan, CancellationToken cancellationToken)
	{
		if (!IsReachable) return CommandOutcome.NotConnected;

		var gate = _deviceGates.GetOrAdd(serial, _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			if (FindBySerial(serial) is not { } device) return CommandOutcome.DeviceNotFound;

			var step = plan(device);
			if (step.Shortcut is { } shortcut) return shortcut;

			var eventType = step.EventType!;
			var isApplied = step.IsApplied!;
			var reply = await _inspector.EvaluateAsync(InspectorScripts.Command(device.Channel, eventType, step.Payload!, step.WindowUpdate), cancellationToken).ConfigureAwait(false);
			var sent = reply is { ValueKind: System.Text.Json.JsonValueKind.Object } value
				&& value.TryGetProperty("sent", out var flag)
				&& flag.ValueKind == System.Text.Json.JsonValueKind.True;

			if (!sent)
			{
				_logger.Warning("Synapse had no renderer to send {EventType} from", eventType);
				return CommandOutcome.NotApplied;
			}

			var deadline = DateTime.UtcNow + _options.ConfirmTimeout;

			while (true)
			{
				await RefreshAsync(cancellationToken).ConfigureAwait(false);
				if (!IsReachable) return CommandOutcome.NotConnected;
				if (FindBySerial(serial) is { } current && isApplied(current)) return CommandOutcome.Applied;
				if (DateTime.UtcNow >= deadline) break;

				await Task.Delay(_options.ConfirmPoll, cancellationToken).ConfigureAwait(false);
			}

			_logger.Warning("Synapse did not confirm {EventType} on {Device} within {Timeout}", eventType, device.Name, _options.ConfirmTimeout);
			return CommandOutcome.NotApplied;
		}
		catch (SynapseInspectorException ex)
		{
			_inspector.Disconnect();
			SetStatus(SynapseStatus.InspectorUnavailable, ex.Message);
			return CommandOutcome.NotConnected;
		}
		finally
		{
			gate.Release();
		}
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var backoff = TimeSpan.FromSeconds(1);

		while (!stoppingToken.IsCancellationRequested)
		{
			await RefreshAsync(stoppingToken).ConfigureAwait(false);

			TimeSpan delay;
			if (Status is SynapseStatus.InspectorUnavailable or SynapseStatus.InspectorUntrusted)
			{
				delay = backoff;
				backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, _options.MaxBackoff.Ticks));
			}
			else
			{
				delay = _options.RefreshInterval;
				backoff = TimeSpan.FromSeconds(1);
			}

			try
			{
				await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}

	private void SetStatus(SynapseStatus status, string? detail = null)
	{
		var previous = (SynapseStatus)Interlocked.Exchange(ref _status, (int)status);
		if (previous == status) return;

		_logger.Information("Synapse status {Previous} -> {Status} (Synapse {Version}, {DeviceCount} devices) {Detail}",
			previous, status, SynapseVersion, Devices.Count, detail ?? "");
	}
}
