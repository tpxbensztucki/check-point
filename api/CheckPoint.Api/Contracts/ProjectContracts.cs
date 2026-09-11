using CheckPoint.Api.Domain;

namespace CheckPoint.Api.Contracts;

public record CreateProjectRequest(string Name);
public record ProjectResponse(Guid Id, string Name, ProjectStatus Status);

public record AddPersonToProjectRequest(Guid PersonId);
public record ProjectMembershipResponse(Guid Id, Guid ProjectId, Guid PersonId, DateTimeOffset JoinedAt);

// MissingStandardRoles is null for a Completed Project (POC completeness is only
// meaningful while a Project is Active) and otherwise computed fresh on every
// read, same as ProjectMembershipPocsResponse.
public record PersonProjectSummary(
    Guid ProjectId, string ProjectName, ProjectStatus Status, IReadOnlyList<PocRole>? MissingStandardRoles);
