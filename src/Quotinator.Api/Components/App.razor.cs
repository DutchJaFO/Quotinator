using System.Globalization;

namespace Quotinator.Api.Components;

public partial class App
{
    private string HtmlLang => CultureInfo.CurrentUICulture.Name;
}
