namespace OwlWidgets.Common
{
    /// <summary>
    /// Waits by the wall clock. The timers behind <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
    /// count unbiased time on Windows and stop while the computer sleeps or hibernates,
    /// so a long delay would be extended by the whole time asleep.
    /// </summary>
    public static class WallClock
    {
        /// <summary>
        /// How often the clock is checked. After waking up, the wait ends within this time:
        /// it also gives the network a moment to come back.
        /// </summary>
        private static readonly TimeSpan Step = TimeSpan.FromSeconds(30);

        public static async Task DelayAsync(TimeSpan delay, CancellationToken token)
        {
            var due = DateTime.UtcNow + delay;
            while (true)
            {
                var remaining = due - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    return;
                await Task.Delay(remaining < Step ? remaining : Step, token);
            }
        }
    }
}
