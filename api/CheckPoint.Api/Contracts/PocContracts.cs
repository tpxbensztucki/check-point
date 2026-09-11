using CheckPoint.Api.Domain;

namespace CheckPoint.Api.Contracts;

public record CreatePocRequest(string Name, string Email, PocRelationship Relationship, PocRole Role);

public record PocResponse(Guid Id, string Name, string Email, PocRelationship Relationship, PocRole Role);

// MissingStandardRoles lists which of {Tech, Dm, Other} have no active Poc yet —
// the "standard set" flag from spec Section 3, computed fresh on every read (never
// stored), matching how Person.IsOrphaned is computed elsewhere in this codebase.
public record ProjectMembershipPocsResponse(
    Guid ProjectMembershipId, IReadOnlyList<PocResponse> Pocs, IReadOnlyList<PocRole> MissingStandardRoles);
