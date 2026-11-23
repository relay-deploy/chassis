using System.Diagnostics;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using Relay.Chassis.Primitives;

namespace Relay.Chassis.Grpc;

public static class ErrorStatus
{
    private const string Domain = "relay";

    public static RpcException ToRpcException(this Error error) =>
        new Google.Rpc.Status
        {
            Code = (int)ToCode(error.Kind),
            Message = error.Message,
            Details = { Any.Pack(new ErrorInfo { Reason = error.Code, Domain = Domain }) },
        }.ToRpcException();

    public static Error ToError(this RpcException exception)
    {
        var reason = exception.GetRpcStatus()?.GetDetail<ErrorInfo>()?.Reason;
        var code = reason ?? $"rpc.{exception.StatusCode.ToString().ToLowerInvariant()}";
        return new Error(ToKind(exception.StatusCode), code, exception.Status.Detail);
    }

    public static T OrThrow<T>(this Result<T> result) =>
        result.IsSuccess ? result.Value : throw result.Error.ToRpcException();

    public static void OrThrow(this Result result)
    {
        if (result.IsFailure)
        {
            throw result.Error.ToRpcException();
        }
    }

    private static Code ToCode(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => Code.InvalidArgument,
        ErrorKind.NotFound => Code.NotFound,
        ErrorKind.Conflict => Code.FailedPrecondition,
        ErrorKind.Unavailable => Code.Unavailable,
        _ => throw new UnreachableException($"Unmapped error kind {kind}."),
    };

    private static ErrorKind ToKind(StatusCode code) => code switch
    {
        StatusCode.InvalidArgument or StatusCode.OutOfRange => ErrorKind.Validation,
        StatusCode.NotFound => ErrorKind.NotFound,
        StatusCode.AlreadyExists or StatusCode.FailedPrecondition or StatusCode.Aborted => ErrorKind.Conflict,
        _ => ErrorKind.Unavailable,
    };
}
