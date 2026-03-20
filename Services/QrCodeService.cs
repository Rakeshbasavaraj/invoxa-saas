using QRCoder;

namespace Invoxa.Web.Services;

public static class QrCodeService
{
    public static byte[] GeneratePng(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        var qr = new PngByteQRCode(data);
        return qr.GetGraphic(20);
    }
}
