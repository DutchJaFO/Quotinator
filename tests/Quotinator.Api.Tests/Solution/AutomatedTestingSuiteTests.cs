namespace Quotinator.Api.Tests.Solution;

/// <summary>
/// States a scenario for each rule the T2 suite's documents are held to, and asserts the outcome.
/// </summary>
/// <remarks>
/// Every document here is written by the test. The live suite is checked separately, in
/// <see cref="RepositoryStructureTests"/>, and that check cannot establish what a rule does: it reports
/// on whatever <c>docs/automated-testing/</c> happens to contain, so a rule went green once those
/// documents were fixed and said nothing about any shape the tree did not hold. Three of those shapes
/// are covered here and were never in the tree: a document set with nothing in it, a heading inside a
/// fenced block, and a fence nested in another. See #339 step 13.
/// </remarks>
[TestClass]
public class AutomatedTestingSuiteTests
{
    #region The document template

    [TestMethod]
    public void TemplateElements_ReadsEveryFieldAndSectionTheSampleStates()
    {
        List<string> elements = [.. AutomatedTestingSuite.TemplateElements(FixtureIndex).Select(e => e.Text)];

        Assert.AreEqual(
            "**Smoke:** | **Deterministic:** | **Fully green after:** | ## Preconditions | "
            + "## Determinism | ## Explicitly not covered here | ## Steps | ## Observed effect | "
            + "## Cleanup",
            string.Join(" | ", elements),
            "The template's elements are read from the index's own sample, in the order it states them.");
    }

    [TestMethod]
    public void TemplateElements_MarksTheElementsTheSampleCallsOnlyWhenItAppliesOptional()
    {
        List<string> optional =
        [
            .. AutomatedTestingSuite.TemplateElements(FixtureIndex)
                .Where(e => e.Optional)
                .Select(e => e.Text)
        ];

        Assert.AreEqual(
            "**Fully green after:** | ## Explicitly not covered here",
            string.Join(" | ", optional),
            "An element the sample marks 'only when it applies' is optional, and no other is.");
    }

    [TestMethod]
    public void TemplateElements_ReadsPastAFenceNestedInTheSample()
    {
        List<string> sections =
        [
            .. AutomatedTestingSuite.TemplateElements(FixtureIndex)
                .Where(e => e.IsSection)
                .Select(e => e.Text)
        ];

        Assert.Contains("## Cleanup", sections,
            "Cleanup is stated after the sample's own PowerShell fence, so reading that fence as the "
            + "end of the sample would silently drop every element below it.");
    }

    [TestMethod]
    public void TemplateElements_ReadsNothingFromAnIndexWithNoTemplateSection()
    {
        List<TemplateElement> elements = AutomatedTestingSuite.TemplateElements(IndexWithNoTemplateSection);

        Assert.IsEmpty(elements,
            "The template is the one under 'Document template'. This index holds a markdown sample "
            + "under a different heading, and reading that one would hold every document to whatever "
            + "the nearest sample happened to show.");
    }

    [TestMethod]
    public void DocumentTemplateBlock_ReadsNothingFromASampleThatIsNeverClosed()
    {
        List<string> block = AutomatedTestingSuite.DocumentTemplateBlock(
            "## Document template\n\n````markdown\n# <title>\n\n## Preconditions\n");

        Assert.IsEmpty(block,
            "An unclosed sample runs to the end of the index, so its elements would be whatever the "
            + "rest of the index happens to say.");
    }

    #endregion

    #region Reading a document's own headings

    [TestMethod]
    public void SectionHeadings_ReadsTheDocumentsOwnHeadingsInOrder()
    {
        List<string> headings = AutomatedTestingSuite.SectionHeadings(
            Document(FirstDocument, Title, Fields, Preconditions, Determinism, Steps, MeasuredEffect, Cleanup).Text);

        Assert.AreEqual(
            "## Preconditions | ## Determinism | ## Steps | ## Observed effect | ## Cleanup",
            string.Join(" | ", headings),
            "A document's sections are its level-two headings, in the order it writes them.");
    }

    [TestMethod]
    public void SectionHeadings_IgnoresAThirdLevelStepHeading()
    {
        List<string> headings = AutomatedTestingSuite.SectionHeadings("## Steps\n\n### 1. Ask for a quote\n");

        Assert.AreEqual("## Steps", string.Join(" | ", headings),
            "A step heading is not a section, or every document would carry a different set of them.");
    }

