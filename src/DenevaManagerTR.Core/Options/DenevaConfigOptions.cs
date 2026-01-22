using System;

namespace DenevaManagerTR.Core.Options;

public sealed class DenevaConfigOptions
{
    // Fallback local path (mantener compatibilidad)
    public string Path { get; set; } = "deneva.config";

    // Si está presente, se intentará descargar deneva.config desde esta URL
    public string? Url { get; set; } = null;

    // Encabezado Authorization (ej: "Bearer <token>" o valor tal cual)
    // Si no está en config, el código mirará la variable de entorno DENEVA_CONFIG_AUTHORIZATION
    public string? Authorization { get; set; } = null;

    public string RabbitSection { get; set; } = "RabbiMQ.My.MySettings";
}