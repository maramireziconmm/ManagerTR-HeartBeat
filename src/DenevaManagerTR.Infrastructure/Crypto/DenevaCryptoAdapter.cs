using System;
using System.Globalization;
using System.Text;

// IMPORTANTE:
// Ajusta estos using/namespace a lo que exponga tu DenevaCrypto.dll.
// Si Visual Studio no reconoce "DenevaCrypto", abre el Explorador de objetos
// y mira el namespace exacto. Luego cambia el using de abajo.
using DenevaCrypto; // <-- si falla, lo corregimos al namespace real.

public static class DenevaCryptoAdapter
{
    // Si en tu legacy existe un prefijo tipo UTF8Cadena, ponlo aquí.
    // Si no lo sabes, déjalo vacío; la rama "StartsWith" no se ejecutará.
    private const string UTF8Cadena = "";

    public static string Desencriptar(string cadena)
    {
        if (string.IsNullOrEmpty(cadena))
            return string.Empty;

        try
        {
            // Caso 1: cadena con prefijo UTF8Cadena (si aplica)
            if (!string.IsNullOrEmpty(UTF8Cadena) && cadena.StartsWith(UTF8Cadena, StringComparison.Ordinal))
            {
                cadena = cadena.Substring(UTF8Cadena.Length);
                var cryptedBuffer = HexToBytes(cadena);

                // Igual que VB: DES + UTF8
                using (var dc = new DenevaCrypto.DenevaCrypto())
                {
                    dc.EncryptionAlgorithm = DenevaCrypto.DenevaCrypto.EC_CRYPT_ALGO_ID.DES;
                    var buffer = dc.DecryptByteData(cryptedBuffer);
                    return Encoding.UTF8.GetString(buffer);
                }
            }

            // Caso 2: hex normal (sin prefijo) -> DES + 1252
            var b = HexToBytes(cadena);

            using (var dc = new DenevaCrypto.DenevaCrypto())
            {
                dc.EncryptionAlgorithm = DenevaCrypto.DenevaCrypto.EC_CRYPT_ALGO_ID.DES;
                var decB = dc.DecryptByteData(b);
                return Encoding.GetEncoding(1252).GetString(decB);
            }
        }
        catch
        {
            // Si tu legacy tenía fallback ManagedDenevaCrypto, aquí lo podríamos añadir
            // si también tienes esa DLL. De momento devolvemos vacío para no romper.
            return string.Empty;
        }
    }

    private static byte[] HexToBytes(string hex)
    {
        if (hex.Length % 2 != 0)
            throw new FormatException("Cadena hex inválida (longitud impar).");

        var bytes = new byte[hex.Length / 2];
        var j = 0;

        for (int i = 0; i < hex.Length; i += 2)
        {
            bytes[j++] = byte.Parse(hex.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return bytes;
    }
}

