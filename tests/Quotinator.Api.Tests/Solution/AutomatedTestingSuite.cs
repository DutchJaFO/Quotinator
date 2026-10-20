using System.Text.RegularExpressions;

namespace Quotinator.Api.Tests.Solution;

/// <summary>One T2 test document, as a rule reads it.</summary>
/// <param name="Path">
/// The path the index links to: forward slashes, relative to <c>docs/automated-testing/</c>.
/// </param>
/// <param name="Text">The document's whole text.</param>
internal sealed record SuiteDocument(string Path, string Text);

/// <summary>
/// One element of the index's document template: a header field such as <c>**Smoke:**</c>, or a
/// section heading such as <c>## Canary</c>.
/// </summary>
/// <param name="Text">The literal a document must carry.</param>
/// <param name="IsSection">
/// True for a section heading, false for a header field. Only sections carry an order.
/// </param>
/// <param name="Optional">
/// True where the template marks the element with its "only when it applies" note, as the
/// <c>Fully green after:</c> field and the exclusions section are.
/// </param>
internal sealed record TemplateElement(string Text, bool IsSection, bool Optional);

/// <summary>A fenced code block, as the markdown scanner found it.</summary>
/// <param name="Language">The opening fence's info string, empty where it names none.</param>
/// <param name="FirstLineNumber">The 1-based number of the block's first content line.</param>
/// <param name="Lines">The block's content, both fence lines excluded.</param>
/// <param name="IsClosed">False where no closing fence appears before the end of the document.</param>
internal sealed record FencedBlock(string Language, int FirstLineNumber, List<string> Lines, bool IsClosed);

/// <summary>
/// The rules the T2 suite's documents are held to, each taking the documents and the template to check
/// them against rather than reading the repository for itself.
/// </summary>
/// <remarks>
/// Every rule here is a function from a document set to the failures in it, so a test states a scenario
/// and asserts the outcome. Reading <c>docs/automated-testing/</c> is the live check that the suite as
/// it stands complies, and stays in <see cref="RepositoryStructureTests"/>: it can report on today's
/// content, but it cannot establish what a rule does with a shape the tree does not happen to contain,
/// and a rule that examined nothing produces the same green as a rule that held. See #339 step 13.
/// </remarks>
internal static partial class AutomatedTestingSuite
{
    /// <summary>
    /// What every rule reports when handed no documents at all. The empty document set is a failure in
    /// its own right: a rule that examined nothing returns no failures, which reads exactly like the
    /// rule holding. Zero documents *of a kind* is a separate question, and each rule answers that one
    /// for itself.
    /// </summary>
    internal const string NoDocumentsSupplied =
        "no documents were supplied, so this rule examined nothing";

    /// <summary>What a template-driven rule reports when the template defines nothing of its kind.</summary>
    internal const string NoTemplateSupplied =
        "the document template defines no element of this kind, so there was nothing to check against";

    /// <summary>The line a non-deterministic document names its variable on.</summary>
    internal const string WhatVariesLine = "**What varies:**";

    /// <summary>The note the template marks an optional element with.</summary>
    internal const string OptionalMarker = "only when it applies";

    /// <summary>The index heading the document template sits under.</summary>
    internal const string DocumentTemplateHeading = "## Document template";

    #region Rules

    /// <summary>
    /// The elements of the template that a document does not carry, plus any document with no title.
    /// </summary>
    /// <param name="documents">The documents to check.</param>
    /// <param name="template">The template they are held to.</param>
    /// <returns>One line per missing element, ordered.</returns>
    public static List<string> MissingTemplateElements(
        IReadOnlyList<SuiteDocument> documents,
        IReadOnlyList<TemplateElement> template)
    {
        if (documents.Count == 0)
            return [NoDocumentsSupplied];

        List<TemplateElement> required = [.. template.Where(e => !e.Optional)];

        if (required.Count == 0)
            return [NoTemplateSupplied];

        List<string> failures = [];

        foreach (SuiteDocument document in documents)
        {
            List<string> headings = SectionHeadings(document.Text);

            if (!DocumentTitle().IsMatch(document.Text))
                failures.Add($"{document.Path}: no '# ' title line");

            foreach (TemplateElement element in required.Where(e => !Carries(document.Text, headings, e)))
                failures.Add($"{document.Path}: missing '{element.Text}'");
        }

        return [.. failures.Order()];
    }

