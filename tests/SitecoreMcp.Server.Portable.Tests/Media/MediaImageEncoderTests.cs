using System.Collections.Generic;
using SitecoreMcp.Server.Media;
using SitecoreMcp.Server.Tools;
using Xunit;

namespace SitecoreMcp.Server.Portable.Tests.Media
{
    public class MediaImageEncoderTests
    {
        /// <summary>
        /// Stands in for Sitecore's resizer: renders the image within the requested square box, never
        /// upscaling, at <see cref="bytesPerPixel"/> so byte size follows pixel count.
        /// </summary>
        private sealed class FakeRenderer
        {
            private readonly int _width;
            private readonly int _height;
            private readonly double _bytesPerPixel;

            public FakeRenderer(int width, int height, double bytesPerPixel = 1)
            {
                _width = width;
                _height = height;
                _bytesPerPixel = bytesPerPixel;
            }

            public List<int> RequestedBounds { get; } = new List<int>();

            public byte[] Render(int maxDimension)
            {
                RequestedBounds.Add(maxDimension);
                var scale = System.Math.Min(1.0, (double)maxDimension / System.Math.Max(_width, _height));
                var pixels = System.Math.Round(_width * scale) * System.Math.Round(_height * scale);
                return new byte[(int)(pixels * _bytesPerPixel)];
            }
        }

        private static MediaImageEncoder Encoder(int maxDimension = 1024, long maxBytes = 10_000_000) =>
            new MediaImageEncoder(new ImageLimits(maxDimension, maxBytes));

        [Fact]
        public void An_image_within_the_limits_is_returned_at_its_own_size()
        {
            var renderer = new FakeRenderer(640, 480);

            var image = Encoder().Encode(new ImageSource("image/png", 640, 480), null, renderer.Render);

            Assert.Equal("image/png", image.MimeType);
            Assert.Equal(640, image.Width);
            Assert.Equal(480, image.Height);
            Assert.False(image.Resized);
            Assert.Equal(640 * 480, image.Data.Length);
            Assert.Equal(new[] { 640 }, renderer.RequestedBounds);
        }

        [Fact]
        public void A_larger_image_is_resized_within_the_configured_max_dimension_keeping_its_aspect_ratio()
        {
            var renderer = new FakeRenderer(4000, 3000);

            var image = Encoder(maxDimension: 1024).Encode(new ImageSource("image/jpeg", 4000, 3000), null, renderer.Render);

            Assert.True(image.Resized);
            Assert.Equal(1024, image.Width);
            Assert.Equal(768, image.Height);
            Assert.Equal(4000, image.OriginalWidth);
            Assert.Equal(3000, image.OriginalHeight);
            Assert.Equal(new[] { 1024 }, renderer.RequestedBounds);
        }

        [Fact]
        public void A_portrait_image_is_bounded_by_its_height()
        {
            var image = Encoder(maxDimension: 500).Encode(
                new ImageSource("image/png", 1000, 2000), null, new FakeRenderer(1000, 2000).Render);

            Assert.Equal(250, image.Width);
            Assert.Equal(500, image.Height);
        }

        [Fact]
        public void A_smaller_requested_max_dimension_is_honoured()
        {
            var renderer = new FakeRenderer(4000, 3000);

            var image = Encoder(maxDimension: 1024).Encode(new ImageSource("image/png", 4000, 3000), 200, renderer.Render);

            Assert.Equal(200, image.Width);
            Assert.Equal(150, image.Height);
        }

        [Fact]
        public void A_requested_max_dimension_above_the_configured_limit_is_capped_at_the_limit()
        {
            var renderer = new FakeRenderer(4000, 3000);

            var image = Encoder(maxDimension: 1024).Encode(new ImageSource("image/png", 4000, 3000), 3000, renderer.Render);

            Assert.Equal(1024, image.Width);
            Assert.Equal(new[] { 1024 }, renderer.RequestedBounds);
        }

