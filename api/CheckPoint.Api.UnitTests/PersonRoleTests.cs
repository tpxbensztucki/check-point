using CheckPoint.Api.Domain;

namespace CheckPoint.Api.UnitTests;

// Pure object-model tests for the Person<->Role relationship — no database. See
// CheckPoint.Api.IntegrationTests for the persisted-many-to-many behaviour.
public class PersonRoleTests
{
    [Fact]
    public void NewPerson_HasNoRoles()
    {
        var person = new Person { FullName = "Alex Doe" };

        Assert.Empty(person.Roles);
    }

    [Fact]
    public void Person_CanHoldMultipleRolesSimultaneously()
    {
        var person = new Person { FullName = "Alex Doe" };
        var practiceLead = new Role { Id = 1, Name = RoleNames.PracticeLead };
        var lineManager = new Role { Id = 2, Name = RoleNames.LineManager };

        person.Roles.Add(lineManager);
        person.Roles.Add(practiceLead);

        Assert.Equal([lineManager, practiceLead], person.Roles);
    }

    [Fact]
    public void RemovingARole_LeavesOtherRolesOnThatPersonIntact()
    {
        var person = new Person { FullName = "Alex Doe" };
        var practiceLead = new Role { Id = 1, Name = RoleNames.PracticeLead };
        var lineManager = new Role { Id = 2, Name = RoleNames.LineManager };
        person.Roles.Add(lineManager);
        person.Roles.Add(practiceLead);

        person.Roles.Remove(lineManager);

        Assert.Equal([practiceLead], person.Roles);
    }

    [Fact]
    public void AddingARoleToOnePerson_DoesNotAffectAnotherPersonsRoles()
    {
        var admin = new Role { Id = 1, Name = RoleNames.Admin };
        var personA = new Person { FullName = "Alex Doe" };
        var personB = new Person { FullName = "Sam Doe" };

        personA.Roles.Add(admin);

        Assert.Contains(admin, personA.Roles);
        Assert.Empty(personB.Roles);
    }
}
