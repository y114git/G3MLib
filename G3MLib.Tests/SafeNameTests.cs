using G3MLib.Modding.Resources;

namespace G3MLib.Tests;

public class SafeNameTests
{
	[Theory]
	[InlineData("a/b", "a_b")]
	[InlineData("a:b", "a_b")]
	[InlineData("spr_player", "spr_player")]
	public void OrdinaryNamesKeepTheirShape(string name, string expected)
	{
		Assert.Equal(expected, ResourceExportService.SafeName(name));
	}

	[Theory]
	[InlineData("..")]
	[InlineData(".")]
	[InlineData("name. ")]
	[InlineData("CON")]
	[InlineData("nul.txt")]
	public void RiskyNamesGetAUniqueSafeForm(string name)
	{
		var safe = ResourceExportService.SafeName(name);
		Assert.StartsWith("_", safe);
		Assert.DoesNotContain("/", safe);
		Assert.NotEqual(".", safe.TrimEnd());
		Assert.NotEqual(ResourceExportService.SafeName("_" + name.TrimEnd('.', ' ')), safe);
		Assert.NotEqual(ResourceExportService.SafeName(name + "x"), safe);
	}

	[Fact]
	public void NamesThatDifferOnlyByTrailingDotsDoNotCollide()
	{
		Assert.NotEqual(ResourceExportService.SafeName("spr_a"), ResourceExportService.SafeName("spr_a."));
	}
}
