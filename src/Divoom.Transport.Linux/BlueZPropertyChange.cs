namespace Divoom;

internal sealed record BlueZPropertyChange(string Interface, IDictionary<string, object> Changed, string[] Invalidated);
