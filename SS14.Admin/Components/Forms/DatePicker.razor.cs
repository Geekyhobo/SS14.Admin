using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace SS14.Admin.Components.Forms;

public partial class DatePicker : ComponentBase, IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _instance;

    [Parameter] public string? Label { get; set; }

    [Parameter] public DateTime? DateFrom { get; set; }
    [Parameter] public EventCallback<DateTime?> DateFromChanged { get; set; }

    [Parameter] public DateTime? DateTo { get; set; }
    [Parameter] public EventCallback<DateTime?> DateToChanged { get; set; }

    private readonly string _id = $"date-picker-{Guid.NewGuid().ToString()}";

    public DatePicker(IJSRuntime js)
    {
        _js = js;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        var module = await _js.InvokeAsync<IJSObjectReference>("import", "./Components/Forms/DatePicker.razor.js");

        var options = new
        {
            rangeStart = DateFrom?.ToString("yyyy-MM-dd"),
            rangeEnd = DateTo?.ToString("yyyy-MM-dd"),
        };

        _instance = await module.InvokeAsync<IJSObjectReference>("DatePicker.init", DotNetObjectReference.Create(this), _id, options);
        await module.DisposeAsync();
    }

    [JSInvokable]
    public async Task OnDatesConfirmed(string? fromDate, string? toDate)
    {
        // Npgsql requires Utc
        DateFrom = ParseAsUtc(fromDate);
        DateTo = ParseAsUtc(toDate);

        await DateFromChanged.InvokeAsync(DateFrom);
        await DateToChanged.InvokeAsync(DateTo);
    }

    private static DateTime? ParseAsUtc(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        var dt = DateTime.Parse(value);
        return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    }

    public async ValueTask DisposeAsync()
    {
        if (_instance is null)
            return;

        try
        {
            await _instance.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
