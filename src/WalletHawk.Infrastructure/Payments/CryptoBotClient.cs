using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WalletHawk.Domain.Abstractions;

namespace WalletHawk.Infrastructure.Payments;

public sealed class CryptoBotClient : IPaymentProvider
{
    private readonly HttpClient _http;
    private readonly CryptoBotOptions _opt;
    private readonly ILogger<CryptoBotClient> _log;

    public CryptoBotClient(HttpClient http, IOptions<CryptoBotOptions> opt, ILogger<CryptoBotClient> log)
    {
        _http = http;
        _opt = opt.Value;
        _log = log;

        _http.BaseAddress = new Uri(_opt.BaseUrl);
        if (!string.IsNullOrWhiteSpace(_opt.ApiKey))
        {
            _http.DefaultRequestHeaders.Add("Crypto-Pay-API-Token", _opt.ApiKey);
        }
    }

    public async Task<PaymentInvoice> CreateInvoiceAsync(long telegramUserId, string description, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.ApiKey))
            throw new InvalidOperationException("CryptoBot API key is not configured");

        var payload = new
        {
            asset = "USDT",
            amount = _opt.ProPriceUsdt.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            description = description,
            payload = telegramUserId.ToString(),
            allow_anonymous = false,
            allow_comments = false,
        };

        var resp = await _http.PostAsJsonAsync("/api/createInvoice", payload, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<CryptoBotResponse<InvoiceData>>(cancellationToken: ct)
            ?? throw new InvalidOperationException("CryptoBot returned empty body");

        if (!body.Ok || body.Result is null)
            throw new InvalidOperationException($"CryptoBot error: {body.Error?.Name}");

        return new PaymentInvoice(
            InvoiceId: body.Result.InvoiceId.ToString(),
            PayUrl: body.Result.MiniAppPayUrl ?? body.Result.BotPayUrl ?? body.Result.PayUrl ?? "");
    }

    private sealed class CryptoBotResponse<T>
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("result")] public T? Result { get; set; }
        [JsonPropertyName("error")] public CryptoBotError? Error { get; set; }
    }

    private sealed class CryptoBotError
    {
        [JsonPropertyName("code")] public int Code { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private sealed class InvoiceData
    {
        [JsonPropertyName("invoice_id")] public long InvoiceId { get; set; }
        [JsonPropertyName("pay_url")] public string? PayUrl { get; set; }
        [JsonPropertyName("bot_invoice_url")] public string? BotPayUrl { get; set; }
        [JsonPropertyName("mini_app_invoice_url")] public string? MiniAppPayUrl { get; set; }
    }
}
