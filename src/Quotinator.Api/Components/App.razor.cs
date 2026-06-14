using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;

namespace Quotinator.Api.Components;

/// <summary>Root application component. Sets the HTML language attribute and base href from the active culture and request path base.</summary>
public partial class App
{
    [CascadingParameter]
    private HttpContext? HttpContext { get; set; }

    private string HtmlLang => CultureInfo.CurrentUICulture.Name;

    private string BaseHref
    {
        get
        {
            var pathBase = HttpContext?.Request.PathBase.Value;
            return string.IsNullOrEmpty(pathBase) ? "/" : pathBase.TrimEnd('/') + "/";
        }
    }
}
