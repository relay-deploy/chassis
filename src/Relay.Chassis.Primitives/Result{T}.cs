namespace Relay.Chassis.Primitives;

public sealed class Result<T> : Result
{
    private readonly T? value;

    internal Result(T value)
        : base(null) => this.value = value;

    internal Result(Error error)
        : base(error)
    {
    }

    public T Value => IsSuccess ? value! : throw new InvalidOperationException("A failed result has no value.");

    public static implicit operator Result<T>(T value) => new(value);

    public static implicit operator Result<T>(Error error) => new(error);

    public Result<TNext> Map<TNext>(Func<T, TNext> map) =>
        IsSuccess ? Success(map(Value)) : Failure<TNext>(Error);

    public Result<TNext> Bind<TNext>(Func<T, Result<TNext>> bind) =>
        IsSuccess ? bind(Value) : Failure<TNext>(Error);

    public async Task<Result<TNext>> BindAsync<TNext>(Func<T, Task<Result<TNext>>> bind) =>
        IsSuccess ? await bind(Value).ConfigureAwait(false) : Failure<TNext>(Error);

    public TOut Match<TOut>(Func<T, TOut> success, Func<Error, TOut> failure) =>
        IsSuccess ? success(Value) : failure(Error);
}
