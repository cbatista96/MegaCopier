namespace MegaCopier.Engine.Tests;

public sealed class ConnectionTunerTests
{
    /// <summary>Un servidor de mentira: cada conexión da 10 MB/s hasta que se satura en <paramref name="best"/> conexiones.</summary>
    private static double Server(int connections, int best) => connections <= best ? connections * 10e6 : best * 10e6 * (1 - ((connections - best) * 0.08));

    [Theory]
    [InlineData(64, 3)]
    [InlineData(64, 12)]
    [InlineData(64, 40)]
    [InlineData(5, 40)]
    public void SettlesNearWhatTheServerCanTake(int maximum, int best)
    {
        var tuner = new ConnectionTuner(maximum);
        int limit = tuner.Limit;
        for (int step = 0; step < 30; step++)
        {
            limit = tuner.Next(Server(limit, best));
        }

        int expected = Math.Min(maximum, best);
        Assert.InRange(limit, Math.Max(1, expected / 2), Math.Min(maximum, expected + 4));
        Assert.True(Server(limit, best) >= Server(expected, best) * 0.6);
    }

    [Fact]
    public void NoDataChangesNothingAndItNeverPassesTheMaximum()
    {
        var tuner = new ConnectionTuner(3);
        Assert.Equal(2, tuner.Limit);
        Assert.Equal(2, tuner.Next(0));
        for (int i = 0; i < 10; i++)
        {
            Assert.InRange(tuner.Next(1e9 * (i + 1)), 1, 3);
        }
    }
}
