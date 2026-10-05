namespace RemoteClipboard.Core.Tests.TestSupport;

internal static class Eventually
{
    public static async Task TrueAsync(Func<bool> condition, string because, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out waiting until {because}.");
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
    }
}
