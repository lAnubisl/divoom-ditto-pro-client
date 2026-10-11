namespace Divoom;

public sealed class AudioUnavailableException : IOException
{
    public AudioUnavailableException(string message) : this(message, innerException: null) { }

    public AudioUnavailableException(string message, Exception? innerException) : base(message, innerException) { }
}
