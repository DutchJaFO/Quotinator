using Microsoft.AspNetCore.Components;

namespace Quotinator.Api.Components.Controls;

/// <summary>
/// The shared modal-dialog shell (#308) — backdrop, centred dialog, a 95vh cap, and a header/body/footer
/// layout whose body is the only part that scrolls.
/// </summary>
/// <remarks>
/// Extracted once the same shell existed three times: <see cref="StartupSuccessModal"/>,
/// <see cref="StartupErrorModal"/>, and the notification detail popup inside
/// <see cref="NotificationTable"/>. The duplication had already cost something measurable — the two
/// startup modals were given the 95vh cap in one change, and the detail popup turned out to need the
/// identical fix a message later, because each copy had to be found and corrected on its own.
/// <para>
/// **The cap is not optional and is deliberately not a parameter.** A dialog taller than the viewport
/// puts its own footer off-screen, which on the startup modal means the Continue button cannot be
/// reached at all. Every caller wants that prevented; none has a reason to opt out.
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

    /// <summary>Maximum dialog width, as a CSS length. Height is fixed at 95vh for every caller.</summary>
    [Parameter] public string MaxWidth { get; set; } = "80vw";

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
