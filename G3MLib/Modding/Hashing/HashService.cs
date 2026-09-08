namespace G3MLib.Modding.Hashing;

public static class HashService
{
    public static Task<string> ComputeFileHashAsync(string filePath) => FileHash.ComputeMd5Async(filePath);

    public static string ComputeFileHash(string filePath) => FileHash.ComputeMd5(filePath);

    public static bool FileSizesMatch(string file1, string file2) => FileHash.HaveSameSize(file1, file2);
}
