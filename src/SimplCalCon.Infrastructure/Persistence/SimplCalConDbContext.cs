using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SimplCalCon.Domain.Acl;
using SimplCalCon.Domain.Authentication;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Common;
using SimplCalCon.Domain.Objects;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Domain.Scheduling;
using SimplCalCon.Domain.Tenants;

namespace SimplCalCon.Infrastructure.Persistence;

public class SimplCalConDbContext(DbContextOptions<SimplCalConDbContext> options) : DbContext(options)
{
    private static readonly ValueConverter<DateTime, DateTime> UtcConverter = new(
        write => write.Kind == DateTimeKind.Utc ? write : write.ToUniversalTime(),
        read => DateTime.SpecifyKind(read, DateTimeKind.Utc));

    private static readonly ValueConverter<DateTime?, DateTime?> NullableUtcConverter = new(
        write => write == null ? null : (write.Value.Kind == DateTimeKind.Utc ? write : write.Value.ToUniversalTime()),
        read => read == null ? null : DateTime.SpecifyKind(read.Value, DateTimeKind.Utc));

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Principal> Principals => Set<Principal>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMembership> GroupMemberships => Set<GroupMembership>();
    public DbSet<AppPassword> AppPasswords => Set<AppPassword>();
    public DbSet<Token> Tokens => Set<Token>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<Calendar> Calendars => Set<Calendar>();
    public DbSet<AddressBook> AddressBooks => Set<AddressBook>();
    public DbSet<ScheduleInbox> ScheduleInboxes => Set<ScheduleInbox>();
    public DbSet<ScheduleMessage> ScheduleMessages => Set<ScheduleMessage>();
    public DbSet<CollectionObject> Objects => Set<CollectionObject>();
    public DbSet<CalendarObject> CalendarObjects => Set<CalendarObject>();
    public DbSet<ContactObject> ContactObjects => Set<ContactObject>();
    public DbSet<ObjectRevision> ObjectRevisions => Set<ObjectRevision>();
    public DbSet<EventAttendee> EventAttendees => Set<EventAttendee>();
    public DbSet<AclEntry> AclEntries => Set<AclEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SimplCalConDbContext).Assembly);

        // Every IHasConcurrencyToken entity exposes ConcurrencyToken as its ETag
        // concurrency token (ADR 0009). Configure it once on each root type; TPH
        // derived types (User/Group) inherit it from Principal.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.BaseType is null
                && typeof(IHasConcurrencyToken).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(IHasConcurrencyToken.ConcurrencyToken))
                    .IsConcurrencyToken();
            }
        }

        // Every DateTime column is stored and read back as UTC (the DB is UTC-only;
        // clients localize). Applies to the object-store columns; existing
        // DateTimeOffset columns are unaffected.
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
        {
            if (property.ClrType == typeof(DateTime))
            {
                property.SetValueConverter(UtcConverter);
            }
            else if (property.ClrType == typeof(DateTime?))
            {
                property.SetValueConverter(NullableUtcConverter);
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyInvariants();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyInvariants();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyInvariants()
    {
        RegenerateConcurrencyTokens();
        ValidateGroupMembershipGraph();
    }

    // Never trust a caller-supplied ConcurrencyToken: stamp a fresh one on every
    // insert/update so the stored ETag always reflects the new state.
    private void RegenerateConcurrencyTokens()
    {
        foreach (var entry in ChangeTracker.Entries<IHasConcurrencyToken>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.ConcurrencyToken = Guid.NewGuid();
            }
        }
    }

    // Groups may nest (a member can be a group); reject any added/modified edge that
    // would let a group transitively contain itself. Mirrors the sibling project's
    // deliberate use of InvalidOperationException for DbContext invariants (CLAUDE.md):
    // the Api boundary translates it into a specific ApiException.
    private void ValidateGroupMembershipGraph()
    {
        var pending = ChangeTracker.Entries<GroupMembership>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .Select(e => e.Entity)
            .ToList();

        if (pending.Count == 0)
        {
            return;
        }

        var groupIds = Groups.AsNoTracking().Select(g => g.Id).ToHashSet();
        foreach (var added in ChangeTracker.Entries<Group>().Where(e => e.State == EntityState.Added))
        {
            groupIds.Add(added.Entity.Id);
        }

        var removed = ChangeTracker.Entries<GroupMembership>()
            .Where(e => e.State == EntityState.Deleted)
            .Select(e => (e.Entity.GroupId, e.Entity.MemberId))
            .ToHashSet();

        // Adjacency of "group contains group" edges only — user members are leaves
        // and can never close a cycle.
        var containsGroup = new Dictionary<Guid, List<Guid>>();
        void AddEdge(Guid groupId, Guid memberId)
        {
            if (!groupIds.Contains(memberId))
            {
                return;
            }

            (containsGroup.TryGetValue(groupId, out var members)
                ? members
                : containsGroup[groupId] = []).Add(memberId);
        }

        foreach (var edge in GroupMemberships.AsNoTracking().Select(m => new { m.GroupId, m.MemberId }))
        {
            if (!removed.Contains((edge.GroupId, edge.MemberId)))
            {
                AddEdge(edge.GroupId, edge.MemberId);
            }
        }

        foreach (var edge in pending)
        {
            AddEdge(edge.GroupId, edge.MemberId);
        }

        foreach (var edge in pending)
        {
            if (edge.MemberId == edge.GroupId || CanReach(containsGroup, edge.MemberId, edge.GroupId))
            {
                throw new InvalidOperationException(
                    $"Group membership would create a cycle: group '{edge.GroupId}' cannot contain principal '{edge.MemberId}'.");
            }
        }
    }

    private static bool CanReach(Dictionary<Guid, List<Guid>> adjacency, Guid from, Guid target)
    {
        var stack = new Stack<Guid>();
        var visited = new HashSet<Guid>();
        stack.Push(from);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current == target)
            {
                return true;
            }

            if (!visited.Add(current) || !adjacency.TryGetValue(current, out var next))
            {
                continue;
            }

            foreach (var member in next)
            {
                stack.Push(member);
            }
        }

        return false;
    }
}