    [TestMethod]
    public void SectionHeadings_IgnoresAHeadingInsideABlockOpenedByABareFence()
    {
        List<string> headings = AutomatedTestingSuite.SectionHeadings(QuotedOutputCarryingAHeading);

        Assert.AreEqual("## Preconditions | ## Cleanup", string.Join(" | ", headings),
            "A document quotes program output in a fence that names no language, so a line in that "
            + "output which happens to start with ## is output, not a section. Counting fence depth "
            + "reads the bare opening fence as a close and reports the quoted line as a section.");
    }

    [TestMethod]
    public void SectionHeadings_IgnoresAHeadingInsideAFenceNestedInAnother()
    {
        List<string> headings = AutomatedTestingSuite.SectionHeadings(SampleCarryingANestedFence);

        Assert.AreEqual("## Preconditions | ## Cleanup", string.Join(" | ", headings),
            "A document showing a template sample holds a fence inside a fence. Flipping a boolean on "
            + "each fence line reads the inner fence as a close and reports the sample's own headings "
            + "as sections of the document.");
    }

    [TestMethod]
    public void RecordsAMeasurement_IsTrueForTheMarkerTheIndexDefines()
    {
        bool measured = AutomatedTestingSuite.RecordsAMeasurement(
            Document(FirstDocument, Title, Fields, Steps, MeasuredEffect, Cleanup).Text);

        Assert.IsTrue(measured, "'**Measured <date>**' in the Observed effect section is a measurement.");
    }

    [TestMethod]
    public void RecordsAMeasurement_IsFalseForAnyOtherWording()
    {
        bool measured = AutomatedTestingSuite.RecordsAMeasurement(
            Document(FirstDocument, Title, Fields, Steps,
                "## Observed effect\n\n**Captured 2026-10-20** against 80dc3654: 200, one quote.",
                Cleanup).Text);

        Assert.IsFalse(measured,
            "The index defines one spelling, so a synonym is not a measurement: seven wordings are "
            + "seven things for a rule to miss.");
    }

    [TestMethod]
    public void RecordsAMeasurement_ReadsASectionThatEndsTheDocument()
    {
        bool measured = AutomatedTestingSuite.RecordsAMeasurement(
            Document(FirstDocument, Title, Fields, Steps, MeasuredEffect).Text);

        Assert.IsTrue(measured,
            "A document whose last section is Observed effect still records its measurement, rather "
            + "than reading as unmeasured because nothing follows the section.");
    }

    #endregion

    #region Every document carries every element the template defines

    [TestMethod]
    public void MissingTemplateElements_PassesADocumentCarryingEveryRequiredElement()
    {
        List<string> failures = AutomatedTestingSuite.MissingTemplateElements([Conforming()], Template);

        Assert.IsEmpty(failures, "The conforming document carries every required element.");
    }

    [TestMethod]
    public void MissingTemplateElements_PassesADocumentThatOmitsAnOptionalElement()
    {
        List<string> failures = AutomatedTestingSuite.MissingTemplateElements([Conforming()], Template);

        Assert.IsEmpty(failures,
            "The conforming document states no exclusions section and no 'Fully green after' field, "
            + "both of which the template marks as applying only sometimes.");
    }

    [TestMethod]
    public void MissingTemplateElements_NamesTheSectionADocumentOmits()
    {
        List<string> failures = AutomatedTestingSuite.MissingTemplateElements(
            [Document(FirstDocument, Title, Fields, Preconditions, Determinism, Steps, MeasuredEffect)],
            Template);

        Assert.HasCount(1, failures, "One element is missing, so one failure is reported.");
        Assert.Contains($"{FirstDocument}: missing '## Cleanup'", failures[0]);
    }

    [TestMethod]
    public void MissingTemplateElements_NamesTheHeaderFieldADocumentOmits()
    {
        List<string> failures = AutomatedTestingSuite.MissingTemplateElements(
            [Document(FirstDocument, Title, "**Smoke:** yes", Preconditions, Determinism, Steps,
                MeasuredEffect, Cleanup)],
            Template);

        Assert.HasCount(1, failures, "One field is missing, so one failure is reported.");
        Assert.Contains($"{FirstDocument}: missing '**Deterministic:**'", failures[0]);
    }

    [TestMethod]
    public void MissingTemplateElements_NamesADocumentWithNoTitleLine()
    {
        List<string> failures = AutomatedTestingSuite.MissingTemplateElements(
            [Document(FirstDocument, Fields, Preconditions, Determinism, Steps, MeasuredEffect, Cleanup)],
            Template);

        Assert.HasCount(1, failures, "Only the title is missing.");
        Assert.Contains($"{FirstDocument}: no '# ' title line", failures[0]);
    }

