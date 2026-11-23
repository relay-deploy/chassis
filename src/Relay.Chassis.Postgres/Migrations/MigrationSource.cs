using System.Reflection;

namespace Relay.Chassis.Postgres.Migrations;

public sealed class MigrationSource(Assembly assembly)
{
    private const string Prefix = "migrations/";

    public IEnumerable<MigrationScript> Scripts()
    {
        var source = assembly.GetName().Name;
        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith(Prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            yield return new MigrationScript($"{source}/{resource[Prefix.Length..]}", reader.ReadToEnd());
        }
    }
}
