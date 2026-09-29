using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Tasks;
using SoloCrm.Domain.Tasks;

namespace SoloCrm.Application.Tests.Features.Tasks;

public sealed class CreateTaskTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Validator_BlankTitle_Fails(string? title)
    {
        var validation = new CreateTask.Validator().Validate(new CreateTask.Command(title));

        validation.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("Title");
    }

    [Fact]
    public void Validator_TitleTooLong_Fails()
    {
        var validation = new UpdateTask.Validator().Validate(
            new UpdateTask.Command(Guid.CreateVersion7(), new string('x', TaskItem.TitleMaxLength + 1), null));

        validation.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("Title");
    }

    [Fact]
    public void Validator_FreeTaskWithTitle_IsValid()
    {
        new CreateTask.Validator().Validate(new CreateTask.Command("Angebot nachfassen")).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_InvalidCommand_ReturnsValidationErrorWithoutTouchingDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbFactory = Substitute.For<ICrmDbContextFactory>();
        var handler = new CreateTask.Handler(dbFactory, new CreateTask.Validator());

        var result = await handler.Handle(new CreateTask.Command(""), ct);

        result.Error.Should().BeOfType<ValidationError>();
        await dbFactory.DidNotReceiveWithAnyArgs().CreateDbContextAsync(ct);
    }
}