    [TestMethod]
    public void MissingTemplateElements_CountsASectionInsideAFenceAsAbsent()
    {
        List<string> failures = AutomatedTestingSuite.MissingTemplateElements(
            [Document(FirstDocument, Title, Fields, Preconditions, Determinism, Steps, MeasuredEffect,
                "```powershell\n## Cleanup\n```")],
            Template);

        Assert.HasCount(1, failures,
            "A heading inside a code fence is sample text, so quoting the template satisfies nothing.");
        Assert.Contains($"{FirstDocument}: missing '## Cleanup'", failures[0]);
    }

    [TestMethod]
    public void MissingTemplateElements_FailsAnEmptyDocumentSet()
    {
        List<string> failures = AutomatedTestingSuite.MissingTemplateElements([], Template);

        Assert.HasCount(1, failures, "The empty set is one failure, not a pass.");
        Assert.Contains(AutomatedTestingSuite.NoDocumentsSupplied, failures[0]);
    }

    [TestMethod]
    public void MissingTemplateElements_FailsATemplateThatRequiresNothing()
    {
        List<string> failures = AutomatedTestingSuite.MissingTemplateElements([Conforming()], []);

        Assert.HasCount(1, failures,
            "A template defining nothing would otherwise pass every document ever written.");
        Assert.Contains(AutomatedTestingSuite.NoTemplateSupplied, failures[0]);
    }

    #endregion

    #region The sections appear in the template's order

    [TestMethod]
    public void SectionsOutOfTemplateOrder_PassesADocumentInTemplateOrder()
    {
        List<string> failures = AutomatedTestingSuite.SectionsOutOfTemplateOrder([Conforming()], Template);

        Assert.IsEmpty(failures, "The conforming document states its sections in the template's order.");
    }

    [TestMethod]
    public void SectionsOutOfTemplateOrder_PassesADocumentThatSkipsAnOptionalSection()
    {
        List<string> failures = AutomatedTestingSuite.SectionsOutOfTemplateOrder(
            [Document(FirstDocument, Title, Fields, Preconditions, Determinism, Exclusions, Steps,
                MeasuredEffect, Cleanup)],
            Template);

        Assert.IsEmpty(failures,
            "Order is checked over the sections a document actually carries, so stating the optional "
            + "one is as correct as leaving it out.");
    }

    [TestMethod]
    public void SectionsOutOfTemplateOrder_NamesADocumentThatSwapsTwoSections()
    {
        List<string> failures = AutomatedTestingSuite.SectionsOutOfTemplateOrder(
            [Document(FirstDocument, Title, Fields, Preconditions, Determinism, MeasuredEffect, Steps,
                Cleanup)],
            Template);

        Assert.HasCount(1, failures, "One document is out of order.");
        Assert.Contains("## Observed effect | ## Steps", failures[0]);
    }

    [TestMethod]
    public void SectionsOutOfTemplateOrder_NamesADocumentThatPlacesItsExclusionsLast()
    {
        List<string> failures = AutomatedTestingSuite.SectionsOutOfTemplateOrder(
            [Document(FirstDocument, Title, Fields, Preconditions, Determinism, Steps, MeasuredEffect,
                Exclusions, Cleanup)],
            Template);

        Assert.HasCount(1, failures,
            "This is the shape two live documents had: the exclusions section written after the effect "
            + "rather than before the steps.");
        Assert.Contains("## Observed effect | ## Explicitly not covered here", failures[0]);
    }

    [TestMethod]
    public void SectionsOutOfTemplateOrder_FailsAnEmptyDocumentSet()
    {
        List<string> failures = AutomatedTestingSuite.SectionsOutOfTemplateOrder([], Template);

        Assert.HasCount(1, failures, "The empty set is one failure, not a pass.");
        Assert.Contains(AutomatedTestingSuite.NoDocumentsSupplied, failures[0]);
    }

    [TestMethod]
    public void SectionsOutOfTemplateOrder_FailsATemplateWithNoSections()
    {
        List<string> failures = AutomatedTestingSuite.SectionsOutOfTemplateOrder(
            [Conforming()],
            [new("**Smoke:**", false, false)]);

        Assert.HasCount(1, failures, "With no section order stated, every order would read as correct.");
        Assert.Contains(AutomatedTestingSuite.NoTemplateSupplied, failures[0]);
    }

    #endregion

    #region No document invents a section

