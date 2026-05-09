using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WalletHawk.Domain.Abstractions;
using WalletHawk.Domain.Models;

namespace WalletHawk.Infrastructure.Tron;

public sealed class TronGridClient : ITronExplorerClient
{
    private readonly HttpClient _http;
    private readonly TronGridOptions _opt;
    private readonly ILogger<TronGridClient> _log;

    public TronGridClient(HttpClient http, IOptions<TronGridOptions> opt, ILogger<TronGridClient> log)
    {
        _http = http;
        _opt = opt.Value;
        _log = log;

        _http.BaseAddress = new Uri(_opt.BaseUrl);
        if (!string.IsNullOrWhiteSpace(_opt.ApiKey))
        {
            _http.DefaultRequestHeaders.Add("TRON-PRO-API-KEY", _opt.ApiKey);
        }
    }

    public async Task<IReadOnlyList<Trc20Transfer>> GetUsdtTransfersAsync(
        string address, int limit = 20, CancellationToken ct = default)
    {
        // GET /v1/accounts/{address}/transactions/trc20?limit=N&contract_address=...
        var url = $"/v1/accounts/{Uri.EscapeDataString(address)}/transactions/trc20" +
                  $"?limit={limit}&only_confirmed=true&contract_address={_opt.UsdtContract}";

        TronGridResponse? resp;
        try
        {
            resp = await _http.GetFromJsonAsync<TronGridResponse>(url, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "TronGrid request failed for {Address}", address);
            return Array.Empty<Trc20Transfer>();
        }

        if (resp?.Data is null) return Array.Empty<Trc20Transfer>();

        var list = new List<Trc20Transfer>(resp.Data.Count);
        foreach (var tx in resp.Data)
        {
            if (tx.TokenInfo is null) continue;
            if (!decimal.TryParse(tx.Value, out var raw)) continue;
            var decimals = tx.TokenInfo.Decimals;
            var amount = raw / (decimal)Math.Pow(10, decimals);
            var ts = DateTimeOffset.FromUnixTimeMilliseconds(tx.BlockTimestamp);

            list.Add(new Trc20Transfer(
                TxHash: tx.TransactionId ?? "",
                FromAddress: tx.From ?? "",
                ToAddress: tx.To ?? "",
                Amount: amount,
                TokenSymbol: tx.TokenInfo.Symbol ?? "USDT",
                Timestamp: ts));
        }

        return list;
    }

    private sealed class TronGridResponse
    {
        [JsonPropertyName("data")]
        public List<TronTrc20Tx>? Data { get; set; }
    }

    private sealed class TronTrc20Tx
    {
        [JsonPropertyName("transaction_id")]
        public string? TransactionId { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("to")]
        public string? To { get; set; }

        [JsonPropertyName("value")]
        public string? Value { get; set; }

        [JsonPropertyName("token_info")]
        public TokenInfo? TokenInfo { get; set; }

        [JsonPropertyName("block_timestamp")]
        public long BlockTimestamp { get; set; }
    }

    private sealed class TokenInfo
    {
        [JsonPropertyName("symbol")]
        public string? Symbol { get; set; }

        [JsonPropertyName("decimals")]
        public int Decimals { get; set; }

        [JsonPropertyName("address")]
        public string? Address { get; set; }
    }
}
