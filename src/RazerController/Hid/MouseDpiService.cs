using HidSharp;
using Serilog;

namespace KeystoneDigital.RazerController.Hid;

/// <summary>Current DPI; <see cref="StageIndex"/> is 1-based in DPI order, or 0 when the DPI is on no stage.</summary>
internal sealed record MouseDpiState(int Dpi, int StageIndex, int StageCount);

internal enum DpiOutcome
{
	Applied,
	NoMouse,
	NoSuchStage,
	Failed,
}

/// <summary>
/// The first Razer mouse whose control interface answers a DPI read. HID calls block, so each one runs
/// on the thread pool, one at a time.
/// </summary>
internal sealed class MouseDpiService : IDisposable
{
	private const int VendorId = 0x1532;
	private const int ControlReportLength = 91;
	private const int MinDpi = 100;
	private const int MaxDpi = 20000;

	private readonly Func<IReadOnlyList<RazerMouseControl>> _openControls;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _gate = new(1, 1);

	private RazerMouseControl? _mouse;
	private MouseDpiState? _state;

	public MouseDpiService(Func<IReadOnlyList<RazerMouseControl>> openControls, ILogger logger)
	{
		_openControls = openControls;
		_logger = logger.ForContext<MouseDpiService>();
	}

	public MouseDpiState? State => Volatile.Read(ref _state);

	public static MouseDpiService CreateDefault(ILogger logger) => new(OpenSystemControls, logger);

	public Task RefreshAsync(CancellationToken cancellationToken) =>
		RunAsync(mouse => Publish(mouse) ? DpiOutcome.Applied : DpiOutcome.Failed, cancellationToken);

	public Task<DpiOutcome> StepAsync(bool up, CancellationToken cancellationToken) =>
		RunAsync(mouse =>
		{
			var stages = mouse.ReadStages();
			var dpi = mouse.ReadDpi();
			if (stages is null || dpi is null) return DpiOutcome.Failed;

			if (DpiStepper.NextStage(stages, dpi.Value.X, up) is { } stage && !mouse.SetActiveStage(stage)) return DpiOutcome.Failed;

			Publish(mouse);
			return DpiOutcome.Applied;
		}, cancellationToken);

	public Task<DpiOutcome> GoToStageAsync(int number, CancellationToken cancellationToken) =>
		RunAsync(mouse =>
		{
			var stages = mouse.ReadStages();
			if (stages is null) return DpiOutcome.Failed;
			if (DpiStepper.StageAt(stages, number) is not { } stage) return DpiOutcome.NoSuchStage;
			if (!mouse.SetActiveStage(stage)) return DpiOutcome.Failed;

			Publish(mouse);
			return DpiOutcome.Applied;
		}, cancellationToken);

	public Task<DpiOutcome> SetTemporaryAsync(int dpi, CancellationToken cancellationToken) =>
		RunAsync(mouse =>
		{
			var value = Math.Clamp(dpi, MinDpi, MaxDpi);
			if (!mouse.SetDpi(value, value) || mouse.ReadDpi()?.X != value) return DpiOutcome.Failed;

			Publish(mouse);
			return DpiOutcome.Applied;
		}, cancellationToken);

	public void Dispose()
	{
		_mouse?.Dispose();
		_gate.Dispose();
	}

	private async Task<DpiOutcome> RunAsync(Func<RazerMouseControl, DpiOutcome> work, CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			return await Task.Run(() =>
			{
				var mouse = _mouse ??= FindMouse();
				if (mouse is null)
				{
					Volatile.Write(ref _state, null);
					return DpiOutcome.NoMouse;
				}

				var outcome = work(mouse);
				if (outcome == DpiOutcome.Failed)
				{
					// The mouse may have been unplugged or re-enumerated; find it again next time.
					mouse.Dispose();
					_mouse = null;
				}

				return outcome;
			}, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_gate.Release();
		}
	}

	private RazerMouseControl? FindMouse()
	{
		RazerMouseControl? found = null;

		foreach (var control in _openControls())
		{
			if (found is null && control.ReadDpi() is not null) found = control;
			else control.Dispose();
		}

		if (found is null) _logger.Debug("No Razer mouse control interface answered a DPI read");
		return found;
	}

	private bool Publish(RazerMouseControl mouse)
	{
		var dpi = mouse.ReadDpi();
		var stages = mouse.ReadStages();
		if (dpi is null || stages is null) return false;

		var ordered = stages.Stages.OrderBy(stage => stage.X).ToList();
		Volatile.Write(ref _state, new MouseDpiState(dpi.Value.X, ordered.FindIndex(stage => stage.X == dpi.Value.X) + 1, ordered.Count));
		return true;
	}

	private static List<RazerMouseControl> OpenSystemControls()
	{
		var controls = new List<RazerMouseControl>();

		// HidSharp only enumerates here: its Open asks for read/write access, which a mouse collection refuses.
		foreach (var device in DeviceList.Local.GetHidDevices(VendorId))
		{
			int length;
			try
			{
				length = device.GetMaxFeatureReportLength();
			}
			catch (IOException)
			{
				continue;
			}

			if (length != ControlReportLength) continue;
			if (HidFeatureReportTransport.Open(device.DevicePath) is { } transport)
			{
				controls.Add(new RazerMouseControl(new RazerHidDevice(transport)));
			}
		}

		return controls;
	}
}
