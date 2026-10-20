namespace Evenmere.Tools.Paths;

/// <summary>Finds the repository root: the folder that holds <c>Evenmere.sln</c>.</summary>
internal static class RepoLocator
{
    /// <summary>The file that marks the repository root.</summary>
    public const string SolutionFileName = "Evenmere.sln";

    /// <summary>Walks up from a directory to the first folder that contains the solution file.</summary>
    /// <param name="startDirectory">The directory to start from.</param>
    /// <returns>The repository root, or <see langword="null"/> if no folder above <paramref name="startDirectory"/> holds the solution.</returns>
    public static string? FindRoot(string startDirectory)
    {
        for (DirectoryInfo? directory = new(startDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    /// <summary>Finds the repository root from the current directory, then (so the tool also works when run from elsewhere) from the executable's directory.</summary>
    /// <param name="environment">The directories to search from.</param>
    /// <returns>The repository root, or <see langword="null"/> if neither search finds it.</returns>
    public static string? FindRoot(ToolEnvironment environment) =>
        FindRoot(environment.CurrentDirectory) ?? FindRoot(environment.ExecutableDirectory);
}
