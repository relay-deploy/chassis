namespace Relay.Chassis.Primitives;

public class Result
{
    private static readonly Result Succeeded = new(null);

    private readonly Error? _error;

    protected Result(Error? error) => _error = error;

    public bool IsSuccess => _error is null;

    public bool IsFailure => !IsSuccess;

    public Error Error => _error ?? throw new InvalidOperationException("A successful result has no error.");

    public static Result Success() => Succeeded;

    public static Result<T> Success<T>(T value) => new(value);

    public static Result Failure(Error error) => new(error);

    public static Result<T> Failure<T>(Error error) => new(error);

    public static implicit operator Result(Error error) => Failure(error);
}
