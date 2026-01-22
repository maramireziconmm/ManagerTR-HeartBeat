using Microsoft.Extensions.Logging;
using DenevaManagerTR.Core.Ports;

namespace DenevaManagerTR.Infrastructure.Crypto;

public sealed class DenevaCryptoAdapterWrapper : ICryptoAdapter
{
    private readonly ILogger<DenevaCryptoAdapterWrapper> _logger;

    public DenevaCryptoAdapterWrapper(ILogger<DenevaCryptoAdapterWrapper> logger)
    {
        _logger = logger;
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText))
            return string.Empty;

        try
        {
            // Igual que el legacy: Trim() antes de desencriptar
            return DenevaCryptoAdapter.Desencriptar(cipherText.Trim());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo desencriptar credencial con DenevaCryptoAdapter. Se devuelve texto original.");
            return cipherText;
        }
    }
}
