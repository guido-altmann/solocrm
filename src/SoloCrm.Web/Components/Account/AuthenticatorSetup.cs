using System.Globalization;
using System.Text;
using QRCoder;

namespace SoloCrm.Web.Components.Account;

/// <summary>Shared key and QR code for setting up an authenticator app (TOTP, US-21 AK2).</summary>
public static class AuthenticatorSetup
{
    public const string Issuer = "SoloCRM";

    /// <summary>Key in groups of four, lower case („abcd efgh …“); spaces and case do not matter to the apps.</summary>
    public static string FormatKey(string unformattedKey)
    {
        ArgumentNullException.ThrowIfNull(unformattedKey);

        var result = new StringBuilder();
        for (var position = 0; position < unformattedKey.Length; position += 4)
        {
            if (position > 0)
            {
                result.Append(' ');
            }

            result.Append(unformattedKey.AsSpan(position, Math.Min(4, unformattedKey.Length - position)));
        }

        return result.ToString().ToLowerInvariant();
    }

    /// <summary><c>otpauth://totp/SoloCRM:ada%40example.test?secret=…&amp;issuer=SoloCRM&amp;digits=6</c> (Key URI format).</summary>
    public static string Uri(string accountName, string unformattedKey)
    {
        ArgumentNullException.ThrowIfNull(accountName);
        ArgumentNullException.ThrowIfNull(unformattedKey);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"otpauth://totp/{System.Uri.EscapeDataString(Issuer)}:{System.Uri.EscapeDataString(accountName)}?secret={unformattedKey}&issuer={System.Uri.EscapeDataString(Issuer)}&digits=6");
    }

    /// <summary>QR code as inline SVG, rendered on the server without JavaScript (iteration 6 decision 7).</summary>
    public static string QrCodeSvg(string uri)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        var svg = new SvgQRCode(data);
        return svg.GetGraphic(new System.Drawing.Size(220, 220), "#000000", "#ffffff", drawQuietZones: true, sizingMode: SvgQRCode.SizingMode.ViewBoxAttribute);
    }
}