    [TestMethod]
    public void SectionsTheTemplateDoesNotDefine_PassesADocumentUsingOnlyDefinedSections()
    {
        List<string> failures = AutomatedTestingSuite.SectionsTheTemplateDoesNotDefine([Conforming()], Template);

        Assert.IsEmpty(failures, "Every section the conforming document carries is in the template.");
    }

    [TestMethod]
    public void SectionsTheTemplateDoesNotDefine_NamesAnInventedSection()
    {
        List<string> failures = AutomatedTestingSuite.SectionsTheTemplateDoesNotDefine(
            [Document(FirstDocument, Title, Fields, Preconditions, Determinism, Steps, MeasuredEffect,
                "## How this would fail\n\nAgainst the build before the fix.", Cleanup)],
            Template);

        Assert.HasCount(1, failures,
            "A section one document invents is how the suite reached eleven spellings of its canary.");
        Assert.Contains($"{FirstDocument}: '## How this would fail'", failures[0]);
    }

    [TestMethod]
    public void SectionsTheTemplateDoesNotDefine_IgnoresASectionHeadingInsideAFence()
    {
        List<string> failures = AutomatedTestingSuite.SectionsTheTemplateDoesNotDefine(
            [Document(FirstDocument, Title, Fields, Preconditions, Determinism,
                "## Steps\n\n### 1. Read the log\n\n```\n## Migration 27 applied\n```\n\nThe line above.",
                MeasuredEffect, Cleanup)],
            Template);

        Assert.IsEmpty(failures,
            "Quoted output that happens to start with ## is output. A rule that read it as a section "
            + "would make a document's own log excerpt a template violation.");
    }

    [TestMethod]
    public void SectionsTheTemplateDoesNotDefine_FailsAnEmptyDocumentSet()
    {
        List<string> failures = AutomatedTestingSuite.SectionsTheTemplateDoesNotDefine([], Template);

        Assert.HasCount(1, failures, "The empty set is one failure, not a pass.");
        Assert.Contains(AutomatedTestingSuite.NoDocumentsSupplied, failures[0]);
    }

    [TestMethod]
    public void SectionsTheTemplateDoesNotDefine_FailsATemplateWithNoSections()
    {
        List<string> failures = AutomatedTestingSuite.SectionsTheTemplateDoesNotDefine(
            [Conforming()],
            [new("**Smoke:**", false, false)]);

        Assert.HasCount(1, failures,
            "With no sections defined, every section in every document would read as invented.");
        Assert.Contains(AutomatedTestingSuite.NoTemplateSupplied, failures[0]);
    }

    #endregion

    #region An effect is measured, or listed as awaiting measurement

    [TestMethod]
    public void EffectsNeitherMeasuredNorAwaiting_PassesAMeasuredDocument()
    {
        List<string> failures = AutomatedTestingSuite.EffectsNeitherMeasuredNorAwaiting(
            [Conforming()],
            new Dictionary<string, string>());

        Assert.IsEmpty(failures, "A measured effect needs no row.");
    }

    [TestMethod]
    public void EffectsNeitherMeasuredNorAwaiting_PassesAnUnmeasuredDocumentThatIsListed()
    {
        List<string> failures = AutomatedTestingSuite.EffectsNeitherMeasuredNorAwaiting(
            [Unmeasured()],
            new Dictionary<string, string> { [FirstDocument] = " the status code " });

        Assert.IsEmpty(failures,
            "An effect stated without having been measured is allowed, provided the index says so and "
            + "names what the next run must read.");
    }

    [TestMethod]
    public void EffectsNeitherMeasuredNorAwaiting_NamesAnUnmeasuredDocumentThatIsNotListed()
    {
        List<string> failures = AutomatedTestingSuite.EffectsNeitherMeasuredNorAwaiting(
            [Unmeasured()],
            new Dictionary<string, string>());

        Assert.HasCount(1, failures, "One document describes an effect it never measured.");
        Assert.Contains($"{FirstDocument}: no '**Measured <date>**' marker", failures[0]);
    }

    [TestMethod]
    public void EffectsNeitherMeasuredNorAwaiting_FailsAnEmptyDocumentSet()
    {
        List<string> failures = AutomatedTestingSuite.EffectsNeitherMeasuredNorAwaiting(
            [],
            new Dictionary<string, string>());

        Assert.HasCount(1, failures, "The empty set is one failure, not a pass.");
        Assert.Contains(AutomatedTestingSuite.NoDocumentsSupplied, failures[0]);
    }

    #endregion

    #region The awaiting-measurement list matches the documents

