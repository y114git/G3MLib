using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using G3MLib.DataFile.Util;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Xunit;

namespace G3MLib.Tests;

public sealed class ScriptingUtilTests
{
    [Fact("ScriptingUtilTests.cs", 13)]
    public void PrettifyExceptionGroupsAggregateExceptionTypes()
    {
        string pretty = ScriptingUtil.PrettifyException((Exception)new AggregateException(new InvalidOperationException("first"), new InvalidOperationException("second")));
        Assert.Contains("System.InvalidOperationException (x2)", pretty);
    }

    [Fact("ScriptingUtilTests.cs", 23)]
    public async Task PrettifyExceptionIncludesFailingScriptLine()
    {
        string scriptPath = Path.Combine(Path.GetTempPath(), $"G3MLib-{Guid.NewGuid():N}.csx");
        try
        {
            await File.WriteAllTextAsync(scriptPath, "throw new InvalidOperationException(\"boom\");", TestContext.Current.CancellationToken);
            ScriptOptions options = ScriptingUtil.CreateDefaultScriptOptions().WithFilePath(scriptPath).WithFileEncoding(Encoding.UTF8);
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
            string pretty = ScriptingUtil.PrettifyException(in exception);
            Assert.Contains("Line 1 in script " + Path.GetFileName(scriptPath) + ": throw new InvalidOperationException(\"boom\");", pretty);
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }
}
