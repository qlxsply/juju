namespace Juju.Core.Errors;

public enum ErrorCode
{
    InvalidJson,
    DocumentNotFound,
    DocumentAlreadyExists,
    InvalidDocumentName,
    ExternalModificationConflict,
    DataRootUnavailable,
    DataRootInvalid,
    ClipboardFailed,
    ShortcutRegistrationFailed,
    StorageIoError,
    MetadataCorrupted,
    ImportFailed,
    WebViewInitializationFailed,
    DocumentReadFailed,
    DocumentWriteFailed,
    DocumentRenameFailed,
    DocumentDeleteFailed,
    InvalidDocumentOrder
}

public sealed class JujuException(ErrorCode code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public ErrorCode Code { get; } = code;
}