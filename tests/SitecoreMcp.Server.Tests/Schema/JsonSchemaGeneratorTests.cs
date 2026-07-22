using System.Collections.Generic;
using Newtonsoft.Json;
using SitecoreMcp.Server.Schema;
using Xunit;

namespace SitecoreMcp.Server.Tests.Schema
{
    public class JsonSchemaGeneratorTests
    {
        private sealed class MapArgs
        {
            [McpParam(Description = "Field map.")]
            public Dictionary<string, string> Fields { get; set; }
        }

        private sealed class SampleArgs
        {
            [McpParam(Description = "The item path.", Required = true)]
            public string Path { get; set; }

            [McpParam(Description = "The database.", Enum = new[] { "master", "web", "core" })]
            public string Database { get; set; }

            [McpParam(Description = "Fields to return.")]
            public string[] Fields { get; set; }

            [McpParam(Description = "How many.")]
            public int? Limit { get; set; }

            [JsonProperty("customName")]
            public string Renamed { get; set; }
        }

        [Fact]
        public void Generates_a_lenient_object_so_cached_schemas_stay_forward_compatible()
        {
            var schema = JsonSchemaGenerator.Generate(typeof(SampleArgs));

            Assert.Equal("object", (string)schema["type"]);
            // No additionalProperties:false, so a client with a stale schema can still pass a
            // newly-added parameter without a client-side rejection.
            Assert.Null(schema["additionalProperties"]);
        }

        [Fact]
        public void Marks_required_properties()
        {
            var required = JsonSchemaGenerator.Generate(typeof(SampleArgs))["required"];

            Assert.Contains("path", required.Values<string>());
            Assert.DoesNotContain("database", required.Values<string>());
        }

        [Fact]
        public void Maps_primitive_and_array_and_enum_shapes()
        {
            var properties = JsonSchemaGenerator.Generate(typeof(SampleArgs))["properties"];

            Assert.Equal("string", (string)properties["path"]["type"]);
            Assert.Equal("integer", (string)properties["limit"]["type"]);
            Assert.Equal("array", (string)properties["fields"]["type"]);
            Assert.Equal("string", (string)properties["fields"]["items"]["type"]);
            Assert.Equal("web", properties["database"]["enum"][1]);
        }

        [Fact]
        public void Maps_a_string_dictionary_to_an_open_object()
        {
            var fields = JsonSchemaGenerator.Generate(typeof(MapArgs))["properties"]["fields"];

            Assert.Equal("object", (string)fields["type"]);
            Assert.Equal("string", (string)fields["additionalProperties"]["type"]);
        }

        [Fact]
        public void Honours_JsonProperty_name_over_camel_case()
        {
            var properties = JsonSchemaGenerator.Generate(typeof(SampleArgs))["properties"];

            Assert.NotNull(properties["customName"]);
            Assert.Null(properties["renamed"]);
        }
    }
}
