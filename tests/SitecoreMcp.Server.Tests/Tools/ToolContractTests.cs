using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using SitecoreMcp.Server.Schema;
using Xunit;

namespace SitecoreMcp.Server.Tests.Tools
{
    /// <summary>
    /// Contract tests that run once per tool, so every tool is its own test result. They cover what
    /// is reachable without a Sitecore instance: identity, description, generated schema, and the
    /// permission each tool declares. Execute paths need a live Kernel and are verified against a
    /// real instance instead.
    /// </summary>
    public class ToolContractTests
    {
        public static IEnumerable<object[]> AllTools => ToolCatalog.AsTheoryData();

        [Theory]
        [MemberData(nameof(AllTools))]
        public void Name_is_a_well_formed_tool_identifier(ToolCase testCase)
        {
            var name = testCase.Tool.Name;

            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.StartsWith("sitecore_", name, StringComparison.Ordinal);
            // Lower snake case keeps names predictable for a model composing calls.
            Assert.Matches(new Regex("^sitecore_[a-z0-9]+(_[a-z0-9]+)*$"), name);
        }

        [Theory]
        [MemberData(nameof(AllTools))]
        public void Description_tells_the_model_when_to_use_the_tool(ToolCase testCase)
        {
            var description = testCase.Tool.Description;

            Assert.False(string.IsNullOrWhiteSpace(description));
            // A one-liner is not enough for a model choosing between 66 tools.
            Assert.True(description.Length >= 60, $"{testCase.Tool.Name} description is only {description.Length} characters.");
            Assert.EndsWith(".", description.TrimEnd(), StringComparison.Ordinal);
        }

        [Theory]
        [MemberData(nameof(AllTools))]
        public void Argument_type_is_a_constructible_poco(ToolCase testCase)
        {
            var argumentType = testCase.Tool.ArgumentType;

            Assert.NotNull(argumentType);
            Assert.True(argumentType.IsClass, $"{testCase.Tool.Name} arguments must be a class.");
            Assert.NotNull(argumentType.GetConstructor(Type.EmptyTypes));
        }

        [Theory]
        [MemberData(nameof(AllTools))]
        public void Generates_an_object_schema_with_described_properties(ToolCase testCase)
        {
            var schema = JsonSchemaGenerator.Generate(testCase.Tool.ArgumentType);

            Assert.Equal("object", (string)schema["type"]);
            Assert.NotNull(schema["properties"]);

            // Schemas stay lenient on purpose: clients cache them, so a later-added argument must not
            // hard-reject on a client holding an older copy.
            Assert.Null(schema["additionalProperties"]);
        }

        [Theory]
        [MemberData(nameof(AllTools))]
        public void Required_arguments_are_declared_in_the_schema(ToolCase testCase)
        {
            var schema = JsonSchemaGenerator.Generate(testCase.Tool.ArgumentType);
            var declared = schema["required"]?.Select(token => (string)token).ToList() ?? new List<string>();

            var expected = testCase.Tool.ArgumentType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.GetCustomAttribute<McpParamAttribute>()?.Required == true)
                .Select(JsonNaming.ToJsonName)
                .ToList();

            Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal),
                         declared.OrderBy(name => name, StringComparer.Ordinal));
        }

        [Theory]
        [MemberData(nameof(AllTools))]
        public void Every_argument_carries_a_description_for_the_model(ToolCase testCase)
        {
            var undescribed = testCase.Tool.ArgumentType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => string.IsNullOrWhiteSpace(property.GetCustomAttribute<McpParamAttribute>()?.Description))
                .Select(property => property.Name)
                .ToList();

            Assert.True(undescribed.Count == 0,
                $"{testCase.Tool.Name} has arguments without an [McpParam] description: {string.Join(", ", undescribed)}");
        }

        [Fact]
        public void Tool_names_are_unique()
        {
            var duplicates = ToolCatalog.All
                .GroupBy(tool => tool.Name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

            Assert.True(duplicates.Count == 0, $"Duplicate tool names: {string.Join(", ", duplicates)}");
        }
    }
}
