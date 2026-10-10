namespace Divoom;

public sealed class AudioUnavailableException : IOException
{
    public AudioUnavailableException(string message, Exception? innerException = null) : base(message, innerException) { }
}
