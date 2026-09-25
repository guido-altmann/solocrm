using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Tests.Abstractions;

public sealed class ResultTests
{
    [Fact]
    public void Success_Value_IsSuccessWithValue()
    {
        var result = Result<int>.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Failure_Error_IsFailureWithError()
    {
        var error = Error.NotFound("Contact.NotFound", "Kontakt nicht gefunden.");

        var result = Result<int>.Failure(error);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void Value_OnFailure_Throws()
    {
        Result<int> result = Error.Failure("X", "x");

        var act = () => result.Value;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Error_OnSuccess_Throws()
    {
        Result<int> result = 1;

        var act = () => result.Error;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Match_Failure_InvokesFailureBranch()
    {
        Result<int> result = new ValidationError(new Dictionary<string, string[]> { ["LastName"] = ["Pflichtfeld"] });

        var outcome = result.Match(_ => "ok", e => e.Type.ToString());

        outcome.Should().Be(nameof(ErrorType.Validation));
    }
}
