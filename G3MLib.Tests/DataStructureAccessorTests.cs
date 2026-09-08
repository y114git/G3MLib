using G3MLib.Analyzer.Mock;
using G3MLib.Analyzer.Decompiler;
using Xunit;

namespace G3MLib.Tests;

public class DataStructureAccessorTests
{
    [Fact]
    public void NonArrayAccessorsCompileAndRoundTrip()
    {
        GameContextMock gameContext = new();
        TestUtil.VerifyRoundTrip("""
            list[| 0] = 1;
            value = list[| 0];
            map[? "key"] = 2;
            value2 = map[? "key"];
            grid[# 0, 1] = 3;
            value3 = grid[# 0, 1];
            data_struct[$ "key"] = 4;
            value4 = data_struct[$ "key"];
            """, gameContext: gameContext);
    }

    [Fact]
    public void AccessorCleanupCanBeDisabled()
    {
        TestUtil.VerifyRoundTrip("""
            ds_list_set(list, 0, 1);
            value = ds_list_find_value(list, 0);
            variable_struct_set(data_struct, "key", 2);
            value2 = variable_struct_get(data_struct, "key");
            """, decompileSettings: new DecompileSettings
            {
                CleanupDataStructureAccessors = false,
                CleanupStructVariableAccessors = false
            });
    }

    [Fact]
    public void CompoundNonArrayAccessorAssignmentReportsAnError()
    {
        Assert.Throws<TestCompileErrorException>(() => TestUtil.CompileCode("list[| 0] += 1;"));
    }
}
