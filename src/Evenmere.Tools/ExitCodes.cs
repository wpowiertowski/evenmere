namespace Evenmere.Tools;

/// <summary>The process exit codes of the <c>evenmere</c> tool. Scripts and CI rely on telling "the content is wrong" from "the command line is wrong".</summary>
internal static class ExitCodes
{
    /// <summary>The command did what was asked and found no errors (warnings are allowed).</summary>
    public const int Success = 0;

    /// <summary>The content has errors, so the command stopped (lint found errors, or <c>run</c> had nothing valid to run).</summary>
    public const int ContentErrors = 1;

    /// <summary>The command line is wrong: an unknown command or option, a bad value, or no content root could be found.</summary>
    public const int Usage = 2;
}
