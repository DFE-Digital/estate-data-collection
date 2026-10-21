using Bogus;
using System.Text.Json;

namespace EstateDataCollection.Tests.Common.Generators;

public static class SchoolPayloadGenerator
{
    private static readonly Faker Faker = new();

    public static string GenerateValidV1Json(int? urn = null)
    {
        var payload = new
        {
            urn = urn ?? Faker.Random.Number(100000, 999999),
            school_name = Faker.Company.CompanyName() + " School",
            school_address = Faker.Address.FullAddress(),
            school_capacity = Faker.Random.Number(100, 2000)
        };

        return JsonSerializer.Serialize(payload);
    }
}