    /// <summary>
    /// The documents whose sections do not appear in the order the template states them.
    /// </summary>
    /// <param name="documents">The documents to check.</param>
    /// <param name="template">The template whose section order they are held to.</param>
    /// <returns>One line per document, giving its own order, ordered by path.</returns>
    public static List<string> SectionsOutOfTemplateOrder(
        IReadOnlyList<SuiteDocument> documents,
        IReadOnlyList<TemplateElement> template)
    {
        if (documents.Count == 0)
            return [NoDocumentsSupplied];

        List<string> order = [.. template.Where(e => e.IsSection).Select(e => e.Text)];

        if (order.Count == 0)
            return [NoTemplateSupplied];

        List<string> failures = [];

        foreach (SuiteDocument document in documents)
        {
            List<string> asWritten = [.. SectionHeadings(document.Text).Where(order.Contains)];
            List<string> asTemplated = [.. order.Where(asWritten.Contains)];

            if (!asWritten.SequenceEqual(asTemplated))
                failures.Add($"{document.Path}: {string.Join(" | ", asWritten)}");
        }

        return [.. failures.Order()];
    }

    /// <summary>
    /// The sections documents carry that the template does not define.
    /// </summary>
    /// <param name="documents">The documents to check.</param>
    /// <param name="template">The template that defines which sections exist.</param>
    /// <returns>One line per undefined section, ordered.</returns>
    public static List<string> SectionsTheTemplateDoesNotDefine(
        IReadOnlyList<SuiteDocument> documents,
        IReadOnlyList<TemplateElement> template)
    {
        if (documents.Count == 0)
            return [NoDocumentsSupplied];

        HashSet<string> defined = [.. template.Where(e => e.IsSection).Select(e => e.Text)];

        if (defined.Count == 0)
            return [NoTemplateSupplied];

        List<string> failures = [];

        foreach (SuiteDocument document in documents)
        {
            failures.AddRange(SectionHeadings(document.Text)
                .Where(h => !defined.Contains(h))
                .Select(h => $"{document.Path}: '{h}'"));
        }

        return [.. failures.Order()];
    }

    /// <summary>
    /// The documents whose Observed effect neither records a measurement nor appears on the index's
    /// awaiting-measurement list, so a description could be read as evidence.
    /// </summary>
    /// <param name="documents">The documents to check.</param>
    /// <param name="awaiting">The awaiting-measurement rows, by document path.</param>
    /// <returns>One line per document, ordered.</returns>
    public static List<string> EffectsNeitherMeasuredNorAwaiting(
        IReadOnlyList<SuiteDocument> documents,
        IReadOnlyDictionary<string, string> awaiting)
    {
        if (documents.Count == 0)
            return [NoDocumentsSupplied];

        return
        [
            .. documents
                .Where(d => !RecordsAMeasurement(d.Text) && !awaiting.ContainsKey(d.Path))
                .Select(d => $"{d.Path}: no '**Measured <date>**' marker, and not listed as awaiting one")
                .Order()
        ];
    }