        [Fact]
        public void An_image_over_the_byte_limit_is_rendered_smaller_until_it_fits()
        {
            var renderer = new FakeRenderer(2000, 2000);

            var image = Encoder(maxDimension: 1000, maxBytes: 100_000).Encode(
                new ImageSource("image/png", 2000, 2000), null, renderer.Render);

            Assert.True(image.Data.Length <= 100_000);
            Assert.True(renderer.RequestedBounds.Count > 1);
            Assert.Equal(1000, renderer.RequestedBounds[0]);
            Assert.Equal(image.Width, renderer.RequestedBounds[renderer.RequestedBounds.Count - 1]);
            Assert.True(image.Width < 1000 && image.Width >= 250, "shrinks only as far as needed: " + image.Width);
            Assert.True(image.Resized);
        }

        [Fact]
        public void An_image_that_cannot_be_made_small_enough_is_refused_with_the_limit()
        {
            // A renderer whose output never gets lighter, however small it is asked to render.
            var bounds = new List<int>();
            System.Func<int, byte[]> render = bound => { bounds.Add(bound); return new byte[60_000]; };

            var ex = Assert.Throws<McpToolException>(() => Encoder(maxBytes: 50_000).Encode(
                new ImageSource("image/png", 800, 800), null, render));

            Assert.Contains("50000", ex.Message);
            Assert.True(bounds.Count <= 8, "gives up after a bounded number of attempts");
        }

        [Fact]
        public void An_image_with_unknown_dimensions_is_rendered_within_the_limit_and_reports_no_size()
        {
            var renderer = new FakeRenderer(3000, 1500);

            var image = Encoder(maxDimension: 800).Encode(new ImageSource("image/gif", null, null), null, renderer.Render);

            Assert.Equal(new[] { 800 }, renderer.RequestedBounds);
            Assert.Null(image.Width);
            Assert.Null(image.Height);
            Assert.Null(image.OriginalWidth);
        }

        [Theory]
        [InlineData("image/svg+xml")]
        [InlineData("application/pdf")]
        [InlineData("image/tiff")]
        [InlineData("")]
        [InlineData(null)]
        public void A_media_type_models_cannot_take_as_an_image_is_refused(string mimeType)
        {
            var renderer = new FakeRenderer(10, 10);

            var ex = Assert.Throws<McpToolException>(() => Encoder().Encode(new ImageSource(mimeType, 10, 10), null, renderer.Render));

            Assert.Contains("image/png", ex.Message);
            Assert.Empty(renderer.RequestedBounds);
        }

        [Theory]
        [InlineData("image/png", "image/png")]
        [InlineData("IMAGE/JPEG", "image/jpeg")]
        [InlineData("image/jpg", "image/jpeg")]
        [InlineData("image/pjpeg", "image/jpeg")]
        [InlineData("image/gif", "image/gif")]
        [InlineData("image/webp", "image/webp")]
        public void Supported_media_types_are_normalised(string mimeType, string expected)
        {
            var image = Encoder().Encode(new ImageSource(mimeType, 10, 10), null, new FakeRenderer(10, 10).Render);

            Assert.Equal(expected, image.MimeType);
        }

        [Fact]
        public void A_requested_max_dimension_below_the_minimum_is_raised_to_the_minimum()
        {
            var renderer = new FakeRenderer(4000, 3000);

            Encoder().Encode(new ImageSource("image/png", 4000, 3000), 1, renderer.Render);

            Assert.Equal(new[] { ImageLimits.MinDimension }, renderer.RequestedBounds);
        }

        [Fact]
        public void Unset_or_invalid_limits_fall_back_to_the_defaults()
        {
            var limits = new ImageLimits(0, -5);

            Assert.Equal(ImageLimits.DefaultMaxDimension, limits.MaxDimension);
            Assert.Equal(ImageLimits.DefaultMaxBytes, limits.MaxBytes);
        }
    }
}
