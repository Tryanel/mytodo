using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;

namespace PaperTodo.Tests;

public sealed class LocalizationContractTests
{
    private const string AuditedResourceVersion = "4.1";

    [Fact]
    public void Compiled_resource_sets_have_matching_nonempty_keys()
    {
        var manager = new ResourceManager(
            "PaperTodo.Resources.Strings",
            typeof(Strings).Assembly);
        var cultures = new[]
        {
            CultureInfo.InvariantCulture,
            CultureInfo.GetCultureInfo("en"),
            CultureInfo.GetCultureInfo("ja"),
            CultureInfo.GetCultureInfo("ko")
        };
        var resources = cultures.Select(culture =>
        {
            var set = manager.GetResourceSet(
                culture,
                createIfNotExists: true,
                tryParents: false);
            Assert.NotNull(set);
            return set.Cast<DictionaryEntry>().ToDictionary(
                entry => Assert.IsType<string>(entry.Key),
                entry => Assert.IsType<string>(entry.Value),
                StringComparer.Ordinal);
        }).ToArray();

        var expectedKeys = resources[0].Keys.Order(StringComparer.Ordinal).ToArray();
        foreach (var resource in resources)
        {
            Assert.Equal(
                expectedKeys,
                resource.Keys.Order(StringComparer.Ordinal).ToArray());
            Assert.All(resource.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
            Assert.Equal(AuditedResourceVersion, resource["ResourceTextVersion"]);
        }
    }

    [Fact]
    public void Supplemental_strings_cover_all_four_ui_languages()
    {
        var field = typeof(Strings).GetField(
            "Supplemental",
            BindingFlags.Static | BindingFlags.NonPublic);
        var supplemental = Assert.IsAssignableFrom<
            IReadOnlyDictionary<string, string[]>>(field?.GetValue(null));

        Assert.NotEmpty(supplemental);
        Assert.All(supplemental, pair =>
        {
            Assert.Equal(4, pair.Value.Length);
            Assert.All(pair.Value, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        });
    }
}
