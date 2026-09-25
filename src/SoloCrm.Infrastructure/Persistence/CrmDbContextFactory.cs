using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Infrastructure.Persistence;

internal sealed class CrmDbContextFactory(IDbContextFactory<CrmDbContext> factory) : ICrmDbContextFactory
{
    public async Task<ICrmDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        await factory.CreateDbContextAsync(cancellationToken);
}
