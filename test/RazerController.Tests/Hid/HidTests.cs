using KeystoneDigital.RazerController.Hid;
using NUnit.Framework;

namespace KeystoneDigital.RazerController.Tests.Hid;

[TestFixture]
public sealed class HidTests
{
	[Test]
	public void Crc_is_the_xor_of_bytes_2_to_87()
	{
		var report = RazerReport.Build(0x04, 0x85, 0x07, RazerReport.NoStore);
		byte expected = 0;
		for (var i = 2; i < 88; i++) expected ^= report[i];

		Assert.That(report[88], Is.EqualTo(expected));
		Assert.That(report[6], Is.EqualTo(0x04));
		Assert.That(report[7], Is.EqualTo(0x85));
	}

	[Test]
	public void Exchange_skips_replies_that_belong_to_Synapse()
	{
		var report = RazerReport.Build(0x04, 0x85, 0x07, RazerReport.NoStore);
		var lighting = new byte[91];
		lighting[7] = 0x0F;
		lighting[8] = 0x02;
		var ours = new byte[91];
		ours[1] = 2;
		ours[7] = 0x04;
		ours[8] = 0x85;
		var transport = new FakeTransport(lighting, ours);

		var reply = new RazerHidDevice(transport, replyDelayMs: 0).Exchange(report);

		Assert.That(reply, Is.EqualTo(ours));
		Assert.That(transport.SetCount, Is.EqualTo(2));
	}

	[Test]
	public void Exchange_gives_up_after_max_attempts()
	{
		var unrelated = new byte[91];
		unrelated[7] = 0x0F;
		var transport = new FakeTransport { Responder = _ => unrelated };

		var reply = new RazerHidDevice(transport, maxAttempts: 3, replyDelayMs: 0).Exchange(RazerReport.Build(0x04, 0x85, 0x07));

		Assert.That(reply, Is.Null);
		Assert.That(transport.SetCount, Is.EqualTo(3));
	}

	[Test]
	public void Stages_round_trip_through_payload()
	{
		var stages = new DpiStages(2, [new(1, 800, 800), new(2, 1600, 1600), new(3, 3200, 3200)]);
		var reply = new byte[91];
		reply[1] = (byte)RazerStatus.Success;
		Array.Copy(stages.ToPayload(2, RazerReport.VarStore), 0, reply, 9, DpiStages.PayloadSize);

		var parsed = DpiStages.Parse(reply);

		Assert.That(parsed, Is.Not.Null);
		Assert.That(parsed!.ActiveStageId, Is.EqualTo(2));
		Assert.That(parsed.Stages, Is.EqualTo(stages.Stages));
	}

	[Test]
	public void Parse_rejects_a_failed_status() => Assert.That(DpiStages.Parse(new byte[91]), Is.Null);

	private static readonly DpiStages ThreeStages = new(1, [new(1, 800, 800), new(2, 1600, 1600), new(3, 3200, 3200)]);

	[Test]
	public void Stepping_up_moves_to_the_next_stage() => Assert.That(DpiStepper.NextStage(ThreeStages, 800, up: true), Is.EqualTo((byte)2));

	[Test]
	public void Stepping_down_moves_to_the_previous_stage() => Assert.That(DpiStepper.NextStage(ThreeStages, 1600, up: false), Is.EqualTo((byte)1));

	[Test]
	public void Stepping_past_the_top_is_a_no_op() => Assert.That(DpiStepper.NextStage(ThreeStages, 3200, up: true), Is.Null);

	[Test]
	public void Stepping_up_from_a_temporary_dpi_lands_on_the_next_higher_stage() => Assert.That(DpiStepper.NextStage(ThreeStages, 1000, up: true), Is.EqualTo((byte)2));

	[Test]
	public void Stepping_down_from_a_temporary_dpi_lands_on_the_next_lower_stage() => Assert.That(DpiStepper.NextStage(ThreeStages, 1000, up: false), Is.EqualTo((byte)1));

	[Test]
	public void Stage_number_counts_in_dpi_order()
	{
		var unordered = new DpiStages(1, [new(1, 3200, 3200), new(2, 800, 800), new(3, 1600, 1600)]);

		Assert.That(DpiStepper.StageAt(unordered, 1), Is.EqualTo((byte)2));
		Assert.That(DpiStepper.StageAt(unordered, 3), Is.EqualTo((byte)1));
	}

	[Test]
	public void Stage_number_outside_the_stages_is_null()
	{
		Assert.That(DpiStepper.StageAt(ThreeStages, 0), Is.Null);
		Assert.That(DpiStepper.StageAt(ThreeStages, 4), Is.Null);
	}
}
