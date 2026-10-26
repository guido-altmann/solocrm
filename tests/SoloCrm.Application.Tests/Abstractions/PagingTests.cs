using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Contacts;

namespace SoloCrm.Application.Tests.Abstractions;

public sealed class PagingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(Paging.MaxPage - 1)]
    public void Validator_PageIndexWithinLimit_IsValid(int pageIndex)
    {
        var validation = new GetContacts.Validator().Validate(new GetContacts.Query(PageIndex: pageIndex, PageSize: Paging.MaxPageSize));

        validation.IsValid.Should().BeTrue();
    }

    // Before SOL-31 a huge index overflowed PageIndex × PageSize into a negative offset and Postgres failed.
    [Theory]
    [InlineData(-1)]
    [InlineData(Paging.MaxPage)]
    [InlineData(int.MaxValue - 1)]
    public void Validator_PageIndexOutOfRange_Fails(int pageIndex)
    {
        var validation = new GetContacts.Validator().Validate(new GetContacts.Query(PageIndex: pageIndex));

        validation.Errors.Should().ContainSingle(e => e.PropertyName == "PageIndex");
    }
}
