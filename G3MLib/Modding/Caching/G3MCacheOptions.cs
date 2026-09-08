namespace G3MLib.Modding.Caching;

public sealed class G3MCacheOptions
{
	public static readonly G3MCacheOptions None = new G3MCacheOptions();

	public string? ReadDirectory { get; init; }

	public string? WriteDirectory { get; init; }

	public bool CanRead => !string.IsNullOrWhiteSpace(ReadDirectory);

	public bool CanWrite => !string.IsNullOrWhiteSpace(WriteDirectory);

	public static G3MCacheOptions FromDirectory(string? directory)
	{
		if (!string.IsNullOrWhiteSpace(directory))
		{
			return new G3MCacheOptions
			{
				ReadDirectory = directory,
				WriteDirectory = directory
			};
		}
		return None;
	}
}
