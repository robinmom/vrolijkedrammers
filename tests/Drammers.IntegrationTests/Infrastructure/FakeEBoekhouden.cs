using Drammers.Infrastructure.EBoekhouden;

namespace Drammers.IntegrationTests.Infrastructure;

/// <summary>e-Boekhouden in het geheugen: leden toevoegen, wijzigen en verwijderen tussen twee syncruns.</summary>
public sealed class FakeEBoekhouden : IEBoekhoudenClient
{
    private readonly Dictionary<int, EbMember> _members = [];
    private int _nextId = 1000;

    /// <summary>Laat het openen van een sessie mislukken (bijv. ongeldig token).</summary>
    public bool FailToOpen { get; set; }

    public int OpenSessions { get; private set; }

    public int ClosedSessions { get; private set; }

    public EbMember Add(string memberNumber, string name, string? email = null, string? city = "Loil", string? freeText1 = null, string? freeText2 = null, string? freeText3 = null)
    {
        var member = new EbMember(_nextId++, memberNumber, name, null, "m", "Dorpsstraat 1", "6999 AA", city, "NL", null, null, email,
            freeText1, freeText2, freeText3, null, null, null, null, null, null, null);
        _members[member.Id] = member;
        return member;
    }

    public void Update(string memberNumber, Func<EbMember, EbMember> change)
    {
        var current = _members.Values.Single(m => m.MemberNumber == memberNumber);
        _members[current.Id] = change(current) with { Id = current.Id };
    }

    public void Remove(string memberNumber) => _members.Remove(_members.Values.Single(m => m.MemberNumber == memberNumber).Id);

    public Task<IEBoekhoudenSession> OpenSessionAsync(CancellationToken cancellationToken)
    {
        if (FailToOpen)
        {
            throw new EBoekhoudenException("Aanmelden bij e-Boekhouden mislukt: controleer het API-token.");
        }

        OpenSessions++;
        return Task.FromResult<IEBoekhoudenSession>(new Session(this));
    }

    private sealed class Session(FakeEBoekhouden owner) : IEBoekhoudenSession
    {
        public Task<IReadOnlyList<EbMemberReference>> ListMembersAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EbMemberReference>>([.. owner._members.Values.OrderBy(m => m.Id).Select(m => new EbMemberReference(m.Id, m.MemberNumber))]);

        public Task<EbMember> GetMemberAsync(int id, CancellationToken cancellationToken) => Task.FromResult(owner._members[id]);

        public ValueTask DisposeAsync()
        {
            owner.ClosedSessions++;
            return ValueTask.CompletedTask;
        }
    }
}
