using System.Text;
using G3MLib.DataFile.Util;
using Microsoft.CodeAnalysis.CSharp.Scripting;

namespace G3MLib.Tests;

public sealed class ScriptingUtilTests
{
    [Fact("ScriptingUtilTests.cs", 13)]
    public void PrettifyExceptionGroupsAggregateExceptionTypes()
    {
        var pretty = ScriptingUtil.PrettifyException(new AggregateException(new InvalidOperationException("first"), new InvalidOperationException("second")));
        Assert.Contains("System.InvalidOperationException (x2)", pretty, StringComparison.Ordinal);
    }

    [Fact("ScriptingUtilTests.cs", 23)]
    public async Task PrettifyExceptionIncludesFailingScriptLine()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"G3MLib-{Guid.NewGuid():N}.csx");
        try
        {
            await File.WriteAllTextAsync(scriptPath, "throw new InvalidOperationException(\"boom\");", TestContext.Current.CancellationToken);
            var options = ScriptingUtil.CreateDefaultScriptOptions().WithFilePath(scriptPath).WithFileEncoding(Encoding.UTF8);
            Exception exception;
            try
            {
                await CSharpScript.RunAsync("throw new InvalidOperationException(\"boom\");", options, null, null, TestContext.Current.CancellationToken);
                throw new InvalidOperationException("Script unexpectedly completed.");
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            Assert.IsType<InvalidOperationException>(exception);
            var pretty = ScriptingUtil.PrettifyException(in exception);
            Assert.Contains("Line 1 in script " + Path.GetFileName(scriptPath) + ": throw new InvalidOperationException(\"boom\");", pretty, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }
}
