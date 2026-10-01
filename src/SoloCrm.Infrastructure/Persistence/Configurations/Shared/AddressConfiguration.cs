using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoloCrm.Domain.Common;

namespace SoloCrm.Infrastructure.Persistence.Configurations.Shared;

internal static class AddressConfiguration
{
    /// <summary>
    /// Required complex type (columns <c>address_street</c> …, all nullable); an empty address has only <c>NULL</c>s.
    /// </summary>
    public static void ConfigureAddress(this ComplexPropertyBuilder<Address> address)
    {
        address.Property(a => a.Street).HasMaxLength(Address.StreetMaxLength);
        address.Property(a => a.Street2).HasMaxLength(Address.StreetMaxLength);
        address.Property(a => a.PostalCode).HasMaxLength(Address.PostalCodeMaxLength);
        address.Property(a => a.City).HasMaxLength(Address.CityMaxLength);
        address.Property(a => a.Region).HasMaxLength(Address.RegionMaxLength);
        address.Property(a => a.CountryCode).HasMaxLength(2);
        address.Ignore(a => a.IsEmpty);
    }
}
