namespace Evenmere.Sim.Commands;

/// <summary>
/// The outcome of <see cref="ICommandHandler{TCommand}.Validate"/>: accepted, or rejected with a reason and a detail
/// message. The <see langword="default"/> value is an accepted result.
/// </summary>
public readonly record struct CommandResult
{
    private readonly string? _detail;

    private CommandResult(RejectReason reason, string? detail)
    {
        IsRejected = true;
        Reason = reason;
        _detail = detail;
    }

    /// <summary>Gets a result that accepts the command.</summary>
    public static CommandResult Ok => default;

    /// <summary>Gets a value indicating whether the command was refused.</summary>
    public bool IsRejected { get; }

    /// <summary>Gets the reason the command was refused; meaningful only when <see cref="IsRejected"/> is <see langword="true"/>.</summary>
    public RejectReason Reason { get; }

    /// <summary>Gets the developer-facing explanation; empty when the command was accepted.</summary>
    public string Detail => _detail ?? string.Empty;

    /// <summary>Creates a result that refuses the command.</summary>
    /// <param name="reason">The reason code.</param>
    /// <param name="detail">A short explanation for developers and logs.</param>
    /// <returns>The rejecting result.</returns>
    public static CommandResult Reject(RejectReason reason, string detail) => new(reason, detail);

    /// <summary>Creates a result that refuses the command as <see cref="RejectReason.Invalid"/>.</summary>
    /// <param name="detail">A short explanation for developers and logs.</param>
    /// <returns>The rejecting result.</returns>
    public static CommandResult Invalid(string detail) => new(RejectReason.Invalid, detail);
}
