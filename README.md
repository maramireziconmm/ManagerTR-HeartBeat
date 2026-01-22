# DenevaManagerTR – v0.2.6 (Section pick fix)

El `deneva.config` incluye tanto:

- Definición de sección en `<configSections>`:
  `<section name="RabbiMQ.My.MySettings" .../>`

- Bloque real de valores en `<applicationSettings>`:
  `<RabbiMQ.My.MySettings> ... <setting name="RabbitMQ_ConnectionString"><value>...</value>`

En v0.2.5 el lector podía quedarse con el nodo `<section name="...">`, lo que hacía que no
aparecieran los `<setting>` y devolviese null.

En v0.2.6 se prioriza el elemento cuyo nombre sea exactamente la sección (bloque real) y se
evita usar el nodo `<section>`.

Prueba:
- breakpoint en Get(section="RabbiMQ.My.MySettings", key="RabbitMQ_ConnectionString")
- ahora debe devolver el valor del `<value>`.


## v0.2.10 – Soporte Encoding 1252 en .NET 8

Se registra `CodePagesEncodingProvider` al inicio (Program.cs) y se añade el paquete `System.Text.Encoding.CodePages`.
Esto evita `System.NotSupportedException: No data is available for encoding 1252`.


## v0.3.3
- HeartBeat: Header OriginSystem/ContentType y routingKey desde appsettings (Jobs:Heartbeat).
- Logging a fichero: logs/DenevaManagerTR.Heartbeat-YYYYMMDD.log y logs/DenevaManagerTR.Heartbeat.RabbitMQ-YYYYMMDD.log.


## v0.3.9 – InfoStation (consumidor + respuesta)

Esta entrega compila por defecto **solo** la funcionalidad de InfoStation.

### Selección de funcionalidad (compile-time)

En `Directory.Build.props`:

- `DENEVA_INFOSTATION` -> compila y registra el consumidor de RabbitMQ para `GETINFOSTATION` y publica `SYNCINFOSTATION_RETURN`.
- `DENEVA_HEARTBEAT` -> compila y registra el job de HeartBeat.

Para cambiarlo, sustituye el símbolo en `DefineConstants`.

### Configuración

- Exchange de entrada/salida InfoStation: `RabbitMQ_InfoEstacionExchangeName` (leído desde `deneva.config`).
- Conexión RabbitMQ: `RabbitMQ_ConnectionString`, `RabbitMQ_Port`, `RabbitMQ_User`, `RabbitMQ_Pass`, `RabbitMQ_Host`, `RabbitMQ_VHost` (según tu `deneva.config`).
- Parámetros del consumidor: `Jobs:InfoStation` en `appsettings.json` (cola, topics y AppId).