    [TestMethod]
    public void AwaitingMeasurementRows_ReadsEachRowsDocumentAndQuantity()
    {
        Dictionary<string, string> rows = AutomatedTestingSuite.AwaitingMeasurementRows(FixtureIndex);

        Assert.AreEqual(
            "category/01-serves-a-quote.md=the status code and the quote count",
            string.Join(" | ", rows.Select(r => $"{r.Key}={r.Value.Trim()}")),
            "Each row names the document it is about and the quantity its next run must read.");
    }

    [TestMethod]
    public void AwaitingMeasurementRows_ReadsNothingFromAnIndexWithNoSuchSection()
    {
        Dictionary<string, string> rows =
            AutomatedTestingSuite.AwaitingMeasurementRows(IndexWithNoAwaitingSection);

        Assert.IsEmpty(rows,
            "The index holds other tables that link to a document, and reading one of those as the "
            + "awaiting list would excuse a document nobody excused. No list means no document is "
            + "excused, which is what makes the rule above report every unmeasured effect.");
    }

    [TestMethod]
    public void AwaitingListDisagreements_PassesAListMatchingTheUnmeasuredDocuments()
    {
        List<string> failures = AutomatedTestingSuite.AwaitingListDisagreements(
            [Conforming(), Unmeasured(SecondDocument)],
            new Dictionary<string, string> { [SecondDocument] = " the row count " });

        Assert.IsEmpty(failures, "One unmeasured document, one row, and the row says what to read.");
    }

    [TestMethod]
    public void AwaitingListDisagreements_NamesARowWhoseDocumentNowRecordsAMeasurement()
    {
        List<string> failures = AutomatedTestingSuite.AwaitingListDisagreements(
            [Conforming()],
            new Dictionary<string, string> { [FirstDocument] = " the status code " });

        Assert.HasCount(1, failures,
            "This is the forcing function: a row cannot outlive the gap it describes.");
        Assert.Contains($"{FirstDocument}: listed as awaiting measurement, but its section now records one",
            failures[0]);
    }

    [TestMethod]
    public void AwaitingListDisagreements_NamesAnUnmeasuredDocumentWithNoRow()
    {
        List<string> failures = AutomatedTestingSuite.AwaitingListDisagreements(
            [Unmeasured()],
            new Dictionary<string, string>());

        Assert.HasCount(1, failures, "An unmeasured document the list does not name is a gap in the list.");
        Assert.Contains($"{FirstDocument}: carries no measurement and is not listed", failures[0]);
    }

    [TestMethod]
    public void AwaitingListDisagreements_NamesARowThatSaysNothingAboutWhatToMeasure()
    {
        List<string> failures = AutomatedTestingSuite.AwaitingListDisagreements(
            [Unmeasured()],
            new Dictionary<string, string> { [FirstDocument] = "   " });

        Assert.HasCount(1, failures,
            "A row with no quantity is an intention to look, which is what the list exists to replace.");
        Assert.Contains($"{FirstDocument}: listed, but says nothing about what its next run must measure",
            failures[0]);
    }

    [TestMethod]
    public void AwaitingListDisagreements_NamesARowNamingNoDocumentInTheSuite()
    {
        List<string> failures = AutomatedTestingSuite.AwaitingListDisagreements(
            [Conforming()],
            new Dictionary<string, string> { ["category/99-renamed.md"] = " the status code " });

        Assert.HasCount(1, failures,
            "A row left behind by a rename names a document nobody can measure, and saying so is more "
            + "use than reporting that its section now records a measurement.");
        Assert.Contains("category/99-renamed.md: listed as awaiting measurement, but no such document exists",
            failures[0]);
    }

    [TestMethod]
    public void AwaitingListDisagreements_FailsAnEmptyDocumentSet()
    {
        List<string> failures = AutomatedTestingSuite.AwaitingListDisagreements(
            [],
            new Dictionary<string, string>());

        Assert.HasCount(1, failures, "The empty set is one failure, not a pass.");
        Assert.Contains(AutomatedTestingSuite.NoDocumentsSupplied, failures[0]);
    }

    #endregion

    #region Every document declares whether it is deterministic

    [TestMethod]
    public void DocumentsNotDeclaringDeterminism_PassesADocumentThatDeclaresIt()
    {
        List<string> failures = AutomatedTestingSuite.DocumentsNotDeclaringDeterminism([Conforming()]);

        Assert.IsEmpty(failures, "The conforming document carries the field.");
    }

