using Evenmere.Content.Locale;

namespace Evenmere.Game.Tests;

internal static class TestLocales
{
    internal static LocaleTable Table()
    {
        string repo = FindRepoRoot();
        return LocaleTable.Load(Path.Combine(repo, "content"), "en").Table;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Evenmere.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Evenmere.sln was not found above " + AppContext.BaseDirectory);
    }
}
