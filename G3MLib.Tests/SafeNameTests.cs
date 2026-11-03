using G3MLib.Modding.Resources;

namespace G3MLib.Tests;

public class SafeNameTests
{
	[Theory]
	[InlineData("..", "_")]
	[InlineData(".", "_")]
	[InlineData("name. ", "name")]
	[InlineData("CON", "_CON")]
	[InlineData("nul.txt", "_nul.txt")]
	[InlineData("a/b", "a_b")]
	[InlineData("spr_player", "spr_player")]
	public void NamesStayInsideTheirFolder(string name, string expected)
	{
		Assert.Equal(expected, ResourceExportService.SafeName(name));
	}
}
