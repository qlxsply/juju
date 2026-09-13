namespace Juju.Core.Errors;

/// <summary>跨层可识别的领域错误分类，避免界面依赖易变的异常消息文本。</summary>
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

/// <summary>
/// 携带领域错误码的应用异常。主构造函数直接转发消息和内部异常，类似 Java 异常构造器中的
/// <c>super(message, cause)</c>；内部异常保留底层 IO 或解析失败的诊断信息。
/// </summary>
public sealed class JujuException(ErrorCode code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>供上层转换为用户提示或恢复策略的稳定错误码。</summary>
    public ErrorCode Code { get; } = code;
}