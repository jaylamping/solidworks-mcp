internal sealed record WorkerError(
    string Code,
    string Message,
    string Category,
    string? Hresult = null,
    int? SwErrorCode = null,
    string? SwErrorName = null,
    string? ComInterface = null,
    Dictionary<string, object?>? Context = null,
    string[]? Remediation = null,
    string? DocLink = null,
    WorkerError? CausedBy = null);

internal sealed class WorkerException : Exception
{
    public WorkerError Error { get; }

    public WorkerException(WorkerError error)
        : base(error.Message)
    {
        Error = error;
    }

    public static WorkerException Validation(
        string code,
        string message,
        Dictionary<string, object?> context,
        string[]? remediation = null) =>
        new(new WorkerError(code, message, "validation", Context: context, Remediation: remediation));

    public static WorkerException Worker(
        string code,
        string message,
        Dictionary<string, object?> context,
        string[]? remediation = null) =>
        new(new WorkerError(code, message, "worker", Context: context, Remediation: remediation));

    public static WorkerException FromCom(
        Exception ex,
        string comInterface,
        Dictionary<string, object?> context) =>
        new(SwErrorDecoder.FromException(ex, comInterface, context));
}
