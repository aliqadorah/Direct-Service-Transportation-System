using DirectService.Transportation.Application.Abstractions;
using QRCoder;

namespace DirectService.Transportation.Infrastructure.Printing;

/// <summary>QR codes as inline SVG (QRCoder), error correction M — the e-invoice QR printed on invoices and credit notes (FR-057, FR-062).</summary>
public sealed class QrCodeRenderer : IQrCodeRenderer
{
    public string Svg(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        return new SvgQRCode(data).GetGraphic(4);
    }
}
