using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Admin.UI.Rendering;

public sealed class RazorComponentRenderer(
    IServiceProvider serviceProvider,
    ILoggerFactory loggerFactory)
{
    public async Task<string> RenderComponentAsync<TComponent>(
        IDictionary<string, object?>? parameters = null)
        where TComponent : IComponent
    {
        await using var htmlRenderer = new HtmlRenderer(serviceProvider, loggerFactory);

        var parameterView = parameters is { Count: > 0 }
            ? ParameterView.FromDictionary(parameters)
            : ParameterView.Empty;

        return await htmlRenderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await htmlRenderer.RenderComponentAsync<TComponent>(parameterView);
            return output.ToHtmlString();
        });
    }
}