    /// <summary>
    /// Where the awaiting-measurement list and the documents' own sections disagree: a row whose
    /// document has since been measured, a row naming no document at all, an unmeasured document with
    /// no row, and a row that names no quantity to read.
    /// </summary>
    /// <param name="documents">The documents the list describes.</param>
    /// <param name="awaiting">The awaiting-measurement rows, by document path.</param>
    /// <returns>One line per disagreement, ordered.</returns>
    public static List<string> AwaitingListDisagreements(
        IReadOnlyList<SuiteDocument> documents,
        IReadOnlyDictionary<string, string> awaiting)
    {
        if (documents.Count == 0)
            return [NoDocumentsSupplied];

        HashSet<string> known = [.. documents.Select(d => d.Path)];
        HashSet<string> unmeasured =
            [.. documents.Where(d => !RecordsAMeasurement(d.Text)).Select(d => d.Path)];

        List<string> failures = [];

        foreach (string row in awaiting.Keys.Where(k => !known.Contains(k)))
            failures.Add($"{row}: listed as awaiting measurement, but no such document exists");

        foreach (string row in awaiting.Keys.Where(k => known.Contains(k) && !unmeasured.Contains(k)))
            failures.Add($"{row}: listed as awaiting measurement, but its section now records one");

        foreach (string path in unmeasured.Where(p => !awaiting.ContainsKey(p)))
            failures.Add($"{path}: carries no measurement and is not listed");

        foreach (KeyValuePair<string, string> row in awaiting.Where(r => r.Value.Trim().Length == 0))
            failures.Add($"{row.Key}: listed, but says nothing about what its next run must measure");

        return [.. failures.Order()];
    }

    /// <summary>
    /// The documents carrying no <c>Deterministic:</c> field, so nothing says whether their result
    /// moves between runs.
    /// </summary>
    /// <param name="documents">The documents to check.</param>
    /// <returns>One line per document, ordered.</returns>
    public static List<string> DocumentsNotDeclaringDeterminism(IReadOnlyList<SuiteDocument> documents)
    {
        if (documents.Count == 0)
            return [NoDocumentsSupplied];

        return
        [
            .. documents
                .Where(d => !DeterminismDeclaration().IsMatch(d.Text))
                .Select(d => $"{d.Path}: no '**Deterministic:** yes' or '**Deterministic:** no' field")
                .Order()
        ];
    }