    [TestMethod]
    public void DocumentsNotDeclaringDeterminism_NamesADocumentWithNoSuchField()
    {
        List<string> failures = AutomatedTestingSuite.DocumentsNotDeclaringDeterminism(
            [Document(FirstDocument, Title, "**Smoke:** yes", Preconditions, Determinism, Steps,
                MeasuredEffect, Cleanup)]);

        Assert.HasCount(1, failures, "One document says nothing about whether its result moves.");
        Assert.Contains($"{FirstDocument}: no '**Deterministic:** yes' or '**Deterministic:** no' field",
            failures[0]);
    }

    [TestMethod]
    public void DocumentsNotDeclaringDeterminism_NamesADocumentWhoseAnswerIsNeitherYesNorNo()
    {
        List<string> failures = AutomatedTestingSuite.DocumentsNotDeclaringDeterminism(
            [Document(FirstDocument, Title, "**Smoke:** yes\n**Deterministic:** mostly", Preconditions,
                Determinism, Steps, MeasuredEffect, Cleanup)]);

        Assert.HasCount(1, failures,
            "The field is a declaration, so it answers yes or no. 'Mostly' is the state the field was "
            + "added to stop a reader having to infer.");
        Assert.Contains($"{FirstDocument}: no '**Deterministic:** yes'", failures[0]);
    }

    [TestMethod]
    public void DocumentsNotDeclaringDeterminism_FailsAnEmptyDocumentSet()
    {
        List<string> failures = AutomatedTestingSuite.DocumentsNotDeclaringDeterminism([]);

        Assert.HasCount(1, failures, "The empty set is one failure, not a pass.");
        Assert.Contains(AutomatedTestingSuite.NoDocumentsSupplied, failures[0]);
    }

    #endregion

    #region A non-deterministic document says what varies

    [TestMethod]
    public void NonDeterministicDocumentsNotNamingWhatVaries_PassesADocumentThatNamesIt()
    {
        List<string> failures = AutomatedTestingSuite.NonDeterministicDocumentsNotNamingWhatVaries(
            [Document(FirstDocument, Title, "**Smoke:** yes\n**Deterministic:** no", Preconditions,
                "## Determinism\n\n**What varies:** the download, which is a live GitHub fetch.",
                Steps, MeasuredEffect, Cleanup)]);

        Assert.IsEmpty(failures, "The document declares 'no' and names the variable.");
    }

    [TestMethod]
    public void NonDeterministicDocumentsNotNamingWhatVaries_NamesADocumentThatDeclaresNoAndSaysNothing()
    {
        List<string> failures = AutomatedTestingSuite.NonDeterministicDocumentsNotNamingWhatVaries(
            [Document(FirstDocument, Title, "**Smoke:** yes\n**Deterministic:** no", Preconditions,
                Determinism, Steps, MeasuredEffect, Cleanup)]);

        Assert.HasCount(1, failures,
            "Otherwise 'no' is an admission with no content: the reader learns that the result moves, "
            + "and not what moves it.");
        Assert.Contains($"{FirstDocument}: declares itself non-deterministic", failures[0]);
    }

    [TestMethod]
    public void NonDeterministicDocumentsNotNamingWhatVaries_ReadsADeterminismSectionThatEndsTheDocument()
    {
        List<string> failures = AutomatedTestingSuite.NonDeterministicDocumentsNotNamingWhatVaries(
            [Document(FirstDocument, Title, "**Smoke:** yes\n**Deterministic:** no",
                "## Determinism\n\n**What varies:** the download, which is a live GitHub fetch.")]);

        Assert.IsEmpty(failures,
            "A document whose last section is Determinism still has its answer read, rather than "
            + "being reported for saying nothing because no section follows it.");
    }

    [TestMethod]
    public void NonDeterministicDocumentsNotNamingWhatVaries_PassesASetInWhichEveryDocumentIsDeterministic()
    {
        List<string> failures = AutomatedTestingSuite.NonDeterministicDocumentsNotNamingWhatVaries(
            [Conforming(), Conforming(SecondDocument)]);

        Assert.IsEmpty(failures,
            "Zero non-deterministic documents is a legitimate state, and was the live one when this "
            + "rule was written. The rule examined two documents to say so.");
    }

    [TestMethod]
    public void NonDeterministicDocumentsNotNamingWhatVaries_FailsAnEmptyDocumentSet()
    {
        List<string> failures = AutomatedTestingSuite.NonDeterministicDocumentsNotNamingWhatVaries([]);

        Assert.HasCount(1, failures,
            "This is the rule that shipped having inspected nothing. Working correctly over no "
            + "document and broken so that it reaches none produce the same green, so the empty set is "
            + "a failure and the test above is what establishes the legitimate case.");
        Assert.Contains(AutomatedTestingSuite.NoDocumentsSupplied, failures[0]);
    }

