namespace Relay.Chassis.Primitives;

public class Result
{
    private static readonly Result succeeded = new(null);

    private readonly Error? error;

    protected Result(Error? error) => this.error = error;

    public bool IsSuccess => error is null;

    public bool IsFailure => !IsSuccess;

    public Error Error => error ?? throw new InvalidOperationException("A successful result has no error.");

    public static Result Success() => succeeded;

    public static Result<T> Success<T>(T value) => new(value);

    public static Result Failure(Error error) => new(error);

    public static Result<T> Failure<T>(Error error) => new(error);

    public static implicit operator Result(Error error) => Failure(error);
}
