namespace EmeraldJournal.Services {
    public static class SaveHelper {
        private const int DebounceDelay = 1000;
        private static readonly Dictionary<string, CancellationTokenSource> _map = new();

        public static void Debounce(string key, Func<Task> action) {
            if (_map.TryGetValue(key, out var oldCts))
                oldCts.Cancel();

            var cts = new CancellationTokenSource();
            _map[key] = cts;
            var tok = cts.Token;

            _ = Task.Run(async () => {
                try {
                    await Task.Delay(DebounceDelay, tok);
                    if (!tok.IsCancellationRequested)
                        await action();          
                } catch (TaskCanceledException) {  }
            });
        }
    }
}
