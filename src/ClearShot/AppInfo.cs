namespace ClearShot;

internal static class AppInfo
{
    public const string Name = "ClearShot";
    /// <summary>From the build (release builds set it from the git tag), without any "+commit" suffix.</summary>
    public static string Version { get; } =
        (typeof(AppInfo).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "1.0.0")
        .Split('+')[0];

    /// <summary>
    /// "Buy me a beer" addresses. Entries with an empty address are hidden, and with none set
    /// the donate link doesn't appear at all.
    /// </summary>
    public static readonly DonationAddress[] Donations =
    [
        new("Bitcoin", "bc1qqz7xqmkgesmstyj40xadrf3szzfjdr2gx4u86y", "BTC only."),
        new("Ethereum and more", "0xB78c5D07b6F957168998315E49210074Dc55F179", "ETH, plus Base, Arbitrum, Optimism, Polygon and BNB Chain, and tokens like USDC and USDT on them."),
        new("Solana", "TC3YtercaL9u6fVfDCpe6hxW48rzV4zg5DKi1vDjzuM", "SOL, plus USDC and other tokens on Solana."),
    ];

    public static IReadOnlyList<DonationAddress> ActiveDonations => Donations.Where(d => !string.IsNullOrWhiteSpace(d.Address)).ToArray();
    public const string RepoUrl = "https://github.com/hodlsharkai/ClearShot";

    public static void OpenUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }
}

internal sealed record DonationAddress(string Network, string Address, string Accepts);
