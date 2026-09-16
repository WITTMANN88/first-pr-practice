using Observation.Core.SystemAccess;

namespace Observation.Tests.Fakes;

/// <summary>Мок запуска процессов — никаких реальных команд, см. «Observation.Tests» в плане.</summary>
public sealed class FakeCommandRunner : ICommandRunner
{
    public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = new();

    public Func<string, IReadOnlyList<string>, CommandResult> Handler { get; set; } =
        (_, _) => new CommandResult(0, "True", string.Empty);

    public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        Calls.Add((fileName, arguments));
        return Task.FromResult(Handler(fileName, arguments));
    }
}
