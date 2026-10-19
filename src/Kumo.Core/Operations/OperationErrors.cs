namespace Kumo.Core.Operations;

public enum OperationErrorKind
{
    Unsupported,
    Corrupt,
    TooLarge,
    Encrypted,
    InvalidInput,
    SourceMissing,
    AccessDenied,
    Locked,
    DiskFull,
    Cancelled,
    Unexpected,
}

/// <summary>A failure with a message written for the person using Kumo, not for a developer.</summary>
public sealed class OperationFailedException : Exception
{
    public OperationFailedException(OperationErrorKind kind, string message, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }

    public OperationErrorKind Kind { get; }
}

public static class OperationErrors
{
    private const int ErrorSharingViolation = unchecked((int)0x80070020);
    private const int ErrorLockViolation = unchecked((int)0x80070021);
    private const int ErrorHandleDiskFull = unchecked((int)0x80070027);
    private const int ErrorDiskFull = unchecked((int)0x80070070);

    public static (OperationErrorKind Kind, string Message) Classify(Exception exception) => exception switch
    {
        OperationFailedException failed => (failed.Kind, failed.Message),
        OperationCanceledException => (OperationErrorKind.Cancelled, "Cancelled."),
        FileNotFoundException or DirectoryNotFoundException => (OperationErrorKind.SourceMissing, "The file or folder no longer exists."),
        UnauthorizedAccessException => (OperationErrorKind.AccessDenied, "Kumo doesn't have permission to read or save here."),
        IOException io when io.HResult is ErrorSharingViolation or ErrorLockViolation =>
            (OperationErrorKind.Locked, "The file is in use by another app. Close it and try again."),
        IOException io when io.HResult is ErrorDiskFull or ErrorHandleDiskFull =>
            (OperationErrorKind.DiskFull, "There isn't enough free disk space."),
        IOException => (OperationErrorKind.Unexpected, "The file could not be read or written."),
        OutOfMemoryException or InsufficientMemoryException => (OperationErrorKind.TooLarge, "The file is too large to process."),
        _ => (OperationErrorKind.Unexpected, "Something went wrong. Details are in the log."),
    };

    public static OperationFailedException Unsupported(string message) => new(OperationErrorKind.Unsupported, message);

    public static OperationFailedException Corrupt(string message, Exception? inner = null) => new(OperationErrorKind.Corrupt, message, inner);

    public static OperationFailedException TooLarge(string message) => new(OperationErrorKind.TooLarge, message);

    public static OperationFailedException InvalidInput(string message) => new(OperationErrorKind.InvalidInput, message);
}
