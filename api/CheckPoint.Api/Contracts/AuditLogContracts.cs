using CheckPoint.Api.Domain;

namespace CheckPoint.Api.Contracts;

public record AuditLogEntryResponse(
    Guid Id,
    Guid ViewerId,
    string ViewerName,
    Guid PersonId,
    string PersonName,
    AuditAction Action,
    DateTimeOffset OccurredAt);
