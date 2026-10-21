using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Globalization;
using Evenmere.Tools.Commands;
using Evenmere.Tools.Paths;

namespace Evenmere.Tools;

/// <summary>
/// The command line of the <c>evenmere</c> tool: builds the System.CommandLine tree and maps its parse result onto the plain command
/// classes in <c>Evenmere.Tools.Commands</c>. This is the only place that knows about System.CommandLine; the commands take
/// ordinary arguments and writers, so they can be called directly.
/// </summary>
internal static class EvenmereCli
{
    private const string JsonFormat = "json";
    private const string TextFormat = "text";
    private const long DefaultTicks = 1000;
    private const ulong DefaultSeed = 42;

    /// <summary>Parses the arguments and runs the command they name.</summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="output">Standard output.</param>
    /// <param name="error">Standard error.</param>
    /// <param name="currentDirectory">The working directory.</param>
    /// <param name="executableDirectory">The directory of the executable, used to find the repository when the working directory is outside it.</param>
    /// <returns>The exit code: 0 on success, 1 for content errors, 2 for a usage error (see <see cref="ExitCodes"/>).</returns>
    public static int Run(string[] args, TextWriter output, TextWriter error, string currentDirectory, string executableDirectory)
    {
        var environment = new ToolEnvironment(output, error, currentDirectory, executableDirectory);
        RootCommand root = Build(environment);
        ParseResult parsed = root.Parse(args);

        // The library prints the message and the help for a bad command line; the exit code is ours, so it cannot be mistaken for 1 ("content has errors").
        // The default exception handler is off: a crash is a bug and keeps its stack trace and a non-0/1/2 exit code.
        var configuration = new InvocationConfiguration { Output = output, Error = error, EnableDefaultExceptionHandler = false };
        if (parsed.Action is ParseErrorAction)
        {
            parsed.Invoke(configuration);
            return ExitCodes.Usage;
        }

        return parsed.Invoke(configuration);
    }

    /// <summary>Builds the command tree. <c>--help</c> and <c>--version</c> come from System.CommandLine's <see cref="RootCommand"/>.</summary>
    /// <param name="environment">The writers and directories the commands use.</param>
    /// <returns>The root command.</returns>
    internal static RootCommand Build(ToolEnvironment environment)
    {
        var content = new Option<string[]>("--content")
        {
            Description = "A content root directory. May be repeated; roots load in argument order and later roots override earlier ones. "
                + "Relative paths resolve against the current directory. Default: <repo>/content once content/eras exists, otherwise tests/fixtures/content/minimal.",
            HelpName = "dir",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false,
            Recursive = true,
        };

        var root = new RootCommand("Evenmere developer tool: lint the content and run the simulation headless.")
        {
            content,
            BuildLint(content, environment),
            BuildRun(content, environment),
            BuildContentHash(content, environment),
        };
        return root;
    }

    private static Command BuildLint(Option<string[]> content, ToolEnvironment environment)
    {
        var format = new Option<string>("--format")
        {
            Description = "text: one diagnostic per line, then a summary. json: one object {contentHash, counts, diagnostics[]} on stdout.",
            HelpName = "text|json",
            DefaultValueFactory = _ => TextFormat,
        };
        format.AcceptOnlyFromAmong(TextFormat, JsonFormat);

        var lint = new Command("lint", "Check the content: shapes, references and the EVL rules. Exits 1 if there are errors; warnings do not fail.")
        {
            format,
        };
        lint.SetAction(parsed =>
            WithContentRoots(parsed, content, environment, roots =>
                LintCommand.Execute(
                    roots,
                    parsed.GetValue(format) == JsonFormat ? LintFormat.Json : LintFormat.Text,
                    environment.Output,
                    environment.Error)));
        return lint;
    }

    private static Command BuildRun(Option<string[]> content, ToolEnvironment environment)
    {
        var ticks = new Option<long>("--ticks")
        {
            Description = "How many ticks to run.",
            HelpName = "n",
            DefaultValueFactory = _ => DefaultTicks,
            CustomParser = NonNegativeWholeNumber("--ticks"),
        };

        var seed = new Option<ulong>("--seed")
        {
            Description = "The world seed.",
            HelpName = "s",
            DefaultValueFactory = _ => DefaultSeed,
        };

        var hashEvery = new Option<long>("--hash-every")
        {
            Description = "Also print 'tick=<t> hash=<x16>' after every k ticks. 0 prints only the final hash.",
            HelpName = "k",
            DefaultValueFactory = _ => 0,
            CustomParser = NonNegativeWholeNumber("--hash-every"),
        };

        var map = new Option<string?>("--map")
        {
            Description = "The island to start on (a folder under maps/). Omit it for the empty world.",
            HelpName = "id",
        };

        var run = new Command(
            "run",
            "Create a new game and tick it. stdout: optional 'tick=<t> hash=<x16>' lines, then 'hash=<x16>'. stderr: a per-step timing table (step, calls, total ms, mean us per call).")
        {
            ticks,
            seed,
            hashEvery,
            map,
        };
        run.SetAction(parsed =>
            WithContentRoots(parsed, content, environment, roots =>
                RunCommand.Execute(
                    roots,
                    parsed.GetValue(ticks),
                    parsed.GetValue(seed),
                    parsed.GetValue(hashEvery),
                    parsed.GetValue(map),
                    environment.Output,
                    environment.Error)));
        return run;
    }

    private static Command BuildContentHash(Option<string[]> content, ToolEnvironment environment)
    {
        var contentHash = new Command("content-hash", "Print the content hash (16 lowercase hex digits) that saves store to detect changed content.");
        contentHash.SetAction(parsed =>
            WithContentRoots(parsed, content, environment, roots =>
                ContentHashCommand.Execute(roots, environment.Output, environment.Error)));
        return contentHash;
    }

    // A parser rather than a validator: a validator that asks for the converted value throws on text that is not a number,
    // which would crash instead of reporting a usage error.
    private static Func<ArgumentResult, long> NonNegativeWholeNumber(string optionName) =>
        result =>
        {
            string text = result.Tokens.Count == 1 ? result.Tokens[0].Value : string.Empty;
            if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long value))
            {
                return value;
            }

            result.AddError($"{optionName} must be a whole number, 0 or more (got '{text}').");
            return 0;
        };

    // Resolves --content (or the default), says which roots it chose on stderr once, then runs the command on them.
    private static int WithContentRoots(ParseResult parsed, Option<string[]> content, ToolEnvironment environment, Func<ImmutableArray<string>, int> command)
    {
        string[] requested = parsed.GetValue(content) ?? [];
        if (!ContentRootResolver.TryResolve(requested, environment, out ImmutableArray<string> roots, out ImmutableArray<string> description))
        {
            foreach (string line in description)
            {
                environment.Error.WriteLine($"evenmere: {line}");
            }

            return ExitCodes.Usage;
        }

        foreach (string line in description)
        {
            environment.Error.WriteLine(line);
        }

        return command(roots);
    }
}
