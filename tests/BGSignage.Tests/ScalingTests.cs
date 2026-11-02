using System.Linq;
using BGSignage.Core.Editing;
using BGSignage.Core.Layout;
using BGSignage.Core.Model;
using Xunit;

namespace BGSignage.Tests
{
    public class ScalingTests
    {
        [Theory]
        [InlineData(0.5)]
        [InlineData(2.0)]
        public void Scaling_a_sign_scales_every_template_exactly(double factor)
        {
            foreach (var name in Presets.Names)
            {
                var before = SignBuilder.Build(Presets.Create(name), new BoxFont());
                var doc = Presets.Create(name);
                SignScaling.Scale(doc, factor, depths: false);
                var after = SignBuilder.Build(doc, new BoxFont());

                Assert.Equal(before.Pieces.Count, after.Pieces.Count);
                Assert.Equal(before.PanelRect.Width * factor, after.PanelRect.Width, 3);
                Assert.Equal(before.PanelRect.Height * factor, after.PanelRect.Height, 3);
                for (int i = 0; i < before.Pieces.Count; i++)
                {
                    var a = before.Pieces[i].Bounds; var b = after.Pieces[i].Bounds;
                    Assert.Equal(a.MinX * factor, b.MinX, 3);
                    Assert.Equal(a.MaxY * factor, b.MaxY, 3);
                    Assert.Equal(a.Width * factor, b.Width, 3);
                }
            }
        }

        [Fact]
        public void Thicknesses_scale_only_when_asked()
        {
            var doc = Presets.Create(Presets.Names.First());
            double t = doc.Build.PanelThickness;
            SignScaling.Scale(doc, 2, depths: false);
            Assert.Equal(t, doc.Build.PanelThickness);
            SignScaling.Scale(doc, 2, depths: true);
            Assert.Equal(t * 2, doc.Build.PanelThickness);
        }

        [Fact]
        public void Family_sizes_are_cleaned_and_survive_saving()
        {
            var doc = new SignDocument();
            doc.Build.FamilySizes = new System.Collections.Generic.List<int> { 150, 75, 100, 75, 0 };
            doc.Normalise();
            Assert.Equal(new[] { 75, 100, 150 }, doc.Build.FamilySizes);
            var back = SignSerializer.FromJson(SignSerializer.ToJson(doc));
            Assert.Equal(new[] { 75, 100, 150 }, back.Build.FamilySizes);
            var old = SignSerializer.FromJson(SignSerializer.ToJson(new SignDocument()).Replace(",\"FamilySizes\":[100]", ""));
            Assert.Equal(new[] { 100 }, old.Normalise().Build.FamilySizes);
        }
    }
}
