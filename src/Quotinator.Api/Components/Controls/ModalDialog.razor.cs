using Microsoft.AspNetCore.Components;

namespace Quotinator.Api.Components.Controls;

/// <summary>
/// The shared modal-dialog shell (#308) — backdrop, centred dialog, and a header/body/footer layout
/// whose body is the only part that scrolls.
/// </summary>
/// <remarks>
/// Extracted once the same shell existed three times: <see cref="StartupSuccessModal"/>,
/// <see cref="StartupErrorModal"/>, and the notification detail popup inside
/// <see cref="NotificationTable"/>. The duplication had already cost something measurable — the same
/// height fix had to be found and applied to each copy on its own.
/// <para>
/// **The height is Bootstrap's and is deliberately not a parameter** (#422).
/// <c>modal-dialog-centered</c> and <c>modal-dialog-scrollable</c> size the dialog to the viewport less
/// its margin and scroll the body within it, so the footer stays reachable, which on the startup modal
/// is what keeps the Continue button clickable. Every caller wants that; none has a reason to opt out.
/// This component declares no <c>max-height</c> of its own: the centred class sets a larger
/// <c>min-height</c>, so one could never bind.
/// </para>
/// </remarks>
public partial class ModalDialog
{
    /// <summary>The heading, rendered inside the modal title element.</summary>
    [Parameter, EditorRequired] public RenderFragment? Title { get; set; }

    /// <summary>The scrolling body content.</summary>
    [Parameter, EditorRequired] public RenderFragment? ChildContent { get; set; }

    /// <summary>Footer content, usually the action buttons. Omitted entirely when <see langword="null"/>.</summary>
    [Parameter] public RenderFragment? Footer { get; set; }

    /// <summary>Maximum dialog width, as a CSS length. Height is Bootstrap's for every caller, and is not settable.</summary>
    [Parameter] public string MaxWidth { get; set; } = "80vw";

    /// <summary>
    /// Whether the dialog is only as wide as its content needs, bounded by <see cref="MaxWidth"/>
    /// (#377). Off by default — Bootstrap's <c>.modal-dialog</c> fills to its maximum, which is what
    /// every existing caller was laid out against.
    /// <para>
    /// Worth setting where the content's width is data-dependent, such as a table whose column count
    /// grows: sizing to the content means the dialog fits whatever it holds, where a fixed maximum is a
    /// guess that silently clips the moment the content outgrows it — which is exactly how #377's
    /// seven-column breakdown lost its last column. <see cref="MaxWidth"/> then serves as a viewport
    /// guard rather than a layout figure to keep re-tuning.
    /// </para>
    /// </summary>
    [Parameter] public bool FitContent { get; set; }

    /// <summary>Whether the header carries a close button.</summary>
    [Parameter] public bool ShowCloseButton { get; set; } = true;

    /// <summary>Invoked by the header close button and by a backdrop click, when <see cref="CloseOnBackdropClick"/> is set.</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>
    /// Whether clicking the backdrop closes the dialog. Off by default: the startup modals are a
    /// deliberate acknowledgement step, and dismissing one with a stray click outside it would skip
    /// what the operator was meant to read.
    /// </summary>
    [Parameter] public bool CloseOnBackdropClick { get; set; }

    /// <summary>Accessible label for the close button.</summary>
    [Parameter] public string CloseAriaLabel { get; set; } = "Close";

    /// <summary>Extra classes for the modal content element — e.g. <c>border-danger</c>.</summary>
    [Parameter] public string? ContentClass { get; set; }

    /// <summary>Extra classes for the header element.</summary>
    [Parameter] public string? HeaderClass { get; set; }

    /// <summary>Extra classes for the title element.</summary>
    [Parameter] public string? TitleClass { get; set; }

    /// <summary>Extra classes for the footer element.</summary>
    [Parameter] public string? FooterClass { get; set; }

    private async Task HandleBackdropClick()
    {
        if (CloseOnBackdropClick)
            await OnClose.InvokeAsync();
    }
}
