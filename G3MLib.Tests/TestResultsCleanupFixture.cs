using Xunit;

[assembly: AssemblyFixture<G3MLib.Tests.TestResultsCleanupFixture>]

namespace G3MLib.Tests;

public sealed class TestResultsCleanupFixture : IDisposable
{
    private readonly bool _preserveTestResults = Environment.GetCommandLineArgs()
        .Any(argument => argument.StartsWith("--results-directory", StringComparison.Ordinal));

    public TestResultsCleanupFixture()
    {
        if (!_preserveTestResults)
        {
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            DeleteDefaultTestResults();
        }
    }

    public void Dispose()
    {
        if (!_preserveTestResults)
            DeleteDefaultTestResults();
    }

    private void OnProcessExit(object? sender, EventArgs e) => DeleteDefaultTestResults();

    private static void DeleteDefaultTestResults()
    {
        var path = Path.Combine(Environment.CurrentDirectory, "TestResults");
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
