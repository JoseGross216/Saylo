namespace Saylo.Modules.Translation.TextTranslation;

/// <summary>Opt-in customer-service vocabulary validation; never rewrites model output.</summary>
public static class CustomerServiceTranslationProfile
{
    public const string Id = "customer-service-v2";

    public static TranslationIntegrityOptions Create(IEnumerable<string>? protectedTerms = null) =>
        new(requireFormalSpanish: true, protectedTerms: protectedTerms, glossary:
        [
            new("en", "customer support", ["atenci\u00f3n al cliente", "soporte al cliente", "servicio al cliente"]),
            new("en", "water damage", ["da\u00f1os por agua", "da\u00f1o por agua", "da\u00f1os causados por agua", "da\u00f1o causado por agua"]),
            new("es", "escalar", ["escalate", "escalating", "escalated"]),
            new("en", "good morning", ["buenos d\u00edas"]),
            new("es", "buenos d\u00edas", ["good morning"]),
            new("en", "order number", ["n\u00famero de pedido", "n\u00famero de orden"]),
            new("es", "n\u00famero de pedido", ["order number", "order reference"]),
            new("en", "tracking number", ["n\u00famero de seguimiento", "c\u00f3digo de seguimiento"]),
            new("es", "n\u00famero de seguimiento", ["tracking number", "tracking reference", "tracking code"]),
            new("en", "invoice", ["factura"]),
            new("es", "factura", ["invoice", "bill"]),
            new("en", "payment", ["pago"]),
            new("es", "pago", ["payment", "paid", "pay"]),
            new("en", "refund", ["reembolso"]),
            new("es", "reembolso", ["refund", "reimbursement"]),
            new("en", "your request", ["su solicitud", "su petici\u00f3n"]),
            new("es", "solicitud", ["request"]),
            new("en", "technical support", ["soporte t\u00e9cnico", "asistencia t\u00e9cnica"]),
            new("es", "soporte t\u00e9cnico", ["technical support", "technical assistance"]),
            new("en", "confirmation", ["confirmaci\u00f3n"]),
            new("es", "confirmaci\u00f3n", ["confirmation"]),
            new("en", "complaint", ["queja", "reclamaci\u00f3n"]),
            new("es", "reclamaci\u00f3n", ["complaint", "claim"]),
            new("en", "thank you", ["gracias"]),
            new("es", "gracias", ["thank", "thanks"]),
            new("en", "goodbye", ["adi\u00f3s", "hasta luego"]),
            new("es", "hasta luego", ["goodbye", "see you"]),
            new("es", "documentos", ["documents"]),
            new("en", "documents", ["documentos"])
        ]);
}
