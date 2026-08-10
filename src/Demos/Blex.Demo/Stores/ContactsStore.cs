namespace Blex.Demo.Stores;

/// <summary>A normalized entity stored in <see cref="ContactsStore"/>.</summary>
public sealed record Contact(int Id, string Name, string Team, bool Starred);

/// <summary>
/// Normalized collection state via <see cref="EntityAdapterBlex{TEntity, TKey}"/>. The adapter is a
/// static field (it holds the id selector and is not serialized); the <c>[StateAttributeBlex]</c> field holds
/// the immutable <see cref="EntityStateBlex{TEntity, TKey}"/> that every operation returns anew.
/// </summary>
[StoreAttributeBlex(Name = "contacts")]
public partial class ContactsStore
{
    /// <summary>Sorted by name, so <c>Ids</c> stays ordered after every operation.</summary>
    private static readonly EntityAdapterBlex<Contact, int> Adapter =
        new(c => c.Id, Comparer<Contact>.Create((a, b) => string.CompareOrdinal(a.Name, b.Name)));

    private int _nextId = 5;

    [StateAttributeBlex] private EntityStateBlex<Contact, int> _contacts = Adapter.GetInitialState(
    [
        new(1, "Ada Lovelace", "Research", true),
        new(2, "Grace Hopper", "Compilers", false),
        new(3, "Alan Turing", "Research", false),
        new(4, "Barbara Liskov", "Languages", true),
    ]);

    [StateAttributeBlex] private string _teamFilter = "All";

    [ComputedAttributeBlex] private int ComputeTotal() => Contacts.Count;

    [ComputedAttributeBlex] private int ComputeStarred() => Contacts.All.Count(c => c.Starred);

    [ComputedAttributeBlex]
    private IReadOnlyList<Contact> ComputeVisible()
        => TeamFilter == "All"
            ? [.. Contacts.All]
            : [.. Contacts.All.Where(c => c.Team == TeamFilter)];

    [ComputedAttributeBlex]
    private IReadOnlyList<string> ComputeTeams()
        => ["All", .. Contacts.All.Select(c => c.Team).Distinct().Order()];

    [ActionAttributeBlex] private void OnSetTeamFilter(string team) => TeamFilter = team;

    [ActionAttributeBlex]
    private void OnAdd(string name, string team)
        => Contacts = Adapter.AddOne(Contacts, new Contact(_nextId++, name, team, false));

    [ActionAttributeBlex]
    private void OnToggleStar(int id)
        => Contacts = Adapter.UpdateOne(Contacts, id, c => c with { Starred = !c.Starred });

    [ActionAttributeBlex] private void OnRemove(int id) => Contacts = Adapter.RemoveOne(Contacts, id);

    [ActionAttributeBlex]
    private void OnStarAll()
        => Contacts = Adapter.Map(Contacts, c => c with { Starred = true });

    [ActionAttributeBlex]
    private void OnRemoveUnstarred()
        => Contacts = Adapter.RemoveMany(Contacts, [.. Contacts.All.Where(c => !c.Starred).Select(c => c.Id)]);

    [ActionAttributeBlex] private void OnRemoveAll() => Contacts = Adapter.RemoveAll(Contacts);

    /// <summary>
    /// Stands in for a save response arriving after the row may already have been removed.
    /// <c>SetOne</c> applies it only if the id is still present; <c>UpsertOne</c> would put a
    /// deleted contact back.
    /// </summary>
    [ActionAttributeBlex]
    private void OnLateSaveResponse(int id)
        => Contacts = Adapter.SetOne(Contacts, new Contact(id, $"Contact {id} (saved)", "Research", true));

    /// <summary>
    /// Reseeds with the exact same four contacts. The adapter compares before it commits, so this
    /// returns the same state instance: no notification, no action recorded, no undo step.
    /// </summary>
    [ActionAttributeBlex]
    private void OnReseedIdentical()
        => Contacts = Adapter.SetAll(Contacts, [.. Contacts.All]);

    [ActionAttributeBlex]
    private void OnReseed()
    {
        _nextId = 5;
        Contacts = Adapter.SetAll(Contacts,
        [
            new(1, "Ada Lovelace", "Research", true),
            new(2, "Grace Hopper", "Compilers", false),
            new(3, "Alan Turing", "Research", false),
            new(4, "Barbara Liskov", "Languages", true),
        ]);
    }
}
