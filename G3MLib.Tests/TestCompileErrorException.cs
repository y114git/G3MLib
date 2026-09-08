using System;

namespace G3MLib.Tests;

public class TestCompileErrorException : Exception
{
    public TestCompileErrorException(string message)
        : base(message)
    {
    }

    public TestCompileErrorException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