    /// <summary>
    /// The documents that declare themselves non-deterministic without naming what varies.
    /// </summary>
    /// <remarks>
    /// A set in which every document is deterministic has nothing to report and returns no failures:
    /// that state is legitimate, and was the live one when this rule was written. An empty set is a
    /// different answer, because those two are the shapes that produced the same green and let this
    /// rule ship having inspected nothing. See #339 step 13.
    /// </remarks>
    /// <param name="documents">The documents to check.</param>
    /// <returns>One line per document, ordered.</returns>
    public static List<string> NonDeterministicDocumentsNotNamingWhatVaries(
        IReadOnlyList<SuiteDocument> documents)
    {
        if (documents.Count == 0)
            return [NoDocumentsSupplied];

        List<string> failures = [];

        foreach (SuiteDocument document in documents)
        {
            Match declared = DeterminismDeclaration().Match(document.Text);

            if (!declared.Success
                || !declared.Groups["answer"].Value.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Match section = DeterminismSection().Match(document.Text);

            if (!section.Success
                || !section.Groups["body"].Value.Contains(WhatVariesLine, StringComparison.Ordinal))
            {
                failures.Add(
                    $"{document.Path}: declares itself non-deterministic, with no '{WhatVariesLine}' line");
            }
        }

        return [.. failures.Order()];
    }

    /// <summary>
    /// The fenced code blocks that are not PowerShell, and the constructs inside a PowerShell fence
    /// that no Windows PowerShell session provides.
    /// </summary>
    /// <param name="documents">The documents to check, the index included.</param>
    /// <returns>One line per finding, ordered.</returns>
    public static List<string> CodeBlocksThatAreNotPowerShell(IReadOnlyList<SuiteDocument> documents)
    {
        if (documents.Count == 0)
            return [NoDocumentsSupplied];

        List<string> failures = [];

        foreach (SuiteDocument document in documents)
        {
            foreach (FencedBlock block in FencedBlocks(Lines(document.Text)))
            {
                int fenceLine = block.FirstLineNumber - 1;

                if (!block.IsClosed)
                {
                    failures.Add($"{document.Path}:{fenceLine}: fenced block is never closed");
                    continue;
                }

                if (ShellFenceLanguages.Contains(block.Language))
                {
                    failures.Add(
                        $"{document.Path}:{fenceLine}: fence names {block.Language}, not powershell");
                }
                else if (block.Language == "powershell")
                {
                    failures.AddRange(UnixOnlyConstructs(document.Path, block));
                }
            }
        }

        return [.. failures.Order()];
    }

    #endregion

    #region Reading a document

    /// <summary>The text's lines, carriage returns stripped, so no rule has to allow for one.</summary>
    /// <param name="text">The text to split.</param>
    /// <returns>The lines, in order.</returns>
    public static List<string> Lines(string text) => [.. text.Split('\n').Select(l => l.TrimEnd('\r'))];

    /// <summary>
    /// A document's own <c>## </c> headings, in order, ignoring any inside a fenced code block so a
    /// heading in sample output is not read as a section of the document.
    /// </summary>
    /// <param name="documentText">The document's text.</param>
    /// <returns>The headings, each as written.</returns>
    public static List<string> SectionHeadings(string documentText) =>
    [
        .. ProseLines(Lines(documentText))
            .Select(l => l.Text.TrimEnd())
            .Where(l => SectionHeading().IsMatch(l))
    ];

    /// <summary>Every fenced code block in the document, in order.</summary>
    /// <param name="lines">The document's lines.</param>
    /// <returns>The blocks, in the order they open.</returns>
    public static List<FencedBlock> FencedBlocks(IReadOnlyList<string> lines) =>
    [
        .. FenceSpans(lines).Select(span => new FencedBlock(
            span.Language,
            span.Open + 2,
            [.. lines.Take(span.Close < 0 ? lines.Count : span.Close).Skip(span.Open + 1)],
            span.Close >= 0))
    ];

    /// <summary>
    /// The lines outside every fenced code block, with their 1-based numbers.
    /// </summary>
    /// <param name="lines">The document's lines.</param>
    /// <returns>The prose lines, in order.</returns>
    public static List<(int Number, string Text)> ProseLines(IReadOnlyList<string> lines)
    {
        bool[] fenced = new bool[lines.Count];

        foreach ((int open, int close, string _) in FenceSpans(lines))
        {
            for (int i = open; i <= (close < 0 ? lines.Count - 1 : close); i++)
                fenced[i] = true;
        }

        List<(int Number, string Text)> prose = [];

        for (int i = 0; i < lines.Count; i++)
        {
            if (!fenced[i])
                prose.Add((i + 1, lines[i]));
        }

        return prose;
    }

    /// <summary>
    /// True when the document's Observed effect section records a measurement, in the one marker
    /// spelling the index defines.
    /// </summary>
    /// <param name="documentText">The document's text.</param>
    /// <returns>True when the section carries the marker.</returns>
    public static bool RecordsAMeasurement(string documentText)
    {
        Match section = ObservedEffectSection().Match(documentText);

        return section.Success && MeasurementMarker().IsMatch(section.Groups["body"].Value);
    }

    #endregion

    #region Reading the index

    /// <summary>
    /// The elements the index's document template defines, in the order it states them.
    /// </summary>
    /// <remarks>
    /// Read from the index rather than hardcoded, so adding a section to the template is what makes it
    /// required of every document, and the template cannot drift from its own guard.
    /// </remarks>
    /// <param name="indexText">The index's text.</param>
    /// <returns>The elements, in template order; empty where the index defines no template.</returns>
    public static List<TemplateElement> TemplateElements(string indexText)
    {
        List<TemplateElement> elements = [];

        foreach (string line in DocumentTemplateBlock(indexText))
        {
            bool optional = line.Contains(OptionalMarker, StringComparison.Ordinal);

            Match field = TemplateHeaderField().Match(line);

            if (field.Success)
            {
                elements.Add(new($"**{field.Groups["name"].Value.Trim()}:**", false, optional));
                continue;
            }

            Match section = SectionHeading().Match(line);

            if (section.Success)
            {
                // The optional marker trails the heading text, so it is not part of the name.
                string name = section.Groups["name"].Value.Split('←')[0].Trim();
                elements.Add(new($"## {name}", true, optional));
            }
        }

        return elements;
    }

    /// <summary>
    /// The lines of the fenced sample under the index's <c>## Document template</c> heading.
    /// </summary>
    /// <remarks>
    /// The sample carries a fence of its own, so it is written as a four-backtick block: three
    /// backticks would be ended by the inner fence's own closing line, which is both the CommonMark
    /// rule and how the block renders.
    /// </remarks>
    /// <param name="indexText">The index's text.</param>
    /// <returns>
    /// The sample's lines, the inner fence included as content; empty where the index carries no such
    /// section, or leaves the block open.
    /// </returns>
    public static List<string> DocumentTemplateBlock(string indexText)
    {
        List<string> lines = Lines(indexText);
        int heading = lines.FindIndex(l => l == DocumentTemplateHeading);

        if (heading < 0)
            return [];

        List<string> fromHeading = [.. lines.Skip(heading)];

        return FencedBlocks(fromHeading)
            .FirstOrDefault(b => b.IsClosed && b.Language == "markdown")
            ?.Lines ?? [];
    }

    /// <summary>
    /// The index's awaiting-measurement rows: document path to the quantity its next run must read.
    /// </summary>
    /// <param name="indexText">The index's text.</param>
    /// <returns>The rows; empty where the index carries no such section.</returns>
    public static Dictionary<string, string> AwaitingMeasurementRows(string indexText)
    {
        Match section = AwaitingMeasurementSection().Match(indexText);

        if (!section.Success)
            return [];

        Dictionary<string, string> rows = [];

        foreach (string line in Lines(section.Value).Where(l => l.StartsWith('|')))
        {
            Match link = MarkdownLinkTarget().Match(line);

            if (!link.Success)
                continue;

            string[] cells = line.Trim('|').Split('|');

            if (cells.Length < 2)
                continue;

            rows[link.Groups["target"].Value] = cells[1];
        }

        return rows;
    }

    #endregion

    #region PowerShell constructs

    /// <summary>Fence languages that name a shell this suite does not run.</summary>
    internal static readonly HashSet<string> ShellFenceLanguages =
        new(StringComparer.OrdinalIgnoreCase) { "bash", "sh", "shell", "zsh" };

    /// <summary>
    /// Each entry pairs a construct no Windows PowerShell session provides with what to write instead,
    /// so a failure tells the reader the fix rather than only the fault.
    /// </summary>
    private static readonly (string Construct, string Instead)[] UnixOnlyShellConstructs =
    [
        ("MSYS_NO_PATHCONV", "nothing: it is a Git Bash workaround with nothing to work around here"),
        ("/dev/null",        "$null, or | Out-Null"),
        ("curl ",            "Invoke-RestMethod, or scripts/testing/http.csx"),
        ("curl.exe",         "Invoke-RestMethod, or scripts/testing/http.csx"),
        ("grep",             "Select-String, and to count, [regex]::Matches(...).Count"),
        ("cut -d",           "a property on the object ConvertFrom-Json returns"),
        ("wc -l",            "Measure-Object, or .Count on a parsed response"),
        ("; do",             "foreach (...) { ... }"),
        ("; done",           "foreach (...) { ... }"),
        ("<<'",              "a here-string: @'...'@"),
    ];

    /// <summary>
    /// Constructs recognised only where a command can begin, because their spelling also occurs inside
    /// ordinary text. Matching anywhere would flag <c># Wait until the app is healthy</c> as a shell
    /// loop and <c>isDismissed </c> as <c>sed</c>, both measured, and a guard that fires on prose gets
    /// worked around rather than obeyed.
    /// </summary>
    private static readonly (string Construct, string Instead)[] UnixOnlyShellCommands =
    [
        ("until", "http.csx --wait-for, or while (...) { Start-Sleep 1 }"),
        ("export", "$env:NAME = 'value'"),
        ("sed", "nothing: ADR 010 forbids it outright"),
        ("awk", "nothing: ADR 010 forbids it outright"),
    ];

    /// <summary>
    /// A fence carrying <c>sh -c</c> runs its payload inside a Linux container, so the Unix forms in it
    /// are correct rather than left over. The exemption is the whole fence, because a continuation
    /// splits one such command across lines.
    /// </summary>
    /// <param name="path">The document's path, for the failure line.</param>
    /// <param name="block">The PowerShell fence to read.</param>
    /// <returns>One line per construct found.</returns>
    private static IEnumerable<string> UnixOnlyConstructs(string path, FencedBlock block)
    {
        if (block.Lines.Any(l => l.Contains("sh -c", StringComparison.Ordinal)))
            yield break;

        for (int i = 0; i < block.Lines.Count; i++)
        {
            int number = block.FirstLineNumber + i;

            foreach ((string construct, string instead) in UnixOnlyShellConstructs)
            {
                if (block.Lines[i].Contains(construct, StringComparison.Ordinal))
                    yield return $"{path}:{number}: `{construct.Trim()}` is not PowerShell; use {instead}";
            }

            string command = block.Lines[i].TrimStart();

            foreach ((string construct, string instead) in UnixOnlyShellCommands)
            {
                if (command.StartsWith($"{construct} ", StringComparison.Ordinal))
                    yield return $"{path}:{number}: `{construct}` is not PowerShell; use {instead}";
            }

            // `for` is the one shared spelling: PowerShell's own loop opens a parenthesis where bash's
            // names a variable, so only the bash form is a finding.
            if (command.StartsWith("for ", StringComparison.Ordinal)
                && !command.StartsWith("for (", StringComparison.Ordinal))
            {
                yield return $"{path}:{number}: bash `for ... in` is not PowerShell; use foreach (...) {{ ... }}";
            }

            // Windows PowerShell 5.1 gives a single PSCustomObject no Count property, so a filter that
            // matches exactly one row prints nothing where the test expects 1: correct for zero rows
            // and for two, blank for the one case a well-targeted assertion is most likely to produce.
            if (block.Lines[i].Contains("Where-Object", StringComparison.Ordinal)
                && block.Lines[i].Contains(".Count", StringComparison.Ordinal)
                && !block.Lines[i].Contains("@(", StringComparison.Ordinal))
            {
                yield return $"{path}:{number}: a filtered .Count needs @(...) around it, "
                    + "or it reports blank for exactly one match";
            }
        }
    }

    #endregion

    #region Scanning

    /// <summary>
    /// Where each fenced code block opens and closes, by the CommonMark rule.
    /// </summary>
    /// <remarks>
    /// Any fence line opens a block, and only a bare fence at least as long as the opening one closes
    /// it, since a closing fence cannot carry an info string (CommonMark 0.31.2, fenced code blocks,
    /// example 147). This suite needs exactly that rule and neither of its approximations: a boolean
    /// toggle reads the nested fence in the index's own template sample as a close, and depth counting
    /// reads the bare fence a document uses to quote program output as one. Both shapes are present
    /// here, which is how one file came to hold a scanner of each kind, each correct only for the input
    /// its own caller happened to pass. See #339 step 13.
    /// </remarks>
    /// <param name="lines">The document's lines.</param>
    /// <returns>
    /// One entry per block: the 0-based line of its opening fence, of its closing fence or -1 where it
    /// is never closed, and the opening fence's info string.
    /// </returns>
    private static List<(int Open, int Close, string Language)> FenceSpans(IReadOnlyList<string> lines)
    {
        List<(int Open, int Close, string Language)> spans = [];

        int open = -1;
        int length = 0;
        string language = "";

        for (int i = 0; i < lines.Count; i++)
        {
            int backticks = LeadingBackticks(lines[i]);

            if (backticks == 0)
                continue;

            string info = lines[i][backticks..].Trim();

            if (open < 0)
            {
                open = i;
                length = backticks;
                language = info;
                continue;
            }

            if (backticks >= length && info.Length == 0)
            {
                spans.Add((open, i, language));
                open = -1;
            }
        }

        if (open >= 0)
            spans.Add((open, -1, language));

        return spans;
    }

    /// <summary>The length of a line's opening run of backticks, or 0 where it is not a fence.</summary>
    /// <param name="line">The line to measure.</param>
    /// <returns>Three or more, or 0.</returns>
    private static int LeadingBackticks(string line)
    {
        int count = 0;

        while (count < line.Length && line[count] == '`')
            count++;

        return count >= 3 ? count : 0;
    }

    /// <summary>True when a document carries one template element.</summary>
    /// <param name="documentText">The document's text.</param>
    /// <param name="headings">Its section headings, already read.</param>
    /// <param name="element">The element to look for.</param>
    /// <returns>True when the element is present.</returns>
    private static bool Carries(string documentText, List<string> headings, TemplateElement element) =>
        element.IsSection
            ? headings.Contains(element.Text)
            : Lines(documentText).Any(l => l.TrimStart().StartsWith(element.Text, StringComparison.Ordinal));

    #endregion

    #region Patterns

    /// <summary>A document's <c># </c> title line.</summary>
    [GeneratedRegex(@"^#\s+[^#\s].*$", RegexOptions.Multiline)]
    private static partial Regex DocumentTitle();

    /// <summary>
    /// A level-two heading. The <c>[^#]</c> keeps it from matching a <c>### </c> step heading.
    /// </summary>
    [GeneratedRegex(@"^##\s+(?<name>[^#].*?)\s*$")]
    private static partial Regex SectionHeading();

    /// <summary>A template header field, such as <c>**Smoke:**</c>.</summary>
    [GeneratedRegex(@"^\*\*(?<name>[^*:]+):\*\*")]
    private static partial Regex TemplateHeaderField();

    /// <summary>A markdown link's target.</summary>
    [GeneratedRegex(@"\]\((?<target>[^)]+)\)")]
    internal static partial Regex MarkdownLinkTarget();

    /// <summary>The one spelling a measured effect opens with.</summary>
    [GeneratedRegex(@"\*\*Measured\s+\d{4}-\d{2}-\d{2}")]
    private static partial Regex MeasurementMarker();

    /// <summary>
    /// A document's Observed effect section, up to the next top-level heading or the end of the
    /// document, so a document that ends on this section is still read.
    /// </summary>
    [GeneratedRegex(
        @"^## Observed effect\s*$(?<body>.*?)(?=^## |\z)",
        RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex ObservedEffectSection();

    /// <summary>A document's Determinism section, up to the next top-level heading or the end.</summary>
    [GeneratedRegex(
        @"^## Determinism\s*$(?<body>.*?)(?=^## |\z)",
        RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex DeterminismSection();

    /// <summary>A document's <c>Deterministic:</c> field and its answer.</summary>
    [GeneratedRegex(
        @"^\*\*Deterministic:\*\*\s*(?<answer>yes|no)\s*$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex DeterminismDeclaration();

    /// <summary>
    /// The awaiting-measurement section, from its own heading up to the next top-level heading.
    /// </summary>
    [GeneratedRegex(
        @"^## Observed effects awaiting measurement$.*?(?=^## )",
        RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex AwaitingMeasurementSection();

    #endregion
}
