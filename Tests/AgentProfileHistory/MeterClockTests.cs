using Gurux.DLMS;
using Xunit;
using Gurux.DLMS.AMI.Agent.Worker;
using Gurux.DLMS.AMI.Shared.DTOs;
using Gurux.DLMS.Objects;

namespace AgentProfileHistory;

/// <summary>
/// A meter stores profile clocks without a time zone deviation and compares a range selection with its own local stamps.
/// The fake meter below does that, inclusive at both ends as some meters do.
/// </summary>
public sealed class MeterClockTests
{
    private static readonly TimeZoneInfo Copenhagen = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");

    [Theory]
    [InlineData("2026-11-23T23:00:00Z", "2026-11-24T00:00:00")]
    [InlineData("2026-06-30T22:00:00Z", "2026-07-01T00:00:00")]
    public void Boundary_is_the_wall_clock_of_the_meter(string utc, string local)
    {
        var result = GXProfileHistoryReader.ToMeterClock(DateTimeOffset.Parse(utc), Copenhagen);
        Assert.Equal(DateTime.Parse(local), result);
        Assert.Equal(DateTimeKind.Unspecified, result.Kind);
    }

    /// <summary>One local day, 15 minute rows stamped at the start, in a meter that holds three days.</summary>
    private static async Task<int> StoredRows(Func<DateTimeOffset, DateTime> toMeter)
    {
        var rows = Enumerable.Range(0, 3 * 96)
            .Select(i => new DateTime(2026, 11, 22, 0, 0, 0).AddMinutes(15 * i)).ToList();
        var job = new GXProfileReadInterval
        {
            IntervalId = Guid.NewGuid(), TaskId = Guid.NewGuid(), AttributeId = Guid.NewGuid(),
            Start = DateTimeOffset.Parse("2026-11-22T23:00:00Z"), End = DateTimeOffset.Parse("2026-11-23T23:00:00Z"),
        };
        GXProfileCommit? commit = null;
        await GXProfileHistoryReader.Execute([job], 0, 900,
            (start, end) =>
            {
                var from = toMeter(start);
                var to = toMeter(end);
                var array = new GXArray();
                foreach (var stamp in rows.Where(r => r >= from && r <= to))
                {
                    array.Add(new GXStructure { new GXDateTime(new DateTimeOffset(stamp, Copenhagen.GetUtcOffset(stamp))) });
                }
                return array;
            },
            c => { commit = c; return Task.CompletedTask; });
        return commit!.Rows.Length;
    }

    [Fact]
    public async Task Utc_boundaries_lose_the_last_rows_of_the_day()
        => Assert.Equal(93, await StoredRows(t => t.UtcDateTime));

    [Fact]
    public async Task Meter_clock_boundaries_return_the_whole_day()
        => Assert.Equal(96, await StoredRows(t => GXProfileHistoryReader.ToMeterClock(t, Copenhagen)));
}
