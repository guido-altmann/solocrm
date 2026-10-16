using SoloCrm.Application.Features.Nextcloud;

namespace SoloCrm.Application.Tests.Features.Nextcloud;

public sealed class ConnectNextcloudValidatorTests
{
    private readonly ConnectNextcloud.Validator _validator = new(new NextcloudServers([" radicale ", ""]));

    [Theory]
    [InlineData("https://cloud.example.test", true)]
    [InlineData("https://cloud.example.test/nextcloud/", true)]
    [InlineData("http://radicale:5232", true)]
    [InlineData("http://cloud.example.test", false)]
    [InlineData("https://guido:secret@cloud.example.test", false)]
    [InlineData("https://cloud.example.test/?x=1", false)]
    [InlineData("cloud.example.test", false)]
    [InlineData("ftp://cloud.example.test", false)]
    public void Validate_ServerUrl_RequiresHttpsOrAllowedHttpHost(string url, bool valid)
    {
        var result = _validator.Validate(new ConnectNextcloud.Command(url, "guido", "app-password"));

        result.IsValid.Should().Be(valid);
    }

    [Fact]
    public void Validate_MissingValues_ReportsEveryField()
    {
        var result = _validator.Validate(new ConnectNextcloud.Command(" ", "", null));

        result.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(
            nameof(ConnectNextcloud.Command.ServerUrl), nameof(ConnectNextcloud.Command.UserName), nameof(ConnectNextcloud.Command.AppPassword));
    }

    [Fact]
    public void Validate_HttpUrl_ExplainsHttps()
    {
        var result = _validator.Validate(new ConnectNextcloud.Command("http://cloud.example.test", "guido", "x"));

        result.Errors.Should().ContainSingle().Which.ErrorMessage.Should().Contain("https");
    }
}
