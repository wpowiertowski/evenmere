namespace Evenmere.Tools;

/// <summary>
/// Everything the commands touch outside their own arguments: the output streams and the two directories used to find the
/// repository. Passing it in (instead of reading <see cref="Console"/> and the process state) keeps the commands testable.
/// </summary>
/// <param name="Output">Standard output: the machine-readable result of a command.</param>
/// <param name="Error">Standard error: diagnostics, the chosen content roots and timing tables.</param>
/// <param name="CurrentDirectory">The directory relative <c>--content</c> paths resolve against, and where the search for the repository starts.</param>
/// <param name="ExecutableDirectory">Where the search for the repository starts if the current directory is outside it.</param>
internal sealed record ToolEnvironment(TextWriter Output, TextWriter Error, string CurrentDirectory, string ExecutableDirectory);
