using Microsoft.EntityFrameworkCore;
using SoloCrm.Infrastructure.Persistence;
using SoloCrm.Infrastructure.Persistence.Seeding;

namespace SoloCrm.IntegrationTests.Performance;

/// <summary>
/// Bulk data for the NFR check (SPEC 6): 10k contacts in 500 organizations, 50k activities over two years,
/// 2k opportunities, 5k tasks and 30k audit entries (mostly invisible changes). Timestamps are relative to
/// <see cref="Now"/>.
/// </summary>
public static class LargeDataset
{
    public const int Organizations = 500;
    public const int Contacts = 10_000;
    public const int Activities = 50_000;

    public static readonly DateTimeOffset Now = new(2026, 9, 29, 8, 0, 0, TimeSpan.Zero);

    public static async Task SeedAsync(CrmDbContext db, CancellationToken ct)
    {
        // Only constants of this class are interpolated.
        var sql = $"""
            CREATE TEMP TABLE seed_base AS SELECT timestamptz '{Now:O}' AS now;

            INSERT INTO organizations (id, name, type, is_archived, extra_fields, created_at, updated_at)
            SELECT gen_random_uuid(), 'Organisation ' || g, 'Client', false, jsonb_build_object(), b.now - interval '2 years', b.now - (g % 300) * interval '1 hour'
            FROM generate_series(0, {Organizations - 1}) g, seed_base b;

            WITH o AS (SELECT id, row_number() OVER (ORDER BY id) - 1 AS n FROM organizations)
            INSERT INTO contacts (id, first_name, last_name, organization_id, is_archived, extra_fields, created_at, updated_at)
            SELECT gen_random_uuid(), 'Vorname ' || g, 'Nachname ' || g, o.id, false, jsonb_build_object(), b.now - interval '2 years', b.now - (g % 900) * interval '1 hour'
            FROM generate_series(0, {Contacts - 1}) g JOIN o ON o.n = g % {Organizations}, seed_base b;

            WITH c AS (SELECT id, row_number() OVER (ORDER BY id) - 1 AS n FROM contacts)
            INSERT INTO activities (id, type, occurred_at, subject, body, contact_id, created_at, updated_at)
            SELECT gen_random_uuid(), 'Note', b.now - (g % 730) * interval '1 day' - (g % 1440) * interval '1 minute',
                   'Betreff ' || g, 'Text ' || g, c.id, b.now, b.now
            FROM generate_series(0, {Activities - 1}) g JOIN c ON c.n = g % {Contacts}, seed_base b;

            WITH c AS (SELECT id, organization_id, row_number() OVER (ORDER BY id) - 1 AS n FROM contacts)
            INSERT INTO opportunities (id, title, stage_id, client_organization_id, primary_contact_id, is_archived, extra_fields, created_at, updated_at)
            SELECT gen_random_uuid(), 'Anfrage ' || g, '{DefaultStages.New}', c.organization_id, c.id, false, jsonb_build_object(),
                   b.now - (g % 60) * interval '1 day', b.now - (g % 60) * interval '1 day'
            FROM generate_series(0, 1999) g JOIN c ON c.n = g * 5, seed_base b;

            WITH o AS (SELECT id, row_number() OVER (ORDER BY id) - 1 AS n FROM opportunities)
            INSERT INTO activities (id, type, occurred_at, body, opportunity_id, created_at, updated_at)
            SELECT gen_random_uuid(), 'Call', b.now - (g % 30) * interval '1 day', 'Anruf ' || g, o.id, b.now, b.now
            FROM generate_series(0, 3999) g JOIN o ON o.n = g % 2000, seed_base b;

            WITH c AS (SELECT id, row_number() OVER (ORDER BY id) - 1 AS n FROM contacts)
            INSERT INTO tasks (id, title, due_date, completed_at, contact_id, created_at, updated_at)
            SELECT gen_random_uuid(), 'Aufgabe ' || g,
                   CASE WHEN g % 5 = 0 THEN NULL ELSE (b.now - (g % 40 - 20) * interval '1 day')::date END,
                   CASE WHEN g % 3 = 0 THEN b.now - (g % 20) * interval '1 day' END,
                   c.id, b.now - (g % 60) * interval '1 day', b.now
            FROM generate_series(0, 4999) g JOIN c ON c.n = g * 2, seed_base b;

            INSERT INTO audit_entries (id, entity_type, entity_id, action, changes, occurred_at)
            SELECT gen_random_uuid(), 'Contact', id, 'Created', jsonb_build_array(), created_at FROM contacts;

            WITH c AS (SELECT id, row_number() OVER (ORDER BY id) - 1 AS n FROM contacts)
            INSERT INTO audit_entries (id, entity_type, entity_id, action, changes, occurred_at)
            SELECT gen_random_uuid(), 'Contact', c.id, 'Updated', jsonb_build_array(jsonb_build_object('field', 'Phone', 'old', null, 'new', '1')),
                   b.now - (g % 700) * interval '1 day'
            FROM generate_series(0, 19999) g JOIN c ON c.n = g % {Contacts}, seed_base b;

            DROP TABLE seed_base;
            ANALYZE;
            """;

        await db.Database.ExecuteSqlRawAsync(sql, ct);
    }
}