    #endregion

    #region Every command is PowerShell

    [TestMethod]
    public void CodeBlocksThatAreNotPowerShell_PassesAPowerShellFence()
    {
        List<string> failures = AutomatedTestingSuite.CodeBlocksThatAreNotPowerShell(
            [Document(FirstDocument, Title, "```powershell\nInvoke-RestMethod http://localhost:8080/health\n```")]);

        Assert.IsEmpty(failures, "A PowerShell command in a PowerShell fence is what the suite is written in.");
    }

    [TestMethod]
    public void CodeBlocksThatAreNotPowerShell_NamesABashFence()
    {
        List<string> failures = AutomatedTestingSuite.CodeBlocksThatAreNotPowerShell(
            [Document(FirstDocument, Title, "```bash\ndocker ps\n```")]);

        Assert.HasCount(1, failures, "ADR 010 puts every command in this suite in PowerShell.");
        Assert.Contains($"{FirstDocument}:3: fence names bash, not powershell", failures[0]);
    }

    [TestMethod]
    public void CodeBlocksThatAreNotPowerShell_NamesAUnixConstructInsideAPowerShellFence()
    {
        List<string> failures = AutomatedTestingSuite.CodeBlocksThatAreNotPowerShell(
            [Document(FirstDocument, Title, "```powershell\ndocker logs quotinator 2>/dev/null\n```")]);

        Assert.HasCount(1, failures, "The fence names PowerShell; the command is not PowerShell.");
        Assert.Contains($"{FirstDocument}:4: `/dev/null` is not PowerShell", failures[0]);
    }

    [TestMethod]
    public void CodeBlocksThatAreNotPowerShell_IgnoresAUnixFormInsideAnShDashCFence()
    {
        List<string> failures = AutomatedTestingSuite.CodeBlocksThatAreNotPowerShell(
            [Document(FirstDocument, Title,
                "```powershell\ndocker exec quotinator sh -c 'ls /data > /dev/null'\n```")]);

        Assert.IsEmpty(failures,
            "What follows sh -c runs inside a Linux container, where a Unix shell is the only shell "
            + "there is, so the Unix form is correct rather than left over.");
    }

    [TestMethod]
    public void CodeBlocksThatAreNotPowerShell_IgnoresAConstructNamedInProse()
    {
        List<string> failures = AutomatedTestingSuite.CodeBlocksThatAreNotPowerShell(
            [Document(FirstDocument, Title,
                "## Preconditions\n\nAn earlier run of this document used grep and curl, which is why "
                + "step 1 reads the way it does.")]);

        Assert.IsEmpty(failures,
            "Prose naming a construct is a record of a past defect, and several such records are "
            + "load-bearing in the index. Only fenced code is checked.");
    }

    [TestMethod]
    public void CodeBlocksThatAreNotPowerShell_NamesAFenceThatIsNeverClosed()
    {
        List<string> failures = AutomatedTestingSuite.CodeBlocksThatAreNotPowerShell(
            [Document(FirstDocument, Title, "```powershell\ndocker ps")]);

        Assert.HasCount(1, failures,
            "An unclosed fence swallows the rest of the document, so every rule that tells code from "
            + "prose reads the remainder as code.");
        Assert.Contains($"{FirstDocument}:3: fenced block is never closed", failures[0]);
    }

    [TestMethod]
    public void CodeBlocksThatAreNotPowerShell_FailsAnEmptyDocumentSet()
    {
        List<string> failures = AutomatedTestingSuite.CodeBlocksThatAreNotPowerShell([]);

        Assert.HasCount(1, failures, "The empty set is one failure, not a pass.");
        Assert.Contains(AutomatedTestingSuite.NoDocumentsSupplied, failures[0]);
    }

    #endregion

    #region The example data

    private const string FirstDocument = "category/01-serves-a-quote.md";
    private const string SecondDocument = "category/02-serves-two-quotes.md";

    private const string Title = "# Serves a quote";
    private const string Fields = "**Smoke:** yes\n**Deterministic:** yes";
    private const string Preconditions = "## Preconditions\n\nA fresh container, nothing beyond the profile.";
    private const string Determinism = "## Determinism\n\nThe seed is fixed and the port is this document's own.";
    private const string Exclusions = "## Explicitly not covered here\n\nThe UI, which 02 covers.";
    private const string Steps = "## Steps\n\n### 1. Ask for a quote\n\nA 200, and one quote in the body.";
    private const string Cleanup = "## Cleanup\n\nThe container and its volume are removed.";

