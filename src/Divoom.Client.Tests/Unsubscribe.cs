internal sealed class Unsubscribe(Action dispose) : IDisposable { public void Dispose() => dispose(); }
