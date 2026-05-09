namespace WalletHawk.Domain.Services;

public static class TronAddress
{
    /// <summary>Light validation: TRON addresses (Base58) start with "T" and are 34 chars.</summary>
    public static bool IsValidTrc20(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        var a = address.Trim();
        if (a.Length != 34) return false;
        if (a[0] != 'T') return false;
        // Base58 alphabet (no 0, O, I, l)
        const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        foreach (var c in a) if (!alphabet.Contains(c)) return false;
        return true;
    }

    public static string Mask(string address)
    {
        if (string.IsNullOrEmpty(address) || address.Length < 12) return address;
        return $"{address[..6]}…{address[^6..]}";
    }
}