    private const string MeasuredEffect =
        "## Observed effect\n\n**Measured 2026-10-20** against 80dc3654: 200, one quote.";

    private const string UnmeasuredEffect = "## Observed effect\n\nA 200, and one quote.";

    /// <summary>The index the template-driven rules read their template from.</summary>
    private static readonly List<TemplateElement> Template = AutomatedTestingSuite.TemplateElements(FixtureIndex);

    /// <summary>A document carrying every required element, in the template's order, measured.</summary>
    /// <param name="path">The path to give it.</param>
    /// <returns>The document.</returns>
    private static SuiteDocument Conforming(string path = FirstDocument) =>
        Document(path, Title, Fields, Preconditions, Determinism, Steps, MeasuredEffect, Cleanup);

    /// <summary>A conforming document whose Observed effect records no measurement.</summary>
    /// <param name="path">The path to give it.</param>
    /// <returns>The document.</returns>
    private static SuiteDocument Unmeasured(string path = FirstDocument) =>
        Document(path, Title, Fields, Preconditions, Determinism, Steps, UnmeasuredEffect, Cleanup);

    /// <summary>A document assembled from the blocks named at the call site, in that order.</summary>
    /// <param name="path">The path to give it.</param>
    /// <param name="blocks">Its blocks, separated by a blank line.</param>
    /// <returns>The document.</returns>
    private static SuiteDocument Document(string path, params string[] blocks) =>
        new(path, string.Join("\n\n", blocks) + "\n");

    /// <summary>
    /// An index defining a template, an awaiting-measurement list, and nothing else the rules read.
    /// </summary>
    /// <remarks>
    /// Deliberately not the live index: a fixture built from the real one would make every test here
    /// depend on today's template, which is the dependency this whole file exists to remove. Its
    /// sample carries a fence of its own, as the live one does, so the template reader is exercised
    /// against that shape rather than only against prose.
    /// </remarks>
    private const string FixtureIndex = """
        # The suite

        ## Document template

        ````markdown
        # <What this verifies>

        **Smoke:** yes | no
        **Deterministic:** yes | no
        **Fully green after:** #NNN: <which steps, and what they read as until then>   ← only when it applies

        ## Preconditions

        The exact state the setup must reach.

        ## Determinism

        Every variable the outcome depends on, and how each is pinned.

        ## Explicitly not covered here   ← only when it applies

        What this document deliberately does not cover, and where that coverage lives instead.

        ## Steps

        ### 1. <what this step does>

        ```powershell
        <the command>
        ```

        Then what a pass looks like, precisely enough to check.

        ## Observed effect

        What this circumstance actually produces.

        ## Cleanup

        How the container and its volume go away.
        ````

        ## Observed effects awaiting measurement

        | Document | What its next run must read |
        |---|---|
        | [Serves a quote](category/01-serves-a-quote.md) | the status code and the quote count |

        ## Where things live

        Beside the document.
        """;

    /// <summary>
    /// An index that defines no document template, but does carry a markdown sample under another
    /// heading, so a reader that did not require the right heading would find something.
    /// </summary>
    private const string IndexWithNoTemplateSection = """
        # The suite

        ## Where things live

        Beside the document, which looks like this:

        ````markdown
        **Smoke:** yes | no

        ## Preconditions

        The state the setup must reach.
        ````
        """;

    /// <summary>
    /// An index that carries no awaiting-measurement list, but does carry another table whose rows
    /// link to a document, so a reader that did not require the right heading would find rows.
    /// </summary>
    private const string IndexWithNoAwaitingSection = """
        # The suite

        ## The tests

        | Document | Category |
        |---|---|
        | [Serves a quote](category/01-serves-a-quote.md) | category |

        ## Where things live

        Beside the document.
        """;

    /// <summary>
    /// A document quoting program output in a fence that names no language, with a line of that output
    /// starting with the same two characters a section heading does.
    /// </summary>
    private const string QuotedOutputCarryingAHeading = """
        # Reports what a migration did

        ## Preconditions

        A database one migration behind.

        ```
        ## Migration 27 applied
        ```

        ## Cleanup

        The container and its volume are removed.
        """;

    /// <summary>
    /// A document showing a template sample, so one fence sits inside another, with a heading inside
    /// the inner fence.
    /// </summary>
    private const string SampleCarryingANestedFence = """
        # Shows what a document looks like

        ## Preconditions

        None.

        ````markdown
        # <What this verifies>

        ```powershell
        Write-Host "## Steps"
        ## Observed effect
        ```
        ````

        ## Cleanup

        Nothing to undo.
        """;

    #endregion
}
