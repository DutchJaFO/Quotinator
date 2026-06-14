using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;

namespace Quotinator.Api.Components;

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
