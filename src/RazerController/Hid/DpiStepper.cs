namespace KeystoneDigital.RazerController.Hid;

internal static class DpiStepper
{
	/// <summary>The stage one step from <paramref name="currentDpi"/>, or null at either end.</summary>
	public static byte? NextStage(DpiStages stages, int currentDpi, bool up)
	{
		var ordered = stages.Stages.OrderBy(stage => stage.X).ToList();
		if (ordered.Count == 0) return null;

		var index = ordered.FindIndex(stage => stage.X == currentDpi);

		if (index < 0)
		{
			// Off every stage, usually after a temporary DPI.
			return up
				? ordered.FirstOrDefault(stage => stage.X > currentDpi)?.Id ?? ordered[0].Id
				: ordered.LastOrDefault(stage => stage.X < currentDpi)?.Id ?? ordered[^1].Id;
		}

		var target = up ? index + 1 : index - 1;
		if (target < 0 || target >= ordered.Count) return null;

		return ordered[target].Id;
	}

	/// <summary>The stage with the 1-based <paramref name="number"/> in DPI order, as the stage variable counts, or null.</summary>
	public static byte? StageAt(DpiStages stages, int number)
	{
		var ordered = stages.Stages.OrderBy(stage => stage.X).ToList();
		return number >= 1 && number <= ordered.Count ? ordered[number - 1].Id : null;
	}
}
