using System.Globalization;
using System.Xml.Linq;

namespace DenevaManagerTR.Core.Domain.Messages;

public sealed class MessageMQ
{
    public string Version { get; set; } = "1.0";
    public MessageHeader Header { get; } = new();
    public MessageBody MessageBody { get; } = new();

    public string TimestampMiliseconds()
    {
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Local);
        var ms = (long)(DateTime.Now - epoch).TotalMilliseconds;
        return ms.ToString(CultureInfo.InvariantCulture);
    }

    public string Serialize()
    {
        var root = new XElement("Message",
            new XAttribute("version", Version),
            new XElement("Header",
                new XElement("OriginSystem", Header.OriginSystem ?? string.Empty),
                new XElement("Timestamp", Header.Timestamp ?? string.Empty),
                new XElement("ContentType", Header.ContentType ?? string.Empty)
            ),
            new XElement("MessageBody",
                new XElement("GenericXElement",
                    MessageBody.GenericXElement ?? new XElement("Empty")
                )
            )
        );

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        return doc.ToString(SaveOptions.None);
    }

    /// <summary>
    /// Deserializa el envelope legacy &lt;Message&gt;...&lt;/Message&gt;.
    /// Se implementa sin XmlSerializer para ser tolerante a variaciones de formato.
    /// </summary>
    public static MessageMQ Deserialize(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            throw new ArgumentException("XML vacío.", nameof(xml));

        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var root = doc.Root;
        if (root is null || !root.Name.LocalName.Equals("Message", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El XML no contiene un nodo raíz <Message>.");

        var msg = new MessageMQ();
        msg.Version = (string?)root.Attribute("version") ?? msg.Version;

        var headerEl = root.Element("Header");
        if (headerEl is not null)
        {
            msg.Header.OriginSystem = headerEl.Element("OriginSystem")?.Value ?? string.Empty;
            msg.Header.Timestamp = headerEl.Element("Timestamp")?.Value ?? string.Empty;
            msg.Header.ContentType = headerEl.Element("ContentType")?.Value ?? string.Empty;
        }

        // MessageBody/GenericXElement puede venir con cualquier payload XML dentro.
        var bodyEl = root.Element("MessageBody");
        var generic = bodyEl?.Element("GenericXElement");
        if (generic is not null)
        {
            // Si tiene hijos, nos quedamos con el primer elemento hijo.
            var firstChild = generic.Elements().FirstOrDefault();
            msg.MessageBody.GenericXElement = firstChild ?? generic;
        }
        else
        {
            msg.MessageBody.GenericXElement = null;
        }

        return msg;
    }
}

public sealed class MessageHeader
{
    public string? OriginSystem { get; set; }
    public string? Timestamp { get; set; }
    public string? ContentType { get; set; }
}

public sealed class MessageBody
{
    public XElement? GenericXElement { get; set; }
}
