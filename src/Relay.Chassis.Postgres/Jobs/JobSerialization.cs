using System.Text.Json;

namespace Relay.Chassis.Postgres.Jobs;

internal static class JobSerialization
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
