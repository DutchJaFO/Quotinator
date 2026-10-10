using System.Text.RegularExpressions;

namespace Quotinator.Api.Tests.Solution;

public partial class RepositoryStructureTests
{
    /// <summary>
    /// Files that stated the 95vh cap `ModalDialog` never actually had (#422). Bootstrap's
    /// <c>.modal-dialog-centered</c> sets <c>min-height: calc(100% - var(--bs-modal-margin) * 2)</c>,
    /// and a larger <c>min-height</c> beats <c>max-height</c>, so the declaration could not bind at any
    /// viewport and the dialog was <c>viewport - 56px</c> whenever its content overflowed.
    /// </summary>
    private static readonly string[] ComponentsThatDescribedTheHeightCap =
    [
        "src/Quotinator.Api/Components/Controls/ModalDialog.razor",
        "src/Quotinator.Api/Components/Controls/ModalDialog.razor.cs",
        "src/Quotinator.Api/Components/Controls/NotificationTable.razor.css",
    ];

    /// <summary>
    /// <c>ModalDialog</c> declares no height cap (#422). The component may only state limits it can
    /// honour, and a <c>max-height</c> on <c>.modal-dialog</c> is not one: the height is governed by
    /// Bootstrap's centred and scrollable classes, which size the dialog to the viewport less its
    /// margin and scroll the body within it.
    /// <para>
    /// Asserted over the markup rather than the rendered result because the two fail differently: a
    /// rendered dialog that happens to fit proves nothing about what the component claims, which is
    /// what was wrong here. The live counterpart is
    /// <c>notifications-and-changelog/13-notification-layout.md</c>'s own height step.
    /// </para>
    /// </summary>
    [TestMethod]
    public void ModalDialog_DeclaresNoHeightCapItCannotHonour()
    {
        string markup = File.ReadAllText(Path.Combine(RepoRoot, "src/Quotinator.Api/Components/Controls/ModalDialog.razor"));

        string[] declarations =
        [
            .. StyleAttribute().Matches(markup)
                .Select(match => match.Groups["style"].Value)
                .Where(style => style.Contains("max-height", StringComparison.OrdinalIgnoreCase))
        ];

        Assert.IsEmpty(declarations,
            "ModalDialog declares a max-height that Bootstrap's centred min-height overrides, so it states a cap it does not have: "
            + string.Join(" | ", declarations));
    }

    /// <summary>Matches each <c>style="..."</c> attribute, so a comment mentioning the property is not a declaration of it.</summary>
    [GeneratedRegex(@"style=""(?<style>[^""]*)""")]
    private static partial Regex StyleAttribute();

    /// <summary>
    /// No component comment describes the inert 95vh cap (#422). The declaration and the prose
    /// describing it went in together and have to come out together: leaving the wording behind would
    /// keep the next reader believing in a cap the code no longer even claims.
    /// <para>
    /// Scoped to the components, not the documents. A test document may name the figure while
    /// recording what it used to assert and why that changed, which is a history this suite keeps
    /// deliberately; a component comment naming it can only be a claim about the code as it stands.
    /// </para>
    /// </summary>
    [TestMethod]
    public void NoComponent_DescribesTheInertHeightCap()
    {
        string[] stillDescribing =
        [
            .. ComponentsThatDescribedTheHeightCap.Where(file =>
                File.ReadAllText(Path.Combine(RepoRoot, file)).Contains("95vh", StringComparison.OrdinalIgnoreCase))
        ];

        Assert.IsEmpty(stillDescribing,
            "these still describe a 95vh cap that never bound: " + string.Join(", ", stillDescribing));
    }
}
