using CheckPoint.Api.Domain;

namespace CheckPoint.Api.Contracts;

public record CreateProjectRequest(string Name);
public record ProjectResponse(Guid Id, string Name, ProjectStatus Status);

public record AddPersonToProjectRequest(Guid PersonId);
public record ProjectMembershipResponse(Guid Id, Guid ProjectId, Guid PersonId, DateTimeOffset JoinedAt);
